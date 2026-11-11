using System.Numerics;

namespace Engine.Plugins.Viewport;

public enum ViewportInteractionMode
{
    Edit = 0,
    Play = 1
}

public enum ViewportGizmoTool
{
    Move = 0,
    Rotate = 1,
    Scale = 2
}

public sealed record ViewportCanvasClickEvent(
    Vector2 VirtualPosition,
    bool ShiftDown,
    bool ControlOrSuperDown);

public sealed record ViewportSelectionStateEvent(
    bool HasSelection,
    string ItemId,
    string ItemName,
    Vector2 WorldPosition,
    Vector2 BaseSize,
    Vector2 OriginNormalized,
    float WorldRotationDegrees,
    Vector2 WorldScale,
    bool Locked,
    string SourceLabel = "")
{
    public static ViewportSelectionStateEvent None { get; } = new(
        HasSelection: false,
        ItemId: string.Empty,
        ItemName: string.Empty,
        WorldPosition: Vector2.Zero,
        BaseSize: Vector2.Zero,
        OriginNormalized: new Vector2(0.5f, 0.5f),
        WorldRotationDegrees: 0f,
        WorldScale: Vector2.One,
        Locked: false,
        SourceLabel: string.Empty);
}

public sealed record ViewportTransformCommitEvent(
    string ItemId,
    Vector2 WorldPosition,
    float WorldRotationDegrees,
    Vector2 WorldScale,
    bool IsBeginDrag,
    bool IsFinalSnapshot);
