using System.Numerics;

namespace Engine.Core.Abstractions;

public enum ScalingMode
{
    None,            // 1:1 native pixel presentation inside viewport
    Stretch,         // Fill target viewport bounds regardless of aspect ratio
    FitLetterbox,    // Preserve aspect ratio with centered letterboxing
    IntegerScale     // Strictly integer magnification factors (x1, x2, x3, x4...)
}

public enum PixelSnapping
{
    Disabled,        // Continuous floating-point coordinates
    Floor,           // Truncate toward negative infinity to virtual grid
    Round            // Round to nearest virtual grid pixel
}

public interface IVirtualDisplayService
{
    Vector2 VirtualResolution { get; set; }
    ScalingMode ScalingMode { get; set; }
    PixelSnapping Snapping { get; set; }

    /// <summary>
    /// Snaps world coordinates according to the active snapping policy.
    /// </summary>
    Vector2 SnapCoordinates(Vector2 worldPos);

    /// <summary>
    /// Converts viewport screen coordinates into virtual canvas coordinates.
    /// </summary>
    Vector2 ScreenToVirtual(Vector2 screenPos);

    /// <summary>
    /// Computes the 2D presentation transform mapping VirtualResolution into targetViewportSize.
    /// </summary>
    Matrix4x4 GetPresentationMatrix(Vector2 targetViewportSize);
}
