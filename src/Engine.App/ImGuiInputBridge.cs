using System.Numerics;
using Engine.Core.Plugins;
using ImGuiNET;
using Raylib_cs;

namespace Engine.App;

/// <summary>
/// Custom per-frame Dear ImGui input bridge for Raylib.
/// Replaces rlImGui.Begin(dt) so lost key-up / mouse-up events (such as during macOS
/// screenshot capture Cmd+Ctrl+Shift+4 or focus switches) never latch stuck modifiers
/// or mouse buttons in ImGui.
/// </summary>
internal sealed class ImGuiInputBridge
{
    private static readonly MouseButton[] MouseButtons =
    [
        MouseButton.Left,
        MouseButton.Right,
        MouseButton.Middle,
        MouseButton.Forward,
        MouseButton.Back
    ];

    private static readonly Dictionary<KeyboardKey, ImGuiKey> KeyMap = BuildKeyMap();

    private readonly bool[] _mouseDown = new bool[5];
    private readonly HashSet<KeyboardKey> _heldKeys = new();
    private readonly HashSet<KeyboardKey> _keysPressedWithSuper = new();
    private readonly List<KeyboardKey> _releasedScratch = new();

    private bool _lastFocused = true;
    private bool _lastShift;
    private bool _lastCtrl;
    private bool _lastAlt;
    private bool _lastSuper;

    private bool _lastLeftShift;
    private bool _lastRightShift;
    private bool _lastLeftCtrl;
    private bool _lastRightCtrl;
    private bool _lastLeftAlt;
    private bool _lastRightAlt;
    private bool _lastLeftSuper;
    private bool _lastRightSuper;

    public void BeginFrame(float dt, RaylibInputService input, IntPtr imguiContext)
    {
        if (imguiContext != IntPtr.Zero)
        {
            ImGui.SetCurrentContext(imguiContext);
        }

        ImGuiIOPtr io = ImGui.GetIO();

        if (Raylib.IsWindowFullscreen())
        {
            int monitor = Raylib.GetCurrentMonitor();
            io.DisplaySize = new Vector2(Raylib.GetMonitorWidth(monitor), Raylib.GetMonitorHeight(monitor));
        }
        else
        {
            io.DisplaySize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        }

        io.DisplayFramebufferScale = (OperatingSystem.IsMacOS() || Raylib.IsWindowState(ConfigFlags.HighDpiWindow))
            ? Raylib.GetWindowScaleDPI()
            : Vector2.One;

        float effectiveDt = dt > 0f ? dt : Raylib.GetFrameTime();
        io.DeltaTime = effectiveDt > 0f ? effectiveDt : 1f / 60f;

        if (io.WantSetMousePos)
        {
            Raylib.SetMousePosition((int)io.MousePos.X, (int)io.MousePos.Y);
        }
        else
        {
            io.AddMousePosEvent(Raylib.GetMouseX(), Raylib.GetMouseY());
        }

        bool focused = Raylib.IsWindowFocused();
        if (focused != _lastFocused)
        {
            io.AddFocusEvent(focused);
            _lastFocused = focused;
        }

        if (!focused)
        {
            ReleaseAllHeldInput(io, input);
            DrainPendingQueues();
            ImGui.NewFrame();
            return;
        }

        for (int b = 0; b < MouseButtons.Length; b++)
        {
            MouseButton rlBtn = MouseButtons[b];
            bool down = b < 3 ? input.IsMouseButtonDown(rlBtn) : Raylib.IsMouseButtonDown(rlBtn);
            if (down != _mouseDown[b])
            {
                io.AddMouseButtonEvent(b, down);
                _mouseDown[b] = down;
            }
        }

        Vector2 wheel = Raylib.GetMouseWheelMoveV();
        io.AddMouseWheelEvent(wheel.X, wheel.Y);

        bool leftShift = input.IsKeyDown(KeyboardKey.LeftShift);
        bool rightShift = input.IsKeyDown(KeyboardKey.RightShift);
        if (input.IsOsShiftDown && !leftShift && !rightShift)
        {
            leftShift = true;
        }
        bool shift = leftShift || rightShift;

        bool leftCtrl = input.IsKeyDown(KeyboardKey.LeftControl);
        bool rightCtrl = input.IsKeyDown(KeyboardKey.RightControl);
        if (input.IsOsControlDown && !leftCtrl && !rightCtrl)
        {
            leftCtrl = true;
        }
        bool ctrl = leftCtrl || rightCtrl;

        bool leftAlt = input.IsKeyDown(KeyboardKey.LeftAlt);
        bool rightAlt = input.IsKeyDown(KeyboardKey.RightAlt);
        if (input.IsOsAltDown && !leftAlt && !rightAlt)
        {
            leftAlt = true;
        }
        bool alt = leftAlt || rightAlt;

        bool leftSuper = input.IsKeyDown(KeyboardKey.LeftSuper);
        bool rightSuper = input.IsKeyDown(KeyboardKey.RightSuper);
        if (input.IsOsSuperDown && !leftSuper && !rightSuper)
        {
            leftSuper = true;
        }
        bool super = leftSuper || rightSuper;

        bool wasSuper = _lastSuper;

        UpdateKeyState(io, ImGuiKey.LeftShift, ref _lastLeftShift, leftShift);
        UpdateKeyState(io, ImGuiKey.RightShift, ref _lastRightShift, rightShift);
        UpdateKeyState(io, ImGuiKey.ModShift, ref _lastShift, shift);

        UpdateKeyState(io, ImGuiKey.LeftCtrl, ref _lastLeftCtrl, leftCtrl);
        UpdateKeyState(io, ImGuiKey.RightCtrl, ref _lastRightCtrl, rightCtrl);
        UpdateKeyState(io, ImGuiKey.ModCtrl, ref _lastCtrl, ctrl);

        UpdateKeyState(io, ImGuiKey.LeftAlt, ref _lastLeftAlt, leftAlt);
        UpdateKeyState(io, ImGuiKey.RightAlt, ref _lastRightAlt, rightAlt);
        UpdateKeyState(io, ImGuiKey.ModAlt, ref _lastAlt, alt);

        UpdateKeyState(io, ImGuiKey.LeftSuper, ref _lastLeftSuper, leftSuper);
        UpdateKeyState(io, ImGuiKey.RightSuper, ref _lastRightSuper, rightSuper);
        UpdateKeyState(io, ImGuiKey.ModSuper, ref _lastSuper, super);

        // Cocoa's performKeyEquivalent swallows keyUp for Cmd+<key> shortcuts;
        // release those keys explicitly when Command (Super) is released.
        if (wasSuper && !super && _keysPressedWithSuper.Count > 0)
        {
            foreach (KeyboardKey superKey in _keysPressedWithSuper)
            {
                if (_heldKeys.Remove(superKey) && KeyMap.TryGetValue(superKey, out ImGuiKey mapped))
                {
                    io.AddKeyEvent(mapped, false);
                }

                input.MarkKeyStale(superKey);
            }

            _keysPressedWithSuper.Clear();
        }

        int pressedCode = Raylib.GetKeyPressed();
        while (pressedCode != 0)
        {
            var rlKey = (KeyboardKey)pressedCode;
            if (!IsModifierKey(rlKey) && KeyMap.TryGetValue(rlKey, out ImGuiKey imguiKey))
            {
                io.AddKeyEvent(imguiKey, true);
                _heldKeys.Add(rlKey);
                if (super)
                {
                    _keysPressedWithSuper.Add(rlKey);
                }
            }

            pressedCode = Raylib.GetKeyPressed();
        }

        _releasedScratch.Clear();
        foreach (KeyboardKey rlKey in _heldKeys)
        {
            if (Raylib.IsKeyReleased(rlKey) || !Raylib.IsKeyDown(rlKey))
            {
                _releasedScratch.Add(rlKey);
            }
        }

        foreach (KeyboardKey rlKey in _releasedScratch)
        {
            _heldKeys.Remove(rlKey);
            _keysPressedWithSuper.Remove(rlKey);
            if (KeyMap.TryGetValue(rlKey, out ImGuiKey imguiKey))
            {
                io.AddKeyEvent(imguiKey, false);
            }
        }

        int ch = Raylib.GetCharPressed();
        while (ch != 0)
        {
            io.AddInputCharacter((uint)ch);
            ch = Raylib.GetCharPressed();
        }

        ImGui.NewFrame();
    }

    private void ReleaseAllHeldInput(ImGuiIOPtr io, RaylibInputService input)
    {
        for (int b = 0; b < _mouseDown.Length; b++)
        {
            if (_mouseDown[b])
            {
                io.AddMouseButtonEvent(b, false);
                _mouseDown[b] = false;
            }
        }

        UpdateKeyState(io, ImGuiKey.LeftShift, ref _lastLeftShift, false);
        UpdateKeyState(io, ImGuiKey.RightShift, ref _lastRightShift, false);
        UpdateKeyState(io, ImGuiKey.ModShift, ref _lastShift, false);

        UpdateKeyState(io, ImGuiKey.LeftCtrl, ref _lastLeftCtrl, false);
        UpdateKeyState(io, ImGuiKey.RightCtrl, ref _lastRightCtrl, false);
        UpdateKeyState(io, ImGuiKey.ModCtrl, ref _lastCtrl, false);

        UpdateKeyState(io, ImGuiKey.LeftAlt, ref _lastLeftAlt, false);
        UpdateKeyState(io, ImGuiKey.RightAlt, ref _lastRightAlt, false);
        UpdateKeyState(io, ImGuiKey.ModAlt, ref _lastAlt, false);

        UpdateKeyState(io, ImGuiKey.LeftSuper, ref _lastLeftSuper, false);
        UpdateKeyState(io, ImGuiKey.RightSuper, ref _lastRightSuper, false);
        UpdateKeyState(io, ImGuiKey.ModSuper, ref _lastSuper, false);

        foreach (KeyboardKey rlKey in _heldKeys)
        {
            if (KeyMap.TryGetValue(rlKey, out ImGuiKey imguiKey))
            {
                io.AddKeyEvent(imguiKey, false);
            }

            input.MarkKeyStale(rlKey);
        }

        _heldKeys.Clear();
        _keysPressedWithSuper.Clear();
        io.ClearInputKeys();
        io.ClearInputMouse();
    }

    private static void DrainPendingQueues()
    {
        while (Raylib.GetKeyPressed() != 0)
        {
        }

        while (Raylib.GetCharPressed() != 0)
        {
        }
    }

    private static void UpdateKeyState(ImGuiIOPtr io, ImGuiKey key, ref bool lastState, bool currentState)
    {
        if (currentState != lastState)
        {
            io.AddKeyEvent(key, currentState);
            lastState = currentState;
        }
    }

    private static bool IsModifierKey(KeyboardKey key)
    {
        return key is KeyboardKey.LeftShift or KeyboardKey.RightShift
            or KeyboardKey.LeftControl or KeyboardKey.RightControl
            or KeyboardKey.LeftAlt or KeyboardKey.RightAlt
            or KeyboardKey.LeftSuper or KeyboardKey.RightSuper;
    }

    private static Dictionary<KeyboardKey, ImGuiKey> BuildKeyMap()
    {
        return new Dictionary<KeyboardKey, ImGuiKey>
        {
            [KeyboardKey.Apostrophe] = ImGuiKey.Apostrophe,
            [KeyboardKey.Comma] = ImGuiKey.Comma,
            [KeyboardKey.Minus] = ImGuiKey.Minus,
            [KeyboardKey.Period] = ImGuiKey.Period,
            [KeyboardKey.Slash] = ImGuiKey.Slash,
            [KeyboardKey.Zero] = ImGuiKey._0,
            [KeyboardKey.One] = ImGuiKey._1,
            [KeyboardKey.Two] = ImGuiKey._2,
            [KeyboardKey.Three] = ImGuiKey._3,
            [KeyboardKey.Four] = ImGuiKey._4,
            [KeyboardKey.Five] = ImGuiKey._5,
            [KeyboardKey.Six] = ImGuiKey._6,
            [KeyboardKey.Seven] = ImGuiKey._7,
            [KeyboardKey.Eight] = ImGuiKey._8,
            [KeyboardKey.Nine] = ImGuiKey._9,
            [KeyboardKey.Semicolon] = ImGuiKey.Semicolon,
            [KeyboardKey.Equal] = ImGuiKey.Equal,
            [KeyboardKey.A] = ImGuiKey.A,
            [KeyboardKey.B] = ImGuiKey.B,
            [KeyboardKey.C] = ImGuiKey.C,
            [KeyboardKey.D] = ImGuiKey.D,
            [KeyboardKey.E] = ImGuiKey.E,
            [KeyboardKey.F] = ImGuiKey.F,
            [KeyboardKey.G] = ImGuiKey.G,
            [KeyboardKey.H] = ImGuiKey.H,
            [KeyboardKey.I] = ImGuiKey.I,
            [KeyboardKey.J] = ImGuiKey.J,
            [KeyboardKey.K] = ImGuiKey.K,
            [KeyboardKey.L] = ImGuiKey.L,
            [KeyboardKey.M] = ImGuiKey.M,
            [KeyboardKey.N] = ImGuiKey.N,
            [KeyboardKey.O] = ImGuiKey.O,
            [KeyboardKey.P] = ImGuiKey.P,
            [KeyboardKey.Q] = ImGuiKey.Q,
            [KeyboardKey.R] = ImGuiKey.R,
            [KeyboardKey.S] = ImGuiKey.S,
            [KeyboardKey.T] = ImGuiKey.T,
            [KeyboardKey.U] = ImGuiKey.U,
            [KeyboardKey.V] = ImGuiKey.V,
            [KeyboardKey.W] = ImGuiKey.W,
            [KeyboardKey.X] = ImGuiKey.X,
            [KeyboardKey.Y] = ImGuiKey.Y,
            [KeyboardKey.Z] = ImGuiKey.Z,
            [KeyboardKey.Space] = ImGuiKey.Space,
            [KeyboardKey.Escape] = ImGuiKey.Escape,
            [KeyboardKey.Enter] = ImGuiKey.Enter,
            [KeyboardKey.Tab] = ImGuiKey.Tab,
            [KeyboardKey.Backspace] = ImGuiKey.Backspace,
            [KeyboardKey.Insert] = ImGuiKey.Insert,
            [KeyboardKey.Delete] = ImGuiKey.Delete,
            [KeyboardKey.Right] = ImGuiKey.RightArrow,
            [KeyboardKey.Left] = ImGuiKey.LeftArrow,
            [KeyboardKey.Down] = ImGuiKey.DownArrow,
            [KeyboardKey.Up] = ImGuiKey.UpArrow,
            [KeyboardKey.PageUp] = ImGuiKey.PageUp,
            [KeyboardKey.PageDown] = ImGuiKey.PageDown,
            [KeyboardKey.Home] = ImGuiKey.Home,
            [KeyboardKey.End] = ImGuiKey.End,
            [KeyboardKey.CapsLock] = ImGuiKey.CapsLock,
            [KeyboardKey.ScrollLock] = ImGuiKey.ScrollLock,
            [KeyboardKey.NumLock] = ImGuiKey.NumLock,
            [KeyboardKey.PrintScreen] = ImGuiKey.PrintScreen,
            [KeyboardKey.Pause] = ImGuiKey.Pause,
            [KeyboardKey.F1] = ImGuiKey.F1,
            [KeyboardKey.F2] = ImGuiKey.F2,
            [KeyboardKey.F3] = ImGuiKey.F3,
            [KeyboardKey.F4] = ImGuiKey.F4,
            [KeyboardKey.F5] = ImGuiKey.F5,
            [KeyboardKey.F6] = ImGuiKey.F6,
            [KeyboardKey.F7] = ImGuiKey.F7,
            [KeyboardKey.F8] = ImGuiKey.F8,
            [KeyboardKey.F9] = ImGuiKey.F9,
            [KeyboardKey.F10] = ImGuiKey.F10,
            [KeyboardKey.F11] = ImGuiKey.F11,
            [KeyboardKey.F12] = ImGuiKey.F12,
            [KeyboardKey.LeftShift] = ImGuiKey.LeftShift,
            [KeyboardKey.LeftControl] = ImGuiKey.LeftCtrl,
            [KeyboardKey.LeftAlt] = ImGuiKey.LeftAlt,
            [KeyboardKey.LeftSuper] = ImGuiKey.LeftSuper,
            [KeyboardKey.RightShift] = ImGuiKey.RightShift,
            [KeyboardKey.RightControl] = ImGuiKey.RightCtrl,
            [KeyboardKey.RightAlt] = ImGuiKey.RightAlt,
            [KeyboardKey.RightSuper] = ImGuiKey.RightSuper,
            [KeyboardKey.KeyboardMenu] = ImGuiKey.Menu,
            [KeyboardKey.LeftBracket] = ImGuiKey.LeftBracket,
            [KeyboardKey.Backslash] = ImGuiKey.Backslash,
            [KeyboardKey.RightBracket] = ImGuiKey.RightBracket,
            [KeyboardKey.Grave] = ImGuiKey.GraveAccent,
            [KeyboardKey.Kp0] = ImGuiKey.Keypad0,
            [KeyboardKey.Kp1] = ImGuiKey.Keypad1,
            [KeyboardKey.Kp2] = ImGuiKey.Keypad2,
            [KeyboardKey.Kp3] = ImGuiKey.Keypad3,
            [KeyboardKey.Kp4] = ImGuiKey.Keypad4,
            [KeyboardKey.Kp5] = ImGuiKey.Keypad5,
            [KeyboardKey.Kp6] = ImGuiKey.Keypad6,
            [KeyboardKey.Kp7] = ImGuiKey.Keypad7,
            [KeyboardKey.Kp8] = ImGuiKey.Keypad8,
            [KeyboardKey.Kp9] = ImGuiKey.Keypad9,
            [KeyboardKey.KpDecimal] = ImGuiKey.KeypadDecimal,
            [KeyboardKey.KpDivide] = ImGuiKey.KeypadDivide,
            [KeyboardKey.KpMultiply] = ImGuiKey.KeypadMultiply,
            [KeyboardKey.KpSubtract] = ImGuiKey.KeypadSubtract,
            [KeyboardKey.KpAdd] = ImGuiKey.KeypadAdd,
            [KeyboardKey.KpEnter] = ImGuiKey.KeypadEnter,
            [KeyboardKey.KpEqual] = ImGuiKey.KeypadEqual
        };
    }
}
