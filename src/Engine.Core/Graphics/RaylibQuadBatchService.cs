using System.Numerics;
using System.Runtime.CompilerServices;
using Engine.Core.Abstractions;
using Raylib_cs;

namespace Engine.Core.Graphics;

public sealed unsafe class RaylibQuadBatchService : IQuadBatchService, IQuadBatchStats, IDisposable
{
    public const int MaxQuads = 16_384;
    public const int MaxVertices = MaxQuads * 4; // 65,536
    public const int MaxIndices = MaxQuads * 6;  // 98,304

    private static readonly int VertexStride = Unsafe.SizeOf<BatchVertex>();

    private readonly RaylibGraphicsService _graphics;
    private readonly BatchVertex[] _vertices;
    private readonly ushort[] _indices;

    private uint _vaoId;
    private uint _vboId;
    private uint _eboId;
    private uint _defaultWhiteTextureId;
    private bool _gpuInitialized;
    private bool _disposed;

    private bool _isBatchOpen;
    private int _quadCount;
    private TextureHandle _activeHandle;
    private uint _activeTextureId;
    private bool _isUsingDefaultWhiteTexture;
    private float _invTexWidth = 1f;
    private float _invTexHeight = 1f;
    private BlendMode _activeBlendMode = BlendMode.Alpha;
    private string? _activeShaderPath;
    private bool _hasActiveShader;
    private Shader _activeShader;

    private int _currentFrameDrawCalls;
    private int _currentFrameQuads;
    private int _lastFrameDrawCalls;
    private int _lastFrameQuads;

    public RaylibQuadBatchService(RaylibGraphicsService graphics)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _graphics.AttachQuadBatchService(this);

        // Allocate permanently pinned POH buffers once at initialization so frame draw calls incur 0 B heap allocations.
        _vertices = GC.AllocateArray<BatchVertex>(MaxVertices, pinned: true);
        _indices = GC.AllocateArray<ushort>(MaxIndices, pinned: true);

        for (int q = 0; q < MaxQuads; q++)
        {
            ushort v = (ushort)(q * 4);
            int i = q * 6;
            _indices[i + 0] = v;
            _indices[i + 1] = (ushort)(v + 1);
            _indices[i + 2] = (ushort)(v + 2);
            _indices[i + 3] = v;
            _indices[i + 4] = (ushort)(v + 2);
            _indices[i + 5] = (ushort)(v + 3);
        }
    }

    public int CurrentFrameDrawCalls => _currentFrameDrawCalls;
    public int CurrentFrameQuads => _currentFrameQuads;
    public int LastFrameDrawCalls => _lastFrameDrawCalls;
    public int LastFrameQuads => _lastFrameQuads;

    public void BeginFrame()
    {
        _currentFrameDrawCalls = 0;
        _currentFrameQuads = 0;
    }

    public void EndFrame()
    {
        EnsureEndedIfOpen();
        _lastFrameDrawCalls = _currentFrameDrawCalls;
        _lastFrameQuads = _currentFrameQuads;
    }

    public void Begin(TextureHandle texture, BlendMode blendMode = BlendMode.Alpha, string? shaderPath = null)
    {
        if (_disposed)
        {
            return;
        }

        if (_isBatchOpen)
        {
            bool sameState = _activeHandle == texture &&
                             _activeBlendMode == blendMode &&
                             string.Equals(_activeShaderPath, shaderPath, StringComparison.Ordinal);
            if (sameState)
            {
                return;
            }

            if (_quadCount > 0)
            {
                Flush();
            }
        }
        else
        {
            _isBatchOpen = true;
        }

        ApplyPipelineState(texture, blendMode, shaderPath);
    }

    public void DrawQuad(Vector2 position, Vector2 size, Rectangle srcRect, Color tint)
    {
        if (!_isBatchOpen || _disposed)
        {
            return;
        }

        if (_quadCount >= MaxQuads)
        {
            Flush();
        }

        ComputeUVs(srcRect, out float u0, out float v0, out float u1, out float v1);

        float x0 = position.X;
        float y0 = position.Y;
        float x1 = x0 + size.X;
        float y1 = y0 + size.Y;

        int vBase = _quadCount * 4;
        _vertices[vBase + 0] = new BatchVertex(new Vector2(x0, y0), new Vector2(u0, v0), tint);
        _vertices[vBase + 1] = new BatchVertex(new Vector2(x0, y1), new Vector2(u0, v1), tint);
        _vertices[vBase + 2] = new BatchVertex(new Vector2(x1, y1), new Vector2(u1, v1), tint);
        _vertices[vBase + 3] = new BatchVertex(new Vector2(x1, y0), new Vector2(u1, v0), tint);
        _quadCount++;
    }

    public void DrawQuadRotated(Vector2 position, Vector2 size, Rectangle srcRect, float rotationDeg, Vector2 origin, Color tint)
    {
        if (!_isBatchOpen || _disposed)
        {
            return;
        }

        if (_quadCount >= MaxQuads)
        {
            Flush();
        }

        ComputeUVs(srcRect, out float u0, out float v0, out float u1, out float v1);

        float lx0 = -origin.X;
        float ly0 = -origin.Y;
        float lx1 = size.X - origin.X;
        float ly1 = size.Y - origin.Y;

        Vector2 p0, p1, p2, p3;
        if (MathF.Abs(rotationDeg) < 1e-5f)
        {
            float px = position.X;
            float py = position.Y;
            p0 = new Vector2(px + lx0, py + ly0);
            p1 = new Vector2(px + lx0, py + ly1);
            p2 = new Vector2(px + lx1, py + ly1);
            p3 = new Vector2(px + lx1, py + ly0);
        }
        else
        {
            float rad = rotationDeg * (MathF.PI / 180f);
            (float sin, float cos) = MathF.SinCos(rad);
            float px = position.X;
            float py = position.Y;

            p0 = new Vector2(px + lx0 * cos - ly0 * sin, py + lx0 * sin + ly0 * cos);
            p1 = new Vector2(px + lx0 * cos - ly1 * sin, py + lx0 * sin + ly1 * cos);
            p2 = new Vector2(px + lx1 * cos - ly1 * sin, py + lx1 * sin + ly1 * cos);
            p3 = new Vector2(px + lx1 * cos - ly0 * sin, py + lx1 * sin + ly0 * cos);
        }

        int vBase = _quadCount * 4;
        _vertices[vBase + 0] = new BatchVertex(p0, new Vector2(u0, v0), tint);
        _vertices[vBase + 1] = new BatchVertex(p1, new Vector2(u0, v1), tint);
        _vertices[vBase + 2] = new BatchVertex(p2, new Vector2(u1, v1), tint);
        _vertices[vBase + 3] = new BatchVertex(p3, new Vector2(u1, v0), tint);
        _quadCount++;
    }

    public void Flush()
    {
        if (_quadCount <= 0 || _disposed)
        {
            return;
        }

        int submittedQuads = _quadCount;
        _quadCount = 0;

        if (!Raylib.IsWindowReady())
        {
            _currentFrameDrawCalls++;
            _currentFrameQuads += submittedQuads;
            return;
        }

        EnsureGpuResources();
        if (!_gpuInitialized)
        {
            return;
        }

        // Flush any pending immediate Raylib draw batch first to preserve exact painter order.
        Rlgl.DrawRenderBatchActive();

        fixed (BatchVertex* pVerts = _vertices)
        {
            Rlgl.UpdateVertexBuffer(_vboId, pVerts, submittedQuads * 4 * VertexStride, 0);
        }

        Matrix4x4 mv = Rlgl.GetMatrixModelview();
        Matrix4x4 proj = Rlgl.GetMatrixProjection();
        Matrix4x4 tr = Rlgl.GetMatrixTransform();
        Matrix4x4 mvp = Matrix4x4.Multiply(proj, Matrix4x4.Multiply(mv, tr));

        uint shaderId;
        int* locs;
        if (_hasActiveShader)
        {
            shaderId = _activeShader.Id;
            locs = _activeShader.Locs;
        }
        else
        {
            shaderId = Rlgl.GetShaderIdDefault();
            locs = Rlgl.GetShaderLocsDefault();
        }

        Rlgl.SetBlendMode(_activeBlendMode);
        Rlgl.EnableShader(shaderId);

        if (locs != null)
        {
            int mvpLoc = locs[(int)ShaderLocationIndex.MatrixMvp];
            if (mvpLoc >= 0)
            {
                Rlgl.SetUniformMatrix(mvpLoc, mvp);
            }

            int colDiffLoc = locs[(int)ShaderLocationIndex.ColorDiffuse];
            if (colDiffLoc >= 0)
            {
                float* white = stackalloc float[4];
                white[0] = 1f;
                white[1] = 1f;
                white[2] = 1f;
                white[3] = 1f;
                Rlgl.SetUniform(colDiffLoc, white, (int)ShaderUniformDataType.Vec4, 1);
            }

            int mapDiffLoc = locs[(int)ShaderLocationIndex.MapDiffuse];
            if (mapDiffLoc >= 0)
            {
                int slot = 0;
                Rlgl.SetUniform(mapDiffLoc, &slot, (int)ShaderUniformDataType.Sampler2D, 1);
            }
        }

        Rlgl.ActiveTextureSlot(0);
        Rlgl.EnableTexture(_activeTextureId);

        Rlgl.EnableVertexArray(_vaoId);
        Rlgl.DrawVertexArrayElements(0, submittedQuads * 6, null);
        Rlgl.DisableVertexArray();

        Rlgl.DisableTexture();

        if (_graphics.CurrentShader is { } outerShader && Raylib.IsShaderValid(outerShader))
        {
            Rlgl.EnableShader(outerShader.Id);
        }
        else if (_hasActiveShader)
        {
            Rlgl.DisableShader();
        }
        else
        {
            Rlgl.DisableShader();
        }

        if (_activeBlendMode != _graphics.CurrentBlendMode)
        {
            Rlgl.SetBlendMode(_graphics.CurrentBlendMode);
        }

        _currentFrameDrawCalls++;
        _currentFrameQuads += submittedQuads;
    }

    public void End()
    {
        if (!_isBatchOpen)
        {
            return;
        }

        Flush();
        _isBatchOpen = false;
        _activeHandle = TextureHandle.Invalid;
        _activeShaderPath = null;
        _hasActiveShader = false;
        _activeShader = default;
    }

    internal void EnsureEndedIfOpen()
    {
        if (_isBatchOpen)
        {
            End();
        }
    }

    private void ApplyPipelineState(TextureHandle texture, BlendMode blendMode, string? shaderPath)
    {
        _activeHandle = texture;
        _activeBlendMode = blendMode;
        _activeShaderPath = shaderPath;

        if (_graphics.TryResolveTextureForBatch(texture, out Texture2D resolvedTex) && resolvedTex.Id != 0)
        {
            _activeTextureId = resolvedTex.Id;
            _isUsingDefaultWhiteTexture = false;
            _invTexWidth = 1f / MathF.Max(1f, resolvedTex.Width);
            _invTexHeight = 1f / MathF.Max(1f, resolvedTex.Height);
        }
        else
        {
            if (_defaultWhiteTextureId == 0 && Raylib.IsWindowReady())
            {
                _defaultWhiteTextureId = Rlgl.GetTextureIdDefault();
            }

            _activeTextureId = _defaultWhiteTextureId;
            _isUsingDefaultWhiteTexture = true;
            _invTexWidth = 1f;
            _invTexHeight = 1f;
        }

        if (!string.IsNullOrWhiteSpace(shaderPath) &&
            _graphics.TryResolveShaderForBatch(shaderPath, out Shader resolvedShader))
        {
            _activeShader = resolvedShader;
            _hasActiveShader = true;
        }
        else
        {
            _activeShader = default;
            _hasActiveShader = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ComputeUVs(Rectangle srcRect, out float u0, out float v0, out float u1, out float v1)
    {
        if (_isUsingDefaultWhiteTexture)
        {
            u0 = 0.5f;
            v0 = 0.5f;
            u1 = 0.5f;
            v1 = 0.5f;
            return;
        }

        if (srcRect.Width == 0f && srcRect.Height == 0f)
        {
            u0 = 0f;
            v0 = 0f;
            u1 = 1f;
            v1 = 1f;
            return;
        }

        u0 = srcRect.X * _invTexWidth;
        v0 = srcRect.Y * _invTexHeight;
        u1 = (srcRect.X + srcRect.Width) * _invTexWidth;
        v1 = (srcRect.Y + srcRect.Height) * _invTexHeight;
    }

    private void EnsureGpuResources()
    {
        if (_gpuInitialized || !Raylib.IsWindowReady())
        {
            return;
        }

        _defaultWhiteTextureId = Rlgl.GetTextureIdDefault();
        if (_isUsingDefaultWhiteTexture)
        {
            _activeTextureId = _defaultWhiteTextureId;
        }

        _vaoId = Rlgl.LoadVertexArray();
        Rlgl.EnableVertexArray(_vaoId);

        fixed (BatchVertex* pVerts = _vertices)
        {
            _vboId = Rlgl.LoadVertexBuffer(pVerts, MaxVertices * VertexStride, true);
        }

        // Default vertex attribute layout used by Raylib's standard GLSL 330 shaders:
        // loc 0: vertexPosition (vec2 -> z=0,w=1), loc 1: vertexTexCoord (vec2), loc 3: vertexColor (ubyte4 normalized)
        Rlgl.SetVertexAttribute(0, 2, Rlgl.FLOAT, false, VertexStride, 0);
        Rlgl.EnableVertexAttribute(0);

        Rlgl.SetVertexAttribute(1, 2, Rlgl.FLOAT, false, VertexStride, 8);
        Rlgl.EnableVertexAttribute(1);

        Rlgl.SetVertexAttribute(3, 4, Rlgl.UNSIGNED_BYTE, true, VertexStride, 16);
        Rlgl.EnableVertexAttribute(3);

        fixed (ushort* pIndices = _indices)
        {
            _eboId = Rlgl.LoadVertexBufferElement(pIndices, MaxIndices * sizeof(ushort), false);
        }

        Rlgl.DisableVertexArray();
        Rlgl.DisableVertexBuffer();
        Rlgl.DisableVertexBufferElement();

        _gpuInitialized = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isBatchOpen = false;
        _quadCount = 0;

        if (_gpuInitialized && Raylib.IsWindowReady())
        {
            if (_vaoId != 0) Rlgl.UnloadVertexArray(_vaoId);
            if (_vboId != 0) Rlgl.UnloadVertexBuffer(_vboId);
            if (_eboId != 0) Rlgl.UnloadVertexBuffer(_eboId);
        }

        _vaoId = 0;
        _vboId = 0;
        _eboId = 0;
        _gpuInitialized = false;
    }
}
