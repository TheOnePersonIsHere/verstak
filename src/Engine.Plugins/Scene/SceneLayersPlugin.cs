using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Engine.Core.Plugins;
using Engine.Plugins.Settings;
using Engine.Plugins.Timeline;
using Engine.Plugins.Viewport;
using Engine.Plugins.Workspace;
using ImGuiNET;
using Raylib_cs;

namespace Engine.Plugins.Scene;

public sealed class SceneLayersPlugin : IEnginePlugin
{
    private const string InspectorVisibleSettingKey = "ui.window.inspector.visible";

    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed record UndoSnapshot(List<LayerItem> Items, string? SelectedId);

    private PluginContext? _context;
    private LayerDocument _document = new();
    private string? _activeSceneFilePath;
    private List<string> _sceneFiles = new();
    private string? _selectedItemId;
    private string? _lastSelectedSpriteRelativePath;

    private readonly Stack<UndoSnapshot> _undoStack = new();
    private readonly Stack<UndoSnapshot> _redoStack = new();
    private UndoSnapshot? _pendingDragPreSnapshot;

    private readonly Dictionary<string, float> _timelineOverrides = new(StringComparer.Ordinal);
    private readonly HashSet<string> _manualOverrideBlockSet = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _spriteTextureCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingSpriteLogged = new(StringComparer.Ordinal);
    private bool _timelineDriving;

    private bool _isDirty;
    private float _autoSaveTimer;
    private float _elapsedTime;
    private string _statusText = "Ready";
    private DateTime _suppressReloadUntilUtc = DateTime.MinValue;
    private bool _initialSelectionSyncPending = true;
    private string? _lastProjectRootLoaded;
    private ViewportSelectionStateEvent _lastPublishedSelection = ViewportSelectionStateEvent.None;

    private string _newTagKey = string.Empty;
    private string _newTagValue = string.Empty;

    public string Id => "engine.scene";
    public string DisplayName => "Layers";

    public string? SelectedItemId => _selectedItemId;
    public LayerDocument ActiveDocument => _document;

    public void OnLoaded(PluginContext context)
    {
        _context = context;

        _context.EventBus.Subscribe<ProjectLoadedEvent>(OnProjectLoaded);
        _context.EventBus.Subscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
        _context.EventBus.Subscribe<AssetModifiedEvent>(OnAssetModified);
        _context.EventBus.Subscribe<AssetSelectedEvent>(OnAssetSelected);
        _context.EventBus.Subscribe<ActiveDocumentChangedEvent>(OnActiveDocumentChanged);
        _context.EventBus.Subscribe<ViewportRenderPassEvent>(OnViewportRenderPass);
        _context.EventBus.Subscribe<ViewportCanvasClickEvent>(OnViewportCanvasClick);
        _context.EventBus.Subscribe<ViewportTransformCommitEvent>(OnViewportTransformCommit);
        _context.EventBus.Subscribe<TimelinePropertyEvaluatedEvent>(OnTimelinePropertyEvaluated);
        _context.EventBus.Subscribe<TimelineStateChangedEvent>(OnTimelineStateChanged);

        RegisterMenuItems();
        RefreshSceneFileList();
        LoadInitialScene();
        PublishSelectionState(force: true);
        _lastProjectRootLoaded = Path.GetFullPath(context.Workspace.ProjectRoot);
    }

    public void OnUpdate(float deltaTime)
    {
        if (_context == null)
        {
            return;
        }

        _elapsedTime += deltaTime;

        HandleSceneHotkeys();

        if (_isDirty && _context.Settings.GetBool("scene.autoSave", false))
        {
            _autoSaveTimer += deltaTime;
            if (_autoSaveTimer >= 1.2f)
            {
                _autoSaveTimer = 0f;
                SaveActiveScene();
            }
        }

        bool forceInitialSync = _initialSelectionSyncPending;
        _initialSelectionSyncPending = false;
        PublishSelectionState(force: forceInitialSync);
    }

    public void OnRenderUI()
    {
        if (_context == null)
        {
            return;
        }

        RenderLayersWindow();
        RenderInspectorWindow();
    }

    public void OnUnloaded()
    {
        if (_context != null)
        {
            _context.Menu.UnregisterByPrefix("scene.");
            _context.EventBus.Unsubscribe<ProjectLoadedEvent>(OnProjectLoaded);
            _context.EventBus.Unsubscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
            _context.EventBus.Unsubscribe<AssetModifiedEvent>(OnAssetModified);
            _context.EventBus.Unsubscribe<AssetSelectedEvent>(OnAssetSelected);
            _context.EventBus.Unsubscribe<ActiveDocumentChangedEvent>(OnActiveDocumentChanged);
            _context.EventBus.Unsubscribe<ViewportRenderPassEvent>(OnViewportRenderPass);
            _context.EventBus.Unsubscribe<ViewportCanvasClickEvent>(OnViewportCanvasClick);
            _context.EventBus.Unsubscribe<ViewportTransformCommitEvent>(OnViewportTransformCommit);
            _context.EventBus.Unsubscribe<TimelinePropertyEvaluatedEvent>(OnTimelinePropertyEvaluated);
            _context.EventBus.Unsubscribe<TimelineStateChangedEvent>(OnTimelineStateChanged);
            _context.EventBus.Publish(ViewportSelectionStateEvent.None);
        }

        InvalidateAllSpriteTextures();
    }

    public void CreateNewScene()
    {
        if (_context == null)
        {
            return;
        }

        try
        {
            string scenesDir = _context.Workspace.ResolvePath("scenes");
            Directory.CreateDirectory(scenesDir);

            int index = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(scenesDir, $"scene_{index}.scene.json");
                index++;
            }
            while (File.Exists(candidate));

            var doc = new LayerDocument
            {
                Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(candidate)),
                Items = new List<LayerItem>()
            };

            _document = doc;
            _activeSceneFilePath = candidate;
            _selectedItemId = null;
            _undoStack.Clear();
            _redoStack.Clear();
            _isDirty = false;

            SaveActiveScene();
            RefreshSceneFileList();
            PublishSelectionState(force: true);
        }
        catch (Exception ex)
        {
            _statusText = $"New scene failed: {ex.Message}";
            _context.EventBus.Publish(new EngineLogEvent("SceneLayers", "Error", $"Failed to create new scene: {ex.Message}"));
        }
    }

    public void SaveActiveScene()
    {
        if (_context == null)
        {
            return;
        }

        string? targetPath = null;
        string? tempPath = null;
        bool watcherSuppressed = false;

        try
        {
            if (string.IsNullOrWhiteSpace(_activeSceneFilePath))
            {
                string scenesDir = _context.Workspace.ResolvePath("scenes");
                Directory.CreateDirectory(scenesDir);
                string baseName = string.IsNullOrWhiteSpace(_document.Name) ? "main" : _document.Name;
                _activeSceneFilePath = Path.Combine(scenesDir, $"{baseName}.scene.json");
            }

            targetPath = Path.GetFullPath(_activeSceneFilePath);
            tempPath = targetPath + ".tmp";

            string? dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _suppressReloadUntilUtc = DateTime.UtcNow.AddMilliseconds(1000);
            _context.EventBus.Publish(new FileWatcherSelfSaveEvent(targetPath, IsSaving: true));
            watcherSuppressed = true;

            string json = JsonSerializer.Serialize(_document, WriteJsonOptions);

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, targetPath, overwrite: true);

            _activeSceneFilePath = targetPath;
            _isDirty = false;
            _autoSaveTimer = 0f;
            string rel = _context.Workspace.ToRelativePath(targetPath);
            _statusText = $"Saved {rel}";
            RefreshSceneFileList();
            _context.EventBus.Publish(new EngineLogEvent("SceneLayers", "Info", $"Saved scene atomically: {rel}"));
        }
        catch (Exception ex)
        {
            _statusText = $"Save failed: {ex.Message}";
            _context.EventBus.Publish(new EngineLogEvent("SceneLayers", "Error", $"Failed to save scene: {ex.Message}"));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best-effort cleanup of temp save buffer.
                }
            }

            if (watcherSuppressed && !string.IsNullOrWhiteSpace(targetPath))
            {
                _context.EventBus.Publish(new FileWatcherSelfSaveEvent(targetPath, IsSaving: false));
            }
        }
    }

    public bool LoadSceneFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(fullPath, Encoding.UTF8);
            LayerDocument? loaded = JsonSerializer.Deserialize<LayerDocument>(json, ReadJsonOptions);
            if (loaded == null)
            {
                return false;
            }

            loaded.Items ??= new List<LayerItem>();
            _document = loaded;
            _activeSceneFilePath = Path.GetFullPath(fullPath);
            _selectedItemId = _document.Items.FirstOrDefault()?.Id;
            _undoStack.Clear();
            _redoStack.Clear();
            _manualOverrideBlockSet.Clear();
            _isDirty = false;
            _autoSaveTimer = 0f;

            string rel = _context?.Workspace.ToRelativePath(_activeSceneFilePath) ?? Path.GetFileName(fullPath);
            _statusText = $"Loaded {rel} ({_document.Items.Count} layers)";
            PublishSelectionState(force: true);
            return true;
        }
        catch (Exception ex)
        {
            _statusText = $"JSON error: {ex.Message}";
            return false;
        }
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        _redoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        MarkDirty();
        PublishSelectionState(force: true);
        _statusText = "Undo";
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0)
        {
            return false;
        }

        _undoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        MarkDirty();
        PublishSelectionState(force: true);
        _statusText = "Redo";
        return true;
    }

    public WorldLayerTransform ComputeWorldTransform(LayerItem item)
    {
        return ComputeWorldTransformInternal(item, new HashSet<string>(StringComparer.Ordinal), depth: 0);
    }

    private WorldLayerTransform ComputeWorldTransformInternal(LayerItem item, HashSet<string> visited, int depth)
    {
        Vector2 localPos = new(GetEffectiveFloat(item, "x", item.X), GetEffectiveFloat(item, "y", item.Y));
        float localRot = GetEffectiveFloat(item, "rotation", item.Rotation);
        Vector2 localScale = new(GetEffectiveScaleX(item), GetEffectiveScaleY(item));

        if (string.IsNullOrWhiteSpace(item.ParentId) ||
            depth >= 32 ||
            !visited.Add(item.Id))
        {
            return new WorldLayerTransform(localPos, localRot, localScale);
        }

        LayerItem? parent = _document.Items.FirstOrDefault(x => string.Equals(x.Id, item.ParentId, StringComparison.Ordinal));
        if (parent == null)
        {
            return new WorldLayerTransform(localPos, localRot, localScale);
        }

        WorldLayerTransform parentWorld = ComputeWorldTransformInternal(parent, visited, depth + 1);
        Vector2 scaledOffset = new(localPos.X * parentWorld.Scale.X, localPos.Y * parentWorld.Scale.Y);
        Vector2 rotatedOffset = RotateVector(scaledOffset, parentWorld.RotationDegrees);

        return new WorldLayerTransform(
            parentWorld.Position + rotatedOffset,
            parentWorld.RotationDegrees + localRot,
            new Vector2(parentWorld.Scale.X * localScale.X, parentWorld.Scale.Y * localScale.Y));
    }

    private float GetEffectiveFloat(LayerItem item, string propSuffix, float fallback)
    {
        if (_timelineDriving && !_manualOverrideBlockSet.Contains(item.Id))
        {
            if (item.Tags != null &&
                item.Tags.TryGetValue("bindPrefix", out string? prefix) &&
                !string.IsNullOrWhiteSpace(prefix) &&
                _timelineOverrides.TryGetValue($"{prefix}.{propSuffix}", out float boundVal))
            {
                return boundVal;
            }

            if (_timelineOverrides.TryGetValue($"layer.{item.Id}.{propSuffix}", out float directVal))
            {
                return directVal;
            }
        }

        return fallback;
    }

    private float GetEffectiveScaleX(LayerItem item)
    {
        if (_timelineDriving && !_manualOverrideBlockSet.Contains(item.Id))
        {
            if (item.Tags != null &&
                item.Tags.TryGetValue("bindPrefix", out string? prefix) &&
                !string.IsNullOrWhiteSpace(prefix))
            {
                if (_timelineOverrides.TryGetValue($"{prefix}.scaleX", out float sx)) return sx;
                if (_timelineOverrides.TryGetValue($"{prefix}.scale", out float uniform)) return uniform;
            }

            if (_timelineOverrides.TryGetValue($"layer.{item.Id}.scaleX", out float dsx)) return dsx;
            if (_timelineOverrides.TryGetValue($"layer.{item.Id}.scale", out float du)) return du;
        }

        return item.ScaleX;
    }

    private float GetEffectiveScaleY(LayerItem item)
    {
        if (_timelineDriving && !_manualOverrideBlockSet.Contains(item.Id))
        {
            if (item.Tags != null &&
                item.Tags.TryGetValue("bindPrefix", out string? prefix) &&
                !string.IsNullOrWhiteSpace(prefix))
            {
                if (_timelineOverrides.TryGetValue($"{prefix}.scaleY", out float sy)) return sy;
                if (_timelineOverrides.TryGetValue($"{prefix}.scale", out float uniform)) return uniform;
            }

            if (_timelineOverrides.TryGetValue($"layer.{item.Id}.scaleY", out float dsy)) return dsy;
            if (_timelineOverrides.TryGetValue($"layer.{item.Id}.scale", out float du)) return du;
        }

        return item.ScaleY;
    }

    private void RegisterMenuItems()
    {
        if (_context == null)
        {
            return;
        }

        _context.Menu.Register(new MenuItemDefinition(
            Id: "scene.new",
            Path: "File/New Scene",
            Callback: CreateNewScene,
            Order: 10));

        _context.Menu.Register(new MenuItemDefinition(
            Id: "scene.save",
            Path: "File/Save Scene",
            Callback: SaveActiveScene,
            Shortcut: "Cmd+S",
            IsEnabled: () => _isDirty || !string.IsNullOrWhiteSpace(_activeSceneFilePath),
            Order: 20));

        _context.Menu.Register(new MenuItemDefinition(
            Id: "scene.export_cs",
            Path: "File/Export Scene C#",
            Callback: ExportSceneToCSharp,
            IsEnabled: () => _document.Items.Count > 0,
            Order: 30));
    }

    private void HandleSceneHotkeys()
    {
        if (_context == null ||
            ImGui.GetCurrentContext() == IntPtr.Zero ||
            ImGui.GetIO().WantTextInput)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        bool primaryMod = io.KeySuper || io.KeyCtrl;

        if (primaryMod && !io.KeyAlt)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Z))
            {
                if (io.KeyShift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }
            }
            else if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Y))
            {
                Redo();
            }
            else if (!OperatingSystem.IsMacOS() && ImGui.IsKeyPressed(ImGuiKey.S) && !io.KeyShift)
            {
                SaveActiveScene();
            }
        }
    }

    private void RenderLayersWindow()
    {
        if (_context == null)
        {
            return;
        }

        bool isVisible = _context.Plugins?.IsWindowVisible(Id) ?? true;
        if (!isVisible)
        {
            return;
        }

        bool keepOpen = isVisible;
        if (!ImGui.Begin("Layers", ref keepOpen))
        {
            ImGui.End();
            if (!keepOpen)
            {
                _context.Plugins?.SetWindowVisible(Id, false);
            }

            return;
        }

        if (!keepOpen)
        {
            _context.Plugins?.SetWindowVisible(Id, false);
        }

        // Scene selector bar
        string activeSceneLabel = !string.IsNullOrWhiteSpace(_activeSceneFilePath)
            ? _context.Workspace.ToRelativePath(_activeSceneFilePath)
            : "(Unsaved Scene)";
        if (_isDirty)
        {
            activeSceneLabel += " *";
        }

        float saveW = 54f;
        float comboW = MathF.Max(100f, ImGui.GetContentRegionAvail().X - saveW - ImGui.GetStyle().ItemSpacing.X);
        ImGui.SetNextItemWidth(comboW);
        if (ImGui.BeginCombo("##SceneSelector", activeSceneLabel))
        {
            foreach (string file in _sceneFiles)
            {
                string rel = _context.Workspace.ToRelativePath(file);
                bool selected = string.Equals(file, _activeSceneFilePath, StringComparison.Ordinal);
                if (ImGui.Selectable(rel, selected))
                {
                    LoadSceneFile(file);
                }
            }

            ImGui.Separator();
            if (ImGui.Selectable("+ New Scene..."))
            {
                CreateNewScene();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        bool highlightSave = _isDirty;
        if (highlightSave)
        {
            WorkbenchThemePalette theme = WorkbenchTheme.Current;
            ImGui.PushStyleColor(ImGuiCol.Button, theme.AccentButton);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.AccentButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.AccentButtonActive);
        }

        if (ImGui.Button("Save", new Vector2(saveW, 0f)))
        {
            SaveActiveScene();
        }

        if (highlightSave)
        {
            ImGui.PopStyleColor(3);
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Save Scene (Cmd+S)");

        // Layer action toolbar
        if (ImGui.Button("+ Add"))
        {
            ImGui.OpenPopup("AddLayerMenu");
        }

        if (ImGui.BeginPopup("AddLayerMenu"))
        {
            if (ImGui.MenuItem("Sprite")) AddLayer(LayerKinds.Sprite);
            if (ImGui.MenuItem("Rect")) AddLayer(LayerKinds.Rect);
            if (ImGui.MenuItem("Text")) AddLayer(LayerKinds.Text);
            if (ImGui.MenuItem("Group")) AddLayer(LayerKinds.Group);
            if (ImGui.MenuItem("Surface (Offscreen)")) AddLayer(LayerKinds.Surface);
            ImGui.EndPopup();
        }

        ImGui.SameLine(0f, 4f);
        if (_undoStack.Count == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Undo")) Undo();
        if (_undoStack.Count == 0) ImGui.EndDisabled();

        ImGui.SameLine(0f, 4f);
        if (_redoStack.Count == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Redo")) Redo();
        if (_redoStack.Count == 0) ImGui.EndDisabled();

        LayerItem? selectedItem = GetSelectedItem();
        if (selectedItem != null)
        {
            ImGui.SameLine(0f, 4f);
            if (ImGui.Button("Dup")) DuplicateLayer(selectedItem);
            ImGui.SameLine(0f, 4f);
            if (ImGui.Button("Del")) DeleteLayer(selectedItem);
        }

        ImGui.Separator();

        float footerHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y * 2f + 4f;
        ImGui.BeginChild("LayersTreeScroll", new Vector2(0f, -footerHeight), ImGuiChildFlags.None);
        if (_document.Items.Count == 0)
        {
            ImGui.TextDisabled("No layers yet. Click '+ Add' to create one.");
        }
        else
        {
            RenderLayerHierarchy();
        }
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.TextDisabled(_statusText.Replace("%", "%%", StringComparison.Ordinal));
        ImGui.End();
    }

    private void RenderLayerHierarchy()
    {
        var itemIds = _document.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        List<LayerItem> roots = _document.Items
            .Where(i => string.IsNullOrWhiteSpace(i.ParentId) || !itemIds.Contains(i.ParentId))
            .OrderBy(i => i.ZOrder)
            .ToList();

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (LayerItem root in roots)
        {
            RenderLayerTreeNode(root, visited, depth: 0);
        }
    }

    private void RenderLayerTreeNode(LayerItem item, HashSet<string> visited, int depth)
    {
        if (!visited.Add(item.Id))
        {
            return;
        }

        List<LayerItem> children = _document.Items
            .Where(c => string.Equals(c.ParentId, item.Id, StringComparison.Ordinal))
            .OrderBy(c => c.ZOrder)
            .ToList();

        ImGui.PushID(item.Id);

        if (ImGui.SmallButton(item.Visible ? "V" : "-"))
        {
            PushUndoSnapshot();
            item.Visible = !item.Visible;
            MarkDirty();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(item.Visible ? "Hide layer" : "Show layer");

        ImGui.SameLine(0f, 4f);
        if (ImGui.SmallButton(item.Locked ? "L" : "."))
        {
            PushUndoSnapshot();
            item.Locked = !item.Locked;
            MarkDirty();
            PublishSelectionState(force: true);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(item.Locked ? "Unlock layer" : "Lock layer");

        ImGui.SameLine(0f, 6f);

        if (depth > 0)
        {
            ImGui.Indent(depth * 14f);
        }

        string badge = GetKindBadge(item.Kind, out Vector4 badgeColor);
        ImGui.TextColored(badgeColor, badge);
        ImGui.SameLine(0f, 6f);

        bool selected = string.Equals(_selectedItemId, item.Id, StringComparison.Ordinal);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;
        if (children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        bool opened = ImGui.TreeNodeEx($"{item.Name} (z:{item.ZOrder})##node_{item.Id}", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            _selectedItemId = item.Id;
            PublishSelectionState(force: true);
        }

        if (depth > 0)
        {
            ImGui.Unindent(depth * 14f);
        }

        if (children.Count > 0 && opened)
        {
            foreach (LayerItem child in children)
            {
                RenderLayerTreeNode(child, visited, depth + 1);
            }

            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private void RenderInspectorWindow()
    {
        if (_context == null)
        {
            return;
        }

        bool isVisible = _context.Settings.GetBool(InspectorVisibleSettingKey, true);
        if (!isVisible)
        {
            return;
        }

        bool keepOpen = isVisible;
        if (!ImGui.Begin("Inspector", ref keepOpen))
        {
            ImGui.End();
            if (!keepOpen)
            {
                _context.Settings.SetBool(InspectorVisibleSettingKey, false);
            }

            return;
        }

        if (!keepOpen)
        {
            _context.Settings.SetBool(InspectorVisibleSettingKey, false);
        }

        LayerItem? item = GetSelectedItem();
        if (item == null)
        {
            ImGui.TextDisabled("Select a layer in Viewport or Layers panel.");
            ImGui.Spacing();
            if (_document.Items.Count > 0 && ImGui.Button("Copy Entire Scene C#", new Vector2(-1f, 0f)))
            {
                string code = GenerateSceneClassCSharp(_document);
                ImGui.SetClipboardText(code);
                _statusText = "Copied scene C# to clipboard";
            }

            ImGui.End();
            return;
        }

        ImGui.PushID($"insp_{item.Id}");

        // Identity
        string name = item.Name;
        if (ImGui.InputText("Name", ref name, 128))
        {
            item.Name = name;
            MarkDirty();
            PublishSelectionState(force: true);
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        int kindIdx = Array.IndexOf(LayerKinds.All, item.Kind);
        if (kindIdx < 0) kindIdx = 0;
        if (ImGui.Combo("Kind", ref kindIdx, LayerKinds.All, LayerKinds.All.Length))
        {
            PushUndoSnapshot();
            item.Kind = LayerKinds.All[kindIdx];
            MarkDirty();
            PublishSelectionState(force: true);
        }

        // Parent combo
        string parentLabel = "(Root)";
        if (!string.IsNullOrWhiteSpace(item.ParentId))
        {
            LayerItem? parentItem = _document.Items.FirstOrDefault(x => string.Equals(x.Id, item.ParentId, StringComparison.Ordinal));
            parentLabel = parentItem != null ? $"{parentItem.Name} ({parentItem.Id})" : item.ParentId;
        }

        if (ImGui.BeginCombo("Parent", parentLabel))
        {
            if (ImGui.Selectable("(Root)", string.IsNullOrWhiteSpace(item.ParentId)))
            {
                PushUndoSnapshot();
                ConvertParentPreservingWorld(item, null);
                MarkDirty();
                PublishSelectionState(force: true);
            }

            foreach (LayerItem candidate in _document.Items)
            {
                if (string.Equals(candidate.Id, item.Id, StringComparison.Ordinal) ||
                    IsDescendant(candidate.Id, item.Id))
                {
                    continue;
                }

                bool selectedParent = string.Equals(item.ParentId, candidate.Id, StringComparison.Ordinal);
                if (ImGui.Selectable($"{candidate.Name}##p_{candidate.Id}", selectedParent))
                {
                    PushUndoSnapshot();
                    ConvertParentPreservingWorld(item, candidate.Id);
                    MarkDirty();
                    PublishSelectionState(force: true);
                }
            }

            ImGui.EndCombo();
        }

        int zOrder = item.ZOrder;
        if (ImGui.DragInt("Z-Order", ref zOrder, 0.25f))
        {
            item.ZOrder = zOrder;
            MarkDirty();
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        bool visible = item.Visible;
        if (ImGui.Checkbox("Visible", ref visible))
        {
            PushUndoSnapshot();
            item.Visible = visible;
            MarkDirty();
        }
        ImGui.SameLine();
        bool locked = item.Locked;
        if (ImGui.Checkbox("Locked", ref locked))
        {
            PushUndoSnapshot();
            item.Locked = locked;
            MarkDirty();
            PublishSelectionState(force: true);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("TRANSFORM (LOCAL)");

        Vector2 pos = new(item.X, item.Y);
        if (ImGui.DragFloat2("Position", ref pos, 1f))
        {
            _manualOverrideBlockSet.Add(item.Id);
            item.X = pos.X;
            item.Y = pos.Y;
            MarkDirty();
            PublishSelectionState(force: true);
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        float rot = item.Rotation;
        if (ImGui.DragFloat("Rotation", ref rot, 0.5f, -3600f, 3600f, "%.1f deg"))
        {
            _manualOverrideBlockSet.Add(item.Id);
            item.Rotation = rot;
            MarkDirty();
            PublishSelectionState(force: true);
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        Vector2 scale = new(item.ScaleX, item.ScaleY);
        if (ImGui.DragFloat2("Scale", ref scale, 0.01f, 0.01f, 20f))
        {
            _manualOverrideBlockSet.Add(item.Id);
            item.ScaleX = Math.Clamp(scale.X, 0.01f, 20f);
            item.ScaleY = Math.Clamp(scale.Y, 0.01f, 20f);
            MarkDirty();
            PublishSelectionState(force: true);
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        Vector2 origin = new(item.OriginX, item.OriginY);
        if (ImGui.DragFloat2("Origin (0..1)", ref origin, 0.01f, 0f, 1f))
        {
            item.OriginX = Math.Clamp(origin.X, 0f, 1f);
            item.OriginY = Math.Clamp(origin.Y, 0f, 1f);
            MarkDirty();
            PublishSelectionState(force: true);
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        ImGui.TextDisabled("Pivot Preset:");
        ImGui.SameLine();
        if (ImGui.SmallButton("TopLeft")) { PushUndoSnapshot(); item.OriginX = 0f; item.OriginY = 0f; MarkDirty(); PublishSelectionState(force: true); }
        ImGui.SameLine(0f, 4f);
        if (ImGui.SmallButton("Center")) { PushUndoSnapshot(); item.OriginX = 0.5f; item.OriginY = 0.5f; MarkDirty(); PublishSelectionState(force: true); }
        ImGui.SameLine(0f, 4f);
        if (ImGui.SmallButton("Bottom")) { PushUndoSnapshot(); item.OriginX = 0.5f; item.OriginY = 1.0f; MarkDirty(); PublishSelectionState(force: true); }

        Vector2 size = new(item.Width, item.Height);
        if (ImGui.DragFloat2("Size (0=Auto)", ref size, 1f, 0f, 4096f))
        {
            item.Width = MathF.Max(0f, size.X);
            item.Height = MathF.Max(0f, size.Y);
            MarkDirty();
            PublishSelectionState(force: true);
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("APPEARANCE & SHADER");

        RenderKindSpecificFields(item);

        Vector4 tint = new(item.ColorR / 255f, item.ColorG / 255f, item.ColorB / 255f, item.ColorA / 255f);
        if (ImGui.ColorEdit4("Tint", ref tint))
        {
            item.ColorR = (byte)Math.Clamp(MathF.Round(tint.X * 255f), 0f, 255f);
            item.ColorG = (byte)Math.Clamp(MathF.Round(tint.Y * 255f), 0f, 255f);
            item.ColorB = (byte)Math.Clamp(MathF.Round(tint.Z * 255f), 0f, 255f);
            item.ColorA = (byte)Math.Clamp(MathF.Round(tint.W * 255f), 0f, 255f);
            MarkDirty();
        }
        if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

        RenderShaderPicker(item);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("TIMELINE & CODE");

        if (ImGui.Button("Key Transform at Playhead", new Vector2(-1f, 0f)))
        {
            KeyLayerTransformToTimeline(item);
        }

        if (ImGui.Button("Copy Layer C# Draw", new Vector2(-1f, 0f)))
        {
            string snippet = GenerateLayerCSharpSnippet(item);
            ImGui.SetClipboardText(snippet);
            _statusText = $"Copied C# for '{item.Name}'";
        }

        if (ImGui.Button("Export Scene C#...", new Vector2(-1f, 0f)))
        {
            ExportSceneToCSharp();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        RenderTagsEditor(item);

        ImGui.PopID();
        ImGui.End();
    }

    private void RenderKindSpecificFields(LayerItem item)
    {
        if (_context == null)
        {
            return;
        }

        if (string.Equals(item.Kind, LayerKinds.Sprite, StringComparison.Ordinal))
        {
            string assetPath = item.AssetPath;
            if (ImGui.InputText("Sprite Path", ref assetPath, 260))
            {
                item.AssetPath = assetPath;
                MarkDirty();
                PublishSelectionState(force: true);
            }
            if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
            if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

            if (ImGui.BeginCombo("Pick Sprite", string.IsNullOrWhiteSpace(item.AssetPath) ? "(Choose sprite)" : Path.GetFileName(item.AssetPath)))
            {
                foreach (string spriteFull in EnumerateWorkspaceSprites())
                {
                    string rel = _context.Workspace.ToRelativePath(spriteFull);
                    bool selected = string.Equals(item.AssetPath, rel, StringComparison.Ordinal);
                    if (ImGui.Selectable(rel, selected))
                    {
                        PushUndoSnapshot();
                        item.AssetPath = rel;
                        MarkDirty();
                        PublishSelectionState(force: true);
                    }
                }

                ImGui.EndCombo();
            }
        }
        else if (string.Equals(item.Kind, LayerKinds.Surface, StringComparison.Ordinal))
        {
            string surfaceKey = item.SurfaceKey;
            if (ImGui.InputText("Surface Key", ref surfaceKey, 128))
            {
                item.SurfaceKey = surfaceKey;
                MarkDirty();
                PublishSelectionState(force: true);
            }
            if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
            if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();
        }
        else if (string.Equals(item.Kind, LayerKinds.Text, StringComparison.Ordinal))
        {
            string txt = item.Text;
            if (ImGui.InputText("Text", ref txt, 256))
            {
                item.Text = txt;
                MarkDirty();
                PublishSelectionState(force: true);
            }
            if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
            if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();

            int fontSize = item.FontSize;
            if (ImGui.DragInt("Font Size", ref fontSize, 0.5f, 8, 160))
            {
                item.FontSize = Math.Clamp(fontSize, 8, 160);
                MarkDirty();
                PublishSelectionState(force: true);
            }
            if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
            if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();
        }
    }

    private void RenderShaderPicker(LayerItem item)
    {
        if (_context == null)
        {
            return;
        }

        string currentLabel = string.IsNullOrWhiteSpace(item.ShaderPath)
            ? "(None)"
            : Path.GetFileName(item.ShaderPath);

        if (ImGui.BeginCombo("Shader (.fs)", currentLabel))
        {
            if (ImGui.Selectable("(None)", string.IsNullOrWhiteSpace(item.ShaderPath)))
            {
                PushUndoSnapshot();
                item.ShaderPath = string.Empty;
                MarkDirty();
            }

            foreach (string shaderFull in EnumerateWorkspaceShaders())
            {
                string rel = _context.Workspace.ToRelativePath(shaderFull);
                bool selected = string.Equals(item.ShaderPath, rel, StringComparison.Ordinal);
                if (ImGui.Selectable(rel, selected))
                {
                    PushUndoSnapshot();
                    item.ShaderPath = rel;
                    MarkDirty();
                }
            }

            ImGui.EndCombo();
        }

        if (!string.IsNullOrWhiteSpace(item.ShaderPath))
        {
            float intensity = item.ShaderIntensity;
            if (ImGui.SliderFloat("Shader Intensity", ref intensity, 0f, 2f, "%.2f"))
            {
                item.ShaderIntensity = Math.Clamp(intensity, 0f, 2f);
                MarkDirty();
            }
            if (ImGui.IsItemActivated()) _pendingDragPreSnapshot = CaptureSnapshot();
            if (ImGui.IsItemDeactivatedAfterEdit()) CommitInspectorSnapshot();
        }
    }

    private void RenderTagsEditor(LayerItem item)
    {
        ImGui.TextDisabled("TAGS (KEY = VALUE)");

        if (item.Tags != null)
        {
            Dictionary<string, string> tags = item.Tags;
            foreach (string key in tags.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray())
            {
                string val = tags[key];
                ImGui.TextUnformatted($"{key} = {val}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"x##tag_{key}"))
                {
                    PushUndoSnapshot();
                    tags.Remove(key);
                    if (tags.Count == 0)
                    {
                        item.Tags = null;
                        MarkDirty();
                        break;
                    }
                    MarkDirty();
                }
            }
        }

        ImGui.SetNextItemWidth(90f);
        ImGui.InputText("##tagKey", ref _newTagKey, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        ImGui.InputText("##tagVal", ref _newTagValue, 128);
        ImGui.SameLine();
        if (ImGui.Button("Add Tag") && !string.IsNullOrWhiteSpace(_newTagKey))
        {
            PushUndoSnapshot();
            item.Tags ??= new Dictionary<string, string>(StringComparer.Ordinal);
            item.Tags[_newTagKey.Trim()] = _newTagValue;
            _newTagKey = string.Empty;
            _newTagValue = string.Empty;
            MarkDirty();
        }
    }

    private void KeyLayerTransformToTimeline(LayerItem item)
    {
        if (_context == null)
        {
            return;
        }

        string prefix = item.Tags != null &&
                        item.Tags.TryGetValue("bindPrefix", out string? customPrefix) &&
                        !string.IsNullOrWhiteSpace(customPrefix)
            ? customPrefix
            : $"layer.{item.Id}";

        _context.EventBus.Publish(new TimelineUpsertKeyframeEvent($"{prefix}.x", $"{item.Name} X", item.X, SaveProjectJson: false));
        _context.EventBus.Publish(new TimelineUpsertKeyframeEvent($"{prefix}.y", $"{item.Name} Y", item.Y, SaveProjectJson: false));
        _context.EventBus.Publish(new TimelineUpsertKeyframeEvent($"{prefix}.rotation", $"{item.Name} Rot", item.Rotation, SaveProjectJson: false));

        if (string.Equals(prefix, "sprite", StringComparison.Ordinal))
        {
            _context.EventBus.Publish(new TimelineUpsertKeyframeEvent("sprite.scale", $"{item.Name} Scale", item.ScaleX, SaveProjectJson: true));
        }
        else
        {
            _context.EventBus.Publish(new TimelineUpsertKeyframeEvent($"{prefix}.scaleX", $"{item.Name} ScaleX", item.ScaleX, SaveProjectJson: false));
            _context.EventBus.Publish(new TimelineUpsertKeyframeEvent($"{prefix}.scaleY", $"{item.Name} ScaleY", item.ScaleY, SaveProjectJson: true));
        }

        _manualOverrideBlockSet.Remove(item.Id);
        _statusText = $"Keyed '{item.Name}' at {_context.Runtime.CurrentTime:F2}s";
    }

    private void ExportSceneToCSharp()
    {
        if (_context == null)
        {
            return;
        }

        string rawName = string.IsNullOrWhiteSpace(_document.Name) ? "Main" : _document.Name;
        string pascalName = ToPascalIdentifier(rawName);
        string className = $"Generated{pascalName}Scene";
        string scriptRelPath = $"scripts/{className}.cs";
        string fullPath = Path.GetFullPath(_context.Workspace.ResolvePath(scriptRelPath));
        string tempPath = fullPath + ".tmp";

        try
        {
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string code = GenerateSceneClassCSharp(_document, className);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(code);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
            _statusText = $"Exported {scriptRelPath}";
            _context.EventBus.Publish(new EngineLogEvent("SceneLayers", "Info", $"Exported scene to {scriptRelPath}"));
        }
        catch (Exception ex)
        {
            _statusText = $"Export failed: {ex.Message}";
            _context.EventBus.Publish(new EngineLogEvent("SceneLayers", "Error", $"Failed to export scene C#: {ex.Message}"));
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    private string GenerateLayerCSharpSnippet(LayerItem item)
    {
        var sb = new StringBuilder();
        AppendLayerDrawCode(sb, item, indent: "");
        return sb.ToString();
    }

    private string GenerateSceneClassCSharp(LayerDocument doc, string? classNameOverride = null)
    {
        string rawName = string.IsNullOrWhiteSpace(doc.Name) ? "Main" : doc.Name;
        string className = classNameOverride ?? $"Generated{ToPascalIdentifier(rawName)}Scene";

        var sb = new StringBuilder();
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine("using Engine.Core.Abstractions;");
        sb.AppendLine("using Raylib_cs;");
        sb.AppendLine();
        sb.AppendLine("namespace Project.Generated;");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    public static void DrawAll(IGraphicsService graphics, float time = 0f)");
        sb.AppendLine("    {");

        foreach (LayerItem item in doc.Items.OrderBy(i => i.ZOrder))
        {
            if (!item.Visible || string.Equals(item.Kind, LayerKinds.Group, StringComparison.Ordinal))
            {
                continue;
            }

            AppendLayerDrawCode(sb, item, indent: "        ");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void AppendLayerDrawCode(StringBuilder sb, LayerItem item, string indent)
    {
        WorldLayerTransform world = ComputeWorldTransform(item);
        Vector2 baseSize = ResolveBaseSize(item);
        float drawW = baseSize.X * world.Scale.X;
        float drawH = baseSize.Y * world.Scale.Y;
        float ox = drawW * item.OriginX;
        float oy = drawH * item.OriginY;

        string px = F(world.Position.X);
        string py = F(world.Position.Y);
        string rot = F(world.RotationDegrees);
        string tint = $"new Color((byte){item.ColorR}, (byte){item.ColorG}, (byte){item.ColorB}, (byte){item.ColorA})";

        sb.AppendLine($"{indent}// Layer: {item.Name} ({item.Kind}, z={item.ZOrder})");

        bool hasShader = !string.IsNullOrWhiteSpace(item.ShaderPath);
        if (hasShader)
        {
            string sh = EscapeString(item.ShaderPath);
            sb.AppendLine($"{indent}graphics.BeginShader(\"{sh}\");");
            sb.AppendLine($"{indent}graphics.SetShaderFloat(\"{sh}\", \"uTime\", time);");
            sb.AppendLine($"{indent}graphics.SetShaderFloat(\"{sh}\", \"uIntensity\", {F(item.ShaderIntensity)}f);");
            sb.AppendLine($"{indent}graphics.SetShaderVec2(\"{sh}\", \"uResolution\", new Vector2({F(drawW)}f, {F(drawH)}f));");
        }

        switch (item.Kind)
        {
            case LayerKinds.Sprite when !string.IsNullOrWhiteSpace(item.AssetPath):
            {
                Vector2 texSize = GetSpriteTextureSize(item.AssetPath);
                float scaleX = texSize.X > 0f && item.Width > 0f ? (item.Width / texSize.X) * world.Scale.X : world.Scale.X;
                float scaleY = texSize.Y > 0f && item.Height > 0f ? (item.Height / texSize.Y) * world.Scale.Y : world.Scale.Y;
                sb.AppendLine(
                    $"{indent}graphics.DrawTextureEx(\"{EscapeString(item.AssetPath)}\", " +
                    $"new Vector2({px}f, {py}f), {rot}f, new Vector2({F(scaleX)}f, {F(scaleY)}f), {tint}, " +
                    $"new Vector2({F(ox)}f, {F(oy)}f));");
                break;
            }
            case LayerKinds.Surface when !string.IsNullOrWhiteSpace(item.SurfaceKey):
            {
                sb.AppendLine(
                    $"{indent}graphics.DrawSurface(\"{EscapeString(item.SurfaceKey)}\", " +
                    $"new Vector2({px}f, {py}f), new Vector2({F(drawW)}f, {F(drawH)}f), {tint}, {rot}f, " +
                    $"new Vector2({F(ox)}f, {F(oy)}f));");
                break;
            }
            case LayerKinds.Rect:
            {
                int rx = (int)MathF.Round(world.Position.X - ox);
                int ry = (int)MathF.Round(world.Position.Y - oy);
                sb.AppendLine($"{indent}graphics.DrawRectangle({rx}, {ry}, {(int)MathF.Round(drawW)}, {(int)MathF.Round(drawH)}, {tint});");
                break;
            }
            case LayerKinds.Text:
            {
                int tx = (int)MathF.Round(world.Position.X - ox);
                int ty = (int)MathF.Round(world.Position.Y - oy);
                sb.AppendLine($"{indent}graphics.DrawText(\"{EscapeString(item.Text)}\", {tx}, {ty}, {Math.Max(8, item.FontSize)}, {tint});");
                break;
            }
        }

        if (hasShader)
        {
            sb.AppendLine($"{indent}graphics.EndShader();");
        }
    }

    public static Vector2 SnapPositionToPixel(Vector2 position)
    {
        return new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
    }

    private void OnViewportRenderPass(ViewportRenderPassEvent evt)
    {
        if (_context == null)
        {
            return;
        }

        float shaderTime = _context.Runtime.CurrentTime > 0f ? _context.Runtime.CurrentTime : _elapsedTime;

        foreach (LayerItem item in _document.Items.OrderBy(i => i.ZOrder))
        {
            if (!item.Visible || string.Equals(item.Kind, LayerKinds.Group, StringComparison.Ordinal))
            {
                continue;
            }

            WorldLayerTransform world = ComputeWorldTransform(item);
            Vector2 renderPos = SnapPositionToPixel(world.Position);
            Vector2 baseSize = ResolveBaseSize(item);
            float drawW = MathF.Max(1f, baseSize.X * world.Scale.X);
            float drawH = MathF.Max(1f, baseSize.Y * world.Scale.Y);
            Vector2 originPx = new(MathF.Round(drawW * item.OriginX), MathF.Round(drawH * item.OriginY));
            var tint = new Color(item.ColorR, item.ColorG, item.ColorB, item.ColorA);

            bool hasShader = !string.IsNullOrWhiteSpace(item.ShaderPath) &&
                             File.Exists(_context.Workspace.ResolvePath(item.ShaderPath));
            if (hasShader)
            {
                _context.Graphics.BeginShader(item.ShaderPath);
                _context.Graphics.SetShaderFloat(item.ShaderPath, "uTime", shaderTime);
                _context.Graphics.SetShaderFloat(item.ShaderPath, "uIntensity", item.ShaderIntensity);
                _context.Graphics.SetShaderVec2(item.ShaderPath, "uResolution", new Vector2(drawW, drawH));
            }

            switch (item.Kind)
            {
                case LayerKinds.Sprite when !string.IsNullOrWhiteSpace(item.AssetPath):
                {
                    if (TryGetPointSpriteTexture(item.AssetPath, out Texture2D texture))
                    {
                        Vector2 texSize = new(texture.Width, texture.Height);
                        Vector2 renderScale = world.Scale;
                        if (texSize.X > 0f && texSize.Y > 0f && item.Width > 0f && item.Height > 0f)
                        {
                            renderScale = new Vector2(
                                (item.Width / texSize.X) * world.Scale.X,
                                (item.Height / texSize.Y) * world.Scale.Y);
                        }

                        var source = new Rectangle(0f, 0f, texture.Width, texture.Height);
                        float destWidth = MathF.Max(1f, texture.Width * renderScale.X);
                        float destHeight = MathF.Max(1f, texture.Height * renderScale.Y);
                        var dest = new Rectangle(renderPos.X, renderPos.Y, destWidth, destHeight);
                        var spriteOrigin = new Vector2(
                            MathF.Round(destWidth * item.OriginX),
                            MathF.Round(destHeight * item.OriginY));

                        Raylib.SetTextureFilter(texture, TextureFilter.Point);
                        Raylib.DrawTexturePro(texture, source, dest, spriteOrigin, world.RotationDegrees, tint);
                    }
                    break;
                }

                case LayerKinds.Surface when !string.IsNullOrWhiteSpace(item.SurfaceKey):
                {
                    _context.Graphics.DrawSurface(
                        item.SurfaceKey,
                        renderPos,
                        new Vector2(drawW, drawH),
                        tint,
                        world.RotationDegrees,
                        originPx);
                    break;
                }

                case LayerKinds.Rect:
                {
                    if (MathF.Abs(world.RotationDegrees) < 0.01f)
                    {
                        _context.Graphics.DrawRectangle(
                            (int)MathF.Round(renderPos.X - originPx.X),
                            (int)MathF.Round(renderPos.Y - originPx.Y),
                            (int)MathF.Round(drawW),
                            (int)MathF.Round(drawH),
                            tint);
                    }
                    else
                    {
                        Raylib.DrawRectanglePro(
                            new Rectangle(renderPos.X, renderPos.Y, drawW, drawH),
                            originPx,
                            world.RotationDegrees,
                            tint);
                    }
                    break;
                }

                case LayerKinds.Text:
                {
                    if (!string.IsNullOrEmpty(item.Text))
                    {
                        int scaledFont = Math.Max(8, (int)MathF.Round(item.FontSize * MathF.Abs(world.Scale.Y)));
                        _context.Graphics.DrawText(
                            item.Text,
                            (int)MathF.Round(renderPos.X - originPx.X),
                            (int)MathF.Round(renderPos.Y - originPx.Y),
                            scaledFont,
                            tint);
                    }
                    break;
                }
            }

            if (hasShader)
            {
                _context.Graphics.EndShader();
            }
        }
    }

    private void OnViewportCanvasClick(ViewportCanvasClickEvent evt)
    {
        List<LayerItem> orderedTopDown = _document.Items
            .Select((item, idx) => (Item: item, Index: idx))
            .OrderByDescending(x => x.Item.ZOrder)
            .ThenByDescending(x => x.Index)
            .Select(x => x.Item)
            .ToList();

        LayerItem? hit = null;
        foreach (LayerItem item in orderedTopDown)
        {
            if (!item.Visible || string.Equals(item.Kind, LayerKinds.Group, StringComparison.Ordinal))
            {
                continue;
            }

            WorldLayerTransform world = ComputeWorldTransform(item);
            Vector2 baseSize = ResolveBaseSize(item);
            float w = MathF.Max(16f, baseSize.X * MathF.Abs(world.Scale.X));
            float h = MathF.Max(16f, baseSize.Y * MathF.Abs(world.Scale.Y));

            Vector2 local = RotateVector(evt.VirtualPosition - world.Position, -world.RotationDegrees);
            float minX = -w * item.OriginX;
            float maxX = w * (1f - item.OriginX);
            float minY = -h * item.OriginY;
            float maxY = h * (1f - item.OriginY);

            if (local.X >= minX && local.X <= maxX && local.Y >= minY && local.Y <= maxY)
            {
                hit = item;
                break;
            }
        }

        _selectedItemId = hit?.Id;
        PublishSelectionState(force: true);
    }

    private void OnViewportTransformCommit(ViewportTransformCommitEvent evt)
    {
        LayerItem? item = _document.Items.FirstOrDefault(x => string.Equals(x.Id, evt.ItemId, StringComparison.Ordinal));
        if (item == null || item.Locked)
        {
            return;
        }

        if (evt.IsBeginDrag)
        {
            _pendingDragPreSnapshot = CaptureSnapshot();
            _manualOverrideBlockSet.Add(item.Id);
            return;
        }

        WorldLayerTransform parentWorld = ResolveParentWorldTransform(item);
        Vector2 deltaWorld = evt.WorldPosition - parentWorld.Position;
        Vector2 unrotated = RotateVector(deltaWorld, -parentWorld.RotationDegrees);

        item.X = MathF.Abs(parentWorld.Scale.X) > 1e-4f ? unrotated.X / parentWorld.Scale.X : unrotated.X;
        item.Y = MathF.Abs(parentWorld.Scale.Y) > 1e-4f ? unrotated.Y / parentWorld.Scale.Y : unrotated.Y;
        item.Rotation = evt.WorldRotationDegrees - parentWorld.RotationDegrees;
        item.ScaleX = Math.Clamp(MathF.Abs(parentWorld.Scale.X) > 1e-4f ? evt.WorldScale.X / parentWorld.Scale.X : evt.WorldScale.X, 0.05f, 20f);
        item.ScaleY = Math.Clamp(MathF.Abs(parentWorld.Scale.Y) > 1e-4f ? evt.WorldScale.Y / parentWorld.Scale.Y : evt.WorldScale.Y, 0.05f, 20f);

        _manualOverrideBlockSet.Add(item.Id);
        MarkDirty();

        if (evt.IsFinalSnapshot && _pendingDragPreSnapshot != null)
        {
            PushUndoSnapshot(_pendingDragPreSnapshot);
            _pendingDragPreSnapshot = null;
        }

        PublishSelectionState(force: true);
    }

    private void OnTimelinePropertyEvaluated(TimelinePropertyEvaluatedEvent evt)
    {
        _timelineOverrides[evt.TargetProperty] = evt.Value;
    }

    private void OnTimelineStateChanged(TimelineStateChangedEvent evt)
    {
        if (evt.IsPlaying || evt.IsManualScrub)
        {
            _timelineDriving = true;
            _manualOverrideBlockSet.Clear();
            PublishSelectionState(force: false);
        }
    }

    private void OnProjectLoaded(ProjectLoadedEvent evt)
    {
        _lastProjectRootLoaded = Path.GetFullPath(evt.ProjectRoot);
        ResetForProjectSwitch();
    }

    private void OnWorkspaceChanged(WorkspaceChangedEvent evt)
    {
        string normalized = Path.GetFullPath(evt.ProjectRoot);
        if (string.Equals(_lastProjectRootLoaded, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _lastProjectRootLoaded = normalized;
        ResetForProjectSwitch();
    }

    private void ResetForProjectSwitch()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _pendingDragPreSnapshot = null;
        _manualOverrideBlockSet.Clear();
        _timelineOverrides.Clear();
        _lastSelectedSpriteRelativePath = null;
        _timelineDriving = false;
        InvalidateAllSpriteTextures();
        RefreshSceneFileList();
        LoadInitialScene();
        PublishSelectionState(force: true);
    }

    private void OnAssetSelected(AssetSelectedEvent evt)
    {
        if (_context == null)
        {
            return;
        }

        if (string.Equals(evt.AssetCategory, "sprite", StringComparison.OrdinalIgnoreCase))
        {
            _lastSelectedSpriteRelativePath = _context.Workspace.ToRelativePath(evt.FilePath);
        }
    }

    private void OnActiveDocumentChanged(ActiveDocumentChangedEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.FilePath))
        {
            return;
        }

        if (_isDirty && _context?.Settings.GetBool("scene.autoSave", false) == true)
        {
            SaveActiveScene();
        }

        LoadSceneFile(evt.FilePath);
    }

    private void OnAssetModified(AssetModifiedEvent evt)
    {
        if (_context == null)
        {
            return;
        }

        if (DateTime.UtcNow < _suppressReloadUntilUtc)
        {
            return;
        }

        string fullPath = Path.GetFullPath(evt.FilePath);
        InvalidateSpriteTexture(fullPath);

        if (fullPath.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase))
        {
            RefreshSceneFileList();
            if (string.Equals(fullPath, _activeSceneFilePath, StringComparison.Ordinal))
            {
                LoadSceneFile(fullPath);
            }
        }
    }

    private void AddLayer(string kind)
    {
        if (_context == null)
        {
            return;
        }

        PushUndoSnapshot();

        string prefix = kind.ToLowerInvariant();
        string uniqueId = $"{prefix}_{Guid.NewGuid().ToString("N")[..6]}";
        int nextZ = _document.Items.Count > 0 ? _document.Items.Max(i => i.ZOrder) + 1 : 0;

        float cx = _context.Runtime.VirtualWidth * 0.5f;
        float cy = _context.Runtime.VirtualHeight * 0.5f;

        var item = new LayerItem
        {
            Id = uniqueId,
            Name = $"{kind} {nextZ + 1}",
            Kind = kind,
            X = cx,
            Y = cy,
            ZOrder = nextZ
        };

        if (string.Equals(kind, LayerKinds.Sprite, StringComparison.Ordinal))
        {
            string? fallbackSprite = _lastSelectedSpriteRelativePath
                ?? EnumerateWorkspaceSprites().Select(p => _context.Workspace.ToRelativePath(p)).FirstOrDefault();
            item.AssetPath = fallbackSprite ?? string.Empty;
        }
        else if (string.Equals(kind, LayerKinds.Rect, StringComparison.Ordinal))
        {
            item.Width = 180f;
            item.Height = 110f;
            item.ColorR = 68;
            item.ColorG = 128;
            item.ColorB = 204;
            item.ColorA = 220;
        }
        else if (string.Equals(kind, LayerKinds.Surface, StringComparison.Ordinal))
        {
            item.SurfaceKey = "surface_0";
            item.Width = 320f;
            item.Height = 180f;
        }
        else if (string.Equals(kind, LayerKinds.Text, StringComparison.Ordinal))
        {
            item.Text = "New Label";
            item.FontSize = 28;
        }

        _document.Items.Add(item);
        _selectedItemId = item.Id;
        MarkDirty();
        PublishSelectionState(force: true);
    }

    private void DuplicateLayer(LayerItem source)
    {
        PushUndoSnapshot();
        LayerItem copy = source.Clone();
        copy.Id = $"{source.Kind.ToLowerInvariant()}_{Guid.NewGuid().ToString("N")[..6]}";
        copy.Name = $"{source.Name} Copy";
        copy.X += 24f;
        copy.Y += 24f;
        copy.ZOrder = _document.Items.Max(i => i.ZOrder) + 1;

        _document.Items.Add(copy);
        _selectedItemId = copy.Id;
        MarkDirty();
        PublishSelectionState(force: true);
    }

    private void DeleteLayer(LayerItem target)
    {
        PushUndoSnapshot();
        foreach (LayerItem child in _document.Items.Where(c => string.Equals(c.ParentId, target.Id, StringComparison.Ordinal)))
        {
            ConvertParentPreservingWorld(child, target.ParentId);
        }

        _document.Items.Remove(target);
        if (string.Equals(_selectedItemId, target.Id, StringComparison.Ordinal))
        {
            _selectedItemId = _document.Items.FirstOrDefault()?.Id;
        }

        MarkDirty();
        PublishSelectionState(force: true);
    }

    private void ConvertParentPreservingWorld(LayerItem item, string? newParentId)
    {
        WorldLayerTransform currentWorld = ComputeWorldTransform(item);
        item.ParentId = newParentId;
        WorldLayerTransform newParentWorld = ResolveParentWorldTransform(item);

        Vector2 deltaWorld = currentWorld.Position - newParentWorld.Position;
        Vector2 unrotated = RotateVector(deltaWorld, -newParentWorld.RotationDegrees);

        item.X = MathF.Abs(newParentWorld.Scale.X) > 1e-4f ? unrotated.X / newParentWorld.Scale.X : unrotated.X;
        item.Y = MathF.Abs(newParentWorld.Scale.Y) > 1e-4f ? unrotated.Y / newParentWorld.Scale.Y : unrotated.Y;
        item.Rotation = currentWorld.RotationDegrees - newParentWorld.RotationDegrees;
        item.ScaleX = MathF.Abs(newParentWorld.Scale.X) > 1e-4f ? currentWorld.Scale.X / newParentWorld.Scale.X : currentWorld.Scale.X;
        item.ScaleY = MathF.Abs(newParentWorld.Scale.Y) > 1e-4f ? currentWorld.Scale.Y / newParentWorld.Scale.Y : currentWorld.Scale.Y;
    }

    private WorldLayerTransform ResolveParentWorldTransform(LayerItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ParentId))
        {
            return new WorldLayerTransform(Vector2.Zero, 0f, Vector2.One);
        }

        LayerItem? parent = _document.Items.FirstOrDefault(x => string.Equals(x.Id, item.ParentId, StringComparison.Ordinal));
        return parent != null
            ? ComputeWorldTransform(parent)
            : new WorldLayerTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private bool IsDescendant(string candidateId, string ancestorId)
    {
        string? cur = candidateId;
        int depth = 0;
        while (!string.IsNullOrWhiteSpace(cur) && depth < 32)
        {
            LayerItem? node = _document.Items.FirstOrDefault(x => string.Equals(x.Id, cur, StringComparison.Ordinal));
            if (node == null || string.IsNullOrWhiteSpace(node.ParentId))
            {
                return false;
            }

            if (string.Equals(node.ParentId, ancestorId, StringComparison.Ordinal))
            {
                return true;
            }

            cur = node.ParentId;
            depth++;
        }

        return false;
    }

    private Vector2 ResolveBaseSize(LayerItem item)
    {
        if (_context == null)
        {
            return new Vector2(128f, 128f);
        }

        switch (item.Kind)
        {
            case LayerKinds.Sprite:
            {
                if (item.Width > 0f && item.Height > 0f)
                {
                    return new Vector2(item.Width, item.Height);
                }

                Vector2 texSize = !string.IsNullOrWhiteSpace(item.AssetPath)
                    ? GetSpriteTextureSize(item.AssetPath)
                    : Vector2.Zero;

                return texSize.X > 0f && texSize.Y > 0f
                    ? texSize
                    : new Vector2(128f, 128f);
            }
            case LayerKinds.Surface:
            {
                if (item.Width > 0f && item.Height > 0f)
                {
                    return new Vector2(item.Width, item.Height);
                }

                Vector2 surfSize = !string.IsNullOrWhiteSpace(item.SurfaceKey)
                    ? _context.Graphics.GetSurfaceSize(item.SurfaceKey)
                    : Vector2.Zero;

                return surfSize.X > 0f && surfSize.Y > 0f
                    ? surfSize
                    : new Vector2(320f, 180f);
            }
            case LayerKinds.Rect:
            {
                return new Vector2(
                    item.Width > 0f ? item.Width : 160f,
                    item.Height > 0f ? item.Height : 100f);
            }
            case LayerKinds.Text:
            {
                int font = Math.Max(8, item.FontSize);
                int len = string.IsNullOrEmpty(item.Text) ? 4 : item.Text.Length;
                return new Vector2(MathF.Max(40f, len * font * 0.56f), font * 1.15f);
            }
            default:
                return new Vector2(48f, 48f);
        }
    }

    private void PublishSelectionState(bool force)
    {
        if (_context == null)
        {
            return;
        }

        LayerItem? item = GetSelectedItem();
        ViewportSelectionStateEvent next;
        if (item == null)
        {
            next = ViewportSelectionStateEvent.None;
        }
        else
        {
            WorldLayerTransform world = ComputeWorldTransform(item);
            Vector2 baseSize = ResolveBaseSize(item);
            string sourceLabel = item.Kind switch
            {
                LayerKinds.Sprite when !string.IsNullOrWhiteSpace(item.AssetPath) => Path.GetFileName(item.AssetPath),
                LayerKinds.Surface when !string.IsNullOrWhiteSpace(item.SurfaceKey) => item.SurfaceKey,
                _ => item.Name
            };
            next = new ViewportSelectionStateEvent(
                HasSelection: true,
                ItemId: item.Id,
                ItemName: item.Name,
                WorldPosition: world.Position,
                BaseSize: baseSize,
                OriginNormalized: new Vector2(item.OriginX, item.OriginY),
                WorldRotationDegrees: world.RotationDegrees,
                WorldScale: world.Scale,
                Locked: item.Locked,
                SourceLabel: sourceLabel);
        }

        if (force || !next.Equals(_lastPublishedSelection))
        {
            _lastPublishedSelection = next;
            _context.EventBus.Publish(next);
        }
    }

    private void RefreshSceneFileList()
    {
        if (_context == null)
        {
            _sceneFiles.Clear();
            return;
        }

        _sceneFiles = _context.Workspace
            .EnumerateFiles("scenes", "*.json", recursive: true)
            .Select(Path.GetFullPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void LoadInitialScene()
    {
        if (_sceneFiles.Count == 0)
        {
            _document = new LayerDocument { Name = "main" };
            _activeSceneFilePath = null;
            _selectedItemId = null;
            _isDirty = false;
            return;
        }

        string? mainScene = _sceneFiles.FirstOrDefault(
            p => string.Equals(Path.GetFileName(p), "main.scene.json", StringComparison.OrdinalIgnoreCase));
        LoadSceneFile(mainScene ?? _sceneFiles[0]);
    }

    private LayerItem? GetSelectedItem()
    {
        if (string.IsNullOrWhiteSpace(_selectedItemId))
        {
            return null;
        }

        return _document.Items.FirstOrDefault(i => string.Equals(i.Id, _selectedItemId, StringComparison.Ordinal));
    }

    private IEnumerable<string> EnumerateWorkspaceSprites()
    {
        if (_context == null)
        {
            return Array.Empty<string>();
        }

        return _context.Workspace.EnumerateFiles("assets", "*.*", recursive: true)
            .Where(p =>
                p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> EnumerateWorkspaceShaders()
    {
        if (_context == null)
        {
            return Array.Empty<string>();
        }

        return _context.Workspace.EnumerateFiles("assets", "*.*", recursive: true)
            .Where(p =>
                p.EndsWith(".fs", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }

    private UndoSnapshot CaptureSnapshot()
    {
        return new UndoSnapshot(
            _document.Items.Select(i => i.Clone()).ToList(),
            _selectedItemId);
    }

    private void RestoreSnapshot(UndoSnapshot snap)
    {
        _document.Items = snap.Items.Select(i => i.Clone()).ToList();
        _selectedItemId = snap.SelectedId;
        _timelineDriving = false;
    }

    private void PushUndoSnapshot(UndoSnapshot? customSnapshot = null)
    {
        UndoSnapshot snap = customSnapshot ?? CaptureSnapshot();
        _undoStack.Push(snap);
        if (_undoStack.Count > 60)
        {
            UndoSnapshot[] trimmed = _undoStack.Take(60).Reverse().ToArray();
            _undoStack.Clear();
            foreach (UndoSnapshot s in trimmed)
            {
                _undoStack.Push(s);
            }
        }

        _redoStack.Clear();
    }

    private void CommitInspectorSnapshot()
    {
        if (_pendingDragPreSnapshot != null)
        {
            PushUndoSnapshot(_pendingDragPreSnapshot);
            _pendingDragPreSnapshot = null;
        }
    }

    private void MarkDirty()
    {
        _isDirty = true;
        _autoSaveTimer = 0f;
    }

    private static string GetKindBadge(string kind, out Vector4 color)
    {
        switch (kind)
        {
            case LayerKinds.Sprite:
                color = new Vector4(0.45f, 0.86f, 0.56f, 1.0f);
                return "IMG";
            case LayerKinds.Rect:
                color = new Vector4(0.46f, 0.74f, 0.98f, 1.0f);
                return "RCT";
            case LayerKinds.Text:
                color = new Vector4(0.92f, 0.74f, 0.36f, 1.0f);
                return "TXT";
            case LayerKinds.Surface:
                color = new Vector4(0.36f, 0.86f, 0.88f, 1.0f);
                return "SRF";
            default:
                color = new Vector4(0.76f, 0.64f, 0.94f, 1.0f);
                return "GRP";
        }
    }

    private static Vector2 RotateVector(Vector2 v, float degrees)
    {
        if (MathF.Abs(degrees) < 1e-4f)
        {
            return v;
        }

        float rad = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(rad);
        float s = MathF.Sin(rad);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static string ToPascalIdentifier(string raw)
    {
        var sb = new StringBuilder();
        bool upperNext = true;
        foreach (char ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        if (sb.Length == 0 || char.IsDigit(sb[0]))
        {
            sb.Insert(0, "Scene");
        }

        return sb.ToString();
    }

    private static string EscapeString(string? s)
    {
        return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private Vector2 GetSpriteTextureSize(string assetPath)
    {
        if (TryGetPointSpriteTexture(assetPath, out Texture2D tex))
        {
            return new Vector2(tex.Width, tex.Height);
        }

        if (_context != null)
        {
            Vector2 serviceSize = _context.Graphics.GetTextureSize(assetPath);
            if (serviceSize.X > 0f && serviceSize.Y > 0f)
            {
                return serviceSize;
            }

            string resolved = _context.Workspace.ResolvePath(assetPath);
            if (TryReadImageDimensions(resolved, out int imgW, out int imgH))
            {
                return new Vector2(imgW, imgH);
            }
        }

        return Vector2.Zero;
    }

    private bool TryGetPointSpriteTexture(string path, out Texture2D texture)
    {
        if (_context == null || !Raylib.IsWindowReady() || string.IsNullOrWhiteSpace(path))
        {
            texture = default;
            return false;
        }

        string resolved = Path.GetFullPath(_context.Workspace.ResolvePath(path));
        if (_spriteTextureCache.TryGetValue(resolved, out texture) && Raylib.IsTextureValid(texture))
        {
            return true;
        }

        if (!File.Exists(resolved))
        {
            if (_missingSpriteLogged.Add(resolved))
            {
                _context.EventBus.Publish(new EngineLogEvent("SceneLayers", "Warning", $"Sprite file not found: {resolved}"));
            }

            texture = default;
            return false;
        }

        texture = LoadTextureSmart(resolved);
        if (!Raylib.IsTextureValid(texture))
        {
            if (_missingSpriteLogged.Add(resolved))
            {
                _context.EventBus.Publish(new EngineLogEvent("SceneLayers", "Warning", $"Failed to load sprite texture: {resolved}"));
            }

            return false;
        }

        Raylib.SetTextureFilter(texture, TextureFilter.Point);
        _missingSpriteLogged.Remove(resolved);
        _spriteTextureCache[resolved] = texture;
        return true;
    }

    private void InvalidateSpriteTexture(string path)
    {
        if (_context == null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string resolved = Path.GetFullPath(_context.Workspace.ResolvePath(path));
        _missingSpriteLogged.Remove(resolved);

        if (_spriteTextureCache.Remove(resolved, out Texture2D texture) &&
            Raylib.IsWindowReady() &&
            Raylib.IsTextureValid(texture))
        {
            Raylib.UnloadTexture(texture);
        }
    }

    private void InvalidateAllSpriteTextures()
    {
        if (Raylib.IsWindowReady())
        {
            foreach (Texture2D texture in _spriteTextureCache.Values)
            {
                if (Raylib.IsTextureValid(texture))
                {
                    Raylib.UnloadTexture(texture);
                }
            }
        }

        _spriteTextureCache.Clear();
        _missingSpriteLogged.Clear();
    }

    private static Texture2D LoadTextureSmart(string resolvedPath)
    {
        try
        {
            byte[] data = File.ReadAllBytes(resolvedPath);
            string? detectedExt = DetectImageExtension(data);
            if (detectedExt != null)
            {
                Image image = Raylib.LoadImageFromMemory(detectedExt, data);
                if (Raylib.IsImageValid(image))
                {
                    Texture2D tex = Raylib.LoadTextureFromImage(image);
                    Raylib.UnloadImage(image);
                    if (Raylib.IsTextureValid(tex))
                    {
                        return tex;
                    }
                }
            }
        }
        catch (IOException)
        {
        }

        return Raylib.LoadTexture(resolvedPath);
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return ".png";
        }

        if (data.Length >= 3 &&
            data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ".jpg";
        }

        if (data.Length >= 2 &&
            data[0] == 0x42 && data[1] == 0x4D)
        {
            return ".bmp";
        }

        if (data.Length >= 4 &&
            data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
        {
            return ".gif";
        }

        return null;
    }

    private static bool TryReadImageDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using FileStream fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            int read = fs.Read(header);
            if (read >= 24 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }

            if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                fs.Position = 2;
                while (fs.Position < fs.Length)
                {
                    int b = fs.ReadByte();
                    if (b < 0) break;
                    if (b != 0xFF) continue;

                    int marker = fs.ReadByte();
                    while (marker == 0xFF)
                    {
                        marker = fs.ReadByte();
                    }
                    if (marker < 0) break;

                    if (marker == 0xD8 || marker == 0xD9 || marker == 0x00 || (marker >= 0xD0 && marker <= 0xD7))
                    {
                        continue;
                    }

                    int hi = fs.ReadByte();
                    int lo = fs.ReadByte();
                    if (hi < 0 || lo < 0) break;
                    int segLen = (hi << 8) | lo;
                    if (segLen < 2) break;

                    bool isSof = (marker >= 0xC0 && marker <= 0xCF) &&
                                 marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                    if (isSof && segLen >= 7)
                    {
                        Span<byte> sof = stackalloc byte[5];
                        if (fs.Read(sof) == 5)
                        {
                            height = (sof[1] << 8) | sof[2];
                            width = (sof[3] << 8) | sof[4];
                            return width > 0 && height > 0;
                        }
                        break;
                    }

                    fs.Seek(segLen - 2, SeekOrigin.Current);
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
