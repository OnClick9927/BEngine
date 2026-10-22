using BEngine.Audio;
using BEngine.Video;

namespace BEngine;

internal static class TimelineMediaAdapters
{
    internal static ITimelineMediaPlayable? Create(Component component, BAsset asset)
    {
        if (component is ITimelineMediaPlayable custom && custom.CanPlayTimelineAsset(asset)) return custom;
        if (component is AudioSource audio && asset is AudioClip) return new AudioPlayback(audio);
        if (component is VideoPlayer video && asset is VideoClip) return new VideoPlayback(video);
        foreach (var type in RuntimeTypeCache.GetTypesDerivedFrom<ITimelineMediaAdapter>()
                     .Where(type => !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters && type.GetConstructor(Type.EmptyTypes) is not null))
            if (Activator.CreateInstance(type) is ITimelineMediaAdapter adapter && adapter.CanAdapt(component, asset))
                return adapter.CreatePlayback(component);
        return null;
    }

    private static GameObject CreatePlaybackObject(Component target)
    {
        var owner = target.gameObject.scene?.CreateGameObject("Timeline Media Playback") ?? new GameObject("Timeline Media Playback");
        owner.hideFlags = HideFlags.HideAndDontSave;
        return owner;
    }

    // Keep the bound component intact, including delayed playback, fades and one-shot voices.
    // Only the temporary player is driven by the Timeline clock.
    private sealed class AudioPlayback(AudioSource source) : ITimelineMediaPlayable
    {
        private GameObject? _owner;
        private AudioSource? _player;
        private bool _resume;
        public bool CanPlayTimelineAsset(BAsset asset) => asset is AudioClip { isDestroyed: false };

        public void EvaluateTimeline(BAsset asset, Fix64 localTime, bool playing)
        {
            if (source.isDestroyed || asset is not AudioClip clip) return;
            if (_player is null)
            {
                _resume = source.isPlaying;
                source.Pause();
                source.destroyed += OnSourceDestroyed;
                _owner = CreatePlaybackObject(source);
                _player = _owner.AddComponent<AudioSource>();
                _player.hideFlags = HideFlags.HideAndDontSave;
                _player.playOnAwake = false;
            }
            _player.clip = clip;
            _player.volume = source.volume;
            _player.panStereo = source.panStereo;
            _player.pitch = source.pitch;
            _player.mute = source.mute;
            _player.priority = source.priority;
            _player.outputAudioMixerGroup = source.outputAudioMixerGroup;
            if (playing && localTime < clip.length)
            {
                if (!_player.isPlaying) { _player.UnPause(); if (!_player.isPlaying) _player.Play(); }
            }
            else _player.Pause();
            _player.time = Mathf.Clamp(localTime, 0, clip.length);
        }

        public void StopTimeline()
        {
            if (_owner is null) return;
            source.destroyed -= OnSourceDestroyed;
            BObject.DestroyImmediate(_owner);
            _owner = null;
            _player = null;
            if (_resume && !source.isDestroyed && source.enabled && source.gameObject.activeInHierarchy) source.UnPause();
        }

        private void OnSourceDestroyed(BObject _) => StopTimeline();
    }

    private sealed class VideoPlayback(VideoPlayer source) : ITimelineMediaPlayable
    {
        private GameObject? _owner;
        private VideoPlayer? _player;
        private SpriteRenderer? _renderer;
        private Sprite? _originalSprite;
        private bool _ownsRenderer;
        private bool _resume;
        public bool CanPlayTimelineAsset(BAsset asset) => asset is VideoClip { isDestroyed: false };

        public void EvaluateTimeline(BAsset asset, Fix64 localTime, bool playing)
        {
            if (source.isDestroyed || asset is not VideoClip clip) return;
            if (_player is null)
            {
                _resume = source.isPlaying;
                source.Pause();
                source.destroyed += OnSourceDestroyed;
                _renderer = source.targetRenderer ?? source.GetComponent<SpriteRenderer>();
                if (_renderer is null)
                {
                    _renderer = source.gameObject.AddComponent<SpriteRenderer>();
                    _renderer.hideFlags = HideFlags.HideAndDontSave;
                    _ownsRenderer = true;
                }
                _originalSprite = _renderer.sprite;
                _owner = CreatePlaybackObject(source);
                _player = _owner.AddComponent<VideoPlayer>();
                _player.hideFlags = HideFlags.HideAndDontSave;
                _player.playOnAwake = false;
                _player.enabled = false; // Timeline evaluates frames; the component's Update must not advance them again.
                _player.targetRenderer = _renderer;
            }
            _player.clip = clip;
            _player.volume = source.volume;
            _player.mute = source.mute;
            _player.playbackSpeed = source.playbackSpeed;
            _player.Prepare();
            if (playing && localTime < clip.length) _player.Play();
            else _player.Pause();
            _player.time = Mathf.Clamp(localTime, 0, clip.length);
        }

        public void StopTimeline()
        {
            if (_owner is null) return;
            source.destroyed -= OnSourceDestroyed;
            BObject.DestroyImmediate(_owner);
            _owner = null;
            _player = null;
            if (_renderer is { isDestroyed: false })
            {
                if (_ownsRenderer) BObject.DestroyImmediate(_renderer);
                else _renderer.sprite = _originalSprite is { isDestroyed: false } ? _originalSprite : null;
            }
            if (_resume && !source.isDestroyed && source.enabled && source.gameObject.activeInHierarchy) source.Play();
        }

        private void OnSourceDestroyed(BObject _) => StopTimeline();
    }
}
