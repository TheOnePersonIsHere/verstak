namespace Engine.Core.Abstractions;

public interface IAudioService
{
    void PlaySound(string path, float volume, float pitch);
    void StopSound(string path);
    bool IsSoundPlaying(string path);
    void InvalidateSound(string path);
    void StopAll();
}
