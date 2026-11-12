using System.Numerics;
using Engine.Core.Abstractions;
using Raylib_cs;

namespace Engine.Core.Graphics;

public sealed class SurfaceCompositor : ISurfaceCompositor
{
    private readonly RaylibGraphicsService _graphics;

    public SurfaceCompositor(RaylibGraphicsService graphics)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
    }

    public void EnsureSurface(string surfaceName, int width, int height)
    {
        _graphics.EnsureNamedSurface(surfaceName, width, height);
    }

    public void BeginTarget(string surfaceName, Color? clearColor = null)
    {
        _graphics.BeginNamedTarget(surfaceName, clearColor);
    }

    public void EndTarget()
    {
        _graphics.EndNamedTarget();
    }

    public void ExecutePass(PassDescriptor pass)
    {
        if (!Raylib.IsWindowReady() || string.IsNullOrWhiteSpace(pass.SourceSurface))
        {
            return;
        }

        if (!_graphics.TryGetNamedSurface(pass.SourceSurface, out RenderTexture2D source))
        {
            return;
        }

        _graphics.EnsureBatchEndedIfOpen();

        bool hasNamedTarget = !string.IsNullOrWhiteSpace(pass.TargetSurface);
        Camera2D? suspendedCam = null;
        if (hasNamedTarget)
        {
            if (!_graphics.TryGetNamedSurface(pass.TargetSurface, out _))
            {
                _graphics.EnsureNamedSurface(pass.TargetSurface, source.Texture.Width, source.Texture.Height);
            }

            _graphics.BeginNamedTarget(pass.TargetSurface);
        }
        else
        {
            suspendedCam = _graphics.SuspendActiveCameraForCurrentTarget();
        }

        Vector2 targetSize = _graphics.GetActiveTargetSize();
        float destW = MathF.Max(1f, targetSize.X);
        float destH = MathF.Max(1f, targetSize.Y);

        _graphics.BeginBlendMode(pass.BlendMode);

        bool hasShader = !string.IsNullOrWhiteSpace(pass.ShaderPath);
        if (hasShader)
        {
            _graphics.BeginShader(pass.ShaderPath);
        }

        pass.SetupUniforms?.Invoke(_graphics);

        // Raylib offscreen FBO textures have bottom-left origin in OpenGL; flip height when blitting.
        Rectangle srcRect = new(0f, 0f, source.Texture.Width, -source.Texture.Height);
        Rectangle dstRect = new(0f, 0f, destW, destH);
        Raylib.DrawTexturePro(source.Texture, srcRect, dstRect, Vector2.Zero, 0f, Color.White);

        if (hasShader)
        {
            _graphics.EndShader();
        }

        _graphics.EndBlendMode();

        if (hasNamedTarget)
        {
            _graphics.EndNamedTarget();
        }
        else
        {
            _graphics.RestoreSuspendedCamera(suspendedCam);
        }
    }

    public void PresentToScreen(string surfaceName, Vector2 position, Vector2 size)
    {
        if (!Raylib.IsWindowReady() || string.IsNullOrWhiteSpace(surfaceName))
        {
            return;
        }

        _graphics.EnsureBatchEndedIfOpen();

        if (!_graphics.TryGetNamedSurface(surfaceName, out RenderTexture2D surface))
        {
            return;
        }

        Rectangle srcRect = new(0f, 0f, surface.Texture.Width, -surface.Texture.Height);
        float destW = size.X > 0f ? size.X : surface.Texture.Width;
        float destH = size.Y > 0f ? size.Y : surface.Texture.Height;
        Rectangle dstRect = new(position.X, position.Y, destW, destH);

        Raylib.DrawTexturePro(surface.Texture, srcRect, dstRect, Vector2.Zero, 0f, Color.White);
    }
}
