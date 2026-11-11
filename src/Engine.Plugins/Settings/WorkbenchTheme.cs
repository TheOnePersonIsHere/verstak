using System.Numerics;
using ImGuiNET;

namespace Engine.Plugins.Settings;

public sealed record WorkbenchThemePalette(
    string Id,
    string DisplayName,
    Vector4 Text,
    Vector4 TextDisabled,
    Vector4 WindowBg,
    Vector4 ChildBg,
    Vector4 PopupBg,
    Vector4 Border,
    Vector4 BorderShadow,
    Vector4 FrameBg,
    Vector4 FrameBgHovered,
    Vector4 FrameBgActive,
    Vector4 TitleBg,
    Vector4 TitleBgActive,
    Vector4 TitleBgCollapsed,
    Vector4 MenuBarBg,
    Vector4 ScrollbarBg,
    Vector4 ScrollbarGrab,
    Vector4 ScrollbarGrabHovered,
    Vector4 ScrollbarGrabActive,
    Vector4 CheckMark,
    Vector4 SliderGrab,
    Vector4 SliderGrabActive,
    Vector4 Button,
    Vector4 ButtonHovered,
    Vector4 ButtonActive,
    Vector4 Header,
    Vector4 HeaderHovered,
    Vector4 HeaderActive,
    Vector4 Separator,
    Vector4 SeparatorHovered,
    Vector4 SeparatorActive,
    Vector4 ResizeGrip,
    Vector4 ResizeGripHovered,
    Vector4 ResizeGripActive,
    Vector4 Tab,
    Vector4 TabHovered,
    Vector4 TabSelected,
    Vector4 TabSelectedOverline,
    Vector4 TabDimmed,
    Vector4 TabDimmedSelected,
    Vector4 TabDimmedSelectedOverline,
    Vector4 DockingPreview,
    Vector4 DockingEmptyBg,
    Vector4 PlotLines,
    Vector4 PlotLinesHovered,
    Vector4 PlotHistogram,
    Vector4 PlotHistogramHovered,
    Vector4 TableHeaderBg,
    Vector4 TableBorderStrong,
    Vector4 TableBorderLight,
    Vector4 TableRowBg,
    Vector4 TableRowBgAlt,
    Vector4 TextLink,
    Vector4 TextSelectedBg,
    Vector4 DragDropTarget,
    Vector4 NavCursor,
    Vector4 NavWindowingHighlight,
    // Semantic workbench UI accents used by plugins (custom buttons, timelines, overlays)
    Vector4 AccentButton,
    Vector4 AccentButtonHovered,
    Vector4 AccentButtonActive,
    Vector4 SubtleButton,
    Vector4 SubtleButtonHovered,
    Vector4 PlayButton,
    Vector4 PlayButtonHovered,
    Vector4 PanelSubheaderText,
    Vector4 CanvasBackdrop,
    Vector4 CanvasBorder,
    Vector4 GridLine,
    Vector4 GridAxis,
    Vector4 OverlayBg,
    Vector4 OverlayBorder,
    Vector4 OverlayPrimaryText,
    Vector4 OverlaySecondaryText,
    Vector4 GizmoOutline,
    Vector4 GizmoHandleFill,
    Vector4 TimelineRulerBg,
    Vector4 TimelineTickColor,
    Vector4 TimelinePropLaneBg,
    Vector4 TimelineKeyColor,
    Vector4 TimelineLineColor,
    Vector4 TimelinePropBadge);

public static class WorkbenchTheme
{
    public const string SettingKey = "ui.theme";
    public const string StudioGraphiteId = "studio_graphite";
    public const string NordSlateId = "nord_slate";
    public const string ClassicDarkId = "classic_dark";

    public static readonly WorkbenchThemePalette StudioGraphite = new(
        Id: StudioGraphiteId,
        DisplayName: "Studio Graphite (Warm)",
        Text: new Vector4(0.91f, 0.90f, 0.89f, 1.00f),
        TextDisabled: new Vector4(0.54f, 0.54f, 0.56f, 1.00f),
        WindowBg: new Vector4(0.10f, 0.10f, 0.11f, 1.00f),
        ChildBg: new Vector4(0.12f, 0.12f, 0.13f, 1.00f),
        PopupBg: new Vector4(0.12f, 0.12f, 0.13f, 1.00f),
        Border: new Vector4(0.18f, 0.18f, 0.20f, 0.60f),
        BorderShadow: new Vector4(0.00f, 0.00f, 0.00f, 0.00f),
        FrameBg: new Vector4(0.16f, 0.16f, 0.18f, 1.00f),
        FrameBgHovered: new Vector4(0.22f, 0.22f, 0.24f, 1.00f),
        FrameBgActive: new Vector4(0.28f, 0.24f, 0.20f, 1.00f),
        TitleBg: new Vector4(0.08f, 0.08f, 0.09f, 1.00f),
        TitleBgActive: new Vector4(0.13f, 0.13f, 0.14f, 1.00f),
        TitleBgCollapsed: new Vector4(0.08f, 0.08f, 0.09f, 1.00f),
        MenuBarBg: new Vector4(0.08f, 0.08f, 0.09f, 1.00f),
        ScrollbarBg: new Vector4(0.08f, 0.08f, 0.09f, 0.85f),
        ScrollbarGrab: new Vector4(0.24f, 0.24f, 0.26f, 1.00f),
        ScrollbarGrabHovered: new Vector4(0.32f, 0.31f, 0.30f, 1.00f),
        ScrollbarGrabActive: new Vector4(0.60f, 0.45f, 0.25f, 1.00f),
        CheckMark: new Vector4(0.72f, 0.54f, 0.32f, 1.00f),
        SliderGrab: new Vector4(0.72f, 0.54f, 0.32f, 1.00f),
        SliderGrabActive: new Vector4(0.84f, 0.64f, 0.38f, 1.00f),
        Button: new Vector4(0.18f, 0.18f, 0.20f, 1.00f),
        ButtonHovered: new Vector4(0.24f, 0.24f, 0.26f, 1.00f),
        ButtonActive: new Vector4(0.60f, 0.45f, 0.25f, 1.00f),
        Header: new Vector4(0.20f, 0.20f, 0.22f, 1.00f),
        HeaderHovered: new Vector4(0.26f, 0.26f, 0.29f, 1.00f),
        HeaderActive: new Vector4(0.32f, 0.28f, 0.24f, 1.00f),
        Separator: new Vector4(0.18f, 0.18f, 0.20f, 1.00f),
        SeparatorHovered: new Vector4(0.48f, 0.38f, 0.25f, 0.80f),
        SeparatorActive: new Vector4(0.72f, 0.54f, 0.32f, 1.00f),
        ResizeGrip: new Vector4(0.30f, 0.26f, 0.22f, 0.30f),
        ResizeGripHovered: new Vector4(0.56f, 0.43f, 0.27f, 0.72f),
        ResizeGripActive: new Vector4(0.72f, 0.54f, 0.32f, 0.95f),
        Tab: new Vector4(0.11f, 0.11f, 0.12f, 1.00f),
        TabHovered: new Vector4(0.38f, 0.30f, 0.20f, 1.00f),
        TabSelected: new Vector4(0.72f, 0.54f, 0.32f, 1.00f),
        TabSelectedOverline: new Vector4(0.86f, 0.66f, 0.40f, 1.00f),
        TabDimmed: new Vector4(0.09f, 0.09f, 0.10f, 1.00f),
        TabDimmedSelected: new Vector4(0.24f, 0.20f, 0.17f, 1.00f),
        TabDimmedSelectedOverline: new Vector4(0.55f, 0.42f, 0.26f, 0.70f),
        DockingPreview: new Vector4(0.72f, 0.54f, 0.32f, 0.45f),
        DockingEmptyBg: new Vector4(0.08f, 0.08f, 0.09f, 1.00f),
        PlotLines: new Vector4(0.72f, 0.54f, 0.32f, 1.00f),
        PlotLinesHovered: new Vector4(0.88f, 0.68f, 0.42f, 1.00f),
        PlotHistogram: new Vector4(0.68f, 0.51f, 0.30f, 1.00f),
        PlotHistogramHovered: new Vector4(0.84f, 0.64f, 0.38f, 1.00f),
        TableHeaderBg: new Vector4(0.15f, 0.15f, 0.16f, 1.00f),
        TableBorderStrong: new Vector4(0.22f, 0.22f, 0.24f, 1.00f),
        TableBorderLight: new Vector4(0.17f, 0.17f, 0.19f, 0.80f),
        TableRowBg: new Vector4(0.00f, 0.00f, 0.00f, 0.00f),
        TableRowBgAlt: new Vector4(1.00f, 1.00f, 1.00f, 0.03f),
        TextLink: new Vector4(0.82f, 0.63f, 0.39f, 1.00f),
        TextSelectedBg: new Vector4(0.72f, 0.54f, 0.32f, 0.34f),
        DragDropTarget: new Vector4(0.82f, 0.62f, 0.36f, 0.92f),
        NavCursor: new Vector4(0.72f, 0.54f, 0.32f, 0.90f),
        NavWindowingHighlight: new Vector4(0.85f, 0.68f, 0.44f, 0.70f),
        AccentButton: new Vector4(0.56f, 0.42f, 0.24f, 1.00f),
        AccentButtonHovered: new Vector4(0.66f, 0.50f, 0.29f, 1.00f),
        AccentButtonActive: new Vector4(0.72f, 0.54f, 0.32f, 1.00f),
        SubtleButton: new Vector4(0.18f, 0.18f, 0.20f, 1.00f),
        SubtleButtonHovered: new Vector4(0.24f, 0.24f, 0.26f, 1.00f),
        PlayButton: new Vector4(0.22f, 0.44f, 0.30f, 1.00f),
        PlayButtonHovered: new Vector4(0.27f, 0.52f, 0.36f, 1.00f),
        PanelSubheaderText: new Vector4(0.78f, 0.68f, 0.54f, 1.00f),
        CanvasBackdrop: new Vector4(0.07f, 0.07f, 0.08f, 1.00f),
        CanvasBorder: new Vector4(0.22f, 0.22f, 0.24f, 1.00f),
        GridLine: new Vector4(0.55f, 0.53f, 0.50f, 0.14f),
        GridAxis: new Vector4(0.72f, 0.54f, 0.32f, 0.34f),
        OverlayBg: new Vector4(0.08f, 0.08f, 0.09f, 0.86f),
        OverlayBorder: new Vector4(0.52f, 0.40f, 0.26f, 0.78f),
        OverlayPrimaryText: new Vector4(0.92f, 0.90f, 0.87f, 1.00f),
        OverlaySecondaryText: new Vector4(0.78f, 0.68f, 0.54f, 1.00f),
        GizmoOutline: new Vector4(0.85f, 0.66f, 0.39f, 0.95f),
        GizmoHandleFill: new Vector4(0.12f, 0.12f, 0.13f, 0.96f),
        TimelineRulerBg: new Vector4(0.13f, 0.13f, 0.14f, 1.00f),
        TimelineTickColor: new Vector4(0.58f, 0.57f, 0.55f, 0.85f),
        TimelinePropLaneBg: new Vector4(0.15f, 0.15f, 0.16f, 1.00f),
        TimelineKeyColor: new Vector4(0.78f, 0.60f, 0.36f, 1.00f),
        TimelineLineColor: new Vector4(0.56f, 0.43f, 0.27f, 0.78f),
        TimelinePropBadge: new Vector4(0.80f, 0.62f, 0.38f, 1.00f));

    public static readonly WorkbenchThemePalette NordSlate = new(
        Id: NordSlateId,
        DisplayName: "Nord Slate (Cool Steel)",
        Text: new Vector4(0.88f, 0.90f, 0.93f, 1.00f),
        TextDisabled: new Vector4(0.52f, 0.57f, 0.64f, 1.00f),
        WindowBg: new Vector4(0.12f, 0.14f, 0.17f, 1.00f),
        ChildBg: new Vector4(0.14f, 0.16f, 0.20f, 1.00f),
        PopupBg: new Vector4(0.14f, 0.16f, 0.20f, 1.00f),
        Border: new Vector4(0.20f, 0.23f, 0.28f, 0.65f),
        BorderShadow: new Vector4(0.00f, 0.00f, 0.00f, 0.00f),
        FrameBg: new Vector4(0.18f, 0.21f, 0.25f, 1.00f),
        FrameBgHovered: new Vector4(0.23f, 0.27f, 0.33f, 1.00f),
        FrameBgActive: new Vector4(0.28f, 0.33f, 0.40f, 1.00f),
        TitleBg: new Vector4(0.10f, 0.11f, 0.14f, 1.00f),
        TitleBgActive: new Vector4(0.15f, 0.18f, 0.22f, 1.00f),
        TitleBgCollapsed: new Vector4(0.10f, 0.11f, 0.14f, 1.00f),
        MenuBarBg: new Vector4(0.10f, 0.11f, 0.14f, 1.00f),
        ScrollbarBg: new Vector4(0.10f, 0.11f, 0.14f, 0.85f),
        ScrollbarGrab: new Vector4(0.25f, 0.29f, 0.35f, 1.00f),
        ScrollbarGrabHovered: new Vector4(0.32f, 0.38f, 0.46f, 1.00f),
        ScrollbarGrabActive: new Vector4(0.41f, 0.51f, 0.62f, 1.00f),
        CheckMark: new Vector4(0.51f, 0.64f, 0.76f, 1.00f),
        SliderGrab: new Vector4(0.51f, 0.64f, 0.76f, 1.00f),
        SliderGrabActive: new Vector4(0.62f, 0.75f, 0.87f, 1.00f),
        Button: new Vector4(0.19f, 0.22f, 0.27f, 1.00f),
        ButtonHovered: new Vector4(0.24f, 0.28f, 0.34f, 1.00f),
        ButtonActive: new Vector4(0.35f, 0.46f, 0.58f, 1.00f),
        Header: new Vector4(0.21f, 0.25f, 0.30f, 1.00f),
        HeaderHovered: new Vector4(0.26f, 0.31f, 0.38f, 1.00f),
        HeaderActive: new Vector4(0.30f, 0.37f, 0.45f, 1.00f),
        Separator: new Vector4(0.21f, 0.24f, 0.29f, 1.00f),
        SeparatorHovered: new Vector4(0.38f, 0.49f, 0.60f, 0.80f),
        SeparatorActive: new Vector4(0.51f, 0.64f, 0.76f, 1.00f),
        ResizeGrip: new Vector4(0.28f, 0.34f, 0.42f, 0.32f),
        ResizeGripHovered: new Vector4(0.42f, 0.53f, 0.64f, 0.70f),
        ResizeGripActive: new Vector4(0.51f, 0.64f, 0.76f, 0.95f),
        Tab: new Vector4(0.13f, 0.15f, 0.18f, 1.00f),
        TabHovered: new Vector4(0.27f, 0.34f, 0.42f, 1.00f),
        TabSelected: new Vector4(0.38f, 0.49f, 0.60f, 1.00f),
        TabSelectedOverline: new Vector4(0.56f, 0.70f, 0.82f, 1.00f),
        TabDimmed: new Vector4(0.11f, 0.12f, 0.15f, 1.00f),
        TabDimmedSelected: new Vector4(0.21f, 0.26f, 0.32f, 1.00f),
        TabDimmedSelectedOverline: new Vector4(0.40f, 0.50f, 0.60f, 0.70f),
        DockingPreview: new Vector4(0.51f, 0.64f, 0.76f, 0.42f),
        DockingEmptyBg: new Vector4(0.10f, 0.11f, 0.14f, 1.00f),
        PlotLines: new Vector4(0.51f, 0.64f, 0.76f, 1.00f),
        PlotLinesHovered: new Vector4(0.66f, 0.79f, 0.90f, 1.00f),
        PlotHistogram: new Vector4(0.46f, 0.58f, 0.70f, 1.00f),
        PlotHistogramHovered: new Vector4(0.60f, 0.74f, 0.86f, 1.00f),
        TableHeaderBg: new Vector4(0.16f, 0.19f, 0.23f, 1.00f),
        TableBorderStrong: new Vector4(0.22f, 0.26f, 0.32f, 1.00f),
        TableBorderLight: new Vector4(0.18f, 0.21f, 0.26f, 0.80f),
        TableRowBg: new Vector4(0.00f, 0.00f, 0.00f, 0.00f),
        TableRowBgAlt: new Vector4(0.88f, 0.92f, 1.00f, 0.03f),
        TextLink: new Vector4(0.56f, 0.71f, 0.84f, 1.00f),
        TextSelectedBg: new Vector4(0.45f, 0.58f, 0.70f, 0.35f),
        DragDropTarget: new Vector4(0.56f, 0.70f, 0.82f, 0.92f),
        NavCursor: new Vector4(0.51f, 0.64f, 0.76f, 0.90f),
        NavWindowingHighlight: new Vector4(0.62f, 0.75f, 0.86f, 0.70f),
        AccentButton: new Vector4(0.32f, 0.44f, 0.56f, 1.00f),
        AccentButtonHovered: new Vector4(0.39f, 0.52f, 0.65f, 1.00f),
        AccentButtonActive: new Vector4(0.48f, 0.61f, 0.74f, 1.00f),
        SubtleButton: new Vector4(0.19f, 0.22f, 0.27f, 1.00f),
        SubtleButtonHovered: new Vector4(0.24f, 0.28f, 0.34f, 1.00f),
        PlayButton: new Vector4(0.24f, 0.46f, 0.36f, 1.00f),
        PlayButtonHovered: new Vector4(0.29f, 0.55f, 0.43f, 1.00f),
        PanelSubheaderText: new Vector4(0.62f, 0.73f, 0.84f, 1.00f),
        CanvasBackdrop: new Vector4(0.09f, 0.10f, 0.13f, 1.00f),
        CanvasBorder: new Vector4(0.23f, 0.27f, 0.33f, 1.00f),
        GridLine: new Vector4(0.50f, 0.58f, 0.68f, 0.14f),
        GridAxis: new Vector4(0.51f, 0.64f, 0.76f, 0.30f),
        OverlayBg: new Vector4(0.10f, 0.12f, 0.15f, 0.86f),
        OverlayBorder: new Vector4(0.38f, 0.49f, 0.60f, 0.78f),
        OverlayPrimaryText: new Vector4(0.89f, 0.92f, 0.96f, 1.00f),
        OverlaySecondaryText: new Vector4(0.62f, 0.73f, 0.84f, 1.00f),
        GizmoOutline: new Vector4(0.56f, 0.72f, 0.86f, 0.95f),
        GizmoHandleFill: new Vector4(0.14f, 0.16f, 0.20f, 0.96f),
        TimelineRulerBg: new Vector4(0.14f, 0.16f, 0.20f, 1.00f),
        TimelineTickColor: new Vector4(0.56f, 0.62f, 0.70f, 0.85f),
        TimelinePropLaneBg: new Vector4(0.16f, 0.19f, 0.24f, 1.00f),
        TimelineKeyColor: new Vector4(0.56f, 0.72f, 0.86f, 1.00f),
        TimelineLineColor: new Vector4(0.38f, 0.49f, 0.60f, 0.78f),
        TimelinePropBadge: new Vector4(0.58f, 0.74f, 0.88f, 1.00f));

    public static readonly WorkbenchThemePalette ClassicDark = new(
        Id: ClassicDarkId,
        DisplayName: "Classic Dark (Neutral)",
        Text: new Vector4(0.90f, 0.90f, 0.90f, 1.00f),
        TextDisabled: new Vector4(0.53f, 0.53f, 0.53f, 1.00f),
        WindowBg: new Vector4(0.11f, 0.11f, 0.11f, 1.00f),
        ChildBg: new Vector4(0.14f, 0.14f, 0.14f, 1.00f),
        PopupBg: new Vector4(0.14f, 0.14f, 0.14f, 1.00f),
        Border: new Vector4(0.22f, 0.22f, 0.22f, 0.65f),
        BorderShadow: new Vector4(0.00f, 0.00f, 0.00f, 0.00f),
        FrameBg: new Vector4(0.18f, 0.18f, 0.18f, 1.00f),
        FrameBgHovered: new Vector4(0.24f, 0.24f, 0.24f, 1.00f),
        FrameBgActive: new Vector4(0.29f, 0.29f, 0.29f, 1.00f),
        TitleBg: new Vector4(0.08f, 0.08f, 0.08f, 1.00f),
        TitleBgActive: new Vector4(0.14f, 0.14f, 0.14f, 1.00f),
        TitleBgCollapsed: new Vector4(0.08f, 0.08f, 0.08f, 1.00f),
        MenuBarBg: new Vector4(0.08f, 0.08f, 0.08f, 1.00f),
        ScrollbarBg: new Vector4(0.09f, 0.09f, 0.09f, 0.85f),
        ScrollbarGrab: new Vector4(0.26f, 0.26f, 0.26f, 1.00f),
        ScrollbarGrabHovered: new Vector4(0.35f, 0.35f, 0.35f, 1.00f),
        ScrollbarGrabActive: new Vector4(0.48f, 0.48f, 0.50f, 1.00f),
        CheckMark: new Vector4(0.68f, 0.68f, 0.70f, 1.00f),
        SliderGrab: new Vector4(0.58f, 0.58f, 0.60f, 1.00f),
        SliderGrabActive: new Vector4(0.74f, 0.74f, 0.76f, 1.00f),
        Button: new Vector4(0.19f, 0.19f, 0.19f, 1.00f),
        ButtonHovered: new Vector4(0.25f, 0.25f, 0.25f, 1.00f),
        ButtonActive: new Vector4(0.38f, 0.38f, 0.40f, 1.00f),
        Header: new Vector4(0.22f, 0.22f, 0.22f, 1.00f),
        HeaderHovered: new Vector4(0.28f, 0.28f, 0.28f, 1.00f),
        HeaderActive: new Vector4(0.34f, 0.34f, 0.34f, 1.00f),
        Separator: new Vector4(0.21f, 0.21f, 0.21f, 1.00f),
        SeparatorHovered: new Vector4(0.42f, 0.42f, 0.44f, 0.80f),
        SeparatorActive: new Vector4(0.58f, 0.58f, 0.60f, 1.00f),
        ResizeGrip: new Vector4(0.30f, 0.30f, 0.30f, 0.35f),
        ResizeGripHovered: new Vector4(0.46f, 0.46f, 0.48f, 0.70f),
        ResizeGripActive: new Vector4(0.62f, 0.62f, 0.64f, 0.95f),
        Tab: new Vector4(0.12f, 0.12f, 0.12f, 1.00f),
        TabHovered: new Vector4(0.28f, 0.28f, 0.29f, 1.00f),
        TabSelected: new Vector4(0.38f, 0.38f, 0.40f, 1.00f),
        TabSelectedOverline: new Vector4(0.72f, 0.72f, 0.74f, 1.00f),
        TabDimmed: new Vector4(0.10f, 0.10f, 0.10f, 1.00f),
        TabDimmedSelected: new Vector4(0.20f, 0.20f, 0.20f, 1.00f),
        TabDimmedSelectedOverline: new Vector4(0.48f, 0.48f, 0.50f, 0.70f),
        DockingPreview: new Vector4(0.64f, 0.64f, 0.66f, 0.40f),
        DockingEmptyBg: new Vector4(0.08f, 0.08f, 0.08f, 1.00f),
        PlotLines: new Vector4(0.68f, 0.68f, 0.70f, 1.00f),
        PlotLinesHovered: new Vector4(0.84f, 0.84f, 0.86f, 1.00f),
        PlotHistogram: new Vector4(0.58f, 0.58f, 0.60f, 1.00f),
        PlotHistogramHovered: new Vector4(0.74f, 0.74f, 0.76f, 1.00f),
        TableHeaderBg: new Vector4(0.16f, 0.16f, 0.16f, 1.00f),
        TableBorderStrong: new Vector4(0.24f, 0.24f, 0.24f, 1.00f),
        TableBorderLight: new Vector4(0.19f, 0.19f, 0.19f, 0.80f),
        TableRowBg: new Vector4(0.00f, 0.00f, 0.00f, 0.00f),
        TableRowBgAlt: new Vector4(1.00f, 1.00f, 1.00f, 0.03f),
        TextLink: new Vector4(0.76f, 0.76f, 0.78f, 1.00f),
        TextSelectedBg: new Vector4(0.55f, 0.55f, 0.57f, 0.35f),
        DragDropTarget: new Vector4(0.75f, 0.75f, 0.78f, 0.92f),
        NavCursor: new Vector4(0.66f, 0.66f, 0.68f, 0.90f),
        NavWindowingHighlight: new Vector4(0.78f, 0.78f, 0.80f, 0.70f),
        AccentButton: new Vector4(0.36f, 0.36f, 0.38f, 1.00f),
        AccentButtonHovered: new Vector4(0.44f, 0.44f, 0.47f, 1.00f),
        AccentButtonActive: new Vector4(0.52f, 0.52f, 0.55f, 1.00f),
        SubtleButton: new Vector4(0.19f, 0.19f, 0.19f, 1.00f),
        SubtleButtonHovered: new Vector4(0.25f, 0.25f, 0.25f, 1.00f),
        PlayButton: new Vector4(0.23f, 0.43f, 0.30f, 1.00f),
        PlayButtonHovered: new Vector4(0.28f, 0.51f, 0.36f, 1.00f),
        PanelSubheaderText: new Vector4(0.72f, 0.72f, 0.74f, 1.00f),
        CanvasBackdrop: new Vector4(0.07f, 0.07f, 0.07f, 1.00f),
        CanvasBorder: new Vector4(0.24f, 0.24f, 0.24f, 1.00f),
        GridLine: new Vector4(0.54f, 0.54f, 0.54f, 0.14f),
        GridAxis: new Vector4(0.68f, 0.68f, 0.70f, 0.28f),
        OverlayBg: new Vector4(0.08f, 0.08f, 0.08f, 0.86f),
        OverlayBorder: new Vector4(0.42f, 0.42f, 0.44f, 0.78f),
        OverlayPrimaryText: new Vector4(0.92f, 0.92f, 0.92f, 1.00f),
        OverlaySecondaryText: new Vector4(0.72f, 0.72f, 0.74f, 1.00f),
        GizmoOutline: new Vector4(0.84f, 0.84f, 0.86f, 0.95f),
        GizmoHandleFill: new Vector4(0.14f, 0.14f, 0.14f, 0.96f),
        TimelineRulerBg: new Vector4(0.14f, 0.14f, 0.14f, 1.00f),
        TimelineTickColor: new Vector4(0.58f, 0.58f, 0.58f, 0.85f),
        TimelinePropLaneBg: new Vector4(0.16f, 0.16f, 0.16f, 1.00f),
        TimelineKeyColor: new Vector4(0.76f, 0.76f, 0.78f, 1.00f),
        TimelineLineColor: new Vector4(0.48f, 0.48f, 0.50f, 0.78f),
        TimelinePropBadge: new Vector4(0.76f, 0.76f, 0.78f, 1.00f));

    public static readonly IReadOnlyList<WorkbenchThemePalette> Presets =
    [
        StudioGraphite,
        NordSlate,
        ClassicDark
    ];

    public static WorkbenchThemePalette Current { get; private set; } = StudioGraphite;

    public static WorkbenchThemePalette Resolve(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return StudioGraphite;
        }

        string trimmed = idOrName.Trim();
        foreach (WorkbenchThemePalette preset in Presets)
        {
            if (string.Equals(preset.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return StudioGraphite;
    }

    public static WorkbenchThemePalette Apply(string? idOrName)
    {
        WorkbenchThemePalette palette = Resolve(idOrName);
        Current = palette;

        if (ImGui.GetCurrentContext() != IntPtr.Zero)
        {
            ApplyToStyle(ImGui.GetStyle(), palette);
        }

        return palette;
    }

    public static void ApplyToStyle(ImGuiStylePtr style, WorkbenchThemePalette palette)
    {
        // Geometry & spacing (macOS Modern Feel)
        style.WindowPadding = new Vector2(8f, 8f);
        style.FramePadding = new Vector2(6f, 4f);
        style.CellPadding = new Vector2(6f, 4f);
        style.ItemSpacing = new Vector2(6f, 4f);
        style.ItemInnerSpacing = new Vector2(5f, 4f);
        style.IndentSpacing = 14f;
        style.ScrollbarSize = 11f;
        style.GrabMinSize = 10f;

        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 0f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;

        style.WindowRounding = 6.0f;
        style.ChildRounding = 4.0f;
        style.FrameRounding = 4.0f;
        style.PopupRounding = 6.0f;
        style.TabRounding = 4.0f;
        style.ScrollbarRounding = 9.0f;
        style.GrabRounding = 3.0f;

        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = palette.Text;
        colors[(int)ImGuiCol.TextDisabled] = palette.TextDisabled;
        colors[(int)ImGuiCol.WindowBg] = palette.WindowBg;
        colors[(int)ImGuiCol.ChildBg] = palette.ChildBg;
        colors[(int)ImGuiCol.PopupBg] = palette.PopupBg;
        colors[(int)ImGuiCol.Border] = palette.Border;
        colors[(int)ImGuiCol.BorderShadow] = palette.BorderShadow;

        colors[(int)ImGuiCol.FrameBg] = palette.FrameBg;
        colors[(int)ImGuiCol.FrameBgHovered] = palette.FrameBgHovered;
        colors[(int)ImGuiCol.FrameBgActive] = palette.FrameBgActive;

        colors[(int)ImGuiCol.TitleBg] = palette.TitleBg;
        colors[(int)ImGuiCol.TitleBgActive] = palette.TitleBgActive;
        colors[(int)ImGuiCol.TitleBgCollapsed] = palette.TitleBgCollapsed;
        colors[(int)ImGuiCol.MenuBarBg] = palette.MenuBarBg;

        colors[(int)ImGuiCol.ScrollbarBg] = palette.ScrollbarBg;
        colors[(int)ImGuiCol.ScrollbarGrab] = palette.ScrollbarGrab;
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = palette.ScrollbarGrabHovered;
        colors[(int)ImGuiCol.ScrollbarGrabActive] = palette.ScrollbarGrabActive;

        colors[(int)ImGuiCol.CheckMark] = palette.CheckMark;
        colors[(int)ImGuiCol.SliderGrab] = palette.SliderGrab;
        colors[(int)ImGuiCol.SliderGrabActive] = palette.SliderGrabActive;

        colors[(int)ImGuiCol.Button] = palette.Button;
        colors[(int)ImGuiCol.ButtonHovered] = palette.ButtonHovered;
        colors[(int)ImGuiCol.ButtonActive] = palette.ButtonActive;

        colors[(int)ImGuiCol.Header] = palette.Header;
        colors[(int)ImGuiCol.HeaderHovered] = palette.HeaderHovered;
        colors[(int)ImGuiCol.HeaderActive] = palette.HeaderActive;

        colors[(int)ImGuiCol.Separator] = palette.Separator;
        colors[(int)ImGuiCol.SeparatorHovered] = palette.SeparatorHovered;
        colors[(int)ImGuiCol.SeparatorActive] = palette.SeparatorActive;

        colors[(int)ImGuiCol.ResizeGrip] = palette.ResizeGrip;
        colors[(int)ImGuiCol.ResizeGripHovered] = palette.ResizeGripHovered;
        colors[(int)ImGuiCol.ResizeGripActive] = palette.ResizeGripActive;

        colors[(int)ImGuiCol.Tab] = palette.Tab;
        colors[(int)ImGuiCol.TabHovered] = palette.TabHovered;
        colors[(int)ImGuiCol.TabSelected] = palette.TabSelected;
        colors[(int)ImGuiCol.TabSelectedOverline] = palette.TabSelectedOverline;
        colors[(int)ImGuiCol.TabDimmed] = palette.TabDimmed;
        colors[(int)ImGuiCol.TabDimmedSelected] = palette.TabDimmedSelected;
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = palette.TabDimmedSelectedOverline;

        colors[(int)ImGuiCol.DockingPreview] = palette.DockingPreview;
        colors[(int)ImGuiCol.DockingEmptyBg] = palette.DockingEmptyBg;

        colors[(int)ImGuiCol.PlotLines] = palette.PlotLines;
        colors[(int)ImGuiCol.PlotLinesHovered] = palette.PlotLinesHovered;
        colors[(int)ImGuiCol.PlotHistogram] = palette.PlotHistogram;
        colors[(int)ImGuiCol.PlotHistogramHovered] = palette.PlotHistogramHovered;

        colors[(int)ImGuiCol.TableHeaderBg] = palette.TableHeaderBg;
        colors[(int)ImGuiCol.TableBorderStrong] = palette.TableBorderStrong;
        colors[(int)ImGuiCol.TableBorderLight] = palette.TableBorderLight;
        colors[(int)ImGuiCol.TableRowBg] = palette.TableRowBg;
        colors[(int)ImGuiCol.TableRowBgAlt] = palette.TableRowBgAlt;

        colors[(int)ImGuiCol.TextLink] = palette.TextLink;
        colors[(int)ImGuiCol.TextSelectedBg] = palette.TextSelectedBg;
        colors[(int)ImGuiCol.DragDropTarget] = palette.DragDropTarget;
        colors[(int)ImGuiCol.NavCursor] = palette.NavCursor;
        colors[(int)ImGuiCol.NavWindowingHighlight] = palette.NavWindowingHighlight;
        colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.05f, 0.05f, 0.06f, 0.45f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.04f, 0.04f, 0.05f, 0.55f);
    }
}
