using System.Numerics;
using System.Text;
using System.Text.Json;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Engine.Core.Plugins;
using Engine.Plugins.Settings;
using Engine.Plugins.Workspace;
using ImGuiNET;

namespace Engine.Plugins.Timeline;

public sealed class UniversalTimelinePlugin : IEnginePlugin
{
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

    private PluginContext? _context;
    private float _currentTime;
    private float _maxDuration = 4.0f;
    private bool _isPlaying;
    private bool _loop = true;
    private bool _justStartedFromZero;
    private bool _initialSyncPending = true;
    private DateTime _suppressProjectReloadUntilUtc = DateTime.MinValue;
    private string? _lastProjectRootLoaded;

    private List<PropertyTrack> _propertyTracks = new();
    private List<TriggerTrack> _triggerTracks = new();
    private readonly Dictionary<string, float> _lastEvaluatedValues = new(StringComparer.Ordinal);
    private readonly Dictionary<TimelineTrigger, float> _triggerFlashRemaining = new();
    private string _statusText = "Timeline ready";

    public string Id => "workbench.timeline";
    public string DisplayName => "Timeline";

    public float CurrentTime => _currentTime;
    public float MaxDuration => _maxDuration;
    public bool IsPlaying => _isPlaying;

    public void OnLoaded(PluginContext context)
    {
        _context = context;
        _context.EventBus.Subscribe<AssetModifiedEvent>(OnAssetModified);
        _context.EventBus.Subscribe<ProjectLoadedEvent>(OnProjectLoaded);
        _context.EventBus.Subscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
        _context.EventBus.Subscribe<TimelineUpsertKeyframeEvent>(OnUpsertKeyframe);

        LoadTimelineFromProjectJson();
        EvaluatePropertiesAndPublish(isManualScrub: true);
        _lastProjectRootLoaded = Path.GetFullPath(context.Workspace.ProjectRoot);
    }

    public void OnUpdate(float deltaTime)
    {
        if (_context == null)
        {
            return;
        }

        if (_initialSyncPending)
        {
            _initialSyncPending = false;
            EvaluatePropertiesAndPublish(isManualScrub: true);
        }

        DecayTriggerFlashes(deltaTime);

        if (!_isPlaying || _maxDuration <= 0f)
        {
            return;
        }

        float prevTime = _currentTime;
        float nextTime = prevTime + deltaTime;

        if (_loop)
        {
            if (nextTime >= _maxDuration)
            {
                FireTriggersInRange(prevTime, _maxDuration, includeStartBoundary: _justStartedFromZero, includeEndBoundary: true);
                nextTime %= _maxDuration;
                FireTriggersInRange(0f, nextTime, includeStartBoundary: true, includeEndBoundary: true);
                _justStartedFromZero = false;
            }
            else
            {
                FireTriggersInRange(prevTime, nextTime, includeStartBoundary: _justStartedFromZero, includeEndBoundary: true);
                _justStartedFromZero = false;
            }
        }
        else
        {
            if (nextTime >= _maxDuration)
            {
                nextTime = _maxDuration;
                FireTriggersInRange(prevTime, nextTime, includeStartBoundary: _justStartedFromZero, includeEndBoundary: true);
                _justStartedFromZero = false;
                _isPlaying = false;
            }
            else
            {
                FireTriggersInRange(prevTime, nextTime, includeStartBoundary: _justStartedFromZero, includeEndBoundary: true);
                _justStartedFromZero = false;
            }
        }

        _currentTime = Math.Clamp(nextTime, 0f, _maxDuration);
        EvaluatePropertiesAndPublish(isManualScrub: false);
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
        if (!ImGui.Begin("Timeline", ref keepOpen))
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

        RenderTransportControls();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        RenderTrackLanes();

        ImGui.End();
    }

    public void OnUnloaded()
    {
        if (_context != null)
        {
            _context.EventBus.Unsubscribe<AssetModifiedEvent>(OnAssetModified);
            _context.EventBus.Unsubscribe<ProjectLoadedEvent>(OnProjectLoaded);
            _context.EventBus.Unsubscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
            _context.EventBus.Unsubscribe<TimelineUpsertKeyframeEvent>(OnUpsertKeyframe);
        }
    }

    private void RenderTransportControls()
    {
        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        if (_isPlaying)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, theme.AccentButton);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.AccentButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.AccentButtonActive);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, theme.PlayButton);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.PlayButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.PlayButton);
        }

        if (ImGui.Button(_isPlaying ? "Pause" : "Play", new Vector2(68f, 0f)))
        {
            if (_isPlaying)
            {
                _isPlaying = false;
                PublishTimelineState(isManualScrub: false);
            }
            else
            {
                if (_currentTime >= _maxDuration && !_loop)
                {
                    _currentTime = 0f;
                }

                _isPlaying = true;
                _justStartedFromZero = _currentTime <= 1e-5f;
                PublishTimelineState(isManualScrub: false);
            }
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        if (ImGui.Button("Reset", new Vector2(60f, 0f)))
        {
            _isPlaying = false;
            _currentTime = 0f;
            _justStartedFromZero = false;
            EvaluatePropertiesAndPublish(isManualScrub: true);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Loop", ref _loop);

        ImGui.SameLine(0f, 16f);
        ImGui.SetNextItemWidth(MathF.Min(360f, MathF.Max(160f, ImGui.GetContentRegionAvail().X - 240f)));
        if (ImGui.SliderFloat("Time", ref _currentTime, 0f, _maxDuration, "%.2f s"))
        {
            _justStartedFromZero = false;
            EvaluatePropertiesAndPublish(isManualScrub: true);
        }

        ImGui.SameLine(0f, 12f);
        ImGui.TextColored(
            _isPlaying ? new Vector4(0.45f, 0.88f, 0.56f, 1.0f) : new Vector4(0.60f, 0.64f, 0.72f, 1.0f),
            _isPlaying ? "PLAYING" : "PAUSED");

        ImGui.SameLine();
        ImGui.TextDisabled($"| {_statusText}".Replace("%", "%%", StringComparison.Ordinal));
    }

    private void RenderTrackLanes()
    {
        ImGui.BeginChild("TimelineTracksArea", Vector2.Zero, ImGuiChildFlags.None);

        const float labelWidth = 240f;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float barWidth = MathF.Max(140f, availableWidth - labelWidth - 16f);

        RenderRulerRow(labelWidth, barWidth);
        ImGui.Separator();

        foreach (PropertyTrack track in _propertyTracks)
        {
            RenderPropertyTrackRow(track, labelWidth, barWidth);
        }

        foreach (TriggerTrack track in _triggerTracks)
        {
            RenderTriggerTrackRow(track, labelWidth, barWidth);
        }

        ImGui.EndChild();
    }

    private void RenderRulerRow(float labelWidth, float barWidth)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Timeline Ruler");
        ImGui.SameLine(labelWidth);

        Vector2 barOrigin = ImGui.GetCursorScreenPos();
        float rowHeight = 22f;
        ImGui.InvisibleButton("##TimelineRulerScrub", new Vector2(barWidth, rowHeight));
        HandleLaneScrub(barOrigin.X, barWidth);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        uint bgColor = ImGui.ColorConvertFloat4ToU32(theme.TimelineRulerBg);
        uint tickColor = ImGui.ColorConvertFloat4ToU32(theme.TimelineTickColor);
        uint playheadColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.34f, 0.34f, 1.0f));

        drawList.AddRectFilled(barOrigin, barOrigin + new Vector2(barWidth, rowHeight), bgColor, 2f);

        int steps = Math.Max(1, (int)MathF.Ceiling(_maxDuration * 2f));
        for (int i = 0; i <= steps; i++)
        {
            float t = i * 0.5f;
            if (t > _maxDuration + 1e-4f) break;

            float x = barOrigin.X + (t / MathF.Max(0.001f, _maxDuration)) * barWidth;
            bool major = (i % 2) == 0;
            drawList.AddLine(
                new Vector2(x, barOrigin.Y + (major ? 4f : 12f)),
                new Vector2(x, barOrigin.Y + rowHeight),
                tickColor,
                1f);

            if (major && x + 28f < barOrigin.X + barWidth + 20f)
            {
                drawList.AddText(new Vector2(x + 4f, barOrigin.Y + 3f), tickColor, $"{t:0.#}s");
            }
        }

        float playheadX = barOrigin.X + (_currentTime / MathF.Max(0.001f, _maxDuration)) * barWidth;
        drawList.AddTriangleFilled(
            new Vector2(playheadX - 5f, barOrigin.Y),
            new Vector2(playheadX + 5f, barOrigin.Y),
            new Vector2(playheadX, barOrigin.Y + 8f),
            playheadColor);
        drawList.AddLine(
            new Vector2(playheadX, barOrigin.Y),
            new Vector2(playheadX, barOrigin.Y + rowHeight),
            playheadColor,
            2f);
    }

    private void RenderPropertyTrackRow(PropertyTrack track, float labelWidth, float barWidth)
    {
        float currentValue = _lastEvaluatedValues.TryGetValue(track.TargetProperty, out float val)
            ? val
            : track.Evaluate(_currentTime);

        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        ImGui.AlignTextToFramePadding();
        string label = string.IsNullOrWhiteSpace(track.DisplayName) ? track.TargetProperty : track.DisplayName;
        ImGui.TextColored(theme.TimelinePropBadge, "PROP");
        ImGui.SameLine(0f, 8f);
        ImGui.TextUnformatted($"{label}: {currentValue:F1}");

        ImGui.SameLine(labelWidth);
        Vector2 barOrigin = ImGui.GetCursorScreenPos();
        float rowHeight = 22f;
        ImGui.InvisibleButton($"##PropTrack_{track.Id}", new Vector2(barWidth, rowHeight));
        HandleLaneScrub(barOrigin.X, barWidth);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint laneBg = ImGui.ColorConvertFloat4ToU32(theme.TimelinePropLaneBg);
        uint keyColor = ImGui.ColorConvertFloat4ToU32(theme.TimelineKeyColor);
        uint lineColor = ImGui.ColorConvertFloat4ToU32(theme.TimelineLineColor);
        uint playheadColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.34f, 0.34f, 0.9f));

        drawList.AddRectFilled(barOrigin, barOrigin + new Vector2(barWidth, rowHeight), laneBg, 2f);

        if (track.Keyframes.Count >= 2)
        {
            float x0 = barOrigin.X + (track.Keyframes[0].Time / MathF.Max(0.001f, _maxDuration)) * barWidth;
            float x1 = barOrigin.X + (track.Keyframes[^1].Time / MathF.Max(0.001f, _maxDuration)) * barWidth;
            drawList.AddLine(new Vector2(x0, barOrigin.Y + rowHeight * 0.5f), new Vector2(x1, barOrigin.Y + rowHeight * 0.5f), lineColor, 2f);
        }

        foreach (PropertyKeyframe kf in track.Keyframes)
        {
            float kx = barOrigin.X + (kf.Time / MathF.Max(0.001f, _maxDuration)) * barWidth;
            float ky = barOrigin.Y + rowHeight * 0.5f;
            const float r = 4.5f;
            drawList.AddQuadFilled(
                new Vector2(kx, ky - r),
                new Vector2(kx + r, ky),
                new Vector2(kx, ky + r),
                new Vector2(kx - r, ky),
                keyColor);
        }

        float playheadX = barOrigin.X + (_currentTime / MathF.Max(0.001f, _maxDuration)) * barWidth;
        drawList.AddLine(new Vector2(playheadX, barOrigin.Y), new Vector2(playheadX, barOrigin.Y + rowHeight), playheadColor, 1.5f);
    }

    private void RenderTriggerTrackRow(TriggerTrack track, float labelWidth, float barWidth)
    {
        ImGui.AlignTextToFramePadding();
        string label = string.IsNullOrWhiteSpace(track.DisplayName) ? track.Id : track.DisplayName;
        ImGui.TextColored(new Vector4(0.92f, 0.72f, 0.34f, 1.0f), "TRIG");
        ImGui.SameLine(0f, 8f);
        ImGui.TextUnformatted(label);

        if (track.Triggers.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Test##{track.Id}"))
            {
                FireTrigger(track, track.Triggers[0]);
            }
        }

        ImGui.SameLine(labelWidth);
        Vector2 barOrigin = ImGui.GetCursorScreenPos();
        float rowHeight = 22f;
        ImGui.InvisibleButton($"##TrigTrack_{track.Id}", new Vector2(barWidth, rowHeight));
        HandleLaneScrub(barOrigin.X, barWidth);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint laneBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.17f, 0.15f, 0.14f, 1.0f));
        uint markerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.70f, 0.28f, 1.0f));
        uint flashColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.98f, 0.36f, 0.28f, 1.0f));
        uint playheadColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.34f, 0.34f, 0.9f));

        drawList.AddRectFilled(barOrigin, barOrigin + new Vector2(barWidth, rowHeight), laneBg, 2f);

        foreach (TimelineTrigger trigger in track.Triggers)
        {
            float tx = barOrigin.X + (trigger.Time / MathF.Max(0.001f, _maxDuration)) * barWidth;
            float ty = barOrigin.Y + rowHeight * 0.5f;
            bool flashing = _triggerFlashRemaining.TryGetValue(trigger, out float rem) && rem > 0f;
            drawList.AddCircleFilled(new Vector2(tx, ty), flashing ? 5.5f : 4.2f, flashing ? flashColor : markerColor);
        }

        float playheadX = barOrigin.X + (_currentTime / MathF.Max(0.001f, _maxDuration)) * barWidth;
        drawList.AddLine(new Vector2(playheadX, barOrigin.Y), new Vector2(playheadX, barOrigin.Y + rowHeight), playheadColor, 1.5f);
    }

    private void HandleLaneScrub(float barStartX, float barWidth)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            float norm = Math.Clamp((ImGui.GetMousePos().X - barStartX) / MathF.Max(1f, barWidth), 0f, 1f);
            float scrubbedTime = norm * _maxDuration;
            if (MathF.Abs(scrubbedTime - _currentTime) > 1e-4f || _isPlaying)
            {
                _currentTime = scrubbedTime;
                _justStartedFromZero = false;
                EvaluatePropertiesAndPublish(isManualScrub: true);
            }
        }
    }

    private void OnUpsertKeyframe(TimelineUpsertKeyframeEvent evt)
    {
        if (_context == null || string.IsNullOrWhiteSpace(evt.TargetProperty))
        {
            return;
        }

        float keyTime = MathF.Round(Math.Clamp(evt.TimeOverride ?? _currentTime, 0f, _maxDuration), 2);
        PropertyTrack? track = _propertyTracks.FirstOrDefault(
            t => string.Equals(t.TargetProperty, evt.TargetProperty, StringComparison.Ordinal));

        if (track == null)
        {
            track = new PropertyTrack
            {
                Id = $"track_{evt.TargetProperty.Replace('.', '_')}",
                DisplayName = string.IsNullOrWhiteSpace(evt.DisplayName) ? evt.TargetProperty : evt.DisplayName,
                TargetProperty = evt.TargetProperty
            };
            _propertyTracks.Add(track);
        }

        PropertyKeyframe? existing = track.Keyframes.FirstOrDefault(k => MathF.Abs(k.Time - keyTime) <= 0.02f);
        if (existing != null)
        {
            existing.Value = evt.Value;
            existing.Easing = evt.Easing;
        }
        else
        {
            track.Keyframes.Add(new PropertyKeyframe
            {
                Time = keyTime,
                Value = evt.Value,
                Easing = evt.Easing
            });
        }

        track.SortKeyframes();
        _lastEvaluatedValues[track.TargetProperty] = evt.Value;
        _context.Runtime.SetProperty(track.TargetProperty, evt.Value);
        _statusText = $"{_propertyTracks.Count} Prop / {_triggerTracks.Count} Trig";

        if (evt.SaveProjectJson)
        {
            SaveTimelineToProjectJson();
        }
    }

    private void EvaluatePropertiesAndPublish(bool isManualScrub)
    {
        if (_context == null)
        {
            return;
        }

        foreach (PropertyTrack track in _propertyTracks)
        {
            float val = track.Evaluate(_currentTime);
            _lastEvaluatedValues[track.TargetProperty] = val;
            _context.Runtime.SetProperty(track.TargetProperty, val);
            _context.EventBus.Publish(new TimelinePropertyEvaluatedEvent(track.Id, track.TargetProperty, val));
        }

        _context.Runtime.CurrentTime = _currentTime;
        _context.Runtime.MaxDuration = _maxDuration;
        _context.Runtime.IsPlaying = _isPlaying;

        PublishTimelineState(isManualScrub);
    }

    private void PublishTimelineState(bool isManualScrub)
    {
        _context?.EventBus.Publish(new TimelineStateChangedEvent(_currentTime, _maxDuration, _isPlaying, isManualScrub));
    }

    private void FireTriggersInRange(float startTime, float endTime, bool includeStartBoundary, bool includeEndBoundary)
    {
        foreach (TriggerTrack track in _triggerTracks)
        {
            foreach (TimelineTrigger trigger in track.Triggers)
            {
                bool afterStart = includeStartBoundary ? trigger.Time >= startTime : trigger.Time > startTime;
                bool beforeEnd = includeEndBoundary ? trigger.Time <= endTime : trigger.Time < endTime;
                if (afterStart && beforeEnd)
                {
                    FireTrigger(track, trigger);
                }
            }
        }
    }

    private void FireTrigger(TriggerTrack track, TimelineTrigger trigger)
    {
        if (_context == null)
        {
            return;
        }

        _triggerFlashRemaining[trigger] = 0.25f;
        _context.EventBus.Publish(new TimelineTriggerFiredEvent(
            track.Id,
            trigger.Time,
            trigger.ActionType,
            trigger.Target,
            trigger.Argument,
            trigger.Volume,
            trigger.Pitch));
    }

    private void DecayTriggerFlashes(float deltaTime)
    {
        if (_triggerFlashRemaining.Count == 0)
        {
            return;
        }

        foreach (TimelineTrigger key in _triggerFlashRemaining.Keys.ToArray())
        {
            float rem = _triggerFlashRemaining[key] - deltaTime;
            if (rem <= 0f)
            {
                _triggerFlashRemaining.Remove(key);
            }
            else
            {
                _triggerFlashRemaining[key] = rem;
            }
        }
    }

    private void OnAssetModified(AssetModifiedEvent evt)
    {
        if (DateTime.UtcNow < _suppressProjectReloadUntilUtc)
        {
            return;
        }

        if (string.Equals(Path.GetFileName(evt.FilePath), "project.json", StringComparison.OrdinalIgnoreCase))
        {
            LoadTimelineFromProjectJson();
            EvaluatePropertiesAndPublish(isManualScrub: true);
        }
    }

    private void OnProjectLoaded(ProjectLoadedEvent evt)
    {
        _lastProjectRootLoaded = Path.GetFullPath(evt.ProjectRoot);
        ResetAndReloadTimeline();
    }

    private void OnWorkspaceChanged(WorkspaceChangedEvent evt)
    {
        string normalized = Path.GetFullPath(evt.ProjectRoot);
        if (string.Equals(_lastProjectRootLoaded, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _lastProjectRootLoaded = normalized;
        ResetAndReloadTimeline();
    }

    private void ResetAndReloadTimeline()
    {
        _isPlaying = false;
        _currentTime = 0f;
        _justStartedFromZero = false;
        _initialSyncPending = true;
        _propertyTracks.Clear();
        _triggerTracks.Clear();
        _lastEvaluatedValues.Clear();
        _triggerFlashRemaining.Clear();
        LoadTimelineFromProjectJson();
        EvaluatePropertiesAndPublish(isManualScrub: true);
    }

    private void LoadTimelineFromProjectJson()
    {
        if (_context == null)
        {
            return;
        }

        string projectJsonPath = _context.Workspace.ResolvePath("project.json");
        if (!File.Exists(projectJsonPath))
        {
            _statusText = "project.json not found";
            return;
        }

        try
        {
            string json = File.ReadAllText(projectJsonPath);
            ProjectDocument? doc = JsonSerializer.Deserialize<ProjectDocument>(json, ReadJsonOptions);
            if (doc?.Timeline == null)
            {
                _maxDuration = 4.0f;
                _loop = true;
                _propertyTracks = new List<PropertyTrack>();
                _triggerTracks = new List<TriggerTrack>();
                _statusText = "0 Prop / 0 Trig";
                return;
            }

            _maxDuration = MathF.Max(0.1f, doc.Timeline.MaxDuration);
            _loop = doc.Timeline.Loop;
            _currentTime = Math.Clamp(_currentTime, 0f, _maxDuration);

            _propertyTracks = doc.Timeline.PropertyTracks ?? new List<PropertyTrack>();
            foreach (PropertyTrack track in _propertyTracks)
            {
                track.SortKeyframes();
            }

            _triggerTracks = doc.Timeline.TriggerTracks ?? new List<TriggerTrack>();
            foreach (TriggerTrack track in _triggerTracks)
            {
                track.SortTriggers();
            }

            _statusText = $"{_propertyTracks.Count} Prop / {_triggerTracks.Count} Trig";
        }
        catch (Exception ex)
        {
            _statusText = $"JSON error: {ex.Message}";
            _context.EventBus.Publish(new EngineLogEvent("Timeline", "Error", $"Failed to load timeline: {ex.Message}"));
        }
    }

    private void SaveTimelineToProjectJson()
    {
        if (_context == null)
        {
            return;
        }

        string projectJsonPath = Path.GetFullPath(_context.Workspace.ResolvePath("project.json"));
        if (!File.Exists(projectJsonPath))
        {
            return;
        }

        string tempPath = projectJsonPath + ".tmp";
        bool watcherSuppressed = false;

        try
        {
            string json = File.ReadAllText(projectJsonPath);
            ProjectDocument? doc = JsonSerializer.Deserialize<ProjectDocument>(json, ReadJsonOptions);
            if (doc == null)
            {
                return;
            }

            doc.Timeline = new TimelineConfig
            {
                MaxDuration = _maxDuration,
                Loop = _loop,
                PropertyTracks = _propertyTracks,
                TriggerTracks = _triggerTracks
            };

            _suppressProjectReloadUntilUtc = DateTime.UtcNow.AddMilliseconds(1000);
            _context.EventBus.Publish(new FileWatcherSelfSaveEvent(projectJsonPath, IsSaving: true));
            watcherSuppressed = true;

            string updatedJson = JsonSerializer.Serialize(doc, WriteJsonOptions);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(updatedJson);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, projectJsonPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _context.EventBus.Publish(new EngineLogEvent("Timeline", "Error", $"Failed to save timeline: {ex.Message}"));
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

            if (watcherSuppressed)
            {
                _context.EventBus.Publish(new FileWatcherSelfSaveEvent(projectJsonPath, IsSaving: false));
            }
        }
    }
}
