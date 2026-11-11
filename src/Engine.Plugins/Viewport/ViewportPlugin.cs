using System.Numerics;
using System.Text.Json;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Engine.Core.Plugins;
using Engine.Plugins.Settings;
using Engine.Plugins.Workspace;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Engine.Plugins.Viewport;

public sealed class ViewportPlugin : IEnginePlugin
{
    private enum DragKind
    {
        None = 0,
        Move = 1,
        Rotate = 2,
        ScaleUniform = 3,
        ScaleX = 4,
        ScaleY = 5
    }

    private PluginContext? _context;
    private int _virtualWidth = 1280;
    private int _virtualHeight = 720;
    private int _nativeWidth = 1280;
    private int _nativeHeight = 720;
    private bool _fitToWindow = true;
    private bool _showGrid = true;
    private bool _showOverlay;

    private uint _configuredRenderTargetTexId;
    private int _configuredRenderTargetWidth;
    private int _configuredRenderTargetHeight;
    private string? _lastProjectRootLoaded;

    private float _timelineCurrentTime;
    private float _timelineMaxDuration = 4.0f;

    private ViewportInteractionMode _mode = ViewportInteractionMode.Edit;
    private ViewportGizmoTool _tool = ViewportGizmoTool.Move;
    private ViewportSelectionStateEvent _selection = ViewportSelectionStateEvent.None;

    private Vector2 _imageRectMin;
    private Vector2 _imageRectMax;
    private Vector2 _lastVirtualMouse;
    private bool _isMouseInsideViewport;

    private DragKind _activeDrag = DragKind.None;
    private Vector2 _dragStartPos;
    private float _dragStartRot;
    private Vector2 _dragStartScale;
    private Vector2 _dragStartMouseVirtual;
    private Vector2 _dragStartScaleRefPoint;
    private float _dragStartScaleDist;
    private Vector2 _dragStartLocalMouse;

    public string Id => "workbench.viewport";
    public string DisplayName => "Viewport";

    public int VirtualWidth => _virtualWidth;
    public int VirtualHeight => _virtualHeight;
    public int NativeWidth => _nativeWidth;
    public int NativeHeight => _nativeHeight;

    public void OnLoaded(PluginContext context)
    {
        _context = context;
        _fitToWindow = context.Settings.GetBool("viewport.fit", true);
        _showGrid = context.Settings.GetBool("viewport.showGrid", true);
        _showOverlay = context.Settings.GetBool("viewport.showOverlay", false);
        _timelineCurrentTime = context.Runtime.CurrentTime;
        _timelineMaxDuration = context.Runtime.MaxDuration > 0f ? context.Runtime.MaxDuration : 4.0f;

        LoadResolutionFromProjectJson();
        _context.Graphics.EnsureRenderTexture(_virtualWidth, _virtualHeight);
        EnsureRenderTexturePointFilter();
        _context.EventBus.Publish(new ViewportResolutionChangedEvent(_virtualWidth, _virtualHeight));

        _context.EventBus.Subscribe<AssetModifiedEvent>(OnAssetModified);
        _context.EventBus.Subscribe<ProjectLoadedEvent>(OnProjectLoaded);
        _context.EventBus.Subscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
        _context.EventBus.Subscribe<ViewportSelectionStateEvent>(OnSelectionStateChanged);
        _context.EventBus.Subscribe<TimelineStateChangedEvent>(OnTimelineStateChanged);
        _context.EventBus.Subscribe<SettingChangedEvent>(OnSettingChanged);
        _lastProjectRootLoaded = Path.GetFullPath(context.Workspace.ProjectRoot);
    }

    public void OnUpdate(float deltaTime)
    {
        _context?.Graphics.EnsureRenderTexture(_virtualWidth, _virtualHeight);
        EnsureRenderTexturePointFilter();
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
            _context.Input.IsMouseInViewport = false;
            _activeDrag = DragKind.None;
            return;
        }

        bool keepOpen = isVisible;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
        bool open = ImGui.Begin("Viewport", ref keepOpen);
        ImGui.PopStyleVar();

        if (!keepOpen)
        {
            _context.Plugins?.SetWindowVisible(Id, false);
        }

        if (!open)
        {
            _context.Input.IsMouseInViewport = false;
            _activeDrag = DragKind.None;
            ImGui.End();
            return;
        }

        HandleViewportHotkeys();
        RenderToolbar();
        ImGui.Separator();

        RenderTexture2D rt = _context.Graphics.GetRenderTexture();
        EnsureRenderTexturePointFilter(rt);

        Vector2 fullAvail = ImGui.GetContentRegionAvail();
        float statusBarHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y * 2f + 4f;
        float canvasAreaHeight = fullAvail.Y - statusBarHeight;

        if (fullAvail.X < 16f || canvasAreaHeight < 16f)
        {
            _context.Input.IsMouseInViewport = false;
            if (fullAvail.Y >= statusBarHeight)
            {
                RenderStatusBar();
            }
            ImGui.End();
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool childOpen = ImGui.BeginChild(
            "##ViewportCanvasArea",
            new Vector2(fullAvail.X, canvasAreaHeight),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        if (childOpen)
        {
            Vector2 avail = ImGui.GetContentRegionAvail();
            Vector2 regionScreenPos = ImGui.GetCursorScreenPos();
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            WorkbenchThemePalette theme = WorkbenchTheme.Current;
            uint backdropColor = ImGui.ColorConvertFloat4ToU32(theme.CanvasBackdrop);
            drawList.AddRectFilled(regionScreenPos, regionScreenPos + avail, backdropColor);

            if (_fitToWindow)
            {
                rlImGui.ImageRenderTextureFit(rt, center: true);
            }
            else
            {
                float offsetX = MathF.Max(0f, (avail.X - rt.Texture.Width) * 0.5f);
                float offsetY = MathF.Max(0f, (avail.Y - rt.Texture.Height) * 0.5f);
                ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(offsetX, offsetY));
                rlImGui.ImageRenderTexture(rt);
            }

            _imageRectMin = ImGui.GetItemRectMin();
            _imageRectMax = ImGui.GetItemRectMax();
            Vector2 drawnSize = new(
                MathF.Max(1f, _imageRectMax.X - _imageRectMin.X),
                MathF.Max(1f, _imageRectMax.Y - _imageRectMin.Y));

            // Capture mouse input over the canvas so dragging inside Viewport never tears off the dock tab.
            ImGui.SetCursorScreenPos(_imageRectMin);
            ImGui.InvisibleButton("##ViewportCanvasTarget", drawnSize);

            bool canvasHovered = ImGui.IsItemHovered();
            bool canvasActive = ImGui.IsItemActive();

            uint borderColor = ImGui.ColorConvertFloat4ToU32(theme.CanvasBorder);
            drawList.AddRect(_imageRectMin, _imageRectMax, borderColor);

            if (_showGrid && _mode == ViewportInteractionMode.Edit)
            {
                DrawGridOverlay(drawList);
            }

            bool inside = TryScreenToVirtual(ImGui.GetMousePos(), out Vector2 virtualMouse) && canvasHovered;
            _isMouseInsideViewport = inside;
            if (inside)
            {
                _lastVirtualMouse = virtualMouse;
            }

            _context.Input.VirtualMousePosition = _lastVirtualMouse;
            _context.Input.IsMouseInViewport = inside;
            _context.EventBus.Publish(new ViewportMouseMovedEvent(_lastVirtualMouse, inside));

            if (_mode == ViewportInteractionMode.Edit)
            {
                HandleGizmoInteraction(drawList, canvasHovered, canvasActive);
                DrawSelectionGizmos(drawList);
            }
            else if (_activeDrag != DragKind.None)
            {
                CancelOrFinishDrag();
            }

            if (_showOverlay)
            {
                DrawImGuiDebugOverlay(drawList);
            }
        }

        ImGui.EndChild();
        ImGui.Separator();
        RenderStatusBar();

        ImGui.End();
    }

    public void OnUnloaded()
    {
        if (_context != null)
        {
            _context.EventBus.Unsubscribe<AssetModifiedEvent>(OnAssetModified);
            _context.EventBus.Unsubscribe<ProjectLoadedEvent>(OnProjectLoaded);
            _context.EventBus.Unsubscribe<WorkspaceChangedEvent>(OnWorkspaceChanged);
            _context.EventBus.Unsubscribe<ViewportSelectionStateEvent>(OnSelectionStateChanged);
            _context.EventBus.Unsubscribe<TimelineStateChangedEvent>(OnTimelineStateChanged);
            _context.EventBus.Unsubscribe<SettingChangedEvent>(OnSettingChanged);
        }
    }

    public bool TryScreenToVirtual(Vector2 imguiScreenPos, out Vector2 virtualPos)
    {
        float drawnWidth = _imageRectMax.X - _imageRectMin.X;
        float drawnHeight = _imageRectMax.Y - _imageRectMin.Y;

        if (drawnWidth <= 0f || drawnHeight <= 0f)
        {
            virtualPos = Vector2.Zero;
            return false;
        }

        float normX = (imguiScreenPos.X - _imageRectMin.X) / drawnWidth;
        float normY = (imguiScreenPos.Y - _imageRectMin.Y) / drawnHeight;

        virtualPos = new Vector2(
            Math.Clamp(normX * _virtualWidth, 0f, _virtualWidth),
            Math.Clamp(normY * _virtualHeight, 0f, _virtualHeight));

        return normX >= 0f && normX <= 1f && normY >= 0f && normY <= 1f;
    }

    public static void CalculateLetterboxedBounds(
        Vector2 containerSize,
        int virtualWidth,
        int virtualHeight,
        out Vector2 offset,
        out Vector2 fittedSize)
    {
        if (containerSize.X <= 0f || containerSize.Y <= 0f || virtualWidth <= 0 || virtualHeight <= 0)
        {
            offset = Vector2.Zero;
            fittedSize = Vector2.Zero;
            return;
        }

        float scale = MathF.Min(containerSize.X / virtualWidth, containerSize.Y / virtualHeight);
        float w = virtualWidth * scale;
        float h = virtualHeight * scale;
        fittedSize = new Vector2(w, h);
        offset = new Vector2((containerSize.X - w) * 0.5f, (containerSize.Y - h) * 0.5f);
    }

    private void HandleViewportHotkeys()
    {
        if (ImGui.GetIO().WantTextInput ||
            !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.KeyCtrl || io.KeySuper || io.KeyAlt)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.W))
        {
            _tool = ViewportGizmoTool.Move;
            _mode = ViewportInteractionMode.Edit;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.E))
        {
            _tool = ViewportGizmoTool.Rotate;
            _mode = ViewportInteractionMode.Edit;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.R))
        {
            _tool = ViewportGizmoTool.Scale;
            _mode = ViewportInteractionMode.Edit;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.G))
        {
            _showGrid = !_showGrid;
            _context?.Settings.SetBool("viewport.showGrid", _showGrid);
        }
    }

    private void RenderToolbar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5f, 4f));

        RenderToggleButton("Edit", _mode == ViewportInteractionMode.Edit, () => _mode = ViewportInteractionMode.Edit);
        ImGui.SameLine(0f, 3f);
        RenderToggleButton("Play", _mode == ViewportInteractionMode.Play, () => _mode = ViewportInteractionMode.Play);

        ImGui.SameLine(0f, 10f);
        RenderToggleButton("Move", _tool == ViewportGizmoTool.Move && _mode == ViewportInteractionMode.Edit, () =>
        {
            _mode = ViewportInteractionMode.Edit;
            _tool = ViewportGizmoTool.Move;
        });
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move tool (W)");

        ImGui.SameLine(0f, 3f);
        RenderToggleButton("Rot", _tool == ViewportGizmoTool.Rotate && _mode == ViewportInteractionMode.Edit, () =>
        {
            _mode = ViewportInteractionMode.Edit;
            _tool = ViewportGizmoTool.Rotate;
        });
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotate tool (E)");

        ImGui.SameLine(0f, 3f);
        RenderToggleButton("Scale", _tool == ViewportGizmoTool.Scale && _mode == ViewportInteractionMode.Edit, () =>
        {
            _mode = ViewportInteractionMode.Edit;
            _tool = ViewportGizmoTool.Scale;
        });
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scale tool (R)");

        ImGui.SameLine(0f, 10f);
        if (ImGui.Checkbox("Grid", ref _showGrid))
        {
            _context?.Settings.SetBool("viewport.showGrid", _showGrid);
        }

        ImGui.SameLine(0f, 8f);
        if (ImGui.Checkbox("Fit", ref _fitToWindow))
        {
            _context?.Settings.SetBool("viewport.fit", _fitToWindow);
        }

        ImGui.SameLine(0f, 8f);
        if (ImGui.Checkbox("Overlay", ref _showOverlay))
        {
            _context?.Settings.SetBool("viewport.showOverlay", _showOverlay);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Toggle ImGui debug overlay over viewport");

        // Second compact row: resolution presets
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Buffer:");
        ImGui.SameLine(0f, 6f);
        RenderPresetButton("640x360", 640, 360, "Retro Pixel Art (640x360)");
        ImGui.SameLine(0f, 4f);
        RenderPresetButton("960x540", 960, 540, "qHD (960x540)");
        ImGui.SameLine(0f, 4f);
        RenderPresetButton("1280x720", 1280, 720, "HD (1280x720)");
        ImGui.SameLine(0f, 4f);
        RenderPresetButton("1920x1080", 1920, 1080, "Full HD (1920x1080)");
        ImGui.SameLine(0f, 4f);
        bool isNativeActive = _virtualWidth == _nativeWidth && _virtualHeight == _nativeHeight;
        RenderToggleButton("Native", isNativeActive, ApplyNativeResolutionPreset);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Project Native from project.json ({_nativeWidth}x{_nativeHeight})");

        ImGui.PopStyleVar(2);
    }

    private void RenderStatusBar()
    {
        string activeLayerInfo = _selection.HasSelection
            ? (!string.IsNullOrWhiteSpace(_selection.SourceLabel)
                ? $"{_selection.SourceLabel} ({_selection.BaseSize.X:F0}x{_selection.BaseSize.Y:F0})"
                : $"{_selection.ItemName} ({_selection.BaseSize.X:F0}x{_selection.BaseSize.Y:F0})")
            : "None";

        string cursorInfo = _isMouseInsideViewport
            ? $"({_lastVirtualMouse.X:F0}, {_lastVirtualMouse.Y:F0})"
            : "(-, -)";

        string statusLine =
            $"{_virtualWidth}x{_virtualHeight}  |  " +
            $"Time: {_timelineCurrentTime:F2}s / {_timelineMaxDuration:F2}s  |  " +
            $"Active Layer: {activeLayerInfo}  |  " +
            $"Cursor: {cursorInfo}";

        ImGui.TextDisabled(statusLine.Replace("%", "%%", StringComparison.Ordinal));
    }

    private void DrawImGuiDebugOverlay(ImDrawListPtr drawList)
    {
        string activeInfo = _selection.HasSelection
            ? (!string.IsNullOrWhiteSpace(_selection.SourceLabel)
                ? $"{_selection.SourceLabel} ({_selection.BaseSize.X:F0}x{_selection.BaseSize.Y:F0})"
                : $"{_selection.ItemName} ({_selection.BaseSize.X:F0}x{_selection.BaseSize.Y:F0})")
            : "None";

        string line1 = $"t = {_timelineCurrentTime:F2}s / {_timelineMaxDuration:F2}s  |  {_virtualWidth}x{_virtualHeight} (Point)";
        string line2 = $"Layer: {activeInfo}  |  Tool: {_mode} / {_tool}";

        Vector2 size1 = ImGui.CalcTextSize(line1);
        Vector2 size2 = ImGui.CalcTextSize(line2);
        float boxW = MathF.Max(size1.X, size2.X) + 16f;
        float boxH = size1.Y + size2.Y + 14f;

        Vector2 origin = _imageRectMin + new Vector2(10f, 10f);
        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        uint bgColor = ImGui.ColorConvertFloat4ToU32(theme.OverlayBg);
        uint borderColor = ImGui.ColorConvertFloat4ToU32(theme.OverlayBorder);
        uint primaryText = ImGui.ColorConvertFloat4ToU32(theme.OverlayPrimaryText);
        uint secondaryText = ImGui.ColorConvertFloat4ToU32(theme.OverlaySecondaryText);

        drawList.PushClipRect(_imageRectMin, _imageRectMax, true);
        drawList.AddRectFilled(origin, origin + new Vector2(boxW, boxH), bgColor, 4f);
        drawList.AddRect(origin, origin + new Vector2(boxW, boxH), borderColor, 4f, ImDrawFlags.None, 1f);
        drawList.AddText(origin + new Vector2(8f, 5f), primaryText, line1);
        drawList.AddText(origin + new Vector2(8f, 8f + size1.Y), secondaryText, line2);
        drawList.PopClipRect();
    }

    private static void RenderToggleButton(string label, bool active, Action onClick)
    {
        WorkbenchThemePalette theme = WorkbenchTheme.Current;
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

        if (ImGui.Button(label))
        {
            onClick();
        }

        ImGui.PopStyleColor(3);
    }

    private void RenderPresetButton(string label, int width, int height, string? tooltip = null)
    {
        RenderToggleButton(label, _virtualWidth == width && _virtualHeight == height, () => SetVirtualResolution(width, height));
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private void HandleGizmoInteraction(ImDrawListPtr drawList, bool canvasHovered, bool canvasActive)
    {
        if (_context == null)
        {
            return;
        }

        Vector2 mouseScreen = ImGui.GetMousePos();
        Vector2 mouseVirtual = ScreenToVirtualUnclamped(mouseScreen);

        if (canvasHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            ImGuiIOPtr io = ImGui.GetIO();

            if (TryPickSelectionHandle(mouseScreen, out DragKind pickedDrag))
            {
                StartDrag(pickedDrag, mouseVirtual);
                return;
            }

            _context.EventBus.Publish(new ViewportCanvasClickEvent(
                mouseVirtual,
                io.KeyShift,
                io.KeyCtrl || io.KeySuper));

            if (_selection.HasSelection && !_selection.Locked)
            {
                DragKind initialDrag = _tool switch
                {
                    ViewportGizmoTool.Rotate => DragKind.Rotate,
                    ViewportGizmoTool.Scale => DragKind.ScaleUniform,
                    _ => DragKind.Move
                };

                StartDrag(initialDrag, mouseVirtual);
            }

            return;
        }

        if (_activeDrag != DragKind.None)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && (canvasActive || ImGui.IsAnyMouseDown()))
            {
                ApplyDrag(mouseVirtual, isFinal: false);
            }
            else
            {
                ApplyDrag(mouseVirtual, isFinal: true);
                _activeDrag = DragKind.None;
            }
        }
    }

    private bool TryPickSelectionHandle(Vector2 mouseScreen, out DragKind dragKind)
    {
        dragKind = DragKind.None;
        if (!_selection.HasSelection || _selection.Locked)
        {
            return false;
        }

        ComputeSelectionGeometry(
            out Vector2 pivotScreen,
            out Vector2[] cornerScreens,
            out Vector2 topCenterScreen,
            out Vector2 rotateHandleScreen,
            out Vector2 rightEdgeScreen,
            out Vector2 bottomEdgeScreen);

        const float handleRadiusSq = 10.5f * 10.5f;

        if (Vector2.DistanceSquared(mouseScreen, rotateHandleScreen) <= handleRadiusSq)
        {
            dragKind = DragKind.Rotate;
            return true;
        }

        for (int i = 0; i < cornerScreens.Length; i++)
        {
            if (Vector2.DistanceSquared(mouseScreen, cornerScreens[i]) <= handleRadiusSq)
            {
                dragKind = DragKind.ScaleUniform;
                return true;
            }
        }

        if (_tool == ViewportGizmoTool.Scale)
        {
            if (Vector2.DistanceSquared(mouseScreen, rightEdgeScreen) <= handleRadiusSq)
            {
                dragKind = DragKind.ScaleX;
                return true;
            }

            if (Vector2.DistanceSquared(mouseScreen, bottomEdgeScreen) <= handleRadiusSq)
            {
                dragKind = DragKind.ScaleY;
                return true;
            }
        }

        if (_tool == ViewportGizmoTool.Move)
        {
            if (Vector2.DistanceSquared(mouseScreen, pivotScreen) <= handleRadiusSq)
            {
                dragKind = DragKind.Move;
                return true;
            }
        }

        return false;
    }

    private void StartDrag(DragKind kind, Vector2 mouseVirtual)
    {
        if (_context == null || !_selection.HasSelection || _selection.Locked)
        {
            return;
        }

        _activeDrag = kind;
        _dragStartPos = _selection.WorldPosition;
        _dragStartRot = _selection.WorldRotationDegrees;
        _dragStartScale = _selection.WorldScale;
        _dragStartMouseVirtual = mouseVirtual;

        float distToPivot = Vector2.Distance(mouseVirtual, _dragStartPos);
        if (distToPivot < 12f)
        {
            float w = MathF.Max(16f, _selection.BaseSize.X * MathF.Abs(_dragStartScale.X));
            float h = MathF.Max(16f, _selection.BaseSize.Y * MathF.Abs(_dragStartScale.Y));
            Vector2 localCenter = new((0.5f - _selection.OriginNormalized.X) * w, (0.5f - _selection.OriginNormalized.Y) * h);
            _dragStartScaleRefPoint = _dragStartPos + RotateVector(localCenter, _dragStartRot);
            _dragStartScaleDist = MathF.Max(12f, Vector2.Distance(mouseVirtual, _dragStartScaleRefPoint));
        }
        else
        {
            _dragStartScaleRefPoint = _dragStartPos;
            _dragStartScaleDist = distToPivot;
        }

        _dragStartLocalMouse = RotateVector(mouseVirtual - _dragStartPos, -_dragStartRot);

        _context.EventBus.Publish(new ViewportTransformCommitEvent(
            _selection.ItemId,
            _dragStartPos,
            _dragStartRot,
            _dragStartScale,
            IsBeginDrag: true,
            IsFinalSnapshot: false));
    }

    private void ApplyDrag(Vector2 mouseVirtual, bool isFinal)
    {
        if (_context == null || !_selection.HasSelection)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        bool shiftDown = io.KeyShift;
        bool altDown = io.KeyAlt;

        Vector2 newPos = _dragStartPos;
        float newRot = _dragStartRot;
        Vector2 newScale = _dragStartScale;

        switch (_activeDrag)
        {
            case DragKind.Move:
            {
                Vector2 delta = mouseVirtual - _dragStartMouseVirtual;
                if (shiftDown)
                {
                    if (MathF.Abs(delta.X) >= MathF.Abs(delta.Y))
                    {
                        delta.Y = 0f;
                    }
                    else
                    {
                        delta.X = 0f;
                    }
                }

                newPos = _dragStartPos + delta;
                bool snapEnabled = _context.Settings.GetBool("viewport.snapEnabled", false);
                if (snapEnabled && !altDown)
                {
                    float snapSize = MathF.Max(2f, _context.Settings.GetFloat("viewport.snapSize", 16f));
                    newPos = new Vector2(
                        MathF.Round(newPos.X / snapSize) * snapSize,
                        MathF.Round(newPos.Y / snapSize) * snapSize);
                }
                break;
            }

            case DragKind.Rotate:
            {
                Vector2 d0 = _dragStartMouseVirtual - _dragStartPos;
                Vector2 d1 = mouseVirtual - _dragStartPos;
                if (d0.LengthSquared() > 4f && d1.LengthSquared() > 4f)
                {
                    float a0 = MathF.Atan2(d0.Y, d0.X);
                    float a1 = MathF.Atan2(d1.Y, d1.X);
                    float deltaAngle = (a1 - a0) * (180f / MathF.PI);
                    while (deltaAngle > 180f) deltaAngle -= 360f;
                    while (deltaAngle < -180f) deltaAngle += 360f;

                    newRot = _dragStartRot + deltaAngle;
                    if (shiftDown)
                    {
                        newRot = MathF.Round(newRot / 15f) * 15f;
                    }
                }
                break;
            }

            case DragKind.ScaleUniform:
            {
                if (shiftDown && MathF.Abs(_dragStartLocalMouse.X) > 4f && MathF.Abs(_dragStartLocalMouse.Y) > 4f)
                {
                    Vector2 curLocal = RotateVector(mouseVirtual - _dragStartPos, -_dragStartRot);
                    float rx = Math.Clamp(curLocal.X / _dragStartLocalMouse.X, 0.05f, 20f);
                    float ry = Math.Clamp(curLocal.Y / _dragStartLocalMouse.Y, 0.05f, 20f);
                    newScale = new Vector2(
                        Math.Clamp(_dragStartScale.X * rx, 0.05f, 20f),
                        Math.Clamp(_dragStartScale.Y * ry, 0.05f, 20f));
                }
                else
                {
                    float curDist = MathF.Max(1f, Vector2.Distance(mouseVirtual, _dragStartScaleRefPoint));
                    float factor = Math.Clamp(curDist / MathF.Max(1f, _dragStartScaleDist), 0.05f, 20f);
                    newScale = new Vector2(
                        Math.Clamp(_dragStartScale.X * factor, 0.05f, 20f),
                        Math.Clamp(_dragStartScale.Y * factor, 0.05f, 20f));
                }
                break;
            }

            case DragKind.ScaleX:
            {
                Vector2 curLocal = RotateVector(mouseVirtual - _dragStartPos, -_dragStartRot);
                float rx = MathF.Abs(_dragStartLocalMouse.X) > 4f
                    ? Math.Clamp(curLocal.X / _dragStartLocalMouse.X, 0.05f, 20f)
                    : 1f;
                newScale = new Vector2(Math.Clamp(_dragStartScale.X * rx, 0.05f, 20f), _dragStartScale.Y);
                break;
            }

            case DragKind.ScaleY:
            {
                Vector2 curLocal = RotateVector(mouseVirtual - _dragStartPos, -_dragStartRot);
                float ry = MathF.Abs(_dragStartLocalMouse.Y) > 4f
                    ? Math.Clamp(curLocal.Y / _dragStartLocalMouse.Y, 0.05f, 20f)
                    : 1f;
                newScale = new Vector2(_dragStartScale.X, Math.Clamp(_dragStartScale.Y * ry, 0.05f, 20f));
                break;
            }
        }

        _context.EventBus.Publish(new ViewportTransformCommitEvent(
            _selection.ItemId,
            newPos,
            newRot,
            newScale,
            IsBeginDrag: false,
            IsFinalSnapshot: isFinal));
    }

    private void CancelOrFinishDrag()
    {
        if (_activeDrag != DragKind.None)
        {
            ApplyDrag(ScreenToVirtualUnclamped(ImGui.GetMousePos()), isFinal: true);
            _activeDrag = DragKind.None;
        }
    }

    private void DrawGridOverlay(ImDrawListPtr drawList)
    {
        float snapSize = _context?.Settings.GetFloat("viewport.snapSize", 16f) ?? 16f;
        float step = MathF.Max(32f, snapSize * 4f);

        drawList.PushClipRect(_imageRectMin, _imageRectMax, true);

        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        uint lineColor = ImGui.ColorConvertFloat4ToU32(theme.GridLine);
        uint axisColor = ImGui.ColorConvertFloat4ToU32(theme.GridAxis);

        for (float gx = step; gx < _virtualWidth; gx += step)
        {
            Vector2 p0 = VirtualToScreen(new Vector2(gx, 0f));
            Vector2 p1 = VirtualToScreen(new Vector2(gx, _virtualHeight));
            drawList.AddLine(p0, p1, lineColor, 1f);
        }

        for (float gy = step; gy < _virtualHeight; gy += step)
        {
            Vector2 p0 = VirtualToScreen(new Vector2(0f, gy));
            Vector2 p1 = VirtualToScreen(new Vector2(_virtualWidth, gy));
            drawList.AddLine(p0, p1, lineColor, 1f);
        }

        drawList.AddLine(
            VirtualToScreen(new Vector2(_virtualWidth * 0.5f, 0f)),
            VirtualToScreen(new Vector2(_virtualWidth * 0.5f, _virtualHeight)),
            axisColor,
            1.2f);
        drawList.AddLine(
            VirtualToScreen(new Vector2(0f, _virtualHeight * 0.5f)),
            VirtualToScreen(new Vector2(_virtualWidth, _virtualHeight * 0.5f)),
            axisColor,
            1.2f);

        drawList.PopClipRect();
    }

    private void DrawSelectionGizmos(ImDrawListPtr drawList)
    {
        if (!_selection.HasSelection)
        {
            return;
        }

        ComputeSelectionGeometry(
            out Vector2 pivotScreen,
            out Vector2[] corners,
            out Vector2 topCenterScreen,
            out Vector2 rotateHandleScreen,
            out Vector2 rightEdgeScreen,
            out Vector2 bottomEdgeScreen);

        WorkbenchThemePalette theme = WorkbenchTheme.Current;
        uint outlineColor = _selection.Locked
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.75f, 0.56f, 0.34f, 0.85f))
            : ImGui.ColorConvertFloat4ToU32(theme.GizmoOutline);
        uint fillHandle = ImGui.ColorConvertFloat4ToU32(theme.GizmoHandleFill);
        uint rotateColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.42f, 0.90f, 0.58f, 0.98f));

        drawList.PushClipRect(_imageRectMin - new Vector2(2f, 2f), _imageRectMax + new Vector2(2f, 2f), true);

        for (int i = 0; i < 4; i++)
        {
            drawList.AddLine(corners[i], corners[(i + 1) % 4], outlineColor, 1.5f);
        }

        if (!_selection.Locked)
        {
            // Top rotation stem & handle
            drawList.AddLine(topCenterScreen, rotateHandleScreen, outlineColor, 1.5f);
            drawList.AddCircleFilled(rotateHandleScreen, 5f, fillHandle);
            drawList.AddCircle(rotateHandleScreen, 5f, rotateColor, 16, 1.6f);

            // 4 corner scale handles
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = corners[i];
                Vector2 half = new(4f, 4f);
                drawList.AddRectFilled(c - half, c + half, fillHandle);
                drawList.AddRect(c - half, c + half, outlineColor, 0f, ImDrawFlags.None, 1.4f);
            }

            if (_tool == ViewportGizmoTool.Scale)
            {
                Vector2 half = new(3.5f, 3.5f);
                drawList.AddRectFilled(rightEdgeScreen - half, rightEdgeScreen + half, outlineColor);
                drawList.AddRectFilled(bottomEdgeScreen - half, bottomEdgeScreen + half, outlineColor);
            }
            else if (_tool == ViewportGizmoTool.Rotate)
            {
                drawList.AddCircle(pivotScreen, 36f, ImGui.ColorConvertFloat4ToU32(new Vector4(0.42f, 0.90f, 0.58f, 0.45f)), 32, 1.3f);
            }
            else if (_tool == ViewportGizmoTool.Move)
            {
                Vector2 xDir = RotateVector(new Vector2(28f, 0f), _selection.WorldRotationDegrees);
                Vector2 yDir = RotateVector(new Vector2(0f, 28f), _selection.WorldRotationDegrees);
                uint xColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.94f, 0.38f, 0.38f, 0.95f));
                uint yColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.42f, 0.88f, 0.52f, 0.95f));
                drawList.AddLine(pivotScreen, pivotScreen + xDir, xColor, 2f);
                drawList.AddLine(pivotScreen, pivotScreen + yDir, yColor, 2f);
            }
        }

        // Pivot reticle
        drawList.AddCircleFilled(pivotScreen, 3.5f, outlineColor);
        drawList.AddCircle(pivotScreen, 5.5f, outlineColor, 12, 1.2f);

        drawList.PopClipRect();
    }

    private void ComputeSelectionGeometry(
        out Vector2 pivotScreen,
        out Vector2[] cornersScreen,
        out Vector2 topCenterScreen,
        out Vector2 rotateHandleScreen,
        out Vector2 rightEdgeScreen,
        out Vector2 bottomEdgeScreen)
    {
        float w = MathF.Max(16f, _selection.BaseSize.X * MathF.Abs(_selection.WorldScale.X));
        float h = MathF.Max(16f, _selection.BaseSize.Y * MathF.Abs(_selection.WorldScale.Y));
        float ox = _selection.OriginNormalized.X;
        float oy = _selection.OriginNormalized.Y;

        float left = -w * ox;
        float right = w * (1f - ox);
        float top = -h * oy;
        float bottom = h * (1f - oy);

        Vector2[] localCorners =
        [
            new(left, top),
            new(right, top),
            new(right, bottom),
            new(left, bottom)
        ];

        cornersScreen = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            Vector2 world = _selection.WorldPosition + RotateVector(localCorners[i], _selection.WorldRotationDegrees);
            cornersScreen[i] = VirtualToScreen(world);
        }

        pivotScreen = VirtualToScreen(_selection.WorldPosition);
        Vector2 topCenterWorld = _selection.WorldPosition + RotateVector(new Vector2((left + right) * 0.5f, top), _selection.WorldRotationDegrees);
        topCenterScreen = VirtualToScreen(topCenterWorld);

        Vector2 upDir = RotateVector(new Vector2(0f, -1f), _selection.WorldRotationDegrees);
        rotateHandleScreen = topCenterScreen + upDir * 24f;

        Vector2 rightEdgeWorld = _selection.WorldPosition + RotateVector(new Vector2(right, (top + bottom) * 0.5f), _selection.WorldRotationDegrees);
        Vector2 bottomEdgeWorld = _selection.WorldPosition + RotateVector(new Vector2((left + right) * 0.5f, bottom), _selection.WorldRotationDegrees);
        rightEdgeScreen = VirtualToScreen(rightEdgeWorld);
        bottomEdgeScreen = VirtualToScreen(bottomEdgeWorld);
    }

    private Vector2 VirtualToScreen(Vector2 virtualPos)
    {
        float drawnW = MathF.Max(1f, _imageRectMax.X - _imageRectMin.X);
        float drawnH = MathF.Max(1f, _imageRectMax.Y - _imageRectMin.Y);
        return new Vector2(
            _imageRectMin.X + (virtualPos.X / MathF.Max(1f, _virtualWidth)) * drawnW,
            _imageRectMin.Y + (virtualPos.Y / MathF.Max(1f, _virtualHeight)) * drawnH);
    }

    private Vector2 ScreenToVirtualUnclamped(Vector2 screenPos)
    {
        float drawnW = MathF.Max(1f, _imageRectMax.X - _imageRectMin.X);
        float drawnH = MathF.Max(1f, _imageRectMax.Y - _imageRectMin.Y);
        return new Vector2(
            ((screenPos.X - _imageRectMin.X) / drawnW) * _virtualWidth,
            ((screenPos.Y - _imageRectMin.Y) / drawnH) * _virtualHeight);
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

    public void ApplyNativeResolutionPreset()
    {
        LoadResolutionFromProjectJson();
        SetVirtualResolution(_nativeWidth, _nativeHeight);
    }

    public void SetVirtualResolution(int width, int height)
    {
        int clampedW = Math.Max(64, width);
        int clampedH = Math.Max(64, height);

        if (_virtualWidth == clampedW && _virtualHeight == clampedH)
        {
            _context?.Graphics.EnsureRenderTexture(_virtualWidth, _virtualHeight);
            EnsureRenderTexturePointFilter();
            return;
        }

        _virtualWidth = clampedW;
        _virtualHeight = clampedH;
        _context?.Graphics.EnsureRenderTexture(_virtualWidth, _virtualHeight);
        EnsureRenderTexturePointFilter();
        _context?.EventBus.Publish(new ViewportResolutionChangedEvent(_virtualWidth, _virtualHeight));
    }

    private void EnsureRenderTexturePointFilter()
    {
        if (_context == null || !Raylib.IsWindowReady())
        {
            return;
        }

        EnsureRenderTexturePointFilter(_context.Graphics.GetRenderTexture());
    }

    private void EnsureRenderTexturePointFilter(RenderTexture2D rt)
    {
        if (!Raylib.IsWindowReady() || !Raylib.IsTextureValid(rt.Texture))
        {
            return;
        }

        if (_configuredRenderTargetTexId == rt.Texture.Id &&
            _configuredRenderTargetWidth == rt.Texture.Width &&
            _configuredRenderTargetHeight == rt.Texture.Height)
        {
            return;
        }

        Raylib.SetTextureFilter(rt.Texture, TextureFilter.Point);
        _configuredRenderTargetTexId = rt.Texture.Id;
        _configuredRenderTargetWidth = rt.Texture.Width;
        _configuredRenderTargetHeight = rt.Texture.Height;
    }

    private void OnSelectionStateChanged(ViewportSelectionStateEvent evt)
    {
        _selection = evt;
    }

    private void OnTimelineStateChanged(TimelineStateChangedEvent evt)
    {
        _timelineCurrentTime = evt.CurrentTime;
        _timelineMaxDuration = evt.MaxDuration > 0f ? evt.MaxDuration : 4.0f;
    }

    private void OnSettingChanged(SettingChangedEvent evt)
    {
        if (_context == null)
        {
            return;
        }

        if (string.Equals(evt.Key, "viewport.showGrid", StringComparison.Ordinal))
        {
            _showGrid = _context.Settings.GetBool("viewport.showGrid", true);
        }
        else if (string.Equals(evt.Key, "viewport.fit", StringComparison.Ordinal))
        {
            _fitToWindow = _context.Settings.GetBool("viewport.fit", true);
        }
        else if (string.Equals(evt.Key, "viewport.showOverlay", StringComparison.Ordinal))
        {
            _showOverlay = _context.Settings.GetBool("viewport.showOverlay", false);
        }
    }

    private void OnAssetModified(AssetModifiedEvent evt)
    {
        if (string.Equals(Path.GetFileName(evt.FilePath), "project.json", StringComparison.OrdinalIgnoreCase))
        {
            LoadResolutionFromProjectJson();
        }
    }

    private void OnProjectLoaded(ProjectLoadedEvent evt)
    {
        _lastProjectRootLoaded = Path.GetFullPath(evt.ProjectRoot);
        LoadResolutionFromProjectJson();
    }

    private void OnWorkspaceChanged(WorkspaceChangedEvent evt)
    {
        string normalized = Path.GetFullPath(evt.ProjectRoot);
        if (string.Equals(_lastProjectRootLoaded, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _lastProjectRootLoaded = normalized;
        LoadResolutionFromProjectJson();
    }

    private void LoadResolutionFromProjectJson()
    {
        if (_context == null)
        {
            return;
        }

        string projectJsonPath = _context.Workspace.ResolvePath("project.json");
        if (!File.Exists(projectJsonPath))
        {
            _nativeWidth = 1280;
            _nativeHeight = 720;
            return;
        }

        try
        {
            using FileStream stream = File.OpenRead(projectJsonPath);
            using JsonDocument doc = JsonDocument.Parse(stream);
            JsonElement root = doc.RootElement;

            int w = root.TryGetProperty("virtualWidth", out JsonElement wEl) && wEl.TryGetInt32(out int parsedW) ? parsedW : 1280;
            int h = root.TryGetProperty("virtualHeight", out JsonElement hEl) && hEl.TryGetInt32(out int parsedH) ? parsedH : 720;

            if (w >= 64 && h >= 64)
            {
                _nativeWidth = w;
                _nativeHeight = h;
                SetVirtualResolution(w, h);
            }
        }
        catch (Exception)
        {
            // Ignore partial JSON edits during active save in VS Code.
        }
    }
}
