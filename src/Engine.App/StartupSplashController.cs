using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine.Core.Abstractions;
using Engine.Plugins.Execution;
using ImGuiNET;
using Raylib_cs;

namespace Engine.App;

public enum AppLifecycleState
{
    Splash = 0,
    Ready = 1
}

public enum SplashPhase
{
    FadeIn = 0,
    LoadingWarmup = 1,
    FadeOut = 2,
    Completed = 3
}

public sealed class StartupSplashController : IDisposable
{
    public const float FadeInDuration = 0.50f;
    public const float MinWarmupDuration = 1.50f;
    public const float FadeOutDuration = 0.30f;

    private static readonly Color BackdropBaseColor = new(18, 18, 20, 255);      // #121214
    private static readonly Color VoidStartColor = new(6, 6, 8, 255);
    private static readonly Color AmberAccentColor = new(217, 119, 36, 255);     // #D97724
    private static readonly Color AmberDarkFoldColor = new(176, 90, 22, 255);
    private static readonly Color AmberTipColor = new(244, 154, 72, 255);
    private static readonly Color LightPrimaryColor = new(238, 238, 238, 255);   // #EEEEEE
    private static readonly Color LightStrapColor = new(245, 245, 245, 255);
    private static readonly Color LightCreaseColor = new(202, 206, 214, 255);
    private static readonly Color SubtitleColor = new(146, 149, 158, 255);
    private static readonly Color StatusTextColor = new(156, 160, 170, 255);
    private static readonly Color TrackBgColor = new(34, 36, 42, 255);
    private static readonly Color GridLineColor = new(255, 255, 255, 16);

    private readonly IWorkspaceService _workspace;
    private readonly IGraphicsService _graphics;
    private readonly string _imguiIniPath;
    private readonly Action? _onMainThreadFinalizeWorkbench;
    private readonly Action<string>? _onLoadIniFromMemory;
    private readonly Func<bool> _skipInputProbe;
    private readonly CancellationTokenSource _cts = new();

    private readonly object _sync = new();
    private Task? _backgroundTask;
    private volatile bool _backgroundStarted;
    private volatile bool _backgroundDone;
    private bool _mainStepsQueued;
    private bool _allWarmupDone;

    private readonly Queue<(string Status, float TargetProgress, Action Step)> _mainThreadQueue = new();
    private readonly List<string> _statusHistory = new();

    private string[] _discoveredScripts = Array.Empty<string>();
    private string[] _discoveredTextures = Array.Empty<string>();
    private string[] _discoveredShaders = Array.Empty<string>();
    private int _projectVirtualWidth = 1280;
    private int _projectVirtualHeight = 720;
    private string? _preloadedIniContent;

    private volatile string _bgStatus = "Mounting workspace...";
    private float _bgTargetProgress;

    private SplashPhase _phase = SplashPhase.FadeIn;
    private float _phaseElapsed;
    private float _totalElapsed;
    private float _splashAlpha;
    private float _displayProgress;
    private string _statusText = "Mounting workspace...";
    private int _mainTotalSteps;
    private int _mainCompletedSteps;

    private Font _titleFont;
    private Font _subtitleFont;
    private Font _monoFont;
    private bool _fontsLoaded;
    private bool _disposed;

    public StartupSplashController(
        IWorkspaceService workspace,
        IGraphicsService graphics,
        string? imguiIniPath = null,
        Action? onMainThreadFinalizeWorkbench = null,
        Action<string>? onLoadIniFromMemory = null,
        Func<bool>? skipInputProbe = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _imguiIniPath = string.IsNullOrWhiteSpace(imguiIniPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "imgui.ini")
            : imguiIniPath;
        _onMainThreadFinalizeWorkbench = onMainThreadFinalizeWorkbench;
        _onLoadIniFromMemory = onLoadIniFromMemory ?? DefaultLoadIniFromMemory;
        _skipInputProbe = skipInputProbe ?? DefaultSkipInputProbe;
    }

    public SplashPhase Phase => _phase;
    public float PhaseElapsed => _phaseElapsed;
    public float TotalElapsed => _totalElapsed;
    public float Progress => _displayProgress;
    public string StatusText => _statusText;
    public float SplashAlpha => _splashAlpha;
    public bool IsBackgroundWarmupCompleted => _backgroundDone;
    public bool IsAllWarmupCompleted => _allWarmupDone;
    public bool CanSkipNow => _backgroundDone && _phase != SplashPhase.Completed;
    public bool ShouldRenderWorkbenchUnderlay => _phase is SplashPhase.FadeOut or SplashPhase.Completed;
    public bool IsCompleted => _phase == SplashPhase.Completed;

    public int DiscoveredScriptCount => _discoveredScripts.Length;
    public int DiscoveredTextureCount => _discoveredTextures.Length;
    public int DiscoveredShaderCount => _discoveredShaders.Length;
    public bool RoslynWarmedUp { get; private set; }
    public bool IniPreloaded { get; private set; }
    public bool FinalizeExecuted { get; private set; }
    public IReadOnlyList<string> StatusHistory
    {
        get
        {
            lock (_sync)
            {
                return _statusHistory.ToArray();
            }
        }
    }

    public bool Update(float deltaTime)
    {
        if (_phase == SplashPhase.Completed)
        {
            return true;
        }

        float dt = Math.Clamp(deltaTime, 0f, 0.1f);
        _totalElapsed += dt;
        _phaseElapsed += dt;

        // Allow skip ONLY once background warmup tasks have finished cleanly.
        if (CanSkipNow && _skipInputProbe())
        {
            ExecuteSkip();
            return true;
        }

        switch (_phase)
        {
            case SplashPhase.FadeIn:
            {
                float t = Math.Clamp(_phaseElapsed / FadeInDuration, 0f, 1f);
                _splashAlpha = EaseOutCubic(t);
                _displayProgress = 0f;

                if (_phaseElapsed >= FadeInDuration)
                {
                    _splashAlpha = 1f;
                    _phase = SplashPhase.LoadingWarmup;
                    _phaseElapsed = 0f;
                    RecordStatus("Mounting workspace...");
                    StartBackgroundWarmup();
                }

                break;
            }

            case SplashPhase.LoadingWarmup:
            {
                _splashAlpha = 1f;

                if (_backgroundDone && !_mainStepsQueued)
                {
                    BuildMainThreadWarmupQueue();
                    _mainStepsQueued = true;
                }

                if (_mainStepsQueued && !_allWarmupDone)
                {
                    PumpMainThreadWarmupStep();
                }

                float targetProgress = ComputeTargetProgress();
                float speed = _allWarmupDone && _phaseElapsed >= MinWarmupDuration ? 6.5f : 3.2f;
                _displayProgress = MathF.Max(
                    _displayProgress,
                    _displayProgress + (targetProgress - _displayProgress) * Math.Clamp(dt * speed * 3.0f, 0f, 1f));
                if (_allWarmupDone && _phaseElapsed >= MinWarmupDuration && MathF.Abs(targetProgress - _displayProgress) < 0.005f)
                {
                    _displayProgress = 1f;
                }

                if (_allWarmupDone && _displayProgress >= 0.985f)
                {
                    RecordStatus("Workbench ready.");
                }

                if (_allWarmupDone && _phaseElapsed >= MinWarmupDuration && _displayProgress >= 0.995f)
                {
                    _displayProgress = 1f;
                    RecordStatus("Workbench ready.");
                    _phase = SplashPhase.FadeOut;
                    _phaseElapsed = 0f;
                }

                break;
            }

            case SplashPhase.FadeOut:
            {
                float t = Math.Clamp(_phaseElapsed / FadeOutDuration, 0f, 1f);
                _splashAlpha = 1f - EaseInOutCubic(t);
                _displayProgress = 1f;

                if (_phaseElapsed >= FadeOutDuration)
                {
                    _splashAlpha = 0f;
                    _phase = SplashPhase.Completed;
                    return true;
                }

                break;
            }
        }

        return _phase == SplashPhase.Completed;
    }

    public void Render(int screenWidth, int screenHeight)
    {
        if (_phase == SplashPhase.Completed || screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        EnsureFontsLoaded();
        RenderFrame(
            screenWidth,
            screenHeight,
            _phase,
            _phaseElapsed,
            _totalElapsed,
            _splashAlpha,
            _displayProgress,
            _statusText,
            CanSkipNow,
            _fontsLoaded ? _titleFont : null,
            _fontsLoaded ? _subtitleFont : null,
            _fontsLoaded ? _monoFont : null);
    }

    /// <summary>
    /// Stateless visual render entry point, also reusable by offscreen preview verification harnesses.
    /// </summary>
    public static void RenderFrame(
        int screenWidth,
        int screenHeight,
        SplashPhase phase,
        float phaseElapsed,
        float totalElapsed,
        float splashAlpha,
        float progress,
        string statusText,
        bool showSkipHint = false,
        Font? titleFont = null,
        Font? subtitleFont = null,
        Font? monoFont = null)
    {
        float alpha = Math.Clamp(splashAlpha, 0f, 1f);
        if (alpha <= 0.001f)
        {
            return;
        }

        if (phase == SplashPhase.FadeOut)
        {
            // During fade-out the docking workbench renders underneath; dissolve #121214 seamlessly.
            Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, WithAlpha(BackdropBaseColor, alpha));
        }
        else
        {
            // During Fade-In, smoothly brighten from deep void into #121214.
            Color bg = phase == SplashPhase.FadeIn
                ? LerpColor(VoidStartColor, BackdropBaseColor, alpha)
                : BackdropBaseColor;
            Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, bg);
        }

        float scaleFactor = Math.Clamp(Math.Min(screenWidth / 1280f, screenHeight / 720f), 0.72f, 1.35f);
        float centerX = screenWidth * 0.5f;
        float visualCenterY = screenHeight * 0.5f - 28f * scaleFactor;

        // Subtle square studio blueprint grid behind the centerpiece (matching Verstak brand card).
        float gridSpan = 370f * scaleFactor;
        DrawStudioBackdropGrid(centerX, visualCenterY + 6f * scaleFactor, gridSpan, alpha);

        // Soft warm studio ambient halo behind the logo fold (match RGB across center/outer to keep hue pure).
        float haloPulse = 0.88f + 0.12f * MathF.Sin(totalElapsed * 2.6f);
        Raylib.DrawCircleGradient(
            (int)MathF.Round(centerX),
            (int)MathF.Round(visualCenterY - 48f * scaleFactor),
            155f * scaleFactor,
            WithAlpha(AmberAccentColor, alpha * 0.045f * haloPulse),
            new Color(AmberAccentColor.R, AmberAccentColor.G, AmberAccentColor.B, (byte)0));

        // Animated fold & slide progress for the procedural isometric V emblem.
        float armConverge = phase == SplashPhase.FadeIn
            ? EaseOutCubic(Math.Clamp(phaseElapsed / FadeInDuration, 0f, 1f))
            : 1f;
        float strapSlide = phase switch
        {
            SplashPhase.FadeIn => EaseOutCubic(Math.Clamp((phaseElapsed - 0.24f) / 0.26f, 0f, 1f)) * 0.28f,
            SplashPhase.LoadingWarmup => 0.28f + 0.72f * EaseOutCubic(Math.Clamp(phaseElapsed / 0.50f, 0f, 1f)),
            _ => 1f
        };

        float logoSize = 118f * scaleFactor;
        float logoCenterY = visualCenterY - 56f * scaleFactor;
        DrawVerstakLogoMark(new Vector2(centerX, logoCenterY), logoSize, alpha, armConverge, strapSlide);

        // Typography: "VERSTAK" headline + "maximum flexibility. minimum noise." subtitle.
        const string title = "VERSTAK";
        const string subtitle = "maximum flexibility. minimum noise.";

        float titleFontSize = 48f * scaleFactor;
        float titleSpacing = 6.5f * scaleFactor;
        float titleY = logoCenterY + logoSize * 0.62f + 18f * scaleFactor;

        Vector2 titleSize = MeasureStyledText(titleFont, title, titleFontSize, titleSpacing);
        float titleRevealSlide = (1f - armConverge) * 8f * scaleFactor;
        DrawStyledText(
            titleFont,
            title,
            new Vector2(centerX - titleSize.X * 0.5f, titleY + titleRevealSlide),
            titleFontSize,
            titleSpacing,
            WithAlpha(LightPrimaryColor, alpha));

        float subFontSize = 15.5f * scaleFactor;
        float subSpacing = 0.6f * scaleFactor;
        float subY = titleY + titleSize.Y + 6f * scaleFactor;
        Vector2 subSize = MeasureStyledText(subtitleFont, subtitle, subFontSize, subSpacing);
        DrawStyledText(
            subtitleFont,
            subtitle,
            new Vector2(centerX - subSize.X * 0.5f, subY + titleRevealSlide),
            subFontSize,
            subSpacing,
            WithAlpha(SubtitleColor, alpha * 0.92f));

        // Progress indicator: strictly centered 280px x 2px amber bar (#D97724) + monospace status below.
        float barAlpha = phase == SplashPhase.FadeIn
            ? EaseOutCubic(Math.Clamp((phaseElapsed - 0.25f) / 0.25f, 0f, 1f)) * alpha
            : alpha;

        if (barAlpha > 0.005f)
        {
            const float barWidth = 280f;
            const float barHeight = 2f;
            float barX = MathF.Round(centerX - barWidth * 0.5f);
            float barY = MathF.Round(subY + subSize.Y + 34f * scaleFactor);

            Raylib.DrawRectangle(
                (int)barX,
                (int)barY,
                (int)barWidth,
                (int)barHeight,
                WithAlpha(TrackBgColor, barAlpha));

            float clampedProgress = Math.Clamp(progress, 0f, 1f);
            float fillWidth = barWidth * clampedProgress;
            if (fillWidth >= 1f)
            {
                Raylib.DrawRectangle(
                    (int)barX,
                    (int)barY,
                    (int)MathF.Ceiling(fillWidth),
                    (int)barHeight,
                    WithAlpha(AmberAccentColor, barAlpha));

                // Subtle specular leading glint on active bar tip.
                float tipW = MathF.Min(18f, fillWidth);
                Raylib.DrawRectangle(
                    (int)MathF.Round(barX + fillWidth - tipW),
                    (int)barY,
                    (int)MathF.Ceiling(tipW),
                    (int)barHeight,
                    WithAlpha(AmberTipColor, barAlpha * 0.72f));
            }

            if (!string.IsNullOrWhiteSpace(statusText))
            {
                float monoFontSize = 13f * Math.Clamp(scaleFactor, 0.85f, 1.15f);
                float monoSpacing = 0.35f;
                Vector2 statusSize = MeasureStyledText(monoFont, statusText, monoFontSize, monoSpacing);
                DrawStyledText(
                    monoFont,
                    statusText,
                    new Vector2(centerX - statusSize.X * 0.5f, barY + 12f),
                    monoFontSize,
                    monoSpacing,
                    WithAlpha(StatusTextColor, barAlpha * 0.88f));
            }

            if (showSkipHint && phase == SplashPhase.LoadingWarmup)
            {
                const string skipHint = "Press Space or Click to continue";
                float hintFontSize = 11.5f;
                Vector2 hintSize = MeasureStyledText(monoFont, skipHint, hintFontSize, 0.3f);
                DrawStyledText(
                    monoFont,
                    skipHint,
                    new Vector2(centerX - hintSize.X * 0.5f, screenHeight - 36f),
                    hintFontSize,
                    0.3f,
                    WithAlpha(StatusTextColor, barAlpha * 0.42f));
            }
        }
    }

    public static void DrawVerstakLogoMark(
        Vector2 center,
        float size,
        float alpha,
        float armConverge = 1f,
        float strapSlide = 1f)
    {
        float clampedAlpha = Math.Clamp(alpha, 0f, 1f);
        if (clampedAlpha <= 0.001f || size <= 1f)
        {
            return;
        }

        Vector2 MapLocal(Vector2 normalized, Vector2 offset)
        {
            return new Vector2(
                center.X + normalized.X * size + offset.X,
                center.Y + normalized.Y * size + offset.Y);
        }

        // Converging motion: left amber arm enters from upper-left; right white arm enters from upper-right.
        float travel = (1f - Math.Clamp(armConverge, 0f, 1f)) * (size * 0.24f);
        Vector2 leftArmOffset = new(-travel, -travel * 0.42f);
        Vector2 rightArmOffset = new(travel, travel * 0.28f);

        // Left Amber Arm ('\', #D97724) vertices
        Vector2 lTopLeft = MapLocal(new Vector2(-0.40f, -0.45f), leftArmOffset);
        Vector2 lTopRight = MapLocal(new Vector2(-0.19f, -0.45f), leftArmOffset);
        Vector2 lBotRight = MapLocal(new Vector2(0.08f, 0.45f), leftArmOffset);
        Vector2 lBotLeft = MapLocal(new Vector2(-0.13f, 0.45f), leftArmOffset);

        // Right White Arm ('/') glides rigidly as its own clean fold shape; at converge=1 it locks flush along seam (-0.001, +0.18) -> (+0.08, +0.45)
        Vector2 rTopLeft = MapLocal(new Vector2(0.25f, -0.45f), rightArmOffset);
        Vector2 rTopRight = MapLocal(new Vector2(0.47f, -0.45f), rightArmOffset);
        Vector2 rBotRight = MapLocal(new Vector2(0.08f, 0.45f), rightArmOffset);
        Vector2 rCreaseJoin = MapLocal(new Vector2(-0.001f, 0.18f), rightArmOffset);
        Vector2 rFoldEdgeBot = Vector2.Lerp(rTopRight, rBotRight, 0.23f);

        // Edge-to-edge non-overlapping lower body + upper fold cap (clean at all opacities)
        Color rightBodyColor = WithAlpha(new Color(225, 228, 234, 255), clampedAlpha);
        DrawTriangleAuto(rTopLeft, rFoldEdgeBot, rBotRight, rightBodyColor);
        DrawTriangleAuto(rTopLeft, rBotRight, rCreaseJoin, rightBodyColor);
        DrawTriangleAuto(rTopLeft, rTopRight, rFoldEdgeBot, WithAlpha(LightPrimaryColor, clampedAlpha));

        // Subtle inner-fold shade only in the lower V crease tip
        Vector2 rShadeTip = Vector2.Lerp(rFoldEdgeBot, rBotRight, 0.75f);
        DrawTriangleAuto(rCreaseJoin, rShadeTip, rBotRight, WithAlpha(LightCreaseColor, clampedAlpha * 0.36f));

        // Main Left Amber Arm (#D97724)
        DrawQuadAuto(lTopLeft, lTopRight, lBotRight, lBotLeft, WithAlpha(AmberAccentColor, clampedAlpha));

        // Top Layer: Diagonal White Cross-Strap ('/' folding cleanly from top-right across front of amber arm)
        float strapProgress = Math.Clamp(strapSlide, 0f, 1f);
        if (strapProgress > 0.01f)
        {
            Vector2 sTopRight = rTopLeft;
            Vector2 sBotRight = rFoldEdgeBot;

            Vector2 sMidOffset = Vector2.Lerp(rightArmOffset, leftArmOffset, 0.5f);
            Vector2 sTopLeftTarget = MapLocal(new Vector2(-0.42f, 0.03f), sMidOffset);
            Vector2 sBotLeftTarget = MapLocal(new Vector2(-0.30f, 0.18f), sMidOffset);

            Vector2 sTopLeft = Vector2.Lerp(sTopRight, sTopLeftTarget, strapProgress);
            Vector2 sBotLeft = Vector2.Lerp(sBotRight, sBotLeftTarget, strapProgress);

            // Clipped cast shadow strictly across the full span of the amber left arm under the diagonal strap
            float shadowAlpha = clampedAlpha * Math.Clamp((strapProgress - 0.50f) / 0.50f, 0f, 1f);
            if (shadowAlpha > 0.01f)
            {
                Vector2 armShadowL = Vector2.Lerp(sBotRight, sBotLeftTarget, 0.87f);
                Vector2 armShadowR = Vector2.Lerp(sBotRight, sBotLeftTarget, 0.51f);
                Vector2 armDrop = new(0.012f * size, 0.044f * size);
                DrawQuadAuto(
                    armShadowL,
                    armShadowR,
                    armShadowR + armDrop,
                    armShadowL + armDrop,
                    WithAlpha(AmberDarkFoldColor, shadowAlpha * 0.65f));
            }

            // Bright crisp diagonal strap ribbon (#F5F5F5)
            DrawQuadAuto(sTopLeft, sTopRight, sBotRight, sBotLeft, WithAlpha(LightStrapColor, clampedAlpha));
        }
    }

    private void StartBackgroundWarmup()
    {
        if (_backgroundStarted)
        {
            return;
        }

        _backgroundStarted = true;
        _bgStatus = "Mounting workspace...";
        _bgTargetProgress = 0.10f;

        _backgroundTask = Task.Run(() =>
        {
            try
            {
                CancellationToken ct = _cts.Token;
                ct.ThrowIfCancellationRequested();

                // Step 1: Scan active workspace & pre-read imgui.ini
                SetBackgroundProgress("Mounting workspace...", 0.25f);
                _workspace.ScanDirectory();

                string[] csFiles = _workspace.EnumerateFiles("scripts", "*.cs", recursive: true).ToArray();
                var scriptSet = new HashSet<string>(csFiles, StringComparer.Ordinal);
                ParseProjectJsonForWarmup(scriptSet);
                _discoveredScripts = scriptSet.ToArray();

                ct.ThrowIfCancellationRequested();
                DiscoverAssetsForGpuWarmup();

                if (File.Exists(_imguiIniPath))
                {
                    try
                    {
                        _preloadedIniContent = File.ReadAllText(_imguiIniPath);
                        IniPreloaded = true;
                    }
                    catch (IOException)
                    {
                    }
                }

                // Step 2: Roslyn JIT warmup & script compilation test
                ct.ThrowIfCancellationRequested();
                SetBackgroundProgress("Warming up Roslyn runtime...", 0.62f);
                RunRoslynWarmup(_discoveredScripts);
                RoslynWarmedUp = true;

                SetBackgroundProgress("Pre-caching shaders...", 0.76f);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // Keep splash resilient even if workspace contains unreadable assets or syntax errors.
                RoslynWarmedUp = true;
            }
            finally
            {
                _backgroundDone = true;
            }
        }, _cts.Token);
    }

    private void ParseProjectJsonForWarmup(HashSet<string> scriptSet)
    {
        string projectJsonPath = _workspace.ResolvePath("project.json");
        if (!File.Exists(projectJsonPath))
        {
            return;
        }

        try
        {
            using FileStream stream = File.OpenRead(projectJsonPath);
            using JsonDocument doc = JsonDocument.Parse(stream);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("virtualWidth", out JsonElement wEl) && wEl.TryGetInt32(out int w) && w >= 64)
            {
                _projectVirtualWidth = w;
            }

            if (root.TryGetProperty("virtualHeight", out JsonElement hEl) && hEl.TryGetInt32(out int h) && h >= 64)
            {
                _projectVirtualHeight = h;
            }

            if (root.TryGetProperty("display", out JsonElement dispEl) && dispEl.ValueKind == JsonValueKind.Object)
            {
                if (dispEl.TryGetProperty("virtualWidth", out JsonElement dwEl) && dwEl.TryGetInt32(out int dw) && dw >= 64)
                {
                    _projectVirtualWidth = dw;
                }

                if (dispEl.TryGetProperty("virtualHeight", out JsonElement dhEl) && dhEl.TryGetInt32(out int dh) && dh >= 64)
                {
                    _projectVirtualHeight = dh;
                }
            }

            if (root.TryGetProperty("nodes", out JsonElement nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement node in nodesEl.EnumerateArray())
                {
                    if (node.TryGetProperty("scriptPath", out JsonElement spEl) && spEl.ValueKind == JsonValueKind.String)
                    {
                        string? sp = spEl.GetString();
                        if (!string.IsNullOrWhiteSpace(sp))
                        {
                            string resolved = _workspace.ResolvePath(sp);
                            if (File.Exists(resolved))
                            {
                                scriptSet.Add(resolved);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void DiscoverAssetsForGpuWarmup()
    {
        var textures = new List<string>();
        var shaders = new List<string>();

        foreach (string fullPath in _workspace.EnumerateFiles(null, "*", recursive: true))
        {
            string ext = Path.GetExtension(fullPath);
            if (string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase))
            {
                if (textures.Count < 32)
                {
                    textures.Add(_workspace.ToRelativePath(fullPath));
                }
            }
            else if (string.Equals(ext, ".fs", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ext, ".frag", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ext, ".glsl", StringComparison.OrdinalIgnoreCase))
            {
                if (shaders.Count < 16)
                {
                    shaders.Add(_workspace.ToRelativePath(fullPath));
                }
            }
        }

        _discoveredTextures = textures.ToArray();
        _discoveredShaders = shaders.ToArray();
    }

    private static void RunRoslynWarmup(string[] scriptPaths)
    {
        string? tempWarmupPath = null;
        try
        {
            string[] targetPaths = scriptPaths;
            if (targetPaths.Length == 0)
            {
                tempWarmupPath = Path.Combine(Path.GetTempPath(), $"verstak_warmup_{Guid.NewGuid():N}.cs");
                File.WriteAllText(tempWarmupPath, "namespace VerstakWarmup { internal static class WarmupPing { internal static int Value => 1; } }");
                targetPaths = [tempWarmupPath];
            }

            ScriptCompileResult result = ScriptCompiler.CompileNodesFromFiles(targetPaths);
            result.LoadContext?.Unload();
        }
        finally
        {
            if (tempWarmupPath != null && File.Exists(tempWarmupPath))
            {
                try
                {
                    File.Delete(tempWarmupPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private void BuildMainThreadWarmupQueue()
    {
        _mainThreadQueue.Clear();

        _mainThreadQueue.Enqueue((
            "Pre-caching shaders...",
            0.80f,
            () => _graphics.EnsureRenderTexture(_projectVirtualWidth, _projectVirtualHeight)));

        foreach (string shaderPath in _discoveredShaders)
        {
            string pathCopy = shaderPath;
            _mainThreadQueue.Enqueue((
                "Pre-caching shaders...",
                0.84f,
                () =>
                {
                    if (Raylib.IsWindowReady())
                    {
                        _graphics.SetShaderFloat(pathCopy, "uTime", 0f);
                    }
                }));
        }

        foreach (string texPath in _discoveredTextures)
        {
            string pathCopy = texPath;
            _mainThreadQueue.Enqueue((
                "Pre-caching shaders...",
                0.89f,
                () =>
                {
                    if (Raylib.IsWindowReady())
                    {
                        _ = _graphics.GetTextureSize(pathCopy);
                    }
                }));
        }

        _mainThreadQueue.Enqueue((
            "Restoring workbench layout...",
            0.96f,
            () =>
            {
                if (!string.IsNullOrEmpty(_preloadedIniContent))
                {
                    _onLoadIniFromMemory?.Invoke(_preloadedIniContent);
                }
            }));

        _mainThreadQueue.Enqueue((
            "Restoring workbench layout...",
            1.00f,
            () =>
            {
                if (!FinalizeExecuted)
                {
                    FinalizeExecuted = true;
                    _onMainThreadFinalizeWorkbench?.Invoke();
                }
            }));

        _mainTotalSteps = _mainThreadQueue.Count;
        _mainCompletedSteps = 0;
    }

    private void PumpMainThreadWarmupStep()
    {
        var frameBudget = Stopwatch.StartNew();
        while (_mainThreadQueue.Count > 0)
        {
            var next = _mainThreadQueue.Dequeue();
            RecordStatus(next.Status);
            try
            {
                next.Step();
            }
            catch (Exception)
            {
            }

            _mainCompletedSteps++;
            if (frameBudget.ElapsedMilliseconds >= 8)
            {
                break;
            }
        }

        if (_mainThreadQueue.Count == 0)
        {
            _allWarmupDone = true;
        }
    }

    private void ExecuteSkip()
    {
        if (!_backgroundDone)
        {
            return;
        }

        if (!_mainStepsQueued)
        {
            BuildMainThreadWarmupQueue();
            _mainStepsQueued = true;
        }

        while (_mainThreadQueue.Count > 0)
        {
            var next = _mainThreadQueue.Dequeue();
            RecordStatus(next.Status);
            try
            {
                next.Step();
            }
            catch (Exception)
            {
            }

            _mainCompletedSteps++;
        }

        _allWarmupDone = true;
        _displayProgress = 1f;
        _splashAlpha = 0f;
        _phase = SplashPhase.Completed;
    }

    private float ComputeTargetProgress()
    {
        string liveStatus;
        float liveTarget;
        lock (_sync)
        {
            liveStatus = _bgStatus;
            liveTarget = _bgTargetProgress;
        }

        float timePacing = Math.Clamp(_phaseElapsed / MinWarmupDuration, 0f, 1f);

        if (!_backgroundDone)
        {
            RecordStatus(liveStatus);
            float combined = liveTarget * 0.45f + timePacing * 0.50f;
            return Math.Clamp(MathF.Max(liveTarget * 0.65f, combined), 0.04f, 0.78f);
        }

        if (!_allWarmupDone)
        {
            float mainFrac = _mainTotalSteps > 0 ? (float)_mainCompletedSteps / _mainTotalSteps : 1f;
            float workCeiling = 0.76f + 0.20f * mainFrac;
            return Math.Clamp(MathF.Min(workCeiling, 0.40f + timePacing * 0.58f), _displayProgress, 0.96f);
        }

        float inv = 1f - timePacing;
        float easedTime = 1f - inv * inv;
        return Math.Clamp(MathF.Max(_displayProgress, easedTime), 0f, 1f);
    }

    private void SetBackgroundProgress(string status, float targetProgress)
    {
        lock (_sync)
        {
            _bgStatus = status;
            _bgTargetProgress = Math.Clamp(targetProgress, 0f, 1f);
            RecordStatusLocked(status);
        }
    }

    private void RecordStatus(string status)
    {
        _statusText = status;
        lock (_sync)
        {
            RecordStatusLocked(status);
        }
    }

    private void RecordStatusLocked(string status)
    {
        if (_statusHistory.Count == 0 || !string.Equals(_statusHistory[^1], status, StringComparison.Ordinal))
        {
            _statusHistory.Add(status);
        }
    }

    private void EnsureFontsLoaded()
    {
        if (_fontsLoaded || !Raylib.IsWindowReady())
        {
            return;
        }

        float dpiScale = MathF.Max(1f, Raylib.GetWindowScaleDPI().X);
        string? sansPath = FindExistingPath(
            "/System/Library/Fonts/SFNS.ttf",
            "/System/Library/Fonts/Helvetica.ttc",
            "/System/Library/Fonts/Supplemental/Arial.ttf");
        string? monoPath = FindExistingPath(
            "/System/Library/Fonts/SFNSMono.ttf",
            "/System/Library/Fonts/Menlo.ttc",
            "/System/Library/Fonts/Monaco.ttf",
            sansPath);

        if (sansPath != null)
        {
            _titleFont = LoadCrispFont(sansPath, (int)MathF.Ceiling(56f * dpiScale));
            _subtitleFont = LoadCrispFont(sansPath, (int)MathF.Ceiling(18f * dpiScale));
        }

        if (monoPath != null)
        {
            _monoFont = LoadCrispFont(monoPath, (int)MathF.Ceiling(15f * dpiScale));
        }

        _fontsLoaded = true;
    }

    private static Font LoadCrispFont(string path, int pixelSize)
    {
        Font font = Raylib.LoadFontEx(path, Math.Clamp(pixelSize, 12, 160), null, 256);
        if (Raylib.IsFontValid(font))
        {
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        }

        return font;
    }

    private static string? FindExistingPath(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void DrawStudioBackdropGrid(float centerX, float centerY, float span, float alpha)
    {
        int cells = 12;
        float step = span / cells;
        float half = span * 0.5f;
        float left = centerX - half;
        float top = centerY - half;

        for (int i = 0; i <= cells; i++)
        {
            float t = i / (float)cells;
            float edgeFade = MathF.Sin(t * MathF.PI); // feather smoothly to 0 alpha on outer grid bounds
            float lineAlpha = alpha * (edgeFade * edgeFade);
            if (lineAlpha <= 0.01f)
            {
                continue;
            }

            float x = MathF.Round(left + i * step);
            float y = MathF.Round(top + i * step);
            Color c = WithAlpha(GridLineColor, lineAlpha);

            Raylib.DrawLineEx(new Vector2(x, top), new Vector2(x, top + span), 1f, c);
            Raylib.DrawLineEx(new Vector2(left, y), new Vector2(left + span, y), 1f, c);
        }
    }

    private static Vector2 MeasureStyledText(Font? font, string text, float fontSize, float spacing)
    {
        if (font.HasValue && Raylib.IsFontValid(font.Value))
        {
            return Raylib.MeasureTextEx(font.Value, text, fontSize, spacing);
        }

        int sizeInt = Math.Max(8, (int)MathF.Round(fontSize));
        return new Vector2(Raylib.MeasureText(text, sizeInt), sizeInt);
    }

    private static void DrawStyledText(Font? font, string text, Vector2 pos, float fontSize, float spacing, Color color)
    {
        if (font.HasValue && Raylib.IsFontValid(font.Value))
        {
            Raylib.DrawTextEx(font.Value, text, pos, fontSize, spacing, color);
            return;
        }

        Raylib.DrawText(text, (int)MathF.Round(pos.X), (int)MathF.Round(pos.Y), Math.Max(8, (int)MathF.Round(fontSize)), color);
    }

    private static void DrawQuadAuto(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color color)
    {
        DrawTriangleAuto(p0, p1, p2, color);
        DrawTriangleAuto(p0, p2, p3, color);
    }

    private static void DrawTriangleAuto(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        // In screen coordinates (+X right, +Y down), Raylib CCW front-facing triangles satisfy cross < 0.
        float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (cross > 0f)
        {
            Raylib.DrawTriangle(a, c, b, color);
        }
        else
        {
            Raylib.DrawTriangle(a, b, c, color);
        }
    }

    private static Color WithAlpha(Color color, float alphaScale)
    {
        byte a = (byte)Math.Clamp(color.A * alphaScale, 0f, 255f);
        return new Color(color.R, color.G, color.B, a);
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        float k = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)Math.Clamp(a.R + (b.R - a.R) * k, 0f, 255f),
            (byte)Math.Clamp(a.G + (b.G - a.G) * k, 0f, 255f),
            (byte)Math.Clamp(a.B + (b.B - a.B) * k, 0f, 255f),
            (byte)Math.Clamp(a.A + (b.A - a.A) * k, 0f, 255f));
    }

    private static float EaseOutCubic(float t)
    {
        float inv = 1f - Math.Clamp(t, 0f, 1f);
        return 1f - inv * inv * inv;
    }

    private static float EaseInOutCubic(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return x < 0.5f ? 4f * x * x * x : 1f - MathF.Pow(-2f * x + 2f, 3f) * 0.5f;
    }

    private static void DefaultLoadIniFromMemory(string iniContent)
    {
        if (ImGui.GetCurrentContext() != IntPtr.Zero && !string.IsNullOrEmpty(iniContent))
        {
            ImGui.LoadIniSettingsFromMemory(iniContent);
        }
    }

    private static bool DefaultSkipInputProbe()
    {
        if (!Raylib.IsWindowReady())
        {
            return false;
        }

        return Raylib.IsKeyPressed(KeyboardKey.Space) ||
               Raylib.IsKeyPressed(KeyboardKey.Enter) ||
               Raylib.IsKeyPressed(KeyboardKey.KpEnter) ||
               Raylib.IsKeyPressed(KeyboardKey.Escape) ||
               Raylib.IsMouseButtonPressed(MouseButton.Left) ||
               Raylib.IsMouseButtonPressed(MouseButton.Right) ||
               Raylib.IsMouseButtonPressed(MouseButton.Middle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        if (_backgroundTask != null)
        {
            try
            {
                _backgroundTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
        }

        if (_fontsLoaded && Raylib.IsWindowReady())
        {
            if (Raylib.IsFontValid(_titleFont)) Raylib.UnloadFont(_titleFont);
            if (Raylib.IsFontValid(_subtitleFont)) Raylib.UnloadFont(_subtitleFont);
            if (Raylib.IsFontValid(_monoFont)) Raylib.UnloadFont(_monoFont);
            _fontsLoaded = false;
        }

        _cts.Dispose();
    }
}
