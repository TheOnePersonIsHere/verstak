using System.Numerics;
using System.Runtime.InteropServices;
using Engine.Core.Abstractions;
using Engine.Core.Audio;
using Engine.Core.Events;
using Engine.Core.Graphics;
using Engine.Core.Plugins;
using Engine.Plugins.Execution;
using Engine.Plugins.Scene;
using Engine.Plugins.Settings;
using Engine.Plugins.Timeline;
using Engine.Plugins.Viewport;
using Engine.Plugins.Workspace;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using ExecutionContext = Engine.Core.Abstractions.ExecutionContext;

namespace Engine.App;

public static class Program
{
    private const string DockSpaceName = "WorkbenchDockSpaceV2";
    private const string InspectorVisibleSettingKey = "ui.window.inspector.visible";

    public static void Main(string[] args)
    {
        string projectRoot = ResolveInitialProjectRoot(args);
        int? maxFrames = ResolveSmokeFrames(args);
        bool noSplash = ResolveNoSplash(args);

        Raylib.SetConfigFlags(
            ConfigFlags.ResizableWindow |
            ConfigFlags.VSyncHint |
            ConfigFlags.HighDpiWindow |
            ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(1400, 860, "Workbench // Minimal 2D");
        Raylib.SetWindowMinSize(960, 600);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.InitAudioDevice();

        rlImGui.Setup(darkTheme: true, enableDocking: true);
        IntPtr imguiContext = ImGui.GetCurrentContext();
        float baseFontScale = ConfigureInputAndFonts();
        ApplyWorkbenchTheme();

        var eventBus = new EventBus();
        var settings = new JsonSettingsService(eventBus);
        WorkbenchTheme.Apply(settings.GetString(WorkbenchTheme.SettingKey, WorkbenchTheme.StudioGraphiteId));
        var menuRegistry = new MenuRegistry();
        var workspace = new WorkspaceService(projectRoot, eventBus);
        using var graphics = new RaylibGraphicsService(workspace, eventBus);
        using var audio = new RaylibAudioService(workspace, eventBus);
        var input = new RaylibInputService();
        var runtime = new ExecutionContext(graphics, audio, eventBus, workspace, input);
        var imguiInputBridge = new ImGuiInputBridge();

        var pluginContext = new PluginContext(graphics, audio, eventBus, workspace, input, settings, menuRegistry, runtime);
        var pluginHost = new PluginHost(pluginContext);

        pluginHost.RegisterPlugin(new WorkspaceExplorerPlugin(), canDisable: true, defaultWindowVisible: true);
        pluginHost.RegisterPlugin(new SceneLayersPlugin(), canDisable: true, defaultWindowVisible: true);
        pluginHost.RegisterPlugin(new ViewportPlugin(), canDisable: false, defaultWindowVisible: true);
        pluginHost.RegisterPlugin(new UniversalTimelinePlugin(), canDisable: true, defaultWindowVisible: true);
        pluginHost.RegisterPlugin(new ExecutionPlugin(), canDisable: true, defaultWindowVisible: true);
        pluginHost.RegisterPlugin(new WorkbenchSettingsPlugin(), canDisable: false, defaultWindowVisible: false);

        bool resetLayoutRequested = false;

        eventBus.Subscribe<WorkbenchResetLayoutEvent>(_ => resetLayoutRequested = true);
        eventBus.Subscribe<SettingChangedEvent>(evt =>
        {
            if (string.Equals(evt.Key, "ui.fontScale", StringComparison.Ordinal))
            {
                ApplyFontScale(baseFontScale, settings);
            }
        });

        MacOsMenuBridge? macOsMenu = null;
        bool workbenchInitialized = false;

        void InitializeWorkbenchOnMainThread()
        {
            if (workbenchInitialized)
            {
                return;
            }

            workbenchInitialized = true;
            ApplyFontScale(baseFontScale, settings);
            pluginHost.LoadAll();
            RegisterWorkbenchMenus(menuRegistry, pluginHost, settings, () => resetLayoutRequested = true);
            macOsMenu = new MacOsMenuBridge(menuRegistry);
        }

        bool showSplash = !noSplash && settings.GetBool(WorkbenchSettingsPlugin.ShowSplashSettingKey, true);
        AppLifecycleState lifecycleState = showSplash ? AppLifecycleState.Splash : AppLifecycleState.Ready;
        StartupSplashController? splash = showSplash
            ? new StartupSplashController(
                workspace,
                graphics,
                onMainThreadFinalizeWorkbench: InitializeWorkbenchOnMainThread)
            : null;

        if (lifecycleState == AppLifecycleState.Ready)
        {
            InitializeWorkbenchOnMainThread();
        }

        bool layoutChecked = false;
        int frameCount = 0;

        while (!Raylib.WindowShouldClose())
        {
            if (maxFrames.HasValue && frameCount >= maxFrames.Value)
            {
                break;
            }

            frameCount++;
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.1f);

            if (lifecycleState == AppLifecycleState.Splash && splash != null)
            {
                splash.Update(dt);

                if (splash.ShouldRenderWorkbenchUnderlay && !splash.IsCompleted)
                {
                    InitializeWorkbenchOnMainThread();
                    RenderWorkbenchFrame(
                        dt,
                        macOsMenu,
                        input,
                        pluginHost,
                        graphics,
                        eventBus,
                        settings,
                        menuRegistry,
                        imguiInputBridge,
                        imguiContext,
                        ref layoutChecked,
                        ref resetLayoutRequested,
                        drawAfterImGui: () => splash.Render(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()));
                }
                else if (!splash.IsCompleted)
                {
                    // Docking workspace & plugin windows stay unrendered until animation and warmup finish.
                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(new Color(18, 18, 20, 255));
                    splash.Render(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                    Raylib.EndDrawing();
                }

                if (splash.IsCompleted)
                {
                    InitializeWorkbenchOnMainThread();
                    splash.Dispose();
                    splash = null;
                    lifecycleState = AppLifecycleState.Ready;
                    input.SyncFrameState();

                    // Immediately render first Ready frame if completed mid-tick so there is zero blank frame.
                    RenderWorkbenchFrame(
                        dt,
                        macOsMenu,
                        input,
                        pluginHost,
                        graphics,
                        eventBus,
                        settings,
                        menuRegistry,
                        imguiInputBridge,
                        imguiContext,
                        ref layoutChecked,
                        ref resetLayoutRequested);
                }

                continue;
            }

            RenderWorkbenchFrame(
                dt,
                macOsMenu,
                input,
                pluginHost,
                graphics,
                eventBus,
                settings,
                menuRegistry,
                imguiInputBridge,
                imguiContext,
                ref layoutChecked,
                ref resetLayoutRequested);
        }

        splash?.Dispose();
        settings.Save();
        macOsMenu?.Dispose();
        pluginHost.UnloadAll();
        audio.Dispose();
        graphics.Dispose();
        rlImGui.Shutdown();
        Raylib.CloseAudioDevice();
        Raylib.CloseWindow();
    }

    private static void RenderWorkbenchFrame(
        float dt,
        MacOsMenuBridge? macOsMenu,
        RaylibInputService input,
        PluginHost pluginHost,
        RaylibGraphicsService graphics,
        IEventBus eventBus,
        ISettingsService settings,
        IMenuService menuRegistry,
        ImGuiInputBridge imguiInputBridge,
        IntPtr imguiContext,
        ref bool layoutChecked,
        ref bool resetLayoutRequested,
        Action? drawAfterImGui = null)
    {
        macOsMenu?.Update();
        input.SyncFrameState();
        pluginHost.Update(dt);

        RenderTexture2D renderTarget = graphics.GetRenderTexture();
        var clearColor = new Color(
            (byte)Math.Clamp(settings.GetFloat("viewport.bgR", 20f / 255f) * 255f, 0f, 255f),
            (byte)Math.Clamp(settings.GetFloat("viewport.bgG", 23f / 255f) * 255f, 0f, 255f),
            (byte)Math.Clamp(settings.GetFloat("viewport.bgB", 29f / 255f) * 255f, 0f, 255f),
            (byte)255);

        graphics.BeginRenderTarget();
        graphics.Clear(clearColor);
        eventBus.Publish(new ViewportRenderPassEvent(renderTarget.Texture.Width, renderTarget.Texture.Height));
        graphics.EndRenderTarget();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(18, 18, 20, 255));

        imguiInputBridge.BeginFrame(dt, input, imguiContext);

        if (macOsMenu == null || !macOsMenu.IsAvailable)
        {
            RenderImGuiFallbackMenuBar(menuRegistry);
        }

        ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
        uint dockspaceId = ImGui.GetID(DockSpaceName);

        if (!layoutChecked)
        {
            layoutChecked = true;
            if (DockBuilderNative.GetNode(dockspaceId) == IntPtr.Zero)
            {
                resetLayoutRequested = true;
            }
        }

        if (resetLayoutRequested)
        {
            resetLayoutRequested = false;
            BuildDefaultDockLayout(dockspaceId, mainViewport);
        }

        ImGui.DockSpaceOverViewport(dockspaceId, mainViewport, ImGuiDockNodeFlags.None);
        pluginHost.RenderUI();
        rlImGui.End();

        drawAfterImGui?.Invoke();

        Raylib.EndDrawing();
    }

    private static void RegisterWorkbenchMenus(
        IMenuService menu,
        PluginHost pluginHost,
        ISettingsService settings,
        Action requestResetLayout)
    {
        menu.AddSeparator("File", "file.sep.1", order: 50);

        menu.Register(new MenuItemDefinition(
            Id: "view.panel.project",
            Path: "View/Project",
            Callback: () => TogglePanelVisibility(pluginHost, "workspace.explorer"),
            IsChecked: () => pluginHost.IsWindowVisible("workspace.explorer"),
            IsEnabled: () => pluginHost.IsPluginEnabled("workspace.explorer"),
            Order: 10));

        menu.Register(new MenuItemDefinition(
            Id: "view.panel.layers",
            Path: "View/Layers",
            Callback: () => TogglePanelVisibility(pluginHost, "engine.scene"),
            IsChecked: () => pluginHost.IsWindowVisible("engine.scene"),
            IsEnabled: () => pluginHost.IsPluginEnabled("engine.scene"),
            Order: 20));

        menu.Register(new MenuItemDefinition(
            Id: "view.panel.inspector",
            Path: "View/Inspector",
            Callback: () => settings.SetBool(InspectorVisibleSettingKey, !settings.GetBool(InspectorVisibleSettingKey, true)),
            IsChecked: () => settings.GetBool(InspectorVisibleSettingKey, true),
            IsEnabled: () => pluginHost.IsPluginEnabled("engine.scene"),
            Order: 30));

        menu.Register(new MenuItemDefinition(
            Id: "view.panel.viewport",
            Path: "View/Viewport",
            Callback: () => TogglePanelVisibility(pluginHost, "workbench.viewport"),
            IsChecked: () => pluginHost.IsWindowVisible("workbench.viewport"),
            Order: 40));

        menu.Register(new MenuItemDefinition(
            Id: "view.panel.timeline",
            Path: "View/Timeline",
            Callback: () => TogglePanelVisibility(pluginHost, "workbench.timeline"),
            IsChecked: () => pluginHost.IsWindowVisible("workbench.timeline"),
            IsEnabled: () => pluginHost.IsPluginEnabled("workbench.timeline"),
            Order: 50));

        menu.Register(new MenuItemDefinition(
            Id: "view.panel.execution",
            Path: "View/Execution",
            Callback: () => TogglePanelVisibility(pluginHost, "workbench.execution"),
            IsChecked: () => pluginHost.IsWindowVisible("workbench.execution"),
            IsEnabled: () => pluginHost.IsPluginEnabled("workbench.execution"),
            Order: 60));

        menu.AddSeparator("View", "view.sep.1", order: 800);

        menu.Register(new MenuItemDefinition(
            Id: "view.reset_layout",
            Path: "View/Reset Layout",
            Callback: requestResetLayout,
            Order: 900));

        int order = 10;
        foreach (PluginEntryInfo entry in pluginHost.Entries)
        {
            if (!entry.CanDisable)
            {
                continue;
            }

            string pluginId = entry.Id;
            menu.Register(new MenuItemDefinition(
                Id: $"plugins.toggle.{pluginId}",
                Path: $"Plugins/Enable {entry.DisplayName}",
                Callback: () => pluginHost.SetPluginEnabled(pluginId, !pluginHost.IsPluginEnabled(pluginId)),
                IsChecked: () => pluginHost.IsPluginEnabled(pluginId),
                Order: order));
            order += 10;
        }

        menu.AddSeparator("Plugins", "plugins.sep.1", order: 800);
    }

    private static void TogglePanelVisibility(PluginHost host, string pluginId)
    {
        host.SetWindowVisible(pluginId, !host.IsWindowVisible(pluginId));
    }

    private static void RenderImGuiFallbackMenuBar(IMenuService menu)
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        IEnumerable<IGrouping<string, MenuItemDefinition>> groups = menu.Items
            .Select(item =>
            {
                int slash = item.Path.IndexOf('/');
                string root = slash > 0 ? item.Path[..slash] : item.Path;
                return (Root: root, Item: item);
            })
            .GroupBy(x => x.Root, x => x.Item, StringComparer.Ordinal);

        foreach (IGrouping<string, MenuItemDefinition> group in groups)
        {
            if (ImGui.BeginMenu(group.Key))
            {
                foreach (MenuItemDefinition item in group.OrderBy(i => i.Order))
                {
                    if (item.IsSeparator)
                    {
                        ImGui.Separator();
                        continue;
                    }

                    int slash = item.Path.IndexOf('/');
                    string label = slash >= 0 ? item.Path[(slash + 1)..] : item.Path;
                    bool isChecked = item.IsChecked?.Invoke() ?? false;
                    bool isEnabled = item.Callback != null && (item.IsEnabled?.Invoke() ?? true);

                    if (ImGui.MenuItem(label, item.Shortcut ?? string.Empty, isChecked, isEnabled))
                    {
                        item.Callback?.Invoke();
                    }
                }

                ImGui.EndMenu();
            }
        }

        ImGui.EndMainMenuBar();
    }

    private static void BuildDefaultDockLayout(uint dockspaceId, ImGuiViewportPtr viewport)
    {
        DockBuilderNative.RemoveNode(dockspaceId);
        DockBuilderNative.AddNode(dockspaceId, (ImGuiDockNodeFlags)(1 << 10)); // ImGuiDockNodeFlags_DockSpace
        DockBuilderNative.SetNodePos(dockspaceId, viewport.WorkPos);
        DockBuilderNative.SetNodeSize(dockspaceId, viewport.WorkSize);

        DockBuilderNative.SplitNode(dockspaceId, ImGuiDir.Down, 0.25f, out uint bottomId, out uint topId);
        DockBuilderNative.SplitNode(topId, ImGuiDir.Left, 0.21f, out uint leftId, out uint centerRightId);
        DockBuilderNative.SplitNode(leftId, ImGuiDir.Down, 0.52f, out uint leftBottomId, out uint leftTopId);
        DockBuilderNative.SplitNode(centerRightId, ImGuiDir.Right, 0.25f, out uint rightId, out uint centerId);

        DockBuilderNative.DockWindow("Project", leftTopId);
        DockBuilderNative.DockWindow("Layers", leftBottomId);
        DockBuilderNative.DockWindow("Viewport", centerId);
        DockBuilderNative.DockWindow("Inspector", rightId);
        DockBuilderNative.DockWindow("Execution", rightId);
        DockBuilderNative.DockWindow("Timeline", bottomId);
        DockBuilderNative.Finish(dockspaceId);
    }

    private static float ConfigureInputAndFonts()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        // Preserve the native macOS Apple pointer without ImGui hiding or swapping cursors.
        io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
        io.MouseDrawCursor = false;

        string[] fontCandidates =
        [
            "/System/Library/Fonts/SFNS.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/System/Library/Fonts/Supplemental/Tahoma.ttf"
        ];

        string? fontPath = fontCandidates.FirstOrDefault(File.Exists);
        if (fontPath == null)
        {
            return 1.0f;
        }

        float dpiScale = MathF.Max(1.0f, Raylib.GetWindowScaleDPI().X);
        io.Fonts.Clear();
        io.Fonts.AddFontFromFileTTF(
            fontPath,
            14.5f * dpiScale,
            new ImFontConfigPtr(IntPtr.Zero),
            io.Fonts.GetGlyphRangesDefault());
        float baseScale = 1.0f / dpiScale;
        io.FontGlobalScale = baseScale;
        rlImGui.ReloadFonts();
        return baseScale;
    }

    private static void ApplyFontScale(float baseFontScale, ISettingsService settings)
    {
        float userScale = Math.Clamp(settings.GetFloat("ui.fontScale", 1.0f), 0.85f, 1.40f);
        ImGui.GetIO().FontGlobalScale = baseFontScale * userScale;
    }

    private static void ApplyWorkbenchTheme()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(10f, 10f);
        style.FramePadding = new Vector2(10f, 5f);
        style.CellPadding = new Vector2(6f, 4f);
        style.ItemSpacing = new Vector2(8f, 6f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.IndentSpacing = 16f;
        style.ScrollbarSize = 11f;
        style.GrabMinSize = 10f;

        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 0f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;

        style.WindowRounding = 0f;
        style.ChildRounding = 2f;
        style.FrameRounding = 3f;
        style.PopupRounding = 3f;
        style.ScrollbarRounding = 3f;
        style.GrabRounding = 2f;
        style.TabRounding = 3f;

        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.92f, 0.95f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.54f, 0.58f, 0.65f, 1.00f);
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.11f, 0.12f, 0.15f, 1.00f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.09f, 0.10f, 0.12f, 1.00f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.12f, 0.13f, 0.16f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.20f, 0.23f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);

        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.18f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.21f, 0.24f, 0.29f, 1.00f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.24f, 0.28f, 0.34f, 1.00f);

        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.09f, 0.10f, 0.12f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.13f, 0.15f, 0.19f, 1.00f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.09f, 0.10f, 0.12f, 1.00f);
        colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.10f, 0.11f, 0.14f, 1.00f);

        colors[(int)ImGuiCol.Button] = new Vector4(0.18f, 0.20f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.24f, 0.28f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.20f, 0.42f, 0.72f, 1.00f);

        colors[(int)ImGuiCol.Header] = new Vector4(0.20f, 0.36f, 0.58f, 0.45f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.24f, 0.42f, 0.68f, 0.65f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.24f, 0.45f, 0.74f, 0.85f);

        colors[(int)ImGuiCol.Tab] = new Vector4(0.10f, 0.11f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.TabHovered] = new Vector4(0.22f, 0.26f, 0.33f, 1.00f);
        colors[(int)ImGuiCol.TabSelected] = new Vector4(0.16f, 0.19f, 0.24f, 1.00f);
        colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.09f, 0.10f, 0.12f, 1.00f);
        colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.13f, 0.15f, 0.19f, 1.00f);

        colors[(int)ImGuiCol.CheckMark] = new Vector4(0.45f, 0.72f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.40f, 0.66f, 0.94f, 1.00f);
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.50f, 0.76f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.Separator] = new Vector4(0.20f, 0.23f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.28f, 0.54f, 0.86f, 0.60f);
    }

    private static string ResolveInitialProjectRoot(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--project", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(args[i + 1]))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "SampleProject");
        if (Directory.Exists(cwdCandidate))
        {
            return Path.GetFullPath(cwdCandidate);
        }

        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SampleProject");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static int? ResolveSmokeFrames(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--smoke-frames", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int frames) &&
                frames > 0)
            {
                return frames;
            }
        }

        return null;
    }

    private static bool ResolveNoSplash(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--no-splash", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static class DockBuilderNative
    {
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderGetNode")]
        public static extern IntPtr GetNode(uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderRemoveNode")]
        public static extern void RemoveNode(uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderAddNode")]
        public static extern uint AddNode(uint nodeId, ImGuiDockNodeFlags flags);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSetNodePos")]
        public static extern void SetNodePos(uint nodeId, Vector2 pos);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSetNodeSize")]
        public static extern void SetNodeSize(uint nodeId, Vector2 size);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSplitNode")]
        public static extern uint SplitNode(
            uint nodeId,
            ImGuiDir splitDir,
            float sizeRatioForNodeAtDir,
            out uint outIdAtDir,
            out uint outIdAtOppositeDir);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderDockWindow")]
        public static extern void DockWindow(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string windowName,
            uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderFinish")]
        public static extern void Finish(uint nodeId);
    }
}
