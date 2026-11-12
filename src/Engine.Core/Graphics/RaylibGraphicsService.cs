using System.Numerics;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Raylib_cs;

namespace Engine.Core.Graphics;

public sealed class RaylibGraphicsService : IGraphicsService, IDisposable
{
    private readonly IWorkspaceService _workspace;
    private readonly IEventBus _eventBus;
    private IVirtualDisplayService? _displayService;
    private RaylibQuadBatchService? _quadBatchService;

    private readonly List<Texture2D> _texturesByHandle = new() { default };
    private readonly List<string> _handleResolvedPaths = new() { string.Empty };
    private readonly Dictionary<string, int> _resolvedPathToHandleId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingLogged = new(StringComparer.Ordinal);

    private readonly Dictionary<string, RenderTexture2D> _surfaceCache = new(StringComparer.Ordinal);
    private readonly Stack<RenderTexture2D> _activeTargetStack = new();

    private readonly Dictionary<string, Shader> _shaderCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Shader> _shaderByRawPath = new(StringComparer.Ordinal);
    private readonly Dictionary<(uint ShaderId, string UniformName), int> _shaderLocationCache = new();
    private readonly HashSet<string> _invalidShadersLogged = new(StringComparer.Ordinal);

    private readonly Stack<Shader?> _shaderStack = new();
    private readonly Stack<(int TargetDepth, Camera2D Camera)> _camera2DStack = new();
    private readonly Stack<BlendMode> _blendModeStack = new();

    private RenderTexture2D _renderTexture;
    private bool _hasRenderTexture;
    private bool _isInRenderTarget;
    private bool _disposed;

    public RaylibGraphicsService(
        IWorkspaceService workspace,
        IEventBus eventBus,
        IVirtualDisplayService? displayService = null)
    {
        _workspace = workspace;
        _eventBus = eventBus;
        _displayService = displayService;
    }

    internal void AttachDisplayService(IVirtualDisplayService displayService)
    {
        _displayService = displayService;
    }

    internal void AttachQuadBatchService(RaylibQuadBatchService batchService)
    {
        _quadBatchService = batchService;
    }

    internal BlendMode CurrentBlendMode => _blendModeStack.TryPeek(out BlendMode mode) ? mode : BlendMode.Alpha;

    internal Shader? CurrentShader => _shaderStack.TryPeek(out Shader? shader) ? shader : null;

    internal bool TryResolveTextureForBatch(TextureHandle handle, out Texture2D texture)
    {
        return TryGetTextureByHandle(handle, out texture);
    }

    internal bool TryResolveShaderForBatch(string? fragmentShaderPath, out Shader shader)
    {
        if (string.IsNullOrWhiteSpace(fragmentShaderPath))
        {
            shader = default;
            return false;
        }

        if (_shaderByRawPath.TryGetValue(fragmentShaderPath, out shader) && Raylib.IsShaderValid(shader))
        {
            return true;
        }

        if (TryGetOrLoadShader(fragmentShaderPath, null, out shader))
        {
            _shaderByRawPath[fragmentShaderPath] = shader;
            return true;
        }

        return false;
    }

    public TextureHandle GetTextureHandle(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return TextureHandle.Invalid;
        }

        return TryGetOrLoadTexture(assetPath, out int handleId, out _)
            ? new TextureHandle(handleId)
            : TextureHandle.Invalid;
    }

    public void DrawTexture(TextureHandle handle, Vector2 position, Rectangle? srcRect = null, Color? tint = null)
    {
        if (!TryGetTextureByHandle(handle, out Texture2D texture))
        {
            return;
        }

        Rectangle source = srcRect ?? new Rectangle(0f, 0f, texture.Width, texture.Height);
        Raylib.DrawTextureRec(texture, source, position, tint ?? Color.White);
    }

    public void DrawTextureEx(
        TextureHandle handle,
        Vector2 position,
        float rotation,
        float scale,
        Rectangle? srcRect = null,
        Color? tint = null)
    {
        if (!TryGetTextureByHandle(handle, out Texture2D texture))
        {
            return;
        }

        Rectangle source = srcRect ?? new Rectangle(0f, 0f, texture.Width, texture.Height);
        float destWidth = MathF.Abs(source.Width) * scale;
        float destHeight = MathF.Abs(source.Height) * scale;
        Rectangle dest = new(position.X, position.Y, destWidth, destHeight);
        Vector2 pivot = new(destWidth * 0.5f, destHeight * 0.5f);

        Raylib.DrawTexturePro(texture, source, dest, pivot, rotation, tint ?? Color.White);
    }

    public void DrawTexture(string path, Vector2 pos, Rectangle? srcRect, Color tint)
    {
        if (!TryGetOrLoadTexture(path, out _, out Texture2D texture))
        {
            return;
        }

        Rectangle source = srcRect ?? new Rectangle(0f, 0f, texture.Width, texture.Height);
        Raylib.DrawTextureRec(texture, source, pos, tint);
    }

    public void DrawTextureEx(
        string path,
        Vector2 pos,
        float rotation,
        Vector2 scale,
        Color tint,
        Vector2? origin = null,
        Rectangle? srcRect = null)
    {
        if (!TryGetOrLoadTexture(path, out _, out Texture2D texture))
        {
            return;
        }

        Rectangle source = srcRect ?? new Rectangle(0f, 0f, texture.Width, texture.Height);
        float destWidth = MathF.Abs(source.Width) * scale.X;
        float destHeight = MathF.Abs(source.Height) * scale.Y;
        Rectangle dest = new(pos.X, pos.Y, destWidth, destHeight);
        Vector2 pivot = origin ?? new Vector2(destWidth * 0.5f, destHeight * 0.5f);

        Raylib.DrawTexturePro(texture, source, dest, pivot, rotation, tint);
    }

    public Vector2 GetTextureSize(string path)
    {
        return TryGetOrLoadTexture(path, out _, out Texture2D texture)
            ? new Vector2(texture.Width, texture.Height)
            : Vector2.Zero;
    }

    public void Clear(Color color)
    {
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        Raylib.ClearBackground(color);
    }

    public void DrawRectangle(int x, int y, int width, int height, Color color)
    {
        Raylib.DrawRectangle(x, y, width, height, color);
    }

    public void DrawRectangleLines(int x, int y, int width, int height, Color color)
    {
        Raylib.DrawRectangleLines(x, y, width, height, color);
    }

    public void DrawCircle(Vector2 center, float radius, Color color)
    {
        Raylib.DrawCircleV(center, radius, color);
    }

    public void DrawLine(Vector2 start, Vector2 end, float thickness, Color color)
    {
        Raylib.DrawLineEx(start, end, thickness, color);
    }

    public void DrawText(string text, int x, int y, int fontSize, Color color)
    {
        Raylib.DrawText(text, x, y, fontSize, color);
    }

    public void BeginCamera2D(Vector2 target, Vector2 offset, float rotation = 0f, float zoom = 1f)
    {
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        if (_displayService != null && _displayService.Snapping != PixelSnapping.Disabled)
        {
            target = _displayService.SnapCoordinates(target);
            offset = _displayService.SnapCoordinates(offset);
        }

        int targetDepth = _activeTargetStack.Count;
        if (_camera2DStack.TryPeek(out var current) && current.TargetDepth == targetDepth)
        {
            Raylib.EndMode2D();
        }

        var camera = new Camera2D
        {
            Target = target,
            Offset = offset,
            Rotation = rotation,
            Zoom = zoom
        };

        Raylib.BeginMode2D(camera);
        _camera2DStack.Push((targetDepth, camera));
    }

    public void EndCamera2D()
    {
        if (_camera2DStack.Count == 0)
        {
            return;
        }

        _camera2DStack.Pop();
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        Raylib.EndMode2D();
        int targetDepth = _activeTargetStack.Count;
        if (_camera2DStack.TryPeek(out var outer) && outer.TargetDepth == targetDepth)
        {
            Raylib.BeginMode2D(outer.Camera);
        }
    }

    public void BeginBlendMode(BlendMode mode)
    {
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        Raylib.BeginBlendMode(mode);
        _blendModeStack.Push(mode);
    }

    public void EndBlendMode()
    {
        if (_blendModeStack.Count == 0)
        {
            return;
        }

        _blendModeStack.Pop();
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        if (_blendModeStack.TryPeek(out BlendMode outer))
        {
            Raylib.BeginBlendMode(outer);
        }
        else
        {
            Raylib.EndBlendMode();
        }
    }

    public RenderTexture2D GetRenderTexture()
    {
        if (!_hasRenderTexture)
        {
            int w = _displayService != null ? Math.Max(64, (int)_displayService.VirtualResolution.X) : 960;
            int h = _displayService != null ? Math.Max(64, (int)_displayService.VirtualResolution.Y) : 540;
            EnsureRenderTexture(w, h);
        }

        return _renderTexture;
    }

    public void EnsureRenderTexture(int width, int height)
    {
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        int clampedW = Math.Max(1, width);
        int clampedH = Math.Max(1, height);

        if (_hasRenderTexture &&
            Raylib.IsRenderTextureValid(_renderTexture) &&
            _renderTexture.Texture.Width == clampedW &&
            _renderTexture.Texture.Height == clampedH)
        {
            return;
        }

        if (_hasRenderTexture && Raylib.IsRenderTextureValid(_renderTexture))
        {
            Raylib.UnloadRenderTexture(_renderTexture);
        }

        _renderTexture = Raylib.LoadRenderTexture(clampedW, clampedH);
        _hasRenderTexture = Raylib.IsRenderTextureValid(_renderTexture);
    }

    public void BeginRenderTarget()
    {
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        if (!_hasRenderTexture)
        {
            int w = _displayService != null ? Math.Max(64, (int)_displayService.VirtualResolution.X) : 960;
            int h = _displayService != null ? Math.Max(64, (int)_displayService.VirtualResolution.Y) : 540;
            EnsureRenderTexture(w, h);
        }

        if (_hasRenderTexture && !_isInRenderTarget)
        {
            _activeTargetStack.Clear();
            Raylib.BeginTextureMode(_renderTexture);
            _activeTargetStack.Push(_renderTexture);
            _isInRenderTarget = true;
        }
    }

    public void EndRenderTarget()
    {
        _quadBatchService?.EnsureEndedIfOpen();

        if (!Raylib.IsWindowReady())
        {
            _camera2DStack.Clear();
            _shaderStack.Clear();
            _blendModeStack.Clear();
            _activeTargetStack.Clear();
            _isInRenderTarget = false;
            return;
        }

        if (_camera2DStack.Count > 0)
        {
            Raylib.EndMode2D();
            _camera2DStack.Clear();
        }

        bool hasActiveShader = false;
        while (_shaderStack.Count > 0)
        {
            if (_shaderStack.Pop().HasValue)
            {
                hasActiveShader = true;
            }
        }
        if (hasActiveShader)
        {
            Raylib.EndShaderMode();
        }

        if (_blendModeStack.Count > 0)
        {
            Raylib.EndBlendMode();
            _blendModeStack.Clear();
        }

        if (_activeTargetStack.Count > 0 || _isInRenderTarget)
        {
            Raylib.EndTextureMode();
            _activeTargetStack.Clear();
            _isInRenderTarget = false;
        }
    }

    internal RenderTexture2D EnsureNamedSurface(string key, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!Raylib.IsWindowReady())
        {
            return default;
        }

        int clampedW = Math.Clamp(width, 1, 4096);
        int clampedH = Math.Clamp(height, 1, 4096);

        if (_surfaceCache.TryGetValue(key, out RenderTexture2D surface) &&
            Raylib.IsRenderTextureValid(surface) &&
            surface.Texture.Width == clampedW &&
            surface.Texture.Height == clampedH)
        {
            return surface;
        }

        if (Raylib.IsRenderTextureValid(surface))
        {
            Raylib.UnloadRenderTexture(surface);
        }

        surface = Raylib.LoadRenderTexture(clampedW, clampedH);
        if (Raylib.IsRenderTextureValid(surface))
        {
            _surfaceCache[key] = surface;
            return surface;
        }

        return default;
    }

    internal bool TryGetNamedSurface(string key, out RenderTexture2D surface)
    {
        if (_surfaceCache.TryGetValue(key, out surface) && Raylib.IsRenderTextureValid(surface))
        {
            return true;
        }

        surface = default;
        return false;
    }

    internal Vector2 GetActiveTargetSize()
    {
        if (_activeTargetStack.Count > 0)
        {
            RenderTexture2D top = _activeTargetStack.Peek();
            return new Vector2(top.Texture.Width, top.Texture.Height);
        }

        if (_hasRenderTexture && Raylib.IsRenderTextureValid(_renderTexture))
        {
            return new Vector2(_renderTexture.Texture.Width, _renderTexture.Texture.Height);
        }

        return Raylib.IsWindowReady()
            ? new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())
            : new Vector2(960f, 540f);
    }

    internal void BeginNamedTarget(string key, Color? clearColor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        if (!TryGetNamedSurface(key, out RenderTexture2D surface))
        {
            int defaultW = _displayService != null ? (int)_displayService.VirtualResolution.X : 960;
            int defaultH = _displayService != null ? (int)_displayService.VirtualResolution.Y : 540;
            surface = EnsureNamedSurface(key, defaultW, defaultH);
        }

        if (!Raylib.IsRenderTextureValid(surface))
        {
            return;
        }

        BeginSurfaceWithExisting(surface);
        if (clearColor.HasValue)
        {
            Raylib.ClearBackground(clearColor.Value);
        }
    }

    internal void EndNamedTarget()
    {
        EndSurface();
    }

    internal void EnsureBatchEndedIfOpen()
    {
        _quadBatchService?.EnsureEndedIfOpen();
    }

    internal Camera2D? SuspendActiveCameraForCurrentTarget()
    {
        if (!Raylib.IsWindowReady() || _camera2DStack.Count == 0)
        {
            return null;
        }

        var peek = _camera2DStack.Peek();
        if (peek.TargetDepth != _activeTargetStack.Count)
        {
            return null;
        }

        Raylib.EndMode2D();
        return peek.Camera;
    }

    internal void RestoreSuspendedCamera(Camera2D? camera)
    {
        if (camera.HasValue && Raylib.IsWindowReady())
        {
            Raylib.BeginMode2D(camera.Value);
        }
    }

    public void BeginSurface(string key, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!Raylib.IsWindowReady())
        {
            return;
        }

        RenderTexture2D surface = EnsureNamedSurface(key, width, height);
        if (!Raylib.IsRenderTextureValid(surface))
        {
            return;
        }

        BeginSurfaceWithExisting(surface);
    }

    private void BeginSurfaceWithExisting(RenderTexture2D surface)
    {
        _quadBatchService?.EnsureEndedIfOpen();

        if (_activeTargetStack.Count > 0)
        {
            if (_camera2DStack.TryPeek(out var currentCam) && currentCam.TargetDepth == _activeTargetStack.Count)
            {
                Raylib.EndMode2D();
            }

            Raylib.EndTextureMode();
        }

        Raylib.BeginTextureMode(surface);
        _activeTargetStack.Push(surface);
    }

    public void EndSurface()
    {
        _quadBatchService?.EnsureEndedIfOpen();

        if (_activeTargetStack.Count == 0)
        {
            return;
        }

        int leavingDepth = _activeTargetStack.Count;
        bool hadCameraInSurface = false;
        while (_camera2DStack.Count > 0 && _camera2DStack.Peek().TargetDepth >= leavingDepth)
        {
            _camera2DStack.Pop();
            hadCameraInSurface = true;
        }

        if (hadCameraInSurface && Raylib.IsWindowReady())
        {
            Raylib.EndMode2D();
        }

        Raylib.EndTextureMode();
        _activeTargetStack.Pop();

        if (_activeTargetStack.Count > 0)
        {
            Raylib.BeginTextureMode(_activeTargetStack.Peek());
            if (_camera2DStack.TryPeek(out var restoredCam) && restoredCam.TargetDepth == _activeTargetStack.Count)
            {
                Raylib.BeginMode2D(restoredCam.Camera);
            }
        }
    }

    public void DrawSurface(
        string key,
        Vector2 pos,
        Vector2 size,
        Color tint,
        float rotation = 0f,
        Vector2? origin = null,
        Rectangle? srcRect = null)
    {
        if (!_surfaceCache.TryGetValue(key, out RenderTexture2D surface) || !Raylib.IsRenderTextureValid(surface))
        {
            return;
        }

        // Raylib FBO textures are stored inverted vertically in OpenGL; flip height when using full surface.
        Rectangle source = srcRect ?? new Rectangle(0f, 0f, surface.Texture.Width, -surface.Texture.Height);
        float destW = size.X > 0f ? size.X : surface.Texture.Width;
        float destH = size.Y > 0f ? size.Y : surface.Texture.Height;
        Rectangle dest = new(pos.X, pos.Y, destW, destH);
        Vector2 pivot = origin ?? new Vector2(destW * 0.5f, destH * 0.5f);

        Raylib.DrawTexturePro(surface.Texture, source, dest, pivot, rotation, tint);
    }

    public Vector2 GetSurfaceSize(string key)
    {
        return _surfaceCache.TryGetValue(key, out RenderTexture2D surface) && Raylib.IsRenderTextureValid(surface)
            ? new Vector2(surface.Texture.Width, surface.Texture.Height)
            : Vector2.Zero;
    }

    public void ReleaseSurface(string key)
    {
        if (_surfaceCache.Remove(key, out RenderTexture2D surface) && Raylib.IsRenderTextureValid(surface))
        {
            Raylib.UnloadRenderTexture(surface);
        }
    }

    public void BeginShader(string fragmentShaderPath, string? vertexShaderPath = null)
    {
        if (string.IsNullOrWhiteSpace(fragmentShaderPath) || !TryGetOrLoadShader(fragmentShaderPath, vertexShaderPath, out Shader shader))
        {
            _shaderStack.Push(null);
            return;
        }

        if (_shaderStack.Count > 0 && _shaderStack.Peek().HasValue)
        {
            Raylib.EndShaderMode();
        }

        Raylib.BeginShaderMode(shader);
        _shaderStack.Push(shader);
    }

    public void EndShader()
    {
        if (_shaderStack.Count == 0)
        {
            return;
        }

        Shader? popped = _shaderStack.Pop();
        if (popped.HasValue)
        {
            Raylib.EndShaderMode();
        }

        if (_shaderStack.TryPeek(out Shader? outer) && outer.HasValue)
        {
            Raylib.BeginShaderMode(outer.Value);
        }
    }

    public void SetShaderFloat(string fragmentShaderPath, string uniformName, float value)
    {
        if (!TryGetOrLoadShader(fragmentShaderPath, null, out Shader shader))
        {
            return;
        }

        int loc = GetCachedShaderLocation(shader, uniformName);
        if (loc >= 0)
        {
            Raylib.SetShaderValue(shader, loc, value, ShaderUniformDataType.Float);
        }
    }

    public void SetShaderVec2(string fragmentShaderPath, string uniformName, Vector2 value)
    {
        if (!TryGetOrLoadShader(fragmentShaderPath, null, out Shader shader))
        {
            return;
        }

        int loc = GetCachedShaderLocation(shader, uniformName);
        if (loc >= 0)
        {
            Raylib.SetShaderValue(shader, loc, value, ShaderUniformDataType.Vec2);
        }
    }

    public void SetShaderColor(string fragmentShaderPath, string uniformName, Color value)
    {
        if (!TryGetOrLoadShader(fragmentShaderPath, null, out Shader shader))
        {
            return;
        }

        int loc = GetCachedShaderLocation(shader, uniformName);
        if (loc >= 0)
        {
            Vector4 vec = new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
            Raylib.SetShaderValue(shader, loc, vec, ShaderUniformDataType.Vec4);
        }
    }

    public void SetShaderSurface(string fragmentShaderPath, string uniformName, string surfaceName)
    {
        if (!TryGetOrLoadShader(fragmentShaderPath, null, out Shader shader) ||
            !TryGetNamedSurface(surfaceName, out RenderTexture2D surface))
        {
            return;
        }

        int loc = GetCachedShaderLocation(shader, uniformName);
        if (loc >= 0)
        {
            Raylib.SetShaderValueTexture(shader, loc, surface.Texture);
        }
    }

    public void SetShaderTexture(string fragmentShaderPath, string uniformName, TextureHandle textureHandle)
    {
        if (!TryGetOrLoadShader(fragmentShaderPath, null, out Shader shader) ||
            !TryGetTextureByHandle(textureHandle, out Texture2D texture))
        {
            return;
        }

        int loc = GetCachedShaderLocation(shader, uniformName);
        if (loc >= 0)
        {
            Raylib.SetShaderValueTexture(shader, loc, texture);
        }
    }

    public void InvalidateShader(string path)
    {
        string resolved = _workspace.ResolvePath(path);
        _invalidShadersLogged.Remove(resolved);
        _shaderByRawPath.Clear();

        if (_shaderCache.Remove(resolved, out Shader shader) && Raylib.IsShaderValid(shader))
        {
            RemoveCachedLocationsForShader(shader.Id);
            Raylib.UnloadShader(shader);
            _eventBus.Publish(new EngineLogEvent("Graphics", "Info", $"Shader invalidated for reload: {_workspace.ToRelativePath(resolved)}"));
        }
    }

    public void InvalidateTexture(string path)
    {
        string resolved = _workspace.ResolvePath(path);
        _missingLogged.Remove(resolved);

        if (_resolvedPathToHandleId.TryGetValue(resolved, out int handleId) &&
            (uint)handleId < (uint)_texturesByHandle.Count &&
            handleId > 0)
        {
            Texture2D texture = _texturesByHandle[handleId];
            _texturesByHandle[handleId] = default;

            if (Raylib.IsTextureValid(texture))
            {
                Raylib.UnloadTexture(texture);
                _eventBus.Publish(new EngineLogEvent("Graphics", "Info", $"Texture invalidated for reload: {_workspace.ToRelativePath(resolved)}"));
            }
        }
    }

    public void InvalidateAllTextures()
    {
        for (int i = 1; i < _texturesByHandle.Count; i++)
        {
            Texture2D texture = _texturesByHandle[i];
            if (Raylib.IsTextureValid(texture))
            {
                Raylib.UnloadTexture(texture);
            }
        }

        ResetTextureCacheState();
    }

    private int GetCachedShaderLocation(Shader shader, string uniformName)
    {
        var key = (shader.Id, uniformName);
        if (_shaderLocationCache.TryGetValue(key, out int loc))
        {
            return loc;
        }

        loc = Raylib.GetShaderLocation(shader, uniformName);
        _shaderLocationCache[key] = loc;
        return loc;
    }

    private void RemoveCachedLocationsForShader(uint shaderId)
    {
        List<(uint ShaderId, string UniformName)>? keysToRemove = null;
        foreach (var key in _shaderLocationCache.Keys)
        {
            if (key.ShaderId == shaderId)
            {
                keysToRemove ??= new List<(uint, string)>();
                keysToRemove.Add(key);
            }
        }

        if (keysToRemove != null)
        {
            foreach (var key in keysToRemove)
            {
                _shaderLocationCache.Remove(key);
            }
        }
    }

    private void ResetTextureCacheState()
    {
        _texturesByHandle.Clear();
        _texturesByHandle.Add(default);
        _handleResolvedPaths.Clear();
        _handleResolvedPaths.Add(string.Empty);
        _resolvedPathToHandleId.Clear();
        _missingLogged.Clear();
    }

    private bool TryGetTextureByHandle(TextureHandle handle, out Texture2D texture)
    {
        int id = handle.Id;
        if (id <= 0 || (uint)id >= (uint)_texturesByHandle.Count)
        {
            texture = default;
            return false;
        }

        texture = _texturesByHandle[id];
        if (texture.Id != 0)
        {
            return true;
        }

        return TryReloadHandle(id, out texture);
    }

    private bool TryReloadHandle(int handleId, out Texture2D texture)
    {
        if (!Raylib.IsWindowReady() || (uint)handleId >= (uint)_handleResolvedPaths.Count)
        {
            texture = default;
            return false;
        }

        string resolved = _handleResolvedPaths[handleId];
        if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
        {
            if (!string.IsNullOrEmpty(resolved) && _missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Graphics", "Warning", $"Texture file not found: {resolved}"));
            }

            texture = default;
            return false;
        }

        texture = LoadTextureSmart(resolved);
        if (!Raylib.IsTextureValid(texture))
        {
            if (_missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Graphics", "Warning", $"Failed to load texture: {resolved}"));
            }

            return false;
        }

        _missingLogged.Remove(resolved);
        _texturesByHandle[handleId] = texture;
        return true;
    }

    private bool TryGetOrLoadTexture(string path, out int handleId, out Texture2D texture)
    {
        if (!Raylib.IsWindowReady())
        {
            handleId = 0;
            texture = default;
            return false;
        }

        string resolved = _workspace.ResolvePath(path);
        bool hasSlot = _resolvedPathToHandleId.TryGetValue(resolved, out int existingId);
        if (hasSlot && (uint)existingId < (uint)_texturesByHandle.Count && existingId > 0)
        {
            texture = _texturesByHandle[existingId];
            if (Raylib.IsTextureValid(texture))
            {
                handleId = existingId;
                return true;
            }
        }

        if (!File.Exists(resolved))
        {
            if (_missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Graphics", "Warning", $"Texture file not found: {resolved}"));
            }

            handleId = 0;
            texture = default;
            return false;
        }

        texture = LoadTextureSmart(resolved);
        if (!Raylib.IsTextureValid(texture))
        {
            if (_missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Graphics", "Warning", $"Failed to load texture: {resolved}"));
            }

            handleId = 0;
            return false;
        }

        _missingLogged.Remove(resolved);
        if (hasSlot && (uint)existingId < (uint)_texturesByHandle.Count && existingId > 0)
        {
            _texturesByHandle[existingId] = texture;
            handleId = existingId;
        }
        else
        {
            handleId = _texturesByHandle.Count;
            _texturesByHandle.Add(texture);
            _handleResolvedPaths.Add(resolved);
            _resolvedPathToHandleId[resolved] = handleId;
        }

        return true;
    }

    private static Texture2D LoadTextureSmart(string resolvedPath)
    {
        try
        {
            byte[] data = File.ReadAllBytes(resolvedPath);
            string? detectedExt = DetectImageExtension(data);
            if (detectedExt != null)
            {
                Image image = Raylib.LoadImageFromMemory(detectedExt, data);
                if (Raylib.IsImageValid(image))
                {
                    Texture2D tex = Raylib.LoadTextureFromImage(image);
                    Raylib.UnloadImage(image);
                    if (Raylib.IsTextureValid(tex))
                    {
                        return tex;
                    }
                }
            }
        }
        catch (IOException)
        {
            // Fallback to Raylib file loader below.
        }

        return Raylib.LoadTexture(resolvedPath);
    }

    private bool TryGetOrLoadShader(string fragmentShaderPath, string? vertexShaderPath, out Shader shader)
    {
        if (!Raylib.IsWindowReady())
        {
            shader = default;
            return false;
        }

        if (string.IsNullOrWhiteSpace(vertexShaderPath) &&
            _shaderByRawPath.TryGetValue(fragmentShaderPath, out shader) &&
            Raylib.IsShaderValid(shader))
        {
            return true;
        }

        string resolvedFs = _workspace.ResolvePath(fragmentShaderPath);
        if (_shaderCache.TryGetValue(resolvedFs, out shader) && Raylib.IsShaderValid(shader))
        {
            if (string.IsNullOrWhiteSpace(vertexShaderPath))
            {
                _shaderByRawPath[fragmentShaderPath] = shader;
            }

            return true;
        }

        if (!File.Exists(resolvedFs))
        {
            if (_invalidShadersLogged.Add(resolvedFs))
            {
                _eventBus.Publish(new EngineLogEvent("Graphics", "Warning", $"Shader file not found: {resolvedFs}"));
            }

            shader = default;
            return false;
        }

        string? resolvedVs = string.IsNullOrWhiteSpace(vertexShaderPath)
            ? null
            : _workspace.ResolvePath(vertexShaderPath);

        shader = Raylib.LoadShader(resolvedVs!, resolvedFs);
        if (!Raylib.IsShaderValid(shader))
        {
            if (_invalidShadersLogged.Add(resolvedFs))
            {
                _eventBus.Publish(new EngineLogEvent("Graphics", "Warning", $"Failed to compile shader: {resolvedFs}"));
            }

            return false;
        }

        _invalidShadersLogged.Remove(resolvedFs);
        _shaderCache[resolvedFs] = shader;
        if (string.IsNullOrWhiteSpace(vertexShaderPath))
        {
            _shaderByRawPath[fragmentShaderPath] = shader;
        }

        return true;
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return ".png";
        }

        if (data.Length >= 3 &&
            data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ".jpg";
        }

        if (data.Length >= 2 &&
            data[0] == 0x42 && data[1] == 0x4D)
        {
            return ".bmp";
        }

        if (data.Length >= 4 &&
            data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
        {
            return ".gif";
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!Raylib.IsWindowReady())
        {
            _activeTargetStack.Clear();
            _camera2DStack.Clear();
            _shaderStack.Clear();
            _blendModeStack.Clear();
            ResetTextureCacheState();
            _surfaceCache.Clear();
            _shaderCache.Clear();
            _shaderByRawPath.Clear();
            _shaderLocationCache.Clear();
            _hasRenderTexture = false;
            return;
        }

        EndRenderTarget();
        InvalidateAllTextures();

        foreach (RenderTexture2D surface in _surfaceCache.Values)
        {
            if (Raylib.IsRenderTextureValid(surface))
            {
                Raylib.UnloadRenderTexture(surface);
            }
        }
        _surfaceCache.Clear();

        foreach (Shader shader in _shaderCache.Values)
        {
            if (Raylib.IsShaderValid(shader))
            {
                Raylib.UnloadShader(shader);
            }
        }
        _shaderCache.Clear();
        _shaderByRawPath.Clear();
        _shaderLocationCache.Clear();
        _invalidShadersLogged.Clear();

        if (_hasRenderTexture && Raylib.IsRenderTextureValid(_renderTexture))
        {
            Raylib.UnloadRenderTexture(_renderTexture);
            _hasRenderTexture = false;
        }
    }
}
