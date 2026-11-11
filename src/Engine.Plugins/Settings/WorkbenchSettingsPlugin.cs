using System.Numerics;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Engine.Core.Plugins;
using ImGuiNET;

namespace Engine.Plugins.Settings;

public sealed record OpenSettingsRequestEvent(string? PreferredTab = null);

public sealed record WorkbenchResetLayoutEvent;

public sealed class WorkbenchSettingsPlugin : IEnginePlugin
{
    public const string ShowSplashSettingKey = "ui.showSplashScreen";

    private PluginContext? _context;
    private string _activeTab = "Plugins";

    public string Id => "workbench.settings";
    public string DisplayName => "Settings";

    public void OnLoaded(PluginContext context)
    {
        _context = context;
        _context.EventBus.Subscribe<OpenSettingsRequestEvent>(OnOpenSettingsRequested);
        _context.EventBus.Subscribe<SettingChangedEvent>(OnSettingChanged);

        WorkbenchTheme.Apply(context.Settings.GetString(WorkbenchTheme.SettingKey, WorkbenchTheme.StudioGraphiteId));

        _context.Menu.Register(new MenuItemDefinition(
            Id: "settings.workbench",
            Path: "Settings/Workbench Settings...",
            Callback: () => OpenSettingsWindow("Preferences"),
            Shortcut: "Cmd+,",
            Order: 10));

        _context.Menu.Register(new MenuItemDefinition(
            Id: "plugins.manage",
            Path: "Plugins/Manage Plugins...",
            Callback: () => OpenSettingsWindow("Plugins"),
            Order: 900));
    }

    public void OnUpdate(float deltaTime)
    {
    }

    public void OnRenderUI()
    {
        if (_context == null)
        {
            return;
        }

        bool isVisible = _context.Plugins?.IsWindowVisible(Id) ?? false;
        if (!isVisible)
        {
            return;
        }

        bool keepOpen = isVisible;
        ImGui.SetNextWindowSize(new Vector2(620f, 440f), ImGuiCond.FirstUseEver);
        bool open = ImGui.Begin("Settings", ref keepOpen);

        if (!keepOpen)
        {
            _context.Plugins?.SetWindowVisible(Id, false);
        }

        if (!open)
        {
            ImGui.End();
            return;
        }

        RenderTabHeader("Plugins");
        ImGui.SameLine(0f, 6f);
        RenderTabHeader("Preferences");
        ImGui.Separator();
        ImGui.Spacing();

        if (string.Equals(_activeTab, "Plugins", StringComparison.Ordinal))
        {
            RenderPluginsTab();
        }
        else
        {
            RenderPreferencesTab();
        }

        ImGui.End();
    }

    private void RenderTabHeader(string tabName)
    {
        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        bool active = string.Equals(_activeTab, tabName, StringComparison.Ordinal);
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, theme.AccentButton);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.AccentButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.AccentButtonActive);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, theme.SubtleButton);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.SubtleButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ButtonActive);
        }

        if (ImGui.Button(tabName, new Vector2(110f, 0f)))
        {
            _activeTab = tabName;
        }

        ImGui.PopStyleColor(3);
    }

    public void OnUnloaded()
    {
        if (_context != null)
        {
            _context.Menu.Unregister("settings.workbench");
            _context.Menu.Unregister("plugins.manage");
            _context.EventBus.Unsubscribe<OpenSettingsRequestEvent>(OnOpenSettingsRequested);
            _context.EventBus.Unsubscribe<SettingChangedEvent>(OnSettingChanged);
        }
    }

    private void OnOpenSettingsRequested(OpenSettingsRequestEvent evt)
    {
        OpenSettingsWindow(evt.PreferredTab);
    }

    private void OnSettingChanged(SettingChangedEvent evt)
    {
        if (_context == null)
        {
            return;
        }

        if (string.Equals(evt.Key, WorkbenchTheme.SettingKey, StringComparison.Ordinal))
        {
            WorkbenchTheme.Apply(_context.Settings.GetString(WorkbenchTheme.SettingKey, WorkbenchTheme.StudioGraphiteId));
        }
    }

    private void OpenSettingsWindow(string? tab)
    {
        if (!string.IsNullOrWhiteSpace(tab))
        {
            _activeTab = tab;
        }

        _context?.Plugins?.SetWindowVisible(Id, true);
    }

    private void RenderPluginsTab()
    {
        if (_context?.Plugins == null)
        {
            ImGui.TextDisabled("Plugin manager not available.");
            return;
        }

        ImGui.TextDisabled("Enable or disable workbench modules, or toggle panel visibility.");
        ImGui.Spacing();

        if (ImGui.BeginTable("PluginsTable", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Module", ImGuiTableColumnFlags.WidthStretch, 0.35f);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch, 0.35f);
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 75f);
            ImGui.TableSetupColumn("Window", ImGuiTableColumnFlags.WidthFixed, 75f);
            ImGui.TableHeadersRow();

            foreach (PluginEntryInfo entry in _context.Plugins.Entries)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(entry.DisplayName);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextDisabled(entry.Id);

                ImGui.TableSetColumnIndex(2);
                bool enabled = entry.IsEnabled;
                if (!entry.CanDisable)
                {
                    ImGui.BeginDisabled();
                    ImGui.Checkbox($"##en_{entry.Id}", ref enabled);
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip("Core settings plugin stays active");
                    }
                }
                else if (ImGui.Checkbox($"##en_{entry.Id}", ref enabled))
                {
                    _context.Plugins.SetPluginEnabled(entry.Id, enabled);
                }

                ImGui.TableSetColumnIndex(3);
                bool winVisible = entry.IsWindowVisible;
                if (!entry.IsEnabled)
                {
                    ImGui.BeginDisabled();
                    ImGui.Checkbox($"##vis_{entry.Id}", ref winVisible);
                    ImGui.EndDisabled();
                }
                else if (ImGui.Checkbox($"##vis_{entry.Id}", ref winVisible))
                {
                    _context.Plugins.SetWindowVisible(entry.Id, winVisible);
                }
            }

            // Inspector companion window row
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Inspector Panel");
            ImGui.TableSetColumnIndex(1);
            ImGui.TextDisabled("ui.window.inspector");
            ImGui.TableSetColumnIndex(2);
            bool sceneEnabled = _context.Plugins.IsPluginEnabled("engine.scene");
            ImGui.BeginDisabled();
            ImGui.Checkbox("##en_inspector", ref sceneEnabled);
            ImGui.EndDisabled();
            ImGui.TableSetColumnIndex(3);
            bool inspectorVisible = _context.Settings.GetBool("ui.window.inspector.visible", true);
            if (!sceneEnabled)
            {
                ImGui.BeginDisabled();
                ImGui.Checkbox("##vis_inspector", ref inspectorVisible);
                ImGui.EndDisabled();
            }
            else if (ImGui.Checkbox("##vis_inspector", ref inspectorVisible))
            {
                _context.Settings.SetBool("ui.window.inspector.visible", inspectorVisible);
            }

            ImGui.EndTable();
        }
    }

    private void RenderPreferencesTab()
    {
        if (_context == null)
        {
            return;
        }

        ISettingsService settings = _context.Settings;

        ImGui.TextDisabled("APPEARANCE");

        string currentThemeSetting = settings.GetString(WorkbenchTheme.SettingKey, WorkbenchTheme.StudioGraphiteId);
        WorkbenchThemePalette activeTheme = WorkbenchTheme.Resolve(currentThemeSetting);
        int selectedThemeIdx = 0;
        string[] themeNames = new string[WorkbenchTheme.Presets.Count];
        for (int i = 0; i < WorkbenchTheme.Presets.Count; i++)
        {
            themeNames[i] = WorkbenchTheme.Presets[i].DisplayName;
            if (string.Equals(WorkbenchTheme.Presets[i].Id, activeTheme.Id, StringComparison.Ordinal))
            {
                selectedThemeIdx = i;
            }
        }

        if (ImGui.Combo("Theme", ref selectedThemeIdx, themeNames, themeNames.Length) &&
            selectedThemeIdx >= 0 &&
            selectedThemeIdx < WorkbenchTheme.Presets.Count)
        {
            string nextThemeId = WorkbenchTheme.Presets[selectedThemeIdx].Id;
            settings.SetString(WorkbenchTheme.SettingKey, nextThemeId);
            WorkbenchTheme.Apply(nextThemeId);
        }

        float fontScale = settings.GetFloat("ui.fontScale", 1.0f);
        if (ImGui.SliderFloat("UI Scale", ref fontScale, 0.85f, 1.40f, "%.2fx"))
        {
            settings.SetFloat("ui.fontScale", Math.Clamp(fontScale, 0.85f, 1.40f));
        }

        bool showSplashScreen = settings.GetBool(ShowSplashSettingKey, true);
        if (ImGui.Checkbox("Show Splash Screen on Startup", ref showSplashScreen))
        {
            settings.SetBool(ShowSplashSettingKey, showSplashScreen);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("VIEWPORT");

        bool showGrid = settings.GetBool("viewport.showGrid", true);
        if (ImGui.Checkbox("Show Grid Overlay", ref showGrid))
        {
            settings.SetBool("viewport.showGrid", showGrid);
        }

        bool snapEnabled = settings.GetBool("viewport.snapEnabled", false);
        if (ImGui.Checkbox("Snap Move to Grid", ref snapEnabled))
        {
            settings.SetBool("viewport.snapEnabled", snapEnabled);
        }

        float snapSize = settings.GetFloat("viewport.snapSize", 16f);
        if (ImGui.SliderFloat("Grid Snap Size (px)", ref snapSize, 4f, 64f, "%.0f px"))
        {
            settings.SetFloat("viewport.snapSize", Math.Clamp(snapSize, 4f, 64f));
        }

        Vector3 bgColor = new(
            settings.GetFloat("viewport.bgR", 20f / 255f),
            settings.GetFloat("viewport.bgG", 23f / 255f),
            settings.GetFloat("viewport.bgB", 29f / 255f));
        if (ImGui.ColorEdit3("Canvas Background", ref bgColor))
        {
            settings.SetFloat("viewport.bgR", Math.Clamp(bgColor.X, 0f, 1f));
            settings.SetFloat("viewport.bgG", Math.Clamp(bgColor.Y, 0f, 1f));
            settings.SetFloat("viewport.bgB", Math.Clamp(bgColor.Z, 0f, 1f));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("DOCUMENTS & STORAGE");

        bool autoSave = settings.GetBool("scene.autoSave", false);
        if (ImGui.Checkbox("Auto-Save Scenes on Edit", ref autoSave))
        {
            settings.SetBool("scene.autoSave", autoSave);
        }

        ImGui.TextDisabled($"Settings File: {settings.FilePath}".Replace("%", "%%", StringComparison.Ordinal));

        ImGui.Spacing();
        if (ImGui.Button("Reset Dock Layout"))
        {
            _context.EventBus.Publish(new WorkbenchResetLayoutEvent());
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Defaults"))
        {
            settings.SetString(WorkbenchTheme.SettingKey, WorkbenchTheme.StudioGraphiteId);
            WorkbenchTheme.Apply(WorkbenchTheme.StudioGraphiteId);
            settings.SetFloat("ui.fontScale", 1.0f);
            settings.SetBool(ShowSplashSettingKey, true);
            settings.SetBool("viewport.showGrid", true);
            settings.SetBool("viewport.snapEnabled", false);
            settings.SetFloat("viewport.snapSize", 16f);
            settings.SetFloat("viewport.bgR", 20f / 255f);
            settings.SetFloat("viewport.bgG", 23f / 255f);
            settings.SetFloat("viewport.bgB", 29f / 255f);
            settings.SetBool("scene.autoSave", false);
            settings.Save();
        }
    }
}
