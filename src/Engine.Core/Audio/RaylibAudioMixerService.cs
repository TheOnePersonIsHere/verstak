using Engine.Core.Abstractions;
using Engine.Core.Events;
using Raylib_cs;

namespace Engine.Core.Audio;

public sealed class RaylibAudioMixerService : IAudioMixerService, IDisposable
{
    private sealed class SoundBankEntry
    {
        public Sound BaseSound;
        public readonly Sound[] Aliases = new Sound[MaxAliasesPerSound];
        public int AliasCount;
        public int NextAliasSlot;
    }

    private sealed class StreamChannel
    {
        public required string ChannelId { get; init; }
        public required string ResolvedPath { get; init; }
        public AudioBus Bus { get; set; }
        public Music Music;
        public bool HasNativeMusic;
        public float CurrentVolume;
        public float FadeStartVolume;
        public float FadeTargetVolume;
        public float FadeDuration;
        public float FadeElapsed;
        public bool StopWhenFadeCompletes;
        public bool IsRetiringMusic;
    }

    private const int MaxAliasesPerSound = 8;
    private const string PrimaryMusicChannelId = "__verstak_primary_music__";

    private readonly IWorkspaceService _workspace;
    private readonly IEventBus _eventBus;
    private readonly float[] _busVolumes = [1f, 1f, 1f, 1f, 1f];
    private readonly Dictionary<string, SoundBankEntry> _soundCache = new(StringComparer.Ordinal);
    private readonly List<StreamChannel> _streams = new();
    private readonly HashSet<string> _missingLogged = new(StringComparer.Ordinal);
    private int _retireCounter;
    private bool _disposed;

    public RaylibAudioMixerService(IWorkspaceService workspace, IEventBus eventBus)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public int ActiveStreamCount => _streams.Count;

    public void SetBusVolume(AudioBus bus, float volume)
    {
        int idx = (int)bus;
        if ((uint)idx >= (uint)_busVolumes.Length)
        {
            return;
        }

        _busVolumes[idx] = Math.Clamp(volume, 0f, 4f);
        ApplyVolumesToActiveStreams();
    }

    public float GetBusVolume(AudioBus bus)
    {
        int idx = (int)bus;
        return (uint)idx < (uint)_busVolumes.Length ? _busVolumes[idx] : 1f;
    }

    public void PlayOneShot(string assetPath, AudioBus bus = AudioBus.Effects, float volume = 1f, float pitchVariation = 0f)
    {
        if (_disposed || !Raylib.IsAudioDeviceReady())
        {
            return;
        }

        if (!TryGetOrLoadSoundEntry(assetPath, out SoundBankEntry entry))
        {
            return;
        }

        float pitch = 1.0f;
        if (pitchVariation > 0f)
        {
            float clampedVar = Math.Clamp(pitchVariation, 0f, 0.9f);
            pitch = 1.0f + ((Random.Shared.NextSingle() * 2f - 1f) * clampedVar);
        }

        float effectiveVol = Math.Clamp(volume * GetEffectiveBusMultiplier(bus), 0f, 4f);
        PlaySoundInstance(entry, effectiveVol, Math.Clamp(pitch, 0.1f, 4f));
    }

    public void PlayStream(string channelId, string assetPath, AudioBus bus, float targetVolume, float fadeInSec = 0f)
    {
        if (_disposed || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        string resolved = _workspace.ResolvePath(assetPath);
        float clampedTarget = Math.Clamp(targetVolume, 0f, 4f);
        float clampedFade = MathF.Max(0f, fadeInSec);

        int existingIndex = FindStreamIndex(channelId);
        if (existingIndex >= 0)
        {
            StreamChannel existing = _streams[existingIndex];
            if (string.Equals(existing.ResolvedPath, resolved, StringComparison.Ordinal))
            {
                existing.Bus = bus;
                existing.FadeStartVolume = existing.CurrentVolume;
                existing.FadeTargetVolume = clampedTarget;
                existing.FadeDuration = clampedFade;
                existing.FadeElapsed = 0f;
                existing.StopWhenFadeCompletes = false;

                if (clampedFade <= 0f)
                {
                    existing.CurrentVolume = clampedTarget;
                    ApplyVolume(existing);
                }

                return;
            }

            UnloadStreamNative(existing);
            _streams.RemoveAt(existingIndex);
        }

        StartNewStream(channelId, resolved, bus, clampedTarget, clampedFade);
    }

    public void StopStream(string channelId, float fadeOutSec = 0f)
    {
        int idx = FindStreamIndex(channelId);
        if (idx < 0)
        {
            return;
        }

        StreamChannel channel = _streams[idx];
        float clampedFade = MathF.Max(0f, fadeOutSec);
        if (clampedFade <= 0f)
        {
            UnloadStreamNative(channel);
            _streams.RemoveAt(idx);
            return;
        }

        channel.FadeStartVolume = channel.CurrentVolume;
        channel.FadeTargetVolume = 0f;
        channel.FadeDuration = clampedFade;
        channel.FadeElapsed = 0f;
        channel.StopWhenFadeCompletes = true;
    }

    public void CrossfadeMusic(string newTrackPath, float fadeDurationSec)
    {
        if (_disposed || string.IsNullOrWhiteSpace(newTrackPath))
        {
            return;
        }

        float clampedFade = MathF.Max(0f, fadeDurationSec);
        string resolved = _workspace.ResolvePath(newTrackPath);

        int primaryIdx = FindStreamIndex(PrimaryMusicChannelId);
        if (primaryIdx >= 0)
        {
            StreamChannel currentPrimary = _streams[primaryIdx];
            if (string.Equals(currentPrimary.ResolvedPath, resolved, StringComparison.Ordinal))
            {
                currentPrimary.Bus = AudioBus.Music;
                currentPrimary.FadeStartVolume = currentPrimary.CurrentVolume;
                currentPrimary.FadeTargetVolume = 1f;
                currentPrimary.FadeDuration = clampedFade;
                currentPrimary.FadeElapsed = 0f;
                currentPrimary.StopWhenFadeCompletes = false;
                if (clampedFade <= 0f)
                {
                    currentPrimary.CurrentVolume = 1f;
                    ApplyVolume(currentPrimary);
                }

                return;
            }

            if (clampedFade <= 0f)
            {
                UnloadStreamNative(currentPrimary);
                _streams.RemoveAt(primaryIdx);
            }
            else
            {
                string retiredId = $"__verstak_retired_music_{++_retireCounter}";
                _streams[primaryIdx] = new StreamChannel
                {
                    ChannelId = retiredId,
                    ResolvedPath = currentPrimary.ResolvedPath,
                    Bus = AudioBus.Music,
                    Music = currentPrimary.Music,
                    HasNativeMusic = currentPrimary.HasNativeMusic,
                    CurrentVolume = currentPrimary.CurrentVolume,
                    FadeStartVolume = currentPrimary.CurrentVolume,
                    FadeTargetVolume = 0f,
                    FadeDuration = clampedFade,
                    FadeElapsed = 0f,
                    StopWhenFadeCompletes = true,
                    IsRetiringMusic = true
                };
            }
        }

        StartNewStream(PrimaryMusicChannelId, resolved, AudioBus.Music, 1f, clampedFade);
    }

    public void Tick(float deltaTime)
    {
        if (_disposed || _streams.Count == 0)
        {
            return;
        }

        float dt = MathF.Max(0f, deltaTime);
        bool audioReady = Raylib.IsAudioDeviceReady();

        for (int i = _streams.Count - 1; i >= 0; i--)
        {
            StreamChannel ch = _streams[i];

            bool fadeFinished = false;
            if (ch.FadeDuration > 0f && ch.FadeElapsed < ch.FadeDuration)
            {
                ch.FadeElapsed += dt;
                float t = Math.Clamp(ch.FadeElapsed / ch.FadeDuration, 0f, 1f);
                ch.CurrentVolume = ch.FadeStartVolume + (ch.FadeTargetVolume - ch.FadeStartVolume) * t;
                if (t >= 1f)
                {
                    ch.CurrentVolume = ch.FadeTargetVolume;
                    ch.FadeDuration = 0f;
                    fadeFinished = true;
                }
            }
            else
            {
                ch.CurrentVolume = ch.FadeTargetVolume;
                fadeFinished = true;
            }

            if (ch.StopWhenFadeCompletes && fadeFinished)
            {
                UnloadStreamNative(ch);
                _streams.RemoveAt(i);
                continue;
            }

            if (audioReady && ch.HasNativeMusic && Raylib.IsMusicValid(ch.Music))
            {
                Raylib.UpdateMusicStream(ch.Music);
                Raylib.SetMusicVolume(ch.Music, Math.Clamp(ch.CurrentVolume * GetEffectiveBusMultiplier(ch.Bus), 0f, 4f));
            }
        }
    }

    public bool TryGetStreamVolume(string channelId, out float volume)
    {
        int idx = FindStreamIndex(channelId);
        if (idx >= 0)
        {
            volume = _streams[idx].CurrentVolume;
            return true;
        }

        volume = 0f;
        return false;
    }

    // Legacy IAudioService support routed through AudioBus.Effects
    public void PlaySound(string path, float volume, float pitch)
    {
        if (_disposed || !Raylib.IsAudioDeviceReady())
        {
            return;
        }

        if (!TryGetOrLoadSoundEntry(path, out SoundBankEntry entry))
        {
            return;
        }

        float effectiveVol = Math.Clamp(volume * GetEffectiveBusMultiplier(AudioBus.Effects), 0f, 4f);
        PlaySoundInstance(entry, effectiveVol, Math.Clamp(pitch, 0.1f, 4f));
    }

    public void StopSound(string path)
    {
        if (!Raylib.IsAudioDeviceReady())
        {
            return;
        }

        string resolved = _workspace.ResolvePath(path);
        if (!_soundCache.TryGetValue(resolved, out SoundBankEntry? entry))
        {
            return;
        }

        if (Raylib.IsSoundValid(entry.BaseSound))
        {
            Raylib.StopSound(entry.BaseSound);
        }

        for (int i = 0; i < entry.AliasCount; i++)
        {
            if (Raylib.IsSoundValid(entry.Aliases[i]))
            {
                Raylib.StopSound(entry.Aliases[i]);
            }
        }
    }

    public bool IsSoundPlaying(string path)
    {
        if (!Raylib.IsAudioDeviceReady())
        {
            return false;
        }

        string resolved = _workspace.ResolvePath(path);
        if (!_soundCache.TryGetValue(resolved, out SoundBankEntry? entry))
        {
            return false;
        }

        if (Raylib.IsSoundValid(entry.BaseSound) && Raylib.IsSoundPlaying(entry.BaseSound))
        {
            return true;
        }

        for (int i = 0; i < entry.AliasCount; i++)
        {
            if (Raylib.IsSoundValid(entry.Aliases[i]) && Raylib.IsSoundPlaying(entry.Aliases[i]))
            {
                return true;
            }
        }

        return false;
    }

    public void InvalidateSound(string path)
    {
        string resolved = _workspace.ResolvePath(path);
        _missingLogged.Remove(resolved);

        if (_soundCache.Remove(resolved, out SoundBankEntry? entry))
        {
            UnloadSoundEntry(entry);
            _eventBus.Publish(new EngineLogEvent("Audio", "Info", $"Sound invalidated for reload: {_workspace.ToRelativePath(resolved)}"));
        }
    }

    public void StopAll()
    {
        if (Raylib.IsAudioDeviceReady())
        {
            foreach (SoundBankEntry entry in _soundCache.Values)
            {
                if (Raylib.IsSoundValid(entry.BaseSound))
                {
                    Raylib.StopSound(entry.BaseSound);
                }

                for (int i = 0; i < entry.AliasCount; i++)
                {
                    if (Raylib.IsSoundValid(entry.Aliases[i]))
                    {
                        Raylib.StopSound(entry.Aliases[i]);
                    }
                }
            }
        }

        for (int i = _streams.Count - 1; i >= 0; i--)
        {
            UnloadStreamNative(_streams[i]);
        }

        _streams.Clear();
    }

    private float GetEffectiveBusMultiplier(AudioBus bus)
    {
        float master = _busVolumes[(int)AudioBus.Master];
        if (bus == AudioBus.Master)
        {
            return master;
        }

        return master * _busVolumes[(int)bus];
    }

    private void ApplyVolumesToActiveStreams()
    {
        if (!Raylib.IsAudioDeviceReady())
        {
            return;
        }

        for (int i = 0; i < _streams.Count; i++)
        {
            ApplyVolume(_streams[i]);
        }
    }

    private void ApplyVolume(StreamChannel channel)
    {
        if (Raylib.IsAudioDeviceReady() && channel.HasNativeMusic && Raylib.IsMusicValid(channel.Music))
        {
            Raylib.SetMusicVolume(
                channel.Music,
                Math.Clamp(channel.CurrentVolume * GetEffectiveBusMultiplier(channel.Bus), 0f, 4f));
        }
    }

    private int FindStreamIndex(string channelId)
    {
        for (int i = 0; i < _streams.Count; i++)
        {
            if (string.Equals(_streams[i].ChannelId, channelId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void StartNewStream(string channelId, string resolvedPath, AudioBus bus, float targetVolume, float fadeInSec)
    {
        Music music = default;
        bool hasNative = false;

        if (Raylib.IsAudioDeviceReady())
        {
            if (!File.Exists(resolvedPath))
            {
                if (_missingLogged.Add(resolvedPath))
                {
                    _eventBus.Publish(new EngineLogEvent("Audio", "Warning", $"Audio stream not found: {resolvedPath}"));
                }
            }
            else
            {
                music = Raylib.LoadMusicStream(resolvedPath);
                if (Raylib.IsMusicValid(music))
                {
                    music.Looping = true;
                    hasNative = true;
                }
                else if (_missingLogged.Add(resolvedPath))
                {
                    _eventBus.Publish(new EngineLogEvent("Audio", "Warning", $"Failed to load audio stream: {resolvedPath}"));
                }
            }
        }

        float startVol = fadeInSec > 0f ? 0f : targetVolume;
        var channel = new StreamChannel
        {
            ChannelId = channelId,
            ResolvedPath = resolvedPath,
            Bus = bus,
            Music = music,
            HasNativeMusic = hasNative,
            CurrentVolume = startVol,
            FadeStartVolume = startVol,
            FadeTargetVolume = targetVolume,
            FadeDuration = fadeInSec,
            FadeElapsed = 0f,
            StopWhenFadeCompletes = false
        };

        _streams.Add(channel);

        if (hasNative && Raylib.IsAudioDeviceReady())
        {
            Raylib.SetMusicVolume(music, Math.Clamp(startVol * GetEffectiveBusMultiplier(bus), 0f, 4f));
            Raylib.PlayMusicStream(music);
        }
    }

    private static void UnloadStreamNative(StreamChannel channel)
    {
        if (channel.HasNativeMusic && Raylib.IsAudioDeviceReady() && Raylib.IsMusicValid(channel.Music))
        {
            Raylib.StopMusicStream(channel.Music);
            Raylib.UnloadMusicStream(channel.Music);
        }

        channel.HasNativeMusic = false;
        channel.Music = default;
    }

    private bool TryGetOrLoadSoundEntry(string path, out SoundBankEntry entry)
    {
        string resolved = _workspace.ResolvePath(path);
        if (_soundCache.TryGetValue(resolved, out entry!) && Raylib.IsSoundValid(entry.BaseSound))
        {
            return true;
        }

        if (!File.Exists(resolved))
        {
            if (_missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Audio", "Warning", $"Audio file not found: {resolved}"));
            }

            entry = null!;
            return false;
        }

        Sound baseSound = Raylib.LoadSound(resolved);
        if (!Raylib.IsSoundValid(baseSound))
        {
            if (_missingLogged.Add(resolved))
            {
                _eventBus.Publish(new EngineLogEvent("Audio", "Warning", $"Failed to load audio: {resolved}"));
            }

            entry = null!;
            return false;
        }

        _missingLogged.Remove(resolved);
        entry = new SoundBankEntry { BaseSound = baseSound };
        _soundCache[resolved] = entry;
        return true;
    }

    private static void PlaySoundInstance(SoundBankEntry entry, float volume, float pitch)
    {
        if (!Raylib.IsSoundPlaying(entry.BaseSound))
        {
            Raylib.SetSoundVolume(entry.BaseSound, volume);
            Raylib.SetSoundPitch(entry.BaseSound, pitch);
            Raylib.PlaySound(entry.BaseSound);
            return;
        }

        for (int i = 0; i < entry.AliasCount; i++)
        {
            Sound existingAlias = entry.Aliases[i];
            if (Raylib.IsSoundValid(existingAlias) && !Raylib.IsSoundPlaying(existingAlias))
            {
                Raylib.SetSoundVolume(existingAlias, volume);
                Raylib.SetSoundPitch(existingAlias, pitch);
                Raylib.PlaySound(existingAlias);
                return;
            }
        }

        Sound target;
        if (entry.AliasCount < MaxAliasesPerSound)
        {
            target = Raylib.LoadSoundAlias(entry.BaseSound);
            if (!Raylib.IsSoundValid(target))
            {
                target = entry.BaseSound;
            }
            else
            {
                entry.Aliases[entry.AliasCount++] = target;
            }
        }
        else
        {
            int slot = entry.NextAliasSlot % MaxAliasesPerSound;
            entry.NextAliasSlot = (slot + 1) % MaxAliasesPerSound;
            target = Raylib.IsSoundValid(entry.Aliases[slot]) ? entry.Aliases[slot] : entry.BaseSound;
        }

        Raylib.SetSoundVolume(target, volume);
        Raylib.SetSoundPitch(target, pitch);
        Raylib.PlaySound(target);
    }

    private static void UnloadSoundEntry(SoundBankEntry entry)
    {
        if (!Raylib.IsAudioDeviceReady())
        {
            return;
        }

        for (int i = 0; i < entry.AliasCount; i++)
        {
            if (Raylib.IsSoundValid(entry.Aliases[i]))
            {
                Raylib.StopSound(entry.Aliases[i]);
                Raylib.UnloadSoundAlias(entry.Aliases[i]);
            }
        }

        if (Raylib.IsSoundValid(entry.BaseSound))
        {
            Raylib.StopSound(entry.BaseSound);
            Raylib.UnloadSound(entry.BaseSound);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAll();

        foreach (SoundBankEntry entry in _soundCache.Values)
        {
            UnloadSoundEntry(entry);
        }

        _soundCache.Clear();
        _missingLogged.Clear();
    }
}
