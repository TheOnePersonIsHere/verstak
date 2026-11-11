using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine.Core.Abstractions;

namespace Engine.App;

/// <summary>
/// Synchronizes IMenuService definitions with macOS's native top NSMenu bar
/// while preserving GLFW's application menu and Window menu.
/// </summary>
internal sealed class MacOsMenuBridge : IDisposable
{
    private const string ObjCRuntime = "/usr/lib/libobjc.A.dylib";
    private const nuint ShiftMask = 1 << 17;
    private const nuint ControlMask = 1 << 18;
    private const nuint OptionMask = 1 << 19;
    private const nuint CommandMask = 1 << 20;

    private static readonly string[] PreferredTopLevelOrder = ["File", "Edit", "View", "Plugins", "Settings"];
    private static readonly ConcurrentQueue<int> PendingTags = new();

    private static IntPtr _tagSelector;

    private sealed class MenuBinding
    {
        public required int Tag { get; init; }
        public required IntPtr NsMenuItem { get; init; }
        public required MenuItemDefinition Definition { get; init; }
        public bool LastChecked { get; set; }
        public bool LastEnabled { get; set; } = true;
    }

    private sealed class MenuTreeNode
    {
        public required string Name { get; init; }
        public List<MenuTreeNode> SubMenus { get; } = new();
        public List<MenuItemDefinition> Entries { get; } = new();
    }

    private readonly IMenuService _menuService;
    private readonly IntPtr _nsApp;
    private readonly IntPtr _nsMenuClass;
    private readonly IntPtr _nsMenuItemClass;
    private readonly IntPtr _nsStringClass;
    private readonly IntPtr _allocSel;
    private readonly IntPtr _initSel;
    private readonly IntPtr _releaseSel;
    private readonly IntPtr _mainMenuSel;
    private readonly IntPtr _numberOfItemsSel;
    private readonly IntPtr _addItemSel;
    private readonly IntPtr _insertItemAtIndexSel;
    private readonly IntPtr _removeItemSel;
    private readonly IntPtr _setSubmenuSel;
    private readonly IntPtr _setAutoenablesItemsSel;
    private readonly IntPtr _initWithTitleActionKeySel;
    private readonly IntPtr _separatorItemSel;
    private readonly IntPtr _setTargetSel;
    private readonly IntPtr _setTagSel;
    private readonly IntPtr _setStateSel;
    private readonly IntPtr _setEnabledSel;
    private readonly IntPtr _setKeyModifierMaskSel;
    private readonly IntPtr _stringWithUtf8Sel;
    private readonly IntPtr _actionSel;

    private IntPtr _targetInstance;
    private readonly List<IntPtr> _topLevelItems = new();
    private readonly List<MenuBinding> _bindings = new();
    private readonly Dictionary<int, Action> _tagCallbacks = new();
    private int _lastBuiltVersion = -1;
    private bool _disposed;

    public MacOsMenuBridge(IMenuService menuService)
    {
        _menuService = menuService;
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);

            IntPtr nsAppClass = objc_getClass("NSApplication");
            _nsMenuClass = objc_getClass("NSMenu");
            _nsMenuItemClass = objc_getClass("NSMenuItem");
            _nsStringClass = objc_getClass("NSString");
            IntPtr nsObjectClass = objc_getClass("NSObject");

            IntPtr sharedAppSel = sel_registerName("sharedApplication");
            _mainMenuSel = sel_registerName("mainMenu");
            _numberOfItemsSel = sel_registerName("numberOfItems");
            _addItemSel = sel_registerName("addItem:");
            _insertItemAtIndexSel = sel_registerName("insertItem:atIndex:");
            _removeItemSel = sel_registerName("removeItem:");
            _setSubmenuSel = sel_registerName("setSubmenu:");
            _setAutoenablesItemsSel = sel_registerName("setAutoenablesItems:");
            _allocSel = sel_registerName("alloc");
            _initSel = sel_registerName("init");
            _releaseSel = sel_registerName("release");
            _initWithTitleActionKeySel = sel_registerName("initWithTitle:action:keyEquivalent:");
            _separatorItemSel = sel_registerName("separatorItem");
            _setTargetSel = sel_registerName("setTarget:");
            _setTagSel = sel_registerName("setTag:");
            _setStateSel = sel_registerName("setState:");
            _setEnabledSel = sel_registerName("setEnabled:");
            _setKeyModifierMaskSel = sel_registerName("setKeyEquivalentModifierMask:");
            _stringWithUtf8Sel = sel_registerName("stringWithUTF8String:");
            _actionSel = sel_registerName("onWorkbenchMenuAction:");
            _tagSelector = sel_registerName("tag");

            if (nsAppClass == IntPtr.Zero ||
                _nsMenuClass == IntPtr.Zero ||
                _nsMenuItemClass == IntPtr.Zero ||
                _nsStringClass == IntPtr.Zero)
            {
                return;
            }

            _nsApp = objc_msgSend(nsAppClass, sharedAppSel);
            if (_nsApp == IntPtr.Zero)
            {
                return;
            }

            IntPtr targetClass = objc_getClass("WorkbenchMenuTarget");
            if (targetClass == IntPtr.Zero)
            {
                targetClass = objc_allocateClassPair(nsObjectClass, "WorkbenchMenuTarget", 0);
                if (targetClass != IntPtr.Zero)
                {
                    unsafe
                    {
                        class_addMethod(targetClass, _actionSel, &OnNativeMenuAction, "v@:@");
                    }
                    objc_registerClassPair(targetClass);
                }
            }

            if (targetClass == IntPtr.Zero)
            {
                return;
            }

            _targetInstance = objc_msgSend(objc_msgSend(targetClass, _allocSel), _initSel);
            if (_targetInstance == IntPtr.Zero)
            {
                return;
            }

            while (PendingTags.TryDequeue(out _))
            {
            }

            IsAvailable = true;
            RebuildIfNeeded();
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; private set; }

    public void Update()
    {
        if (!IsAvailable || _disposed)
        {
            return;
        }

        RebuildIfNeeded();

        while (PendingTags.TryDequeue(out int tag))
        {
            if (_tagCallbacks.TryGetValue(tag, out Action? cb))
            {
                cb();
            }
        }

        SyncItemStates();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!IsAvailable)
        {
            return;
        }

        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            IntPtr mainMenu = objc_msgSend(_nsApp, _mainMenuSel);
            RemoveExistingTopLevelItems(mainMenu);
            _bindings.Clear();
            _tagCallbacks.Clear();

            if (_targetInstance != IntPtr.Zero)
            {
                objc_msgSend_Void(_targetInstance, _releaseSel);
                _targetInstance = IntPtr.Zero;
            }
        }
        catch
        {
            // Ignore Cocoa cleanup errors during window shutdown.
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private void RebuildIfNeeded()
    {
        int currentVersion = _menuService.Version;
        if (currentVersion == _lastBuiltVersion)
        {
            return;
        }

        IntPtr mainMenu = objc_msgSend(_nsApp, _mainMenuSel);
        if (mainMenu == IntPtr.Zero)
        {
            return;
        }

        _lastBuiltVersion = currentVersion;

        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            RemoveExistingTopLevelItems(mainMenu);
            _bindings.Clear();
            _tagCallbacks.Clear();

            List<MenuTreeNode> roots = BuildMenuTree(_menuService.Items);
            nint totalItems = objc_msgSend_NInt(mainMenu, _numberOfItemsSel);
            nint insertIndex = Math.Max((nint)1, totalItems - 1);

            int nextTag = 1000;
            foreach (MenuTreeNode root in roots)
            {
                IntPtr titleNs = CreateNsString(root.Name);
                IntPtr emptyKey = CreateNsString(string.Empty);
                IntPtr topItem = objc_msgSend_3(
                    objc_msgSend(_nsMenuItemClass, _allocSel),
                    _initWithTitleActionKeySel,
                    titleNs,
                    IntPtr.Zero,
                    emptyKey);

                IntPtr submenu = BuildNsMenu(root, ref nextTag);
                objc_msgSend_VoidPtr(topItem, _setSubmenuSel, submenu);
                objc_msgSend_VoidPtrNInt(mainMenu, _insertItemAtIndexSel, topItem, insertIndex++);

                objc_msgSend_Void(submenu, _releaseSel);
                objc_msgSend_Void(topItem, _releaseSel);
                _topLevelItems.Add(topItem);
            }

            SyncItemStates(force: true);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private IntPtr BuildNsMenu(MenuTreeNode node, ref int nextTag)
    {
        IntPtr titleNs = CreateNsString(node.Name);
        IntPtr menu = objc_msgSend_1(objc_msgSend(_nsMenuClass, _allocSel), sel_registerName("initWithTitle:"), titleNs);
        objc_msgSend_VoidByte(menu, _setAutoenablesItemsSel, 0);

        foreach (MenuTreeNode sub in node.SubMenus)
        {
            IntPtr subTitleNs = CreateNsString(sub.Name);
            IntPtr emptyKey = CreateNsString(string.Empty);
            IntPtr subItem = objc_msgSend_3(
                objc_msgSend(_nsMenuItemClass, _allocSel),
                _initWithTitleActionKeySel,
                subTitleNs,
                IntPtr.Zero,
                emptyKey);

            IntPtr childMenu = BuildNsMenu(sub, ref nextTag);
            objc_msgSend_VoidPtr(subItem, _setSubmenuSel, childMenu);
            objc_msgSend_VoidPtr(menu, _addItemSel, subItem);

            objc_msgSend_Void(childMenu, _releaseSel);
            objc_msgSend_Void(subItem, _releaseSel);
        }

        List<MenuItemDefinition> orderedEntries = node.Entries
            .OrderBy(e => e.Order)
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .ToList();

        bool lastWasSeparator = true;
        for (int i = 0; i < orderedEntries.Count; i++)
        {
            MenuItemDefinition entry = orderedEntries[i];
            if (entry.IsSeparator)
            {
                if (!lastWasSeparator && i < orderedEntries.Count - 1)
                {
                    IntPtr sep = objc_msgSend(_nsMenuItemClass, _separatorItemSel);
                    objc_msgSend_VoidPtr(menu, _addItemSel, sep);
                    lastWasSeparator = true;
                }

                continue;
            }

            lastWasSeparator = false;
            int slashIdx = entry.Path.LastIndexOf('/');
            string leafLabel = slashIdx >= 0 ? entry.Path[(slashIdx + 1)..] : entry.Path;

            ParseShortcut(entry.Shortcut, out string keyEquiv, out nuint modifierMask);
            IntPtr leafNs = CreateNsString(leafLabel);
            IntPtr keyNs = CreateNsString(keyEquiv);

            IntPtr item = objc_msgSend_3(
                objc_msgSend(_nsMenuItemClass, _allocSel),
                _initWithTitleActionKeySel,
                leafNs,
                entry.Callback != null ? _actionSel : IntPtr.Zero,
                keyNs);

            if (!string.IsNullOrEmpty(keyEquiv))
            {
                objc_msgSend_VoidNUInt(item, _setKeyModifierMaskSel, modifierMask);
            }

            int tag = nextTag++;
            objc_msgSend_VoidNInt(item, _setTagSel, tag);
            if (entry.Callback != null)
            {
                objc_msgSend_VoidPtr(item, _setTargetSel, _targetInstance);
                _tagCallbacks[tag] = entry.Callback;
            }

            objc_msgSend_VoidPtr(menu, _addItemSel, item);
            objc_msgSend_Void(item, _releaseSel);

            _bindings.Add(new MenuBinding
            {
                Tag = tag,
                NsMenuItem = item,
                Definition = entry
            });
        }

        return menu;
    }

    private void SyncItemStates(bool force = false)
    {
        foreach (MenuBinding binding in _bindings)
        {
            bool isChecked = binding.Definition.IsChecked?.Invoke() ?? false;
            bool isEnabled = binding.Definition.Callback != null && (binding.Definition.IsEnabled?.Invoke() ?? true);

            if (force || isChecked != binding.LastChecked)
            {
                binding.LastChecked = isChecked;
                objc_msgSend_VoidNInt(binding.NsMenuItem, _setStateSel, isChecked ? 1 : 0);
            }

            if (force || isEnabled != binding.LastEnabled)
            {
                binding.LastEnabled = isEnabled;
                objc_msgSend_VoidByte(binding.NsMenuItem, _setEnabledSel, isEnabled ? (byte)1 : (byte)0);
            }
        }
    }

    private void RemoveExistingTopLevelItems(IntPtr mainMenu)
    {
        if (mainMenu != IntPtr.Zero)
        {
            foreach (IntPtr item in _topLevelItems)
            {
                if (item != IntPtr.Zero)
                {
                    objc_msgSend_VoidPtr(mainMenu, _removeItemSel, item);
                }
            }
        }

        _topLevelItems.Clear();
    }

    private IntPtr CreateNsString(string text)
    {
        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return objc_msgSend_1(_nsStringClass, _stringWithUtf8Sel, utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static List<MenuTreeNode> BuildMenuTree(IReadOnlyList<MenuItemDefinition> items)
    {
        var roots = new List<MenuTreeNode>();

        foreach (MenuItemDefinition item in items)
        {
            string[] parts = item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            MenuTreeNode root = GetOrCreateChild(roots, parts[0]);
            if (parts.Length == 1)
            {
                root.Entries.Add(item);
                continue;
            }

            if (item.IsSeparator && parts.Length == 1)
            {
                root.Entries.Add(item);
                continue;
            }

            MenuTreeNode current = root;
            int folderCount = item.IsSeparator ? parts.Length : parts.Length - 1;
            for (int i = 1; i < folderCount; i++)
            {
                current = GetOrCreateChild(current.SubMenus, parts[i]);
            }

            current.Entries.Add(item);
        }

        return roots
            .OrderBy(r =>
            {
                int idx = Array.IndexOf(PreferredTopLevelOrder, r.Name);
                return idx >= 0 ? idx : int.MaxValue;
            })
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static MenuTreeNode GetOrCreateChild(List<MenuTreeNode> list, string name)
    {
        MenuTreeNode? existing = list.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));
        if (existing != null)
        {
            return existing;
        }

        var created = new MenuTreeNode { Name = name };
        list.Add(created);
        return created;
    }

    private static void ParseShortcut(string? shortcut, out string keyEquiv, out nuint modifierMask)
    {
        keyEquiv = string.Empty;
        modifierMask = 0;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return;
        }

        string[] tokens = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in tokens)
        {
            if (token.Equals("Cmd", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Command", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Super", StringComparison.OrdinalIgnoreCase))
            {
                modifierMask |= CommandMask;
            }
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifierMask |= ShiftMask;
            }
            else if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifierMask |= ControlMask;
            }
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("Option", StringComparison.OrdinalIgnoreCase))
            {
                modifierMask |= OptionMask;
            }
            else
            {
                keyEquiv = token.Length == 1 ? token.ToLowerInvariant() : token;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNativeMenuAction(IntPtr self, IntPtr cmd, IntPtr sender)
    {
        if (sender == IntPtr.Zero || _tagSelector == IntPtr.Zero)
        {
            return;
        }

        nint tag = objc_msgSend_NInt(sender, _tagSelector);
        PendingTags.Enqueue((int)tag);
    }

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
    private static unsafe extern byte class_addMethod(
        IntPtr cls,
        IntPtr name,
        delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void> imp,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

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
    private static extern IntPtr objc_msgSend_3(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_NInt(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_Void(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_VoidPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_VoidPtrNInt(IntPtr receiver, IntPtr selector, IntPtr arg1, nint arg2);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_VoidNInt(IntPtr receiver, IntPtr selector, nint arg1);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_VoidNUInt(IntPtr receiver, IntPtr selector, nuint arg1);

    [DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_VoidByte(IntPtr receiver, IntPtr selector, byte arg1);
}
