namespace Engine.Core.Abstractions;

public enum AudioBus
{
    Master,
    Music,
    Atmosphere,
    Effects,
    Interface
}

public interface IAudioMixerService : IAudioService
{
    void SetBusVolume(AudioBus bus, float volume);
    float GetBusVolume(AudioBus bus);

    /// <summary>
    /// Plays a short sound with optional random pitch variation (fraction around 1.0f).
    /// </summary>
    void PlayOneShot(string assetPath, AudioBus bus = AudioBus.Effects, float volume = 1f, float pitchVariation = 0f);

    /// <summary>
    /// Starts or updates a looping named audio stream channel with linear fade-in.
    /// </summary>
    void PlayStream(string channelId, string assetPath, AudioBus bus, float targetVolume, float fadeInSec = 0f);

    /// <summary>
    /// Fades out and releases a named looping channel.
    /// </summary>
    void StopStream(string channelId, float fadeOutSec = 0f);

    /// <summary>
    /// Crossfades the primary music bus channel to a new audio track.
    /// </summary>
    void CrossfadeMusic(string newTrackPath, float fadeDurationSec);

    /// <summary>
    /// Updates active channel fade envelopes and stream buffers on the main thread.
    /// </summary>
    void Tick(float deltaTime);
}
