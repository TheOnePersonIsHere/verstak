namespace Engine.Core.Abstractions;

public interface IScriptScope : IDisposable
{
    /// <summary>
    /// Scoped event subscription automatically revoked when the dynamic script assembly scope is disposed.
    /// </summary>
    void Subscribe<T>(Action<T> handler);

    /// <summary>
    /// Tracks an offscreen surface identifier for deterministic GPU release on scope disposal.
    /// </summary>
    void TrackSurface(string surfaceName);
}

public interface IScopedEventBus : IEventBus
{
    /// <summary>
    /// Registers an offscreen surface identifier in the active scope for automatic cleanup on reload/unload.
    /// </summary>
    void TrackSurface(string surfaceName);
}
