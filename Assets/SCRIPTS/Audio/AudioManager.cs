using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Capstone.Audio
{
    /// <summary>
    /// Project-wide audio (persistent, one per game: on the NetworkManager object). Routes sounds to the
    /// <see cref="mixer"/>'s Master > SFX / UI / Music / Jumpscare groups, stores channel volumes (0..1, saved in
    /// PlayerPrefs) in the mixer's exposed parameters and plays music: owners request a track with a priority (e.g. arrival
    /// OST 0, chase theme 10) and the highest request plays, cross-fading when it changes. Every static helper is safe
    /// without an instance (sounds then play unrouted), so gameplay code never needs null checks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        public const float MinDecibels = -80f;

        [SerializeField] private AudioMixer mixer;
        [SerializeField] private AudioMixerGroup masterGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup uiGroup;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup jumpscareGroup;
        [SerializeField, Tooltip("Seconds to cross-fade between music tracks.")]
        private float musicFadeSeconds = 1.5f;
        [SerializeField, Range(0f, 1f)] private float defaultVolume = 1f;

        private static AudioManager instance;
        private readonly List<MusicRequest> musicRequests = new List<MusicRequest>();
        private AudioSource[] musicSources;
        private int activeMusic;
        private AudioClip currentTrack;

        private sealed class MusicRequest
        {
            public object owner;
            public AudioClip clip;
            public int priority;
            public float volume;
            public bool loop;
            public int order;
            public float endTime = -1f;
        }

        private int requestCounter;

        public static AudioManager Instance => instance;
        public AudioMixer Mixer => mixer;
        /// <summary>The music clip currently playing (or fading in); null for silence.</summary>
        public AudioClip CurrentTrack => currentTrack;

        public static string VolumeParameter(AudioChannel channel) => channel + "Volume";
        public static string PrefsKey(AudioChannel channel) => "Capstone.Volume." + channel;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
            musicSources = new AudioSource[2];
            for (int index = 0; index < musicSources.Length; index++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;
                source.outputAudioMixerGroup = musicGroup;
                musicSources[index] = source;
            }
        }

        private void Start()
        {
            foreach (AudioChannel channel in System.Enum.GetValues(typeof(AudioChannel)))
                ApplyVolume(channel, GetVolume(channel));
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        public AudioMixerGroup GroupFor(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Sfx: return sfxGroup;
                case AudioChannel.UI: return uiGroup;
                case AudioChannel.Music: return musicGroup;
                case AudioChannel.Jumpscare: return jumpscareGroup;
                default: return masterGroup;
            }
        }

        /// <summary>The mixer group for a channel, or null without an AudioManager.</summary>
        public static AudioMixerGroup Group(AudioChannel channel) => instance != null ? instance.GroupFor(channel) : null;

        // ---------------------------------------------------------------- volume

        /// <summary>Saved linear volume (0..1) of a channel.</summary>
        public float GetVolume(AudioChannel channel) => PlayerPrefs.GetFloat(PrefsKey(channel), defaultVolume);

        /// <summary>Sets and saves a channel's linear volume (0..1). Master scales every channel.</summary>
        public void SetVolume(AudioChannel channel, float linear)
        {
            linear = Mathf.Clamp01(linear);
            PlayerPrefs.SetFloat(PrefsKey(channel), linear);
            ApplyVolume(channel, linear);
        }

        private void ApplyVolume(AudioChannel channel, float linear)
        {
            if (mixer != null)
                mixer.SetFloat(VolumeParameter(channel), LinearToDecibels(linear));
        }

        public static float LinearToDecibels(float linear) =>
            linear <= 0.0001f ? MinDecibels : Mathf.Max(MinDecibels, 20f * Mathf.Log10(linear));

        // ---------------------------------------------------------------- one-shots

        /// <summary>Plays a non-positional sound (UI clicks, sounds only the local player hears).</summary>
        public static void Play2D(AudioClip clip, AudioChannel channel = AudioChannel.Sfx, float volume = 1f, float pitch = 1f)
        {
            if (clip == null)
                return;
            if (instance != null && Mathf.Approximately(pitch, 1f))
            {
                instance.SourceFor(channel).PlayOneShot(clip, volume);
                return;
            }
            AudioSource temporary = CreateTemporary(clip, Vector3.zero, channel, volume, pitch, 0f, 1f, 500f);
            if (instance != null)
                temporary.transform.SetParent(instance.transform, false);
        }

        /// <summary>Plays a clip from a set without position.</summary>
        public static void Play2D(AudioClipSet set, AudioChannel channel = AudioChannel.Sfx)
        {
            if (set != null && set.HasClips)
                Play2D(set.Pick(), channel, set.volume, set.PickPitch());
        }

        /// <summary>Plays a 3D sound at a world position (fades between <paramref name="minDistance"/> and <paramref name="maxDistance"/>).</summary>
        public static AudioSource PlayAt(AudioClip clip, Vector3 position, AudioChannel channel = AudioChannel.Sfx,
            float volume = 1f, float pitch = 1f, float minDistance = 1f, float maxDistance = 25f)
        {
            return clip == null ? null : CreateTemporary(clip, position, channel, volume, pitch, 1f, minDistance, maxDistance);
        }

        public static AudioSource PlayAt(AudioClipSet set, Vector3 position, AudioChannel channel = AudioChannel.Sfx,
            float minDistance = 1f, float maxDistance = 25f)
        {
            return set != null && set.HasClips
                ? PlayAt(set.Pick(), position, channel, set.volume, set.PickPitch(), minDistance, maxDistance)
                : null;
        }

        private readonly Dictionary<AudioChannel, AudioSource> channelSources = new Dictionary<AudioChannel, AudioSource>();

        private AudioSource SourceFor(AudioChannel channel)
        {
            if (!channelSources.TryGetValue(channel, out AudioSource source) || source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.outputAudioMixerGroup = GroupFor(channel);
                channelSources[channel] = source;
            }
            return source;
        }

        private static AudioSource CreateTemporary(AudioClip clip, Vector3 position, AudioChannel channel, float volume,
            float pitch, float spatialBlend, float minDistance, float maxDistance)
        {
            var go = new GameObject($"Sound ({clip.name})");
            go.transform.position = position;
            AudioSource source = go.AddComponent<AudioSource>();
            Configure(source, channel, spatialBlend, minDistance, maxDistance);
            source.clip = clip;
            source.volume = volume;
            source.pitch = Mathf.Approximately(pitch, 0f) ? 1f : pitch;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.05f, Mathf.Abs(source.pitch)) + 0.1f);
            return source;
        }

        /// <summary>Routes and shapes an AudioSource: 3D (<paramref name="spatialBlend"/> 1) uses linear distance roll-off.</summary>
        public static void Configure(AudioSource source, AudioChannel channel, float spatialBlend = 1f,
            float minDistance = 1f, float maxDistance = 25f)
        {
            if (source == null)
                return;
            source.playOnAwake = false;
            source.outputAudioMixerGroup = Group(channel);
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = Mathf.Max(0.01f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.01f, maxDistance);
            source.dopplerLevel = 0f;
        }

        // ---------------------------------------------------------------- music

        /// <summary>
        /// Asks for <paramref name="clip"/> to play as music. The highest priority request plays (the newest wins ties);
        /// re-requesting with the same owner replaces its request. Release it with <see cref="ReleaseMusic"/>.
        /// </summary>
        public static void RequestMusic(object owner, AudioClip clip, int priority = 0, float volume = 1f, bool loop = true)
        {
            if (instance == null || owner == null)
                return;
            instance.musicRequests.RemoveAll(request => request.owner == owner);
            if (clip != null)
            {
                instance.musicRequests.Add(new MusicRequest
                {
                    owner = owner, clip = clip, priority = priority, volume = volume, loop = loop,
                    order = ++instance.requestCounter
                });
            }
        }

        public static void ReleaseMusic(object owner)
        {
            if (instance != null && owner != null)
                instance.musicRequests.RemoveAll(request => request.owner == owner);
        }

        public static bool IsMusicRequested(object owner) =>
            instance != null && instance.musicRequests.Exists(request => request.owner == owner);

        private MusicRequest Top()
        {
            MusicRequest best = null;
            foreach (MusicRequest request in musicRequests)
            {
                if (best == null || request.priority > best.priority ||
                    (request.priority == best.priority && request.order > best.order))
                    best = request;
            }
            return best;
        }

        private void Update()
        {
            if (musicSources == null)
                return;
            musicRequests.RemoveAll(request => request.owner == null || request.owner is Object unityObject && unityObject == null ||
                                               (!request.loop && request.endTime >= 0f && Time.unscaledTime >= request.endTime));
            MusicRequest top = Top();
            if (top != null && !top.loop && top.endTime < 0f)
                top.endTime = Time.unscaledTime + top.clip.length;
            AudioClip wanted = top != null ? top.clip : null;
            if (wanted != currentTrack)
            {
                currentTrack = wanted;
                if (wanted != null)
                {
                    activeMusic = 1 - activeMusic;
                    AudioSource next = musicSources[activeMusic];
                    next.outputAudioMixerGroup = musicGroup;
                    next.clip = wanted;
                    next.loop = top.loop;
                    next.volume = 0f;
                    next.Play();
                }
            }
            float step = musicFadeSeconds > 0f ? Time.unscaledDeltaTime / musicFadeSeconds : 1f;
            for (int index = 0; index < musicSources.Length; index++)
            {
                AudioSource source = musicSources[index];
                bool active = index == activeMusic && currentTrack != null && source.clip == currentTrack;
                float target = active ? top.volume : 0f;
                source.volume = Mathf.MoveTowards(source.volume, target, step);
                if (!active && source.isPlaying && source.volume <= 0f)
                    source.Stop();
            }
        }
    }
}
