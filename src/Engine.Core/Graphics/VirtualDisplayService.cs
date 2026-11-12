using System.Numerics;
using Engine.Core.Abstractions;

namespace Engine.Core.Graphics;

public sealed class VirtualDisplayService : IVirtualDisplayService
{
    private Vector2 _virtualResolution = new(1280f, 720f);
    private Vector2 _viewportScreenOrigin = Vector2.Zero;
    private Vector2 _viewportScreenSize = new(1280f, 720f);
    private bool _hasExplicitViewportBounds;

    public Vector2 VirtualResolution
    {
        get => _virtualResolution;
        set => _virtualResolution = new Vector2(MathF.Max(1f, value.X), MathF.Max(1f, value.Y));
    }

    public ScalingMode ScalingMode { get; set; } = ScalingMode.FitLetterbox;
    public PixelSnapping Snapping { get; set; } = PixelSnapping.Disabled;

    public Vector2 SnapCoordinates(Vector2 worldPos)
    {
        return Snapping switch
        {
            PixelSnapping.Floor => new Vector2(MathF.Floor(worldPos.X), MathF.Floor(worldPos.Y)),
            PixelSnapping.Round => new Vector2(MathF.Round(worldPos.X), MathF.Round(worldPos.Y)),
            _ => worldPos
        };
    }

    public void SetViewportBounds(Vector2 screenOrigin, Vector2 viewportSize)
    {
        _viewportScreenOrigin = screenOrigin;
        _viewportScreenSize = new Vector2(MathF.Max(1f, viewportSize.X), MathF.Max(1f, viewportSize.Y));
        _hasExplicitViewportBounds = true;
    }

    public void CalculatePresentationRect(Vector2 targetViewportSize, out Vector2 offset, out Vector2 destSize)
    {
        float vw = _virtualResolution.X;
        float vh = _virtualResolution.Y;
        float vpW = MathF.Max(1f, targetViewportSize.X);
        float vpH = MathF.Max(1f, targetViewportSize.Y);

        switch (ScalingMode)
        {
            case ScalingMode.None:
                destSize = new Vector2(vw, vh);
                offset = new Vector2(MathF.Floor((vpW - vw) * 0.5f), MathF.Floor((vpH - vh) * 0.5f));
                break;

            case ScalingMode.Stretch:
                destSize = new Vector2(vpW, vpH);
                offset = Vector2.Zero;
                break;

            case ScalingMode.IntegerScale:
            {
                float rawScale = MathF.Min(vpW / vw, vpH / vh);
                int factor = Math.Max(1, (int)MathF.Floor(rawScale));
                destSize = new Vector2(vw * factor, vh * factor);
                offset = new Vector2(
                    MathF.Floor((vpW - destSize.X) * 0.5f),
                    MathF.Floor((vpH - destSize.Y) * 0.5f));
                break;
            }

            case ScalingMode.FitLetterbox:
            default:
            {
                float scale = MathF.Min(vpW / vw, vpH / vh);
                destSize = new Vector2(vw * scale, vh * scale);
                offset = new Vector2((vpW - destSize.X) * 0.5f, (vpH - destSize.Y) * 0.5f);
                break;
            }
        }
    }

    public Vector2 ScreenToVirtual(Vector2 screenPos)
    {
        Vector2 targetSize = _hasExplicitViewportBounds ? _viewportScreenSize : _virtualResolution;
        CalculatePresentationRect(targetSize, out Vector2 offset, out Vector2 destSize);

        float originX = (_hasExplicitViewportBounds ? _viewportScreenOrigin.X : 0f) + offset.X;
        float originY = (_hasExplicitViewportBounds ? _viewportScreenOrigin.Y : 0f) + offset.Y;

        float vx = ((screenPos.X - originX) / MathF.Max(1e-5f, destSize.X)) * _virtualResolution.X;
        float vy = ((screenPos.Y - originY) / MathF.Max(1e-5f, destSize.Y)) * _virtualResolution.Y;
        return new Vector2(vx, vy);
    }

    public Matrix4x4 GetPresentationMatrix(Vector2 targetViewportSize)
    {
        if (!_hasExplicitViewportBounds)
        {
            _viewportScreenOrigin = Vector2.Zero;
            _viewportScreenSize = new Vector2(
                MathF.Max(1f, targetViewportSize.X),
                MathF.Max(1f, targetViewportSize.Y));
        }

        CalculatePresentationRect(targetViewportSize, out Vector2 offset, out Vector2 destSize);
        float sx = destSize.X / _virtualResolution.X;
        float sy = destSize.Y / _virtualResolution.Y;

        Matrix4x4 matrix = Matrix4x4.Identity;
        matrix.M11 = sx;
        matrix.M22 = sy;
        matrix.M41 = offset.X;
        matrix.M42 = offset.Y;
        return matrix;
    }
}
