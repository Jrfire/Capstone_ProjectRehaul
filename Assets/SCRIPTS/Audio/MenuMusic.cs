using UnityEngine;
using UnityEngine.Audio;

namespace Capstone.Audio
{
    /// <summary>
    /// Main menu music (the menu has no AudioManager): loops <see cref="theme"/> through the mixer's Music group and
    /// applies the saved channel volumes to <see cref="mixer"/> so the Settings sliders take effect at once. Fades in on
    /// start; the track stops when the menu scene unloads.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuMusic : MonoBehaviour
    {
        [SerializeField] private AudioClip theme;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [SerializeField, Min(0f)] private float fadeInSeconds = 2f;
        [SerializeField] private AudioMixer mixer;
        [SerializeField, Tooltip("The mixer's Music group.")]
        private AudioMixerGroup musicGroup;

        private AudioSource source;

        public AudioSource Source => source;
        public AudioClip Theme => theme;
        public AudioMixerGroup MusicGroup => musicGroup;

        private void Start()
        {
            if (mixer != null)
            {
                foreach (AudioChannel channel in System.Enum.GetValues(typeof(AudioChannel)))
                    mixer.SetFloat(AudioManager.VolumeParameter(channel),
                        AudioManager.LinearToDecibels(PlayerPrefs.GetFloat(AudioManager.PrefsKey(channel), 1f)));
            }
            if (theme == null)
                return;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = true;
            source.clip = theme;
            source.outputAudioMixerGroup = musicGroup;
            source.volume = fadeInSeconds > 0f ? 0f : volume;
            source.Play();
        }

        private void Update()
        {
            if (source != null && source.volume < volume)
                source.volume = Mathf.MoveTowards(source.volume, volume, fadeInSeconds > 0f ? Time.unscaledDeltaTime * volume / fadeInSeconds : volume);
        }
    }
}
