using System.Numerics;
using Raylib_cs;

namespace Engine.Core.Abstractions;

public readonly record struct TextureHandle(int Id)
{
    public static TextureHandle Invalid => default;
    public bool IsValid => Id > 0;
}

public interface IGraphicsService
{
    TextureHandle GetTextureHandle(string assetPath);

    void DrawTexture(TextureHandle handle, Vector2 position, Rectangle? srcRect = null, Color? tint = null);

    void DrawTextureEx(
        TextureHandle handle,
        Vector2 position,
        float rotation,
        float scale,
        Rectangle? srcRect = null,
        Color? tint = null);

    void DrawTexture(string path, Vector2 pos, Rectangle? srcRect, Color tint);

    void DrawTextureEx(
        string path,
        Vector2 pos,
        float rotation,
        Vector2 scale,
        Color tint,
        Vector2? origin = null,
        Rectangle? srcRect = null);

    Vector2 GetTextureSize(string path);
    void Clear(Color color);
    void DrawRectangle(int x, int y, int width, int height, Color color);
    void DrawRectangleLines(int x, int y, int width, int height, Color color);
    void DrawCircle(Vector2 center, float radius, Color color);
    void DrawLine(Vector2 start, Vector2 end, float thickness, Color color);
    void DrawText(string text, int x, int y, int fontSize, Color color);

    void BeginCamera2D(Vector2 target, Vector2 offset, float rotation = 0f, float zoom = 1f);
    void EndCamera2D();

    void BeginBlendMode(BlendMode mode);
    void EndBlendMode();

    RenderTexture2D GetRenderTexture();
    void EnsureRenderTexture(int width, int height);
    void BeginRenderTarget();
    void EndRenderTarget();

    // Named offscreen surfaces (FBOs) for multi-pass / composite rendering
    void BeginSurface(string key, int width, int height);
    void EndSurface();
    void DrawSurface(
        string key,
        Vector2 pos,
        Vector2 size,
        Color tint,
        float rotation = 0f,
        Vector2? origin = null,
        Rectangle? srcRect = null);
    Vector2 GetSurfaceSize(string key);
    void ReleaseSurface(string key);

    // 2D Fragment shaders loaded from workspace files
    void BeginShader(string fragmentShaderPath, string? vertexShaderPath = null);
    void EndShader();
    void SetShaderFloat(string fragmentShaderPath, string uniformName, float value);
    void SetShaderVec2(string fragmentShaderPath, string uniformName, Vector2 value);
    void SetShaderColor(string fragmentShaderPath, string uniformName, Color value);
    void SetShaderSurface(string fragmentShaderPath, string uniformName, string surfaceName) { }
    void SetShaderTexture(string fragmentShaderPath, string uniformName, TextureHandle texture) { }
    void InvalidateShader(string path);

    void InvalidateTexture(string path);
    void InvalidateAllTextures();
}
