using Engine.Core.Abstractions;
using Engine.Core.Events;
using Raylib_cs;

namespace Engine.Core.Audio;

public sealed class RaylibAudioService : IAudioService, IDisposable
{
    private readonly IWorkspaceService _workspace;
    private readonly IEventBus _eventBus;
    private readonly Dictionary<string, Sound> _soundCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingLogged = new(StringComparer.Ordinal);
    private bool _disposed;

    public RaylibAudioService(IWorkspaceService workspace, IEventBus eventBus)
    {
        _workspace = workspace;
        _eventBus = eventBus;
    }

    public void PlaySound(string path, float volume, float pitch)
    {
        if (!TryGetOrLoadSound(path, out Sound sound))
        {
            return;
        }

        Raylib.SetSoundVolume(sound, Math.Clamp(volume, 0f, 4f));
        Raylib.SetSoundPitch(sound, Math.Clamp(pitch, 0.1f, 4f));
        Raylib.PlaySound(sound);
    }

    public void StopSound(string path)
    {
        string resolved = _workspace.ResolvePath(path);
        if (_soundCache.TryGetValue(resolved, out Sound sound) && Raylib.IsSoundValid(sound))
        {
            Raylib.StopSound(sound);
        }
    }

    public bool IsSoundPlaying(string path)
    {
        string resolved = _workspace.ResolvePath(path);
        return _soundCache.TryGetValue(resolved, out Sound sound)
            && Raylib.IsSoundValid(sound)
            && Raylib.IsSoundPlaying(sound);
    }

    public void InvalidateSound(string path)
    {
        string resolved = _workspace.ResolvePath(path);
        _missingLogged.Remove(resolved);

        if (_soundCache.Remove(resolved, out Sound sound) && Raylib.IsSoundValid(sound))
        {
            Raylib.StopSound(sound);
            Raylib.UnloadSound(sound);
            _eventBus.Publish(new EngineLogEvent("Audio", "Info", $"Sound invalidated for reload: {_workspace.ToRelativePath(resolved)}"));
        }
    }

    public void StopAll()
    {
        foreach (Sound sound in _soundCache.Values)
        {
            if (Raylib.IsSoundValid(sound))
            {
                Raylib.StopSound(sound);
            }
        }
    }

    private bool TryGetOrLoadSound(string path, out Sound sound)
    {
        string resolved = _workspace.ResolvePath(path);
        if (_soundCache.TryGetValue(resolved, out sound) && Raylib.IsSoundValid(sound))
        {
            return true;
        }

        if (!File.Exists(resolved))
        {
            if (_missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Audio", "Warning", $"Audio file not found: {resolved}"));
            }

            sound = default;
            return false;
        }

        sound = Raylib.LoadSound(resolved);
        if (!Raylib.IsSoundValid(sound))
        {
            if (_missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Audio", "Warning", $"Failed to load audio: {resolved}"));
            }

            return false;
        }

        _missingLogged.Remove(resolved);
        _soundCache[resolved] = sound;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Sound sound in _soundCache.Values)
        {
            if (Raylib.IsSoundValid(sound))
            {
                Raylib.StopSound(sound);
                Raylib.UnloadSound(sound);
            }
        }

        _soundCache.Clear();
        _missingLogged.Clear();
    }
}
