using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Engine.Core.Plugins;
using Engine.Plugins.Timeline;
using Engine.Plugins.Workspace;
using ImGuiNET;
using ExecutionContext = Engine.Core.Abstractions.ExecutionContext;

namespace Engine.Plugins.Execution;

public sealed class ExecutionPlugin : IEnginePlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private PluginContext? _context;
    private ExecutionContext? _executionContext;
    private ScriptAssemblyLoadContext? _scriptLoadContext;
    private ScriptAssemblyScope? _scriptScope;
    private readonly List<IExecutableNode> _activeNodes = new();
    private readonly List<string> _compileErrors = new();
    private readonly List<string> _recentEvents = new();
    private string? _runtimeError;
    private string? _lastProjectRootLoaded;

    public string Id => "workbench.execution";
    public string DisplayName => "Execution";

    public IReadOnlyList<IExecutableNode> ActiveNodes => _activeNodes;
    public IReadOnlyList<string> CompileErrors => _compileErrors;
    public double LastReloadElapsedMs { get; private set; }
    internal ScriptAssemblyScope? CurrentScope => _scriptScope;
    internal ScriptAssemblyLoadContext? CurrentLoadContext => _scriptLoadContext;

    public void OnLoaded(PluginContext context)
    {
        _context = context;
        _executionContext = context.Runtime;

        context.Menu.Register(new MenuItemDefinition(
            Id: "execution.reload",
            Path: "File/Reload Scripts",
            Callback: () => ReloadProjectNodes(applyProjectConfig: false),
            Shortcut: "Cmd+R",
            Order: 70));

        context.EventBus.Subscribe<AssetModifiedEvent>(OnAssetModified);
        context.EventBus.Subscribe<AssetSelectedEvent>(OnAssetSelected);
        context.EventBus.Subscribe<ProjectLoadedEvent>(OnProjectLoaded);
        context.EventBus.Subscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
        context.EventBus.Subscribe<ViewportResolutionChangedEvent>(OnViewportResolutionChanged);
        context.EventBus.Subscribe<TimelinePropertyEvaluatedEvent>(OnTimelinePropertyEvaluated);
        context.EventBus.Subscribe<TimelineStateChangedEvent>(OnTimelineStateChanged);
        context.EventBus.Subscribe<TimelineTriggerFiredEvent>(OnTimelineTriggerFired);
        context.EventBus.Subscribe<ViewportRenderPassEvent>(OnViewportRenderPass);

        ReloadProjectNodes(applyProjectConfig: true);
        _lastProjectRootLoaded = Path.GetFullPath(context.Workspace.ProjectRoot);
    }

    public void OnUpdate(float deltaTime)
    {
        if (_executionContext == null)
        {
            return;
        }

        for (int i = 0; i < _activeNodes.Count; i++)
        {
            IExecutableNode node = _activeNodes[i];
            try
            {
                node.OnUpdate(deltaTime);
            }
            catch (Exception ex)
            {
                _runtimeError = $"[{node.NodeId}] OnUpdate: {ex.Message}";
            }
        }
    }

    public void OnRenderUI()
    {
        if (_context == null || _executionContext == null)
        {
            return;
        }

        bool isVisible = _context.Plugins?.IsWindowVisible(Id) ?? true;
        if (!isVisible)
        {
            return;
        }

        bool keepOpen = isVisible;
        if (!ImGui.Begin("Execution", ref keepOpen))
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

        if (ImGui.Button("Reload Scripts", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            ReloadProjectNodes(applyProjectConfig: true);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled($"ACTIVE NODES ({_activeNodes.Count})  |  Hot-Reload: {LastReloadElapsedMs:F1} ms");
        foreach (IExecutableNode node in _activeNodes)
        {
            ImGui.BulletText($"{node.NodeId} ({node.GetType().Name})");
        }

        if (_compileErrors.Count > 0 || !string.IsNullOrEmpty(_runtimeError))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.95f, 0.36f, 0.36f, 1.0f), "ERRORS");
            foreach (string err in _compileErrors)
            {
                ImGui.TextWrapped(err.Replace("%", "%%", StringComparison.Ordinal));
            }

            if (!string.IsNullOrEmpty(_runtimeError))
            {
                ImGui.TextWrapped(_runtimeError.Replace("%", "%%", StringComparison.Ordinal));
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("CONTEXT PROPERTIES");
        ImGui.BeginChild("ExecPropsScroll", new Vector2(0f, 130f), ImGuiChildFlags.None);
        foreach (KeyValuePair<string, string> kvp in _executionContext.StringProperties.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            ImGui.TextUnformatted($"{kvp.Key} = {kvp.Value}");
        }
        foreach (KeyValuePair<string, float> kvp in _executionContext.FloatProperties.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            ImGui.TextUnformatted($"{kvp.Key} = {kvp.Value:F2}");
        }
        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("EVENT LOG");
        ImGui.BeginChild("ExecLogScroll", Vector2.Zero, ImGuiChildFlags.None);
        for (int i = _recentEvents.Count - 1; i >= 0; i--)
        {
            ImGui.TextWrapped(_recentEvents[i].Replace("%", "%%", StringComparison.Ordinal));
        }
        ImGui.EndChild();

        ImGui.End();
    }

    public void OnUnloaded()
    {
        if (_context != null)
        {
            _context.Menu.UnregisterByPrefix("execution.");
            _context.EventBus.Unsubscribe<AssetModifiedEvent>(OnAssetModified);
            _context.EventBus.Unsubscribe<AssetSelectedEvent>(OnAssetSelected);
            _context.EventBus.Unsubscribe<ProjectLoadedEvent>(OnProjectLoaded);
            _context.EventBus.Unsubscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
            _context.EventBus.Unsubscribe<ViewportResolutionChangedEvent>(OnViewportResolutionChanged);
            _context.EventBus.Unsubscribe<TimelinePropertyEvaluatedEvent>(OnTimelinePropertyEvaluated);
            _context.EventBus.Unsubscribe<TimelineStateChangedEvent>(OnTimelineStateChanged);
            _context.EventBus.Unsubscribe<TimelineTriggerFiredEvent>(OnTimelineTriggerFired);
            _context.EventBus.Unsubscribe<ViewportRenderPassEvent>(OnViewportRenderPass);
        }

        UnloadActiveNodes();
    }

    public bool ReloadProjectNodes() => ReloadProjectNodes(applyProjectConfig: false);

    public bool ReloadProjectNodes(bool applyProjectConfig)
    {
        if (_context == null || _executionContext == null)
        {
            return false;
        }

        var sw = Stopwatch.StartNew();
        _runtimeError = null;

        string[] scriptPaths = ResolveNodeScriptPaths(
            applyProjectConfig,
            out Dictionary<string, string> configuredStringProps,
            out HashSet<string> disabledNodeIds,
            out Dictionary<string, int> configuredOrder);

        foreach (KeyValuePair<string, string> kvp in configuredStringProps)
        {
            _executionContext.SetStringProperty(kvp.Key, kvp.Value);
        }

        ScriptEmitResult emitResult = ScriptCompiler.EmitNodesFromFiles(scriptPaths);

        _compileErrors.Clear();
        if (!emitResult.Success)
        {
            _compileErrors.AddRange(emitResult.Errors);
            sw.Stop();
            LastReloadElapsedMs = sw.Elapsed.TotalMilliseconds;
            AddLog($"Compile error ({emitResult.Errors.Count})");
            return false;
        }

        // Strict Module E teardown: dispose previous scope (events, FBO surfaces, OnUnload) BEFORE ALC.Unload().
        UnloadActiveNodes();

        if (!LoadAndActivateNodes(emitResult.PeBytes, disabledNodeIds, configuredOrder, out IReadOnlyList<string> loadErrors))
        {
            _compileErrors.AddRange(loadErrors);
            sw.Stop();
            LastReloadElapsedMs = sw.Elapsed.TotalMilliseconds;
            AddLog($"Load error ({loadErrors.Count})");
            return false;
        }

        sw.Stop();
        LastReloadElapsedMs = sw.Elapsed.TotalMilliseconds;
        AddLog($"Loaded {_activeNodes.Count} node(s) ({LastReloadElapsedMs:F1} ms)");
        return true;
    }

    private void OnViewportRenderPass(ViewportRenderPassEvent evt)
    {
        if (_context == null)
        {
            return;
        }

        if (_executionContext != null && evt.VirtualWidth > 0 && evt.VirtualHeight > 0)
        {
            if (_executionContext.VirtualWidth != evt.VirtualWidth ||
                _executionContext.VirtualHeight != evt.VirtualHeight)
            {
                _executionContext.Display.VirtualResolution = new Vector2(evt.VirtualWidth, evt.VirtualHeight);
            }
        }

        for (int i = 0; i < _activeNodes.Count; i++)
        {
            IExecutableNode node = _activeNodes[i];
            try
            {
                node.OnDraw(_context.Graphics);
            }
            catch (Exception ex)
            {
                _runtimeError = $"[{node.NodeId}] OnDraw: {ex.Message}";
            }
        }
    }

    private void OnViewportResolutionChanged(ViewportResolutionChangedEvent evt)
    {
        if (_executionContext == null)
        {
            return;
        }

        _executionContext.Display.VirtualResolution = new Vector2(evt.VirtualWidth, evt.VirtualHeight);
    }

    private void OnTimelinePropertyEvaluated(TimelinePropertyEvaluatedEvent evt)
    {
        _executionContext?.SetProperty(evt.TargetProperty, evt.Value);
    }

    private void OnTimelineStateChanged(TimelineStateChangedEvent evt)
    {
        if (_executionContext == null)
        {
            return;
        }

        _executionContext.CurrentTime = evt.CurrentTime;
        _executionContext.MaxDuration = evt.MaxDuration;
        _executionContext.IsPlaying = evt.IsPlaying;

        if (evt.IsManualScrub)
        {
            OnUpdate(0f);
        }
    }

    private void OnTimelineTriggerFired(TimelineTriggerFiredEvent evt)
    {
        if (_context == null)
        {
            return;
        }

        if (string.Equals(evt.ActionType, "sound", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.ActionType, "audio", StringComparison.OrdinalIgnoreCase))
        {
            _context.Audio.PlaySound(evt.Target, evt.Volume, evt.Pitch);
            AddLog($"[{evt.Time:F2}s] Audio: {evt.Target} (vol={evt.Volume:F2}, pitch={evt.Pitch:F2})");
            return;
        }

        if (string.Equals(evt.ActionType, "method", StringComparison.OrdinalIgnoreCase))
        {
            InvokeNodeMethod(evt.Target, evt.Argument);
            AddLog($"[{evt.Time:F2}s] Method: {evt.Target}({evt.Argument ?? string.Empty})");
        }
    }

    private void InvokeNodeMethod(string target, string? argument)
    {
        string? nodeFilter = null;
        string methodName = target;
        int dotIdx = target.IndexOf('.');
        if (dotIdx > 0 && dotIdx < target.Length - 1)
        {
            nodeFilter = target[..dotIdx];
            methodName = target[(dotIdx + 1)..];
        }

        foreach (IExecutableNode node in _activeNodes)
        {
            if (nodeFilter != null && !string.Equals(node.NodeId, nodeFilter, StringComparison.Ordinal))
            {
                continue;
            }

            MethodInfo? method = node.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                continue;
            }

            ParameterInfo[] ps = method.GetParameters();
            try
            {
                if (ps.Length == 0)
                {
                    method.Invoke(node, null);
                }
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    method.Invoke(node, [argument]);
                }
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(ExecutionContext))
                {
                    method.Invoke(node, [_executionContext]);
                }
            }
            catch (Exception ex)
            {
                _runtimeError = $"[{node.NodeId}] {methodName}: {ex.InnerException?.Message ?? ex.Message}";
            }
        }
    }

    private void OnAssetSelected(AssetSelectedEvent evt)
    {
        if (_context == null || _executionContext == null)
        {
            return;
        }

        if (string.Equals(evt.AssetCategory, "sprite", StringComparison.OrdinalIgnoreCase))
        {
            string relative = _context.Workspace.ToRelativePath(evt.FilePath);
            _executionContext.SetStringProperty("sprite.path", relative);
            AddLog($"Active sprite: {relative}");
        }
    }

    private void OnAssetModified(AssetModifiedEvent evt)
    {
        if (_context == null || _executionContext == null)
        {
            return;
        }

        string ext = Path.GetExtension(evt.FilePath);
        string name = Path.GetFileName(evt.FilePath);

        if (string.Equals(name, "project.json", StringComparison.OrdinalIgnoreCase))
        {
            AddLog($"Hot-Reload: {name}");
            ReloadProjectNodes(applyProjectConfig: true);
        }
        else if (string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            AddLog($"Hot-Reload: {name}");
            ReloadProjectNodes(applyProjectConfig: false);
        }
        else
        {
            if ((string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)) &&
                File.Exists(evt.FilePath))
            {
                string currentSprite = _executionContext.GetString("sprite.path");
                if (string.IsNullOrWhiteSpace(currentSprite) || !File.Exists(_context.Workspace.ResolvePath(currentSprite)))
                {
                    string rel = _context.Workspace.ToRelativePath(evt.FilePath);
                    _executionContext.SetStringProperty("sprite.path", rel);
                }
            }

            AddLog($"Asset updated: {name}");
        }
    }

    private void OnProjectLoaded(ProjectLoadedEvent evt)
    {
        _lastProjectRootLoaded = Path.GetFullPath(evt.ProjectRoot);
        ReloadProjectNodes(applyProjectConfig: true);
    }

    private void OnWorkspaceChanged(WorkspaceChangedEvent evt)
    {
        string normalized = Path.GetFullPath(evt.ProjectRoot);
        if (string.Equals(_lastProjectRootLoaded, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _lastProjectRootLoaded = normalized;
        _executionContext?.ClearProperties();
        ReloadProjectNodes(applyProjectConfig: true);
    }

    private string[] ResolveNodeScriptPaths(
        bool applyProjectConfig,
        out Dictionary<string, string> stringProperties,
        out HashSet<string> disabledNodeIds,
        out Dictionary<string, int> configuredOrder)
    {
        stringProperties = new Dictionary<string, string>(StringComparer.Ordinal);
        disabledNodeIds = new HashSet<string>(StringComparer.Ordinal);
        configuredOrder = new Dictionary<string, int>(StringComparer.Ordinal);

        if (_context == null)
        {
            return Array.Empty<string>();
        }

        var pathSet = new HashSet<string>(StringComparer.Ordinal);
        var scriptPaths = new List<string>();

        foreach (string scriptFile in _context.Workspace.EnumerateFiles("scripts", "*.cs", recursive: true))
        {
            string fullPath = Path.GetFullPath(scriptFile);
            if (pathSet.Add(fullPath))
            {
                scriptPaths.Add(fullPath);
            }
        }

        string projectJsonPath = _context.Workspace.ResolvePath("project.json");
        if (File.Exists(projectJsonPath))
        {
            try
            {
                string json = File.ReadAllText(projectJsonPath);
                ProjectDocument? doc = JsonSerializer.Deserialize<ProjectDocument>(json, JsonOptions);
                if (doc != null && _executionContext != null && applyProjectConfig)
                {
                    ApplyManifestConfiguration(doc);
                }

                if (doc?.Nodes is { Count: > 0 })
                {
                    for (int i = 0; i < doc.Nodes.Count; i++)
                    {
                        ProjectNodeConfig nodeCfg = doc.Nodes[i];
                        if (!nodeCfg.Enabled)
                        {
                            if (!string.IsNullOrWhiteSpace(nodeCfg.NodeId))
                            {
                                disabledNodeIds.Add(nodeCfg.NodeId);
                            }

                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(nodeCfg.NodeId))
                        {
                            configuredOrder.TryAdd(nodeCfg.NodeId, i);
                        }

                        if (nodeCfg.Properties != null)
                        {
                            foreach (KeyValuePair<string, string> kvp in nodeCfg.Properties)
                            {
                                stringProperties[kvp.Key] = kvp.Value;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(nodeCfg.ScriptPath))
                        {
                            string resolved = Path.GetFullPath(_context.Workspace.ResolvePath(nodeCfg.ScriptPath));
                            if (File.Exists(resolved) && pathSet.Add(resolved))
                            {
                                scriptPaths.Add(resolved);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore partial JSON edits during save; scripts/ scan still applies cleanly.
            }
        }

        return scriptPaths.ToArray();
    }

    private void ApplyManifestConfiguration(ProjectDocument doc)
    {
        if (_executionContext == null)
        {
            return;
        }

        int virtualW = doc.Display?.VirtualWidth is >= 64 ? doc.Display.VirtualWidth.Value : doc.VirtualWidth;
        int virtualH = doc.Display?.VirtualHeight is >= 64 ? doc.Display.VirtualHeight.Value : doc.VirtualHeight;

        if (virtualW >= 64 && virtualH >= 64)
        {
            _executionContext.Display.VirtualResolution = new Vector2(virtualW, virtualH);
        }

        if (doc.Display != null)
        {
            _executionContext.Display.ScalingMode = Enum.TryParse(doc.Display.ScalingMode, ignoreCase: true, out ScalingMode scaling)
                ? scaling
                : ScalingMode.FitLetterbox;

            _executionContext.Display.Snapping = Enum.TryParse(doc.Display.Snapping, ignoreCase: true, out PixelSnapping snapping)
                ? snapping
                : PixelSnapping.Disabled;
        }
        else
        {
            _executionContext.Display.Snapping = PixelSnapping.Disabled;
        }

        foreach (AudioBus bus in Enum.GetValues<AudioBus>())
        {
            _executionContext.Audio.SetBusVolume(bus, 1.0f);
        }

        if (doc.AudioBuses != null)
        {
            foreach (KeyValuePair<string, float> kvp in doc.AudioBuses)
            {
                if (Enum.TryParse(kvp.Key, ignoreCase: true, out AudioBus parsedBus))
                {
                    _executionContext.Audio.SetBusVolume(parsedBus, kvp.Value);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool LoadAndActivateNodes(
        byte[]? peBytes,
        HashSet<string> disabledNodeIds,
        Dictionary<string, int> configuredOrder,
        out IReadOnlyList<string> errors)
    {
        if (_context == null || _executionContext == null)
        {
            errors = Array.Empty<string>();
            return false;
        }

        ScriptCompileResult result = ScriptCompiler.LoadNodesFromPeBytes(peBytes);
        if (!result.Success)
        {
            errors = result.Errors;
            return false;
        }

        errors = Array.Empty<string>();
        _scriptLoadContext = result.LoadContext;

        var scope = new ScriptAssemblyScope(_context.EventBus, _context.Graphics);
        _scriptScope = scope;
        _executionContext.AttachScope(scope);

        IEnumerable<IExecutableNode> orderedNodes = result.Nodes
            .Where(n => !disabledNodeIds.Contains(n.NodeId))
            .OrderBy(n => configuredOrder.TryGetValue(n.NodeId, out int idx) ? idx : int.MaxValue)
            .ThenBy(n => n.NodeId, StringComparer.Ordinal);

        foreach (IExecutableNode node in orderedNodes)
        {
            try
            {
                scope.RegisterNode(node);
                node.OnInit(_executionContext);
                _activeNodes.Add(node);
            }
            catch (Exception ex)
            {
                _runtimeError = $"[{node.NodeId}] OnInit: {ex.Message}";
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UnloadActiveNodes()
    {
        // Clear references held by plugin first so ALC isn't kept rooted if scope disposal throws.
        var fallbackNodes = _activeNodes.ToArray();
        _activeNodes.Clear();

        if (_scriptScope != null)
        {
            ScriptAssemblyScope scope = _scriptScope;
            _scriptScope = null;
            scope.Dispose((node, ex) =>
            {
                _runtimeError = $"[{node.NodeId}] OnUnload: {ex.Message}";
                AddLog($"[{node.NodeId}] OnUnload error: {ex.Message}");
            });
        }
        else
        {
            for (int i = fallbackNodes.Length - 1; i >= 0; i--)
            {
                IExecutableNode node = fallbackNodes[i];
                try
                {
                    node.OnUnload();
                }
                catch (Exception ex)
                {
                    _runtimeError = $"[{node.NodeId}] OnUnload: {ex.Message}";
                    AddLog($"[{node.NodeId}] OnUnload error: {ex.Message}");
                }
            }
        }

        if (_context != null && _executionContext != null)
        {
            // Detach the disposed scope from ExecutionContext so no references to old script assembly remain reachable.
            _executionContext.AttachScope(new ScriptAssemblyScope(_context.EventBus, _context.Graphics));
        }

        if (_scriptLoadContext != null)
        {
            _scriptLoadContext.Unload();
            _scriptLoadContext = null;
        }
    }

    private void AddLog(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {message}";
        _recentEvents.Add(line);
        if (_recentEvents.Count > 40)
        {
            _recentEvents.RemoveAt(0);
        }
    }
}
