using System.Numerics;
using Raylib_cs;

namespace Engine.Core.Abstractions;

public interface IInputService
{
    Vector2 ScreenMousePosition { get; }
    Vector2 VirtualMousePosition { get; set; }
    bool IsMouseInViewport { get; set; }
    bool IsMouseButtonPressed(MouseButton button);
    bool IsMouseButtonDown(MouseButton button);
    bool IsKeyPressed(KeyboardKey key);
    bool IsKeyDown(KeyboardKey key);
}
