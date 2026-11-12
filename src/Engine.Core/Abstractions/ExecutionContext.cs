using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Engine.Core.Graphics;
using Engine.Core.Plugins;
using Raylib_cs;

namespace Engine.Core.Abstractions;

public sealed class ExecutionContext
{
    private readonly Dictionary<string, float> _floatProperties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _stringProperties = new(StringComparer.Ordinal);

    private IScopedEventBus _events = null!;
    private IScriptScope? _scope;

    public ExecutionContext()
    {
    }

    [SetsRequiredMembers]
    public ExecutionContext(
        IGraphicsService graphics,
        IAudioService audio,
        IEventBus eventBus,
        IWorkspaceService workspace,
        IInputService input,
        IQuadBatchService? batcher = null,
        IVirtualDisplayService? display = null,
        ISurfaceCompositor? compositor = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(input);

        Graphics = graphics;
        Audio = audio as IAudioMixerService ?? new LegacyAudioMixerAdapter(audio);
        Display = display ?? new VirtualDisplayService();

        if (graphics is RaylibGraphicsService raylibGraphics)
        {
            raylibGraphics.AttachDisplayService(Display);
            Batcher = batcher ?? new RaylibQuadBatchService(raylibGraphics);
            Compositor = compositor ?? new SurfaceCompositor(raylibGraphics);
        }
        else
        {
            Batcher = batcher ?? new NoOpQuadBatchService();
            Compositor = compositor ?? new FallbackSurfaceCompositor(graphics);
        }

        if (eventBus is IScopedEventBus scopedBus)
        {
            Events = scopedBus;
        }
        else
        {
            var defaultScope = new ScriptAssemblyScope(eventBus, graphics);
            Events = defaultScope;
        }

        Workspace = workspace;
        Input = input;
        CurrentTime = 0f;
    }

    public required IGraphicsService Graphics { get; init; }
    public required IQuadBatchService Batcher { get; init; }
    public required IVirtualDisplayService Display { get; init; }
    public required ISurfaceCompositor Compositor { get; init; }
    public required IAudioMixerService Audio { get; init; }

    public required IScopedEventBus Events
    {
        get => _events;
        init
        {
            _events = value;
            _scope = value as IScriptScope ?? new ScopedEventBusAdapter(value, Graphics);
        }
    }

    public required float CurrentTime { get; set; }

    public IScriptScope Scope => _scope ??= (_events as IScriptScope ?? new ScopedEventBusAdapter(_events, Graphics));
    public IEventBus EventBus => _events;
    public IWorkspaceService Workspace { get; set; } = new FallbackWorkspaceService();
    public IInputService Input { get; set; } = new FallbackInputService();

    public int VirtualWidth
    {
        get => Math.Max(1, (int)MathF.Round(Display.VirtualResolution.X));
        set => Display.VirtualResolution = new Vector2(Math.Max(1, value), Math.Max(1f, Display.VirtualResolution.Y));
    }

    public int VirtualHeight
    {
        get => Math.Max(1, (int)MathF.Round(Display.VirtualResolution.Y));
        set => Display.VirtualResolution = new Vector2(Math.Max(1f, Display.VirtualResolution.X), Math.Max(1, value));
    }

    public float MaxDuration { get; set; }
    public bool IsPlaying { get; set; }

    public IReadOnlyDictionary<string, float> FloatProperties => _floatProperties;
    public IReadOnlyDictionary<string, string> StringProperties => _stringProperties;

    internal void AttachScope(ScriptAssemblyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _events = scope;
        _scope = scope;
    }

    public void SetProperty(string key, float value)
    {
        _floatProperties[key] = value;
    }

    public void SetStringProperty(string key, string value)
    {
        _stringProperties[key] = value;
    }

    public float GetFloat(string key, float defaultValue = 0f)
    {
        return _floatProperties.TryGetValue(key, out float value) ? value : defaultValue;
    }

    public string GetString(string key, string defaultValue = "")
    {
        return _stringProperties.TryGetValue(key, out string? value) ? value : defaultValue;
    }

    public void ClearProperties()
    {
        _floatProperties.Clear();
        _stringProperties.Clear();
    }

    private sealed class ScopedEventBusAdapter : IScriptScope
    {
        private readonly IScopedEventBus _bus;
        private readonly IGraphicsService? _graphics;
        private readonly HashSet<string> _surfaces = new(StringComparer.Ordinal);
        private readonly List<Action> _unsubscribers = new();

        public ScopedEventBusAdapter(IScopedEventBus bus, IGraphicsService? graphics)
        {
            _bus = bus;
            _graphics = graphics;
        }

        public void Subscribe<T>(Action<T> handler)
        {
            _bus.Subscribe(handler);
            _unsubscribers.Add(() => _bus.Unsubscribe(handler));
        }

        public void TrackSurface(string surfaceName)
        {
            _bus.TrackSurface(surfaceName);
            if (!string.IsNullOrWhiteSpace(surfaceName))
            {
                _surfaces.Add(surfaceName);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _unsubscribers.Count; i++)
            {
                _unsubscribers[i]();
            }

            _unsubscribers.Clear();

            if (_graphics != null)
            {
                foreach (string surface in _surfaces)
                {
                    _graphics.ReleaseSurface(surface);
                }
            }

            _surfaces.Clear();
        }
    }

    private sealed class LegacyAudioMixerAdapter : IAudioMixerService
    {
        private readonly IAudioService _inner;
        private readonly float[] _busVolumes = [1f, 1f, 1f, 1f, 1f];

        public LegacyAudioMixerAdapter(IAudioService inner) => _inner = inner;

        public void PlaySound(string path, float volume, float pitch) => _inner.PlaySound(path, volume, pitch);
        public void StopSound(string path) => _inner.StopSound(path);
        public bool IsSoundPlaying(string path) => _inner.IsSoundPlaying(path);
        public void InvalidateSound(string path) => _inner.InvalidateSound(path);
        public void StopAll() => _inner.StopAll();

        public void SetBusVolume(AudioBus bus, float volume)
        {
            if ((uint)bus < (uint)_busVolumes.Length)
            {
                _busVolumes[(int)bus] = Math.Clamp(volume, 0f, 4f);
            }
        }

        public float GetBusVolume(AudioBus bus) => (uint)bus < (uint)_busVolumes.Length ? _busVolumes[(int)bus] : 1f;

        public void PlayOneShot(string assetPath, AudioBus bus = AudioBus.Effects, float volume = 1f, float pitchVariation = 0f)
        {
            float mult = bus == AudioBus.Master ? _busVolumes[0] : _busVolumes[0] * GetBusVolume(bus);
            float pitch = pitchVariation > 0f ? 1f + (Random.Shared.NextSingle() * 2f - 1f) * pitchVariation : 1f;
            _inner.PlaySound(assetPath, volume * mult, pitch);
        }

        public void PlayStream(string channelId, string assetPath, AudioBus bus, float targetVolume, float fadeInSec = 0f) { }
        public void StopStream(string channelId, float fadeOutSec = 0f) { }
        public void CrossfadeMusic(string newTrackPath, float fadeDurationSec) { }
        public void Tick(float deltaTime) { }
    }

    private sealed class NoOpQuadBatchService : IQuadBatchService
    {
        public void Begin(TextureHandle texture, BlendMode blendMode = BlendMode.Alpha, string? shaderPath = null) { }
        public void DrawQuad(Vector2 position, Vector2 size, Rectangle srcRect, Color tint) { }
        public void DrawQuadRotated(Vector2 position, Vector2 size, Rectangle srcRect, float rotationDeg, Vector2 origin, Color tint) { }
        public void End() { }
        public void Flush() { }
    }

    private sealed class FallbackSurfaceCompositor : ISurfaceCompositor
    {
        private readonly IGraphicsService _graphics;
        private int _lastWidth = 960;
        private int _lastHeight = 540;

        public FallbackSurfaceCompositor(IGraphicsService graphics) => _graphics = graphics;

        public void EnsureSurface(string surfaceName, int width, int height)
        {
            _lastWidth = Math.Max(1, width);
            _lastHeight = Math.Max(1, height);
        }

        public void BeginTarget(string surfaceName, Color? clearColor = null)
        {
            _graphics.BeginSurface(surfaceName, _lastWidth, _lastHeight);
            if (clearColor.HasValue)
            {
                _graphics.Clear(clearColor.Value);
            }
        }

        public void EndTarget() => _graphics.EndSurface();

        public void ExecutePass(PassDescriptor pass)
        {
            Vector2 sz = _graphics.GetSurfaceSize(pass.SourceSurface);
            if (sz.X <= 0f || sz.Y <= 0f)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(pass.TargetSurface))
            {
                _graphics.BeginSurface(pass.TargetSurface, (int)sz.X, (int)sz.Y);
            }

            _graphics.BeginBlendMode(pass.BlendMode);
            if (!string.IsNullOrWhiteSpace(pass.ShaderPath))
            {
                _graphics.BeginShader(pass.ShaderPath);
            }

            pass.SetupUniforms?.Invoke(_graphics);
            _graphics.DrawSurface(pass.SourceSurface, Vector2.Zero, sz, Color.White, 0f, Vector2.Zero);

            if (!string.IsNullOrWhiteSpace(pass.ShaderPath))
            {
                _graphics.EndShader();
            }

            _graphics.EndBlendMode();
            if (!string.IsNullOrWhiteSpace(pass.TargetSurface))
            {
                _graphics.EndSurface();
            }
        }

        public void PresentToScreen(string surfaceName, Vector2 position, Vector2 size)
        {
            _graphics.DrawSurface(surfaceName, position, size, Color.White, 0f, Vector2.Zero);
        }
    }

    private sealed class FallbackWorkspaceService : IWorkspaceService
    {
        public string ProjectRoot => Directory.GetCurrentDirectory();
        public void SetProjectRoot(string projectRoot) { }
        public DirectoryInfo GetRootDirectory() => new(ProjectRoot);
        public WorkspaceDirectoryNode ScanDirectory(string? path = null) =>
            new(string.Empty, ProjectRoot, ".", Array.Empty<WorkspaceDirectoryNode>(), Array.Empty<WorkspaceFileEntry>());
        public IReadOnlyList<string> EnumerateFiles(string? relativeSubfolder = null, string searchPattern = "*", bool recursive = true) =>
            Array.Empty<string>();
        public string ResolvePath(string path) => path;
        public string ToRelativePath(string fullPath) => fullPath;
    }

    private sealed class FallbackInputService : IInputService
    {
        public Vector2 ScreenMousePosition => Vector2.Zero;
        public Vector2 VirtualMousePosition { get; set; }
        public bool IsMouseInViewport { get; set; }
        public bool IsKeyDown(KeyboardKey key) => false;
        public bool IsKeyPressed(KeyboardKey key) => false;
        public bool IsMouseButtonDown(MouseButton button) => false;
        public bool IsMouseButtonPressed(MouseButton button) => false;
    }
}
