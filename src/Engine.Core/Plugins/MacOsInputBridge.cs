using System.Runtime.InteropServices;

namespace Engine.Core.Plugins;

internal readonly record struct MacOsInputState(
    bool ShiftDown,
    bool ControlDown,
    bool AltDown,
    bool SuperDown,
    nuint PressedMouseButtons);

/// <summary>
/// Queries live AppKit device + synthesized modifier and mouse button state via
/// +[NSEvent modifierFlags] and +[NSEvent pressedMouseButtons]. These APIs do not
/// require Input Monitoring / Accessibility permissions and reflect releases even when
/// global overlays (e.g., macOS screenshot capture) intercept NSEvent key/mouse-up delivery.
/// </summary>
internal static class MacOsInputBridge
{
    private const string ObjCRuntime = "/usr/lib/libobjc.A.dylib";

    // AppKit NSEventModifierFlags
    private const nuint ShiftMask = 1 << 17;   // 0x20000
    private const nuint ControlMask = 1 << 18; // 0x40000
    private const nuint OptionMask = 1 << 19;  // 0x80000
    private const nuint CommandMask = 1 << 20; // 0x100000

    private static readonly IntPtr NsEventClass;
    private static readonly IntPtr ModifierFlagsSelector;
    private static readonly IntPtr PressedMouseButtonsSelector;
    private static readonly bool Available;

    static MacOsInputBridge()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);
            NsEventClass = ObjcGetClass("NSEvent");
            ModifierFlagsSelector = SelRegisterName("modifierFlags");
            PressedMouseButtonsSelector = SelRegisterName("pressedMouseButtons");
            Available = NsEventClass != IntPtr.Zero &&
                        ModifierFlagsSelector != IntPtr.Zero &&
                        PressedMouseButtonsSelector != IntPtr.Zero;
        }
        catch
        {
            Available = false;
        }
    }

    public static bool TryGetState(out MacOsInputState state)
    {
        if (!Available)
        {
            state = default;
            return false;
        }

        try
        {
            nuint flags = ObjcMsgSendNUInt(NsEventClass, ModifierFlagsSelector);
            nuint buttons = ObjcMsgSendNUInt(NsEventClass, PressedMouseButtonsSelector);

            state = new MacOsInputState(
                ShiftDown: (flags & ShiftMask) != 0,
                ControlDown: (flags & ControlMask) != 0,
                AltDown: (flags & OptionMask) != 0,
                SuperDown: (flags & CommandMask) != 0,
                PressedMouseButtons: buttons);
            return true;
        }
        catch
        {
            state = default;
            return false;
        }
    }

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern nuint ObjcMsgSendNUInt(IntPtr receiver, IntPtr selector);
}
