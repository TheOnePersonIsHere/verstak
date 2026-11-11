using System.Numerics;

namespace Engine.Core.Events;

public sealed record AssetModifiedEvent(string FilePath);

public sealed record ProjectLoadedEvent(string ProjectRoot);

public sealed record AssetSelectedEvent(string FilePath, string AssetCategory);

public sealed record ViewportResolutionChangedEvent(int VirtualWidth, int VirtualHeight);

public sealed record ViewportRenderPassEvent(int VirtualWidth, int VirtualHeight);

public sealed record ViewportMouseMovedEvent(Vector2 VirtualPosition, bool IsInsideViewport);

public sealed record TimelineStateChangedEvent(
    float CurrentTime,
    float MaxDuration,
    bool IsPlaying,
    bool IsManualScrub);

public sealed record TimelinePropertyEvaluatedEvent(
    string TrackId,
    string TargetProperty,
    float Value);

public sealed record TimelineTriggerFiredEvent(
    string TrackId,
    float Time,
    string ActionType,
    string Target,
    string? Argument,
    float Volume = 1.0f,
    float Pitch = 1.0f);

public sealed record EngineLogEvent(string Source, string Level, string Message);

public sealed record SettingChangedEvent(string Key);

public sealed record PluginStateChangedEvent(string PluginId, bool Enabled, bool WindowVisible);

public sealed record ActiveDocumentChangedEvent(string FilePath);
