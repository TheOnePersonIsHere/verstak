using System.Reflection;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Engine.App;

/// <summary>
/// Loads the embedded Verstak application icon and binds it to both the OS window
/// and the native macOS Cocoa Dock tile (even when launched directly via dotnet/CLI).
/// </summary>
public static unsafe class AppDockIconBridge
{
    private const string ObjCRuntime = "/usr/lib/libobjc.A.dylib";
    private const string EmbeddedIconResourceName = "Engine.App.Assets.VerstakAppIcon.png";

    public static bool ApplyDefaultIcon()
    {
        byte[]? iconBytes = LoadEmbeddedIconBytes();
        if (iconBytes == null || iconBytes.Length == 0)
        {
            return false;
        }

        return ApplyIconBytes(iconBytes);
    }

    public static bool ApplyIconBytes(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return false;
        }

        if (OperatingSystem.IsMacOS())
        {
            return ApplyMacOsDockIconBytes(pngBytes);
        }

        if (Raylib.IsWindowReady())
        {
            try
            {
                Image windowIcon = Raylib.LoadImageFromMemory(".png", pngBytes);
                if (Raylib.IsImageValid(windowIcon))
                {
                    Raylib.SetWindowIcon(windowIcon);
                    Raylib.UnloadImage(windowIcon);
                    return true;
                }
            }
            catch
            {
                // Keep app startup resilient if window icon binding fails on any backend.
            }
        }

        return false;
    }

    public static byte[]? LoadEmbeddedIconBytes()
    {
        Assembly asm = typeof(AppDockIconBridge).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(EmbeddedIconResourceName);
        if (stream == null)
        {
            return null;
        }

        using var ms = new MemoryStream((int)Math.Max(1024, stream.Length));
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static bool HasMacOsApplicationIcon()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);
            IntPtr nsAppClass = objc_getClass("NSApplication");
            if (nsAppClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr nsApp = objc_msgSend(nsAppClass, sel_registerName("sharedApplication"));
            if (nsApp == IntPtr.Zero)
            {
                return false;
            }

            IntPtr icon = objc_msgSend(nsApp, sel_registerName("applicationIconImage"));
            return icon != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static bool ApplyMacOsDockIconBytes(byte[] pngBytes)
    {
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);

            IntPtr nsAppClass = objc_getClass("NSApplication");
            IntPtr nsDataClass = objc_getClass("NSData");
            IntPtr nsImageClass = objc_getClass("NSImage");
            if (nsAppClass == IntPtr.Zero || nsDataClass == IntPtr.Zero || nsImageClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr nsApp = objc_msgSend(nsAppClass, sel_registerName("sharedApplication"));
            if (nsApp == IntPtr.Zero)
            {
                return false;
            }

            IntPtr nsData;
            fixed (byte* ptr = pngBytes)
            {
                nsData = objc_msgSend_PtrNUInt(
                    nsDataClass,
                    sel_registerName("dataWithBytes:length:"),
                    (IntPtr)ptr,
                    (nuint)pngBytes.Length);
            }

            if (nsData == IntPtr.Zero)
            {
                return false;
            }

            IntPtr nsImageAlloc = objc_msgSend(nsImageClass, sel_registerName("alloc"));
            if (nsImageAlloc == IntPtr.Zero)
            {
                return false;
            }

            IntPtr nsImage = objc_msgSend_1(nsImageAlloc, sel_registerName("initWithData:"), nsData);
            if (nsImage == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                objc_msgSend_VoidPtr(nsApp, sel_registerName("setApplicationIconImage:"), nsImage);
                IntPtr dockTile = objc_msgSend(nsApp, sel_registerName("dockTile"));
                if (dockTile != IntPtr.Zero)
                {
                    objc_msgSend_Void(dockTile, sel_registerName("display"));
                }

                return objc_msgSend(nsApp, sel_registerName("applicationIconImage")) != IntPtr.Zero;
            }
            finally
            {
                objc_msgSend_Void(nsImage, sel_registerName("release"));
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_1(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_PtrNUInt(IntPtr receiver, IntPtr selector, IntPtr arg1, nuint arg2);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_Void(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_VoidPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);
}
