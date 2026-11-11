using System.Numerics;
using Engine.Core.Abstractions;
using Raylib_cs;

namespace Engine.Core.Plugins;

public sealed class RaylibInputService : IInputService
{
    private static readonly KeyboardKey[] ModifierKeys =
    [
        KeyboardKey.LeftShift,
        KeyboardKey.RightShift,
        KeyboardKey.LeftControl,
        KeyboardKey.RightControl,
        KeyboardKey.LeftAlt,
        KeyboardKey.RightAlt,
        KeyboardKey.LeftSuper,
        KeyboardKey.RightSuper
    ];

    private static readonly MouseButton[] TrackedMouseButtons =
    [
        MouseButton.Left,
        MouseButton.Right,
        MouseButton.Middle
    ];

    private readonly HashSet<KeyboardKey> _staleKeys = new();
    private readonly HashSet<MouseButton> _staleMouseButtons = new();
    private readonly HashSet<MouseButton> _pressedMouseButtonsThisFrame = new();
    private readonly bool[] _prevOsMouseDown = new bool[3];

    private bool _wasFocused = true;
    private bool _hasOsState;
    private MacOsInputState _osState;

    public Vector2 ScreenMousePosition => Raylib.GetMousePosition();

    public Vector2 VirtualMousePosition { get; set; }

    public bool IsMouseInViewport { get; set; }

    public bool HasOsModifierState => _hasOsState;
    public bool IsOsShiftDown => _hasOsState && _osState.ShiftDown;
    public bool IsOsControlDown => _hasOsState && _osState.ControlDown;
    public bool IsOsAltDown => _hasOsState && _osState.AltDown;
    public bool IsOsSuperDown => _hasOsState && _osState.SuperDown;

    public void SyncFrameState()
    {
        bool focused = Raylib.IsWindowFocused();
        _hasOsState = OperatingSystem.IsMacOS() && MacOsInputBridge.TryGetState(out _osState);
        _pressedMouseButtonsThisFrame.Clear();

        if (!focused)
        {
            foreach (KeyboardKey mod in ModifierKeys)
            {
                if (Raylib.IsKeyDown(mod))
                {
                    _staleKeys.Add(mod);
                }
            }

            foreach (MouseButton btn in TrackedMouseButtons)
            {
                if (Raylib.IsMouseButtonDown(btn))
                {
                    _staleMouseButtons.Add(btn);
                }

                _prevOsMouseDown[(int)btn] = _hasOsState && IsOsMouseButtonDown(btn);
            }

            _wasFocused = false;
            return;
        }

        // Clear any stale non-modifier keys once Raylib confirms release or re-press.
        foreach (KeyboardKey staleKey in _staleKeys.ToArray())
        {
            if (!IsModifierKey(staleKey) && (Raylib.IsKeyPressed(staleKey) || !Raylib.IsKeyDown(staleKey)))
            {
                _staleKeys.Remove(staleKey);
            }
        }

        foreach (KeyboardKey mod in ModifierKeys)
        {
            if (_hasOsState)
            {
                if (IsOsModifierDown(mod))
                {
                    _staleKeys.Remove(mod);
                }
                else if (Raylib.IsKeyDown(mod))
                {
                    _staleKeys.Add(mod);
                }
                else
                {
                    _staleKeys.Remove(mod);
                }

                continue;
            }

            if (Raylib.IsKeyPressed(mod) || !Raylib.IsKeyDown(mod))
            {
                _staleKeys.Remove(mod);
            }
            else if (!_wasFocused)
            {
                _staleKeys.Add(mod);
            }
        }

        foreach (MouseButton btn in TrackedMouseButtons)
        {
            int idx = (int)btn;
            bool osDown = _hasOsState && IsOsMouseButtonDown(btn);
            bool prevOsDown = _prevOsMouseDown[idx];
            _prevOsMouseDown[idx] = osDown;

            if (Raylib.IsMouseButtonPressed(btn))
            {
                _staleMouseButtons.Remove(btn);
                _pressedMouseButtonsThisFrame.Add(btn);
                continue;
            }

            // If GLFW remained stuck down after a missed mouse-up and user clicks again physically, recover.
            if (_hasOsState && _staleMouseButtons.Contains(btn) && osDown && !prevOsDown)
            {
                _staleMouseButtons.Remove(btn);
                _pressedMouseButtonsThisFrame.Add(btn);
                continue;
            }

            if (!Raylib.IsMouseButtonDown(btn))
            {
                _staleMouseButtons.Remove(btn);
                continue;
            }

            if (_hasOsState && !osDown)
            {
                _staleMouseButtons.Add(btn);
            }
            else if (!_wasFocused && !_hasOsState)
            {
                _staleMouseButtons.Add(btn);
            }
        }

        _wasFocused = true;
    }

    public void MarkKeyStale(KeyboardKey key)
    {
        if (Raylib.IsKeyDown(key))
        {
            _staleKeys.Add(key);
        }
    }

    public bool IsMouseButtonPressed(MouseButton button)
    {
        if (!Raylib.IsWindowFocused())
        {
            return false;
        }

        if (_pressedMouseButtonsThisFrame.Contains(button))
        {
            return true;
        }

        if (IsTrackedMouseButton(button))
        {
            return Raylib.IsMouseButtonPressed(button) && !_staleMouseButtons.Contains(button);
        }

        return Raylib.IsMouseButtonPressed(button);
    }

    public bool IsMouseButtonDown(MouseButton button)
    {
        if (!Raylib.IsWindowFocused())
        {
            return false;
        }

        if (_pressedMouseButtonsThisFrame.Contains(button))
        {
            return true;
        }

        if (!Raylib.IsMouseButtonDown(button) || _staleMouseButtons.Contains(button))
        {
            return false;
        }

        return !_hasOsState || !IsTrackedMouseButton(button) || IsOsMouseButtonDown(button);
    }

    public bool IsKeyPressed(KeyboardKey key)
    {
        if (!Raylib.IsWindowFocused())
        {
            return false;
        }

        if (_staleKeys.Contains(key))
        {
            return false;
        }

        return Raylib.IsKeyPressed(key);
    }

    public bool IsKeyDown(KeyboardKey key)
    {
        if (!Raylib.IsWindowFocused())
        {
            return false;
        }

        if (!Raylib.IsKeyDown(key))
        {
            return false;
        }

        if (_staleKeys.Contains(key) && !Raylib.IsKeyPressed(key))
        {
            return false;
        }

        if (IsModifierKey(key) && _hasOsState)
        {
            return Raylib.IsKeyPressed(key) || IsOsModifierDown(key);
        }

        return true;
    }

    private bool IsOsModifierDown(KeyboardKey key)
    {
        return key switch
        {
            KeyboardKey.LeftShift or KeyboardKey.RightShift => _osState.ShiftDown,
            KeyboardKey.LeftControl or KeyboardKey.RightControl => _osState.ControlDown,
            KeyboardKey.LeftAlt or KeyboardKey.RightAlt => _osState.AltDown,
            KeyboardKey.LeftSuper or KeyboardKey.RightSuper => _osState.SuperDown,
            _ => false
        };
    }

    private bool IsOsMouseButtonDown(MouseButton button)
    {
        int bit = (int)button;
        return bit >= 0 && bit <= 2 && (_osState.PressedMouseButtons & (1UL << bit)) != 0;
    }

    private static bool IsModifierKey(KeyboardKey key)
    {
        return key is KeyboardKey.LeftShift or KeyboardKey.RightShift
            or KeyboardKey.LeftControl or KeyboardKey.RightControl
            or KeyboardKey.LeftAlt or KeyboardKey.RightAlt
            or KeyboardKey.LeftSuper or KeyboardKey.RightSuper;
    }

    private static bool IsTrackedMouseButton(MouseButton button)
    {
        return button is MouseButton.Left or MouseButton.Right or MouseButton.Middle;
    }
}
