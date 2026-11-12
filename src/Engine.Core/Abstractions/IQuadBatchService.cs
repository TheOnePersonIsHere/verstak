using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Engine.Core.Abstractions;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct BatchVertex(
    Vector2 Position,
    Vector2 TexCoord,
    Color Tint
);

public interface IQuadBatchService
{
    /// <summary>
    /// Opens a draw batch for a specific texture handle, blend mode, and optional shader.
    /// </summary>
    void Begin(TextureHandle texture, BlendMode blendMode = BlendMode.Alpha, string? shaderPath = null);

    /// <summary>
    /// Appends an axis-aligned quad into the preallocated vertex buffer.
    /// </summary>
    void DrawQuad(Vector2 position, Vector2 size, Rectangle srcRect, Color tint);

    /// <summary>
    /// Appends a rotated quad into the preallocated vertex buffer around the local pivot origin.
    /// </summary>
    void DrawQuadRotated(Vector2 position, Vector2 size, Rectangle srcRect, float rotationDeg, Vector2 origin, Color tint);

    /// <summary>
    /// Flushes accumulated geometry to the GPU in a single draw call and closes the batch.
    /// </summary>
    void End();

    /// <summary>
    /// Forces an immediate GPU flush without closing the active batch.
    /// </summary>
    void Flush();
}

public interface IQuadBatchStats
{
    int CurrentFrameDrawCalls { get; }
    int CurrentFrameQuads { get; }
    int LastFrameDrawCalls { get; }
    int LastFrameQuads { get; }
    void BeginFrame();
    void EndFrame();
}
