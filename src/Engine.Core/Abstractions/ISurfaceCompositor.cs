using System.Numerics;
using Raylib_cs;

namespace Engine.Core.Abstractions;

public readonly record struct PassDescriptor(
    string SourceSurface,
    string TargetSurface,
    string ShaderPath,
    BlendMode BlendMode,
    Action<IGraphicsService>? SetupUniforms = null
);

public interface ISurfaceCompositor
{
    /// <summary>
    /// Creates or reuses an offscreen render surface with the specified fixed dimensions.
    /// </summary>
    void EnsureSurface(string surfaceName, int width, int height);

    /// <summary>
    /// Directs subsequent draw commands into the named offscreen surface.
    /// </summary>
    void BeginTarget(string surfaceName, Color? clearColor = null);

    void EndTarget();

    /// <summary>
    /// Executes a fullscreen blit pass from SourceSurface into TargetSurface with blend state and shader uniforms.
    /// </summary>
    void ExecutePass(PassDescriptor pass);

    /// <summary>
    /// Presents a composited surface into the active render context.
    /// </summary>
    void PresentToScreen(string surfaceName, Vector2 position, Vector2 size);
}
