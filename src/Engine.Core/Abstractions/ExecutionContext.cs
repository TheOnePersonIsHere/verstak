namespace Engine.Core.Abstractions;

public sealed class ExecutionContext
{
    private readonly Dictionary<string, float> _floatProperties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _stringProperties = new(StringComparer.Ordinal);

    public ExecutionContext(
        IGraphicsService graphics,
        IAudioService audio,
        IEventBus eventBus,
        IWorkspaceService workspace,
        IInputService input)
    {
        Graphics = graphics;
        Audio = audio;
        EventBus = eventBus;
        Workspace = workspace;
        Input = input;
    }

    public IGraphicsService Graphics { get; }
    public IAudioService Audio { get; }
    public IEventBus EventBus { get; }
    public IWorkspaceService Workspace { get; }
    public IInputService Input { get; }

    public int VirtualWidth { get; set; } = 960;
    public int VirtualHeight { get; set; } = 540;

    public float CurrentTime { get; set; }
    public float MaxDuration { get; set; }
    public bool IsPlaying { get; set; }

    public IReadOnlyDictionary<string, float> FloatProperties => _floatProperties;
    public IReadOnlyDictionary<string, string> StringProperties => _stringProperties;

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
}
