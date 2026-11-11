using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Engine.Core.Plugins;
using Engine.Plugins.Settings;
using ImGuiNET;

namespace Engine.Plugins.Workspace;

public sealed class WorkspaceExplorerPlugin : IEnginePlugin
{
    private enum AssetFilter
    {
        All = 0,
        Sprites = 1,
        Audio = 2,
        ScriptsAndData = 3
    }

    private static readonly HashSet<string> SpriteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".ogg", ".mp3"
    };

    private static readonly HashSet<string> ShaderExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fs", ".vs", ".glsl"
    };

    private const string RecentProjectsSettingKey = "workspace.recentProjects";
    private const int MaxRecentProjects = 5;

    private static readonly JsonSerializerOptions RecentJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> ScriptDataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".json", ".fs", ".vs", ".glsl"
    };

    private readonly ConcurrentDictionary<string, DateTime> _pendingModifications = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _suppressedSelfSaveUntilUtc = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _pendingOpenedProjects = new();
    private readonly List<string> _recentProjects = new();

    private PluginContext? _context;
    private FileSystemWatcher? _watcher;
    private WorkspaceDirectoryNode? _rootNode;
    private AssetFilter _filter = AssetFilter.All;
    private string _selectedRelativePath = string.Empty;
    private string _statusMessage = "Watching project files";
    private float _needsTreeRefreshCooldown;
    private volatile bool _isSelfSaving;
    private int _selfSaveDepth;
    private int _folderPickerActive;
    private bool _recentMenuBuilt;
    private bool _disposedOrUnloaded;

    public string Id => "workspace.explorer";
    public string DisplayName => "Project";

    public IReadOnlyList<string> RecentProjects => _recentProjects;

    public void OnLoaded(PluginContext context)
    {
        _context = context;
        _disposedOrUnloaded = false;
        _context.EventBus.Subscribe<ProjectLoadedEvent>(OnProjectLoaded);
        _context.EventBus.Subscribe<FileWatcherSelfSaveEvent>(OnFileWatcherSelfSave);

        _context.Menu.Register(new MenuItemDefinition(
            Id: "workspace.open_project",
            Path: "File/Open Project...",
            Callback: TriggerOpenProjectDialog,
            Shortcut: "Cmd+O",
            Order: 15));
        _context.Menu.Register(new MenuItemDefinition(
            Id: "workspace.open_vscode",
            Path: "File/Open Project in VS Code",
            Callback: () =>
            {
                if (_context != null) OpenInVsCode(_context.Workspace.ProjectRoot);
            },
            Order: 80));
        _context.Menu.Register(new MenuItemDefinition(
            Id: "workspace.refresh",
            Path: "File/Refresh Files",
            Callback: () =>
            {
                RefreshTree();
                _statusMessage = "File tree refreshed";
            },
            Order: 85));

        LoadRecentProjectsFromSettings();
        if (Directory.Exists(_context.Workspace.ProjectRoot))
        {
            RecordRecentProject(_context.Workspace.ProjectRoot);
        }
        else
        {
            RefreshRecentProjectsMenu();
        }

        RefreshTree();
        StartWatcher();
    }

    public void OnUpdate(float deltaTime)
    {
        if (_context == null)
        {
            return;
        }

        while (_pendingOpenedProjects.TryDequeue(out string? pendingProjectRoot))
        {
            OpenProjectDirectory(pendingProjectRoot);
        }

        if (_needsTreeRefreshCooldown > 0f)
        {
            _needsTreeRefreshCooldown -= deltaTime;
            if (_needsTreeRefreshCooldown <= 0f)
            {
                RefreshTree();
            }
        }

        DateTime now = DateTime.UtcNow;
        List<string>? readyPaths = null;

        foreach (KeyValuePair<string, DateTime> kvp in _pendingModifications)
        {
            if ((now - kvp.Value).TotalMilliseconds >= 120.0)
            {
                readyPaths ??= new List<string>();
                readyPaths.Add(kvp.Key);
            }
        }

        if (readyPaths == null)
        {
            return;
        }

        bool treeUpdated = false;
        foreach (string path in readyPaths)
        {
            if (!_pendingModifications.TryRemove(path, out _))
            {
                continue;
            }

            if (IsPathSuppressed(path))
            {
                treeUpdated = true;
                continue;
            }

            string ext = Path.GetExtension(path);
            if (SpriteExtensions.Contains(ext))
            {
                _context.Graphics.InvalidateTexture(path);
            }
            else if (AudioExtensions.Contains(ext))
            {
                _context.Audio.InvalidateSound(path);
            }
            else if (ShaderExtensions.Contains(ext))
            {
                _context.Graphics.InvalidateShader(path);
            }

            treeUpdated = true;
            _statusMessage = $"Reloaded: {_context.Workspace.ToRelativePath(path)}";
            _context.EventBus.Publish(new AssetModifiedEvent(path));
        }

        if (treeUpdated)
        {
            RefreshTree();
        }
    }

    public void OnRenderUI()
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
        if (!ImGui.Begin("Project", ref keepOpen))
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

        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        string projectName = Path.GetFileName(_context.Workspace.ProjectRoot.TrimEnd(Path.DirectorySeparatorChar));
        ImGui.TextColored(theme.PanelSubheaderText, $"Project: {projectName}".Replace("%", "%%", StringComparison.Ordinal));
        ImGui.Spacing();

        float openWidth = MathF.Max(58f, ImGui.CalcTextSize("Open...").X + ImGui.GetStyle().FramePadding.X * 2f);
        float refreshWidth = MathF.Max(60f, ImGui.CalcTextSize("Refresh").X + ImGui.GetStyle().FramePadding.X * 2f);
        float spacingX = ImGui.GetStyle().ItemSpacing.X;
        float vsCodeWidth = MathF.Max(98f, ImGui.GetContentRegionAvail().X - openWidth - refreshWidth - spacingX * 2f);

        ImGui.PushStyleColor(ImGuiCol.Button, theme.AccentButton);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.AccentButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.AccentButtonActive);
        if (ImGui.Button("Open in VS Code", new Vector2(vsCodeWidth, 0f)))
        {
            OpenInVsCode(_context.Workspace.ProjectRoot);
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        if (ImGui.Button("Open...", new Vector2(openWidth, 0f)))
        {
            TriggerOpenProjectDialog();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open Project Folder (Cmd+O)");
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh", new Vector2(refreshWidth, 0f)))
        {
            RefreshTree();
            _statusMessage = "File tree refreshed";
        }

        ImGui.Spacing();
        RenderFilterRow();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float footerHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y * 2f + 4f;
        ImGui.BeginChild("WorkspaceTreeScroll", new Vector2(0f, -footerHeight), ImGuiChildFlags.None);
        if (_rootNode != null)
        {
            RenderDirectoryNode(_rootNode, isRoot: true);
        }
        else
        {
            ImGui.TextDisabled("Project directory not found");
        }
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.TextDisabled(_statusMessage.Replace("%", "%%", StringComparison.Ordinal));
        ImGui.End();
    }

    public void OnUnloaded()
    {
        _disposedOrUnloaded = true;
        _recentMenuBuilt = false;
        StopWatcher();
        if (_context != null)
        {
            _context.Menu.UnregisterByPrefix("workspace.");
            _context.EventBus.Unsubscribe<ProjectLoadedEvent>(OnProjectLoaded);
            _context.EventBus.Unsubscribe<FileWatcherSelfSaveEvent>(OnFileWatcherSelfSave);
        }
    }

    public void TriggerOpenProjectDialog()
    {
        if (_context == null || _disposedOrUnloaded)
        {
            return;
        }

        if (!OperatingSystem.IsMacOS())
        {
            _statusMessage = "Native folder picker is only supported on macOS";
            _context.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Warning", _statusMessage));
            return;
        }

        if (Interlocked.CompareExchange(ref _folderPickerActive, 1, 0) != 0)
        {
            return;
        }

        _statusMessage = "Select project root folder...";

        _ = Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add("POSIX path of (choose folder with prompt \"Select Project Root\")");

                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    return;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (_disposedOrUnloaded)
                {
                    return;
                }

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    string selected = stdout.Trim();
                    if (selected.Length > 1)
                    {
                        selected = selected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }

                    _pendingOpenedProjects.Enqueue(selected);
                }
                else if (process.ExitCode != 0 &&
                         !stderr.Contains("User canceled", StringComparison.OrdinalIgnoreCase) &&
                         !stderr.Contains("-128", StringComparison.Ordinal))
                {
                    _context.EventBus.Publish(new EngineLogEvent(
                        "WorkspaceExplorer",
                        "Warning",
                        $"Folder picker closed with code {process.ExitCode}: {stderr.Trim()}"));
                }
            }
            catch (Exception ex)
            {
                if (!_disposedOrUnloaded)
                {
                    _context.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Error", $"Failed to launch folder picker: {ex.Message}"));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _folderPickerActive, 0);
            }
        });
    }

    public bool OpenProjectDirectory(string folderPath)
    {
        if (_context == null || string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(folderPath.Trim());
            if (normalized.Length > 1)
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Invalid path: {ex.Message}";
            _context.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Error", _statusMessage));
            return false;
        }

        if (!Directory.Exists(normalized))
        {
            _statusMessage = $"Project folder not found: {normalized}";
            _context.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Warning", _statusMessage));
            return false;
        }

        try
        {
            EnsureStarterProjectJson(normalized);

            _pendingModifications.Clear();
            _selectedRelativePath = string.Empty;

            _context.Audio.StopAll();
            _context.Graphics.InvalidateAllTextures();
            _context.Runtime.ClearProperties();

            RecordRecentProject(normalized);
            _context.Workspace.SetProjectRoot(normalized);
            _context.EventBus.Publish(new WorkspaceChangedEvent(normalized));

            string projectName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = normalized;
            }

            _statusMessage = $"Opened project: {projectName}";
            _context.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Info", $"Opened project '{projectName}' at {normalized}"));
            return true;
        }
        catch (Exception ex)
        {
            _statusMessage = $"Open project failed: {ex.Message}";
            _context.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Error", $"Failed to open project '{normalized}': {ex.Message}"));
            return false;
        }
    }

    public void SetSelfSaving(string filePath, bool isSaving)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string normalized = Path.GetFullPath(filePath);
        string tempPath = normalized + ".tmp";

        if (isSaving)
        {
            Interlocked.Increment(ref _selfSaveDepth);
            _isSelfSaving = true;
            DateTime blockUntil = DateTime.UtcNow.AddSeconds(5);
            _suppressedSelfSaveUntilUtc[normalized] = blockUntil;
            _suppressedSelfSaveUntilUtc[tempPath] = blockUntil;
            _pendingModifications.TryRemove(normalized, out _);
            _pendingModifications.TryRemove(tempPath, out _);
        }
        else
        {
            int remaining = Interlocked.Decrement(ref _selfSaveDepth);
            if (remaining <= 0)
            {
                Interlocked.Exchange(ref _selfSaveDepth, 0);
                _isSelfSaving = false;
            }

            DateTime tailWindow = DateTime.UtcNow.AddMilliseconds(900);
            _suppressedSelfSaveUntilUtc[normalized] = tailWindow;
            _suppressedSelfSaveUntilUtc[tempPath] = tailWindow;
            _pendingModifications.TryRemove(normalized, out _);
            _pendingModifications.TryRemove(tempPath, out _);
            _needsTreeRefreshCooldown = 0.05f;
        }
    }

    private void OnFileWatcherSelfSave(FileWatcherSelfSaveEvent evt)
    {
        SetSelfSaving(evt.FilePath, evt.IsSaving);
    }

    private void OnProjectLoaded(ProjectLoadedEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.ProjectRoot) && Directory.Exists(evt.ProjectRoot))
        {
            RecordRecentProject(evt.ProjectRoot);
        }

        RefreshTree();
        StartWatcher();
    }

    private void RenderFilterRow()
    {
        ReadOnlySpan<(string Label, AssetFilter Filter)> buttons =
        [
            ("All", AssetFilter.All),
            ("Sprites", AssetFilter.Sprites),
            ("Audio", AssetFilter.Audio),
            ("Code", AssetFilter.ScriptsAndData)
        ];

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5f, 6f));

        float avail = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float padX = ImGui.GetStyle().FramePadding.X * 2f;

        Span<float> naturalWidths = stackalloc float[4];
        float totalNatural = 0f;
        for (int i = 0; i < buttons.Length; i++)
        {
            float w = ImGui.CalcTextSize(buttons[i].Label).X + padX;
            naturalWidths[i] = w;
            totalNatural += w;
        }

        float remaining = MathF.Max(0f, avail - spacing * (buttons.Length - 1) - totalNatural);
        float extraPerButton = remaining / buttons.Length;

        for (int i = 0; i < buttons.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            RenderFilterButton(buttons[i].Label, buttons[i].Filter, MathF.Floor(naturalWidths[i] + extraPerButton));
        }

        ImGui.PopStyleVar(2);
    }

    private void RenderFilterButton(string label, AssetFilter filter, float width)
    {
        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        bool active = _filter == filter;
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

        if (ImGui.Button(label, new Vector2(width, 0f)))
        {
            _filter = filter;
        }

        ImGui.PopStyleColor(3);
    }

    private void RenderDirectoryNode(WorkspaceDirectoryNode node, bool isRoot)
    {
        if (!isRoot && !HasVisibleContent(node))
        {
            return;
        }

        ImGuiTreeNodeFlags dirFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isRoot)
        {
            dirFlags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        bool open = ImGui.TreeNodeEx(isRoot ? node.Name : $"{node.Name}##{node.FullPath}", dirFlags);
        if (!open)
        {
            return;
        }

        foreach (WorkspaceDirectoryNode sub in node.Subdirectories)
        {
            RenderDirectoryNode(sub, isRoot: false);
        }

        foreach (WorkspaceFileEntry file in node.Files)
        {
            if (!MatchesFilter(file.Extension))
            {
                continue;
            }

            string badge = GetBadge(file.Extension, out Vector4 color);
            bool selected = string.Equals(_selectedRelativePath, file.RelativePath, StringComparison.Ordinal);

            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(badge);
            ImGui.PopStyleColor();
            ImGui.SameLine(0f, 6f);

            if (ImGui.Selectable($"{file.Name}##{file.FullPath}", selected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedRelativePath = file.RelativePath;
                _context?.EventBus.Publish(new AssetSelectedEvent(file.FullPath, GetCategory(file.Extension)));

                if (string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase) &&
                    (file.RelativePath.StartsWith("scenes/", StringComparison.OrdinalIgnoreCase) ||
                     file.RelativePath.StartsWith("scenes\\", StringComparison.OrdinalIgnoreCase) ||
                     file.Name.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase)))
                {
                    _context?.EventBus.Publish(new ActiveDocumentChangedEvent(file.FullPath));
                }

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    if (AudioExtensions.Contains(file.Extension))
                    {
                        _context?.Audio.PlaySound(file.FullPath, 0.8f, 1.0f);
                        _statusMessage = $"Playing: {file.Name}";
                    }
                    else
                    {
                        OpenInVsCode(_context!.Workspace.ProjectRoot, file.FullPath);
                    }
                }
            }

            if (ImGui.IsItemHovered())
            {
                string hint = SpriteExtensions.Contains(file.Extension)
                    ? "Click to preview in viewport • Double-click to open"
                    : AudioExtensions.Contains(file.Extension)
                        ? "Double-click to preview sound"
                        : "Double-click to open in VS Code";
                ImGui.SetTooltip($"{file.RelativePath} ({file.SizeBytes} B)\n{hint}".Replace("%", "%%", StringComparison.Ordinal));
            }
        }

        ImGui.TreePop();
    }

    private bool HasVisibleContent(WorkspaceDirectoryNode node)
    {
        if (_filter == AssetFilter.All)
        {
            return true;
        }

        if (node.Files.Any(f => MatchesFilter(f.Extension)))
        {
            return true;
        }

        return node.Subdirectories.Any(HasVisibleContent);
    }

    private bool MatchesFilter(string extension)
    {
        return _filter switch
        {
            AssetFilter.Sprites => SpriteExtensions.Contains(extension),
            AssetFilter.Audio => AudioExtensions.Contains(extension),
            AssetFilter.ScriptsAndData => ScriptDataExtensions.Contains(extension),
            _ => true
        };
    }

    private static string GetBadge(string ext, out Vector4 color)
    {
        if (SpriteExtensions.Contains(ext))
        {
            color = new Vector4(0.45f, 0.86f, 0.56f, 1.0f);
            return "IMG";
        }

        if (AudioExtensions.Contains(ext))
        {
            color = new Vector4(0.92f, 0.74f, 0.36f, 1.0f);
            return "SND";
        }

        if (string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector4(0.46f, 0.74f, 0.98f, 1.0f);
            return "C# ";
        }

        if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector4(0.78f, 0.62f, 0.94f, 1.0f);
            return "JSN";
        }

        if (ShaderExtensions.Contains(ext))
        {
            color = new Vector4(0.36f, 0.86f, 0.88f, 1.0f);
            return "SHD";
        }

        color = new Vector4(0.65f, 0.68f, 0.74f, 1.0f);
        return "FIL";
    }

    private static string GetCategory(string ext)
    {
        if (SpriteExtensions.Contains(ext)) return "sprite";
        if (AudioExtensions.Contains(ext)) return "audio";
        if (ShaderExtensions.Contains(ext)) return "shader";
        if (ScriptDataExtensions.Contains(ext)) return "script";
        return "other";
    }

    private void RefreshTree()
    {
        if (_context == null)
        {
            return;
        }

        _rootNode = _context.Workspace.ScanDirectory();
    }

    private void StartWatcher()
    {
        StopWatcher();

        if (_context == null || !Directory.Exists(_context.Workspace.ProjectRoot))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(_context.Workspace.ProjectRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };

            _watcher.Changed += OnFileSystemEvent;
            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Error += (_, _) => _needsTreeRefreshCooldown = 0.5f;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            StopWatcher();
            _statusMessage = $"Watcher unavailable: {ex.Message}";
            _context.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Warning", $"File watcher failed to start: {ex.Message}"));
        }
    }

    private void StopWatcher()
    {
        if (_watcher == null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileSystemEvent;
        _watcher.Created -= OnFileSystemEvent;
        _watcher.Deleted -= OnFileSystemEvent;
        _watcher.Renamed -= OnFileSystemRenamed;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        QueuePathIfRelevant(e.FullPath);
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        QueuePathIfRelevant(e.FullPath);
    }

    private void QueuePathIfRelevant(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        string fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName) ||
            fileName.StartsWith('.') ||
            fileName.EndsWith('~') ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".swp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (fullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Directory.Exists(fullPath))
        {
            _needsTreeRefreshCooldown = 0.15f;
            return;
        }

        string normalized = Path.GetFullPath(fullPath);
        if (IsPathSuppressed(normalized))
        {
            return;
        }

        _pendingModifications[normalized] = DateTime.UtcNow;
    }

    private bool IsPathSuppressed(string normalizedPath)
    {
        if (_suppressedSelfSaveUntilUtc.TryGetValue(normalizedPath, out DateTime untilUtc))
        {
            if (_isSelfSaving || DateTime.UtcNow < untilUtc)
            {
                return true;
            }

            _suppressedSelfSaveUntilUtc.TryRemove(normalizedPath, out _);
        }

        return false;
    }

    private void EnsureStarterProjectJson(string projectRoot)
    {
        string projectJsonPath = Path.Combine(projectRoot, "project.json");
        if (File.Exists(projectJsonPath))
        {
            return;
        }

        string folderName = Path.GetFileName(projectRoot);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = "UntitledProject";
        }

        var starter = new
        {
            name = folderName,
            virtualWidth = 1280,
            virtualHeight = 720,
            nodes = Array.Empty<object>(),
            timeline = new
            {
                maxDuration = 4.0f,
                loop = true,
                propertyTracks = Array.Empty<object>(),
                triggerTracks = Array.Empty<object>()
            }
        };

        string json = JsonSerializer.Serialize(starter, RecentJsonOptions);
        string tempPath = projectJsonPath + ".tmp";

        SetSelfSaving(projectJsonPath, isSaving: true);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, projectJsonPath, overwrite: true);
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

            SetSelfSaving(projectJsonPath, isSaving: false);
        }
    }

    private void LoadRecentProjectsFromSettings()
    {
        _recentProjects.Clear();
        if (_context == null)
        {
            return;
        }

        string raw = _context.Settings.GetString(RecentProjectsSettingKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            List<string>? parsed = JsonSerializer.Deserialize<List<string>>(raw, RecentJsonOptions);
            if (parsed == null)
            {
                return;
            }

            foreach (string entry in parsed)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                string normalized = NormalizeFolderPath(entry);
                if (!_recentProjects.Contains(normalized, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                {
                    _recentProjects.Add(normalized);
                }

                if (_recentProjects.Count >= MaxRecentProjects)
                {
                    break;
                }
            }
        }
        catch
        {
            // Ignore malformed recent list and rebuild cleanly on next save.
        }
    }

    private void RecordRecentProject(string projectPath)
    {
        if (_context == null || string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        string normalized = NormalizeFolderPath(projectPath);
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        if (_recentMenuBuilt && _recentProjects.Count > 0 && comparer.Equals(_recentProjects[0], normalized))
        {
            return;
        }

        _recentProjects.RemoveAll(existing => comparer.Equals(existing, normalized));
        _recentProjects.Insert(0, normalized);

        if (_recentProjects.Count > MaxRecentProjects)
        {
            _recentProjects.RemoveRange(MaxRecentProjects, _recentProjects.Count - MaxRecentProjects);
        }

        _context.Settings.SetString(RecentProjectsSettingKey, JsonSerializer.Serialize(_recentProjects, RecentJsonOptions));
        RefreshRecentProjectsMenu();
    }

    private void RefreshRecentProjectsMenu()
    {
        if (_context == null)
        {
            return;
        }

        _context.Menu.UnregisterByPrefix("workspace.recent.");
        _recentMenuBuilt = true;

        if (_recentProjects.Count == 0)
        {
            _context.Menu.Register(new MenuItemDefinition(
                Id: "workspace.recent.empty",
                Path: "File/Open Recent/(No Recent Projects)",
                Callback: null,
                Order: 0));
            return;
        }

        for (int i = 0; i < _recentProjects.Count; i++)
        {
            string projectPath = _recentProjects[i];
            string leafLabel = FormatRecentProjectMenuLabel(i, projectPath);
            _context.Menu.Register(new MenuItemDefinition(
                Id: $"workspace.recent.{i}",
                Path: $"File/Open Recent/{leafLabel}",
                Callback: () => OpenProjectDirectory(projectPath),
                IsEnabled: () => Directory.Exists(projectPath),
                Order: i));
        }
    }

    private static string FormatRecentProjectMenuLabel(int index, string projectPath)
    {
        string normalized = NormalizeFolderPath(projectPath);
        string folderName = Path.GetFileName(normalized);
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string displayPath = !string.IsNullOrEmpty(userHome) &&
                             normalized.StartsWith(userHome + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "~" + normalized[userHome.Length..]
            : normalized;

        // Avoid '/' in leaf label so MenuRegistry/MacOsMenuBridge path splitting keeps all recent entries at one submenu level.
        displayPath = displayPath.Replace('/', '\u2215').Replace('\\', '\u2215');

        return string.IsNullOrWhiteSpace(folderName)
            ? $"{index + 1}. {displayPath}"
            : $"{index + 1}. {folderName} — {displayPath}";
    }

    private static string NormalizeFolderPath(string path)
    {
        string full = Path.GetFullPath(path.Trim());
        return full.Length > 1
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    private void OpenInVsCode(string projectPath, string? filePath = null)
    {
        try
        {
            string? codeCli = FindVsCodeCli();
            if (codeCli != null)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = codeCli,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(projectPath);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    startInfo.ArgumentList.Add(filePath);
                }

                Process.Start(startInfo);
                _statusMessage = filePath == null
                    ? "Opened project in VS Code"
                    : $"Opened: {Path.GetFileName(filePath)}";
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                var macOpen = new ProcessStartInfo
                {
                    FileName = "open",
                    UseShellExecute = false
                };
                macOpen.ArgumentList.Add("-a");
                macOpen.ArgumentList.Add("Visual Studio Code");
                macOpen.ArgumentList.Add(projectPath);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    macOpen.ArgumentList.Add(filePath);
                }

                Process.Start(macOpen);
                _statusMessage = "Opened in VS Code";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{projectPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _statusMessage = $"Failed to open VS Code: {ex.Message}";
            _context?.EventBus.Publish(new EngineLogEvent("WorkspaceExplorer", "Warning", _statusMessage));
        }
    }

    private static string? FindVsCodeCli()
    {
        string[] candidates =
        [
            "/usr/local/bin/code",
            "/opt/homebrew/bin/code",
            "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string full = Path.Combine(dir, OperatingSystem.IsWindows() ? "code.cmd" : "code");
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }
}
