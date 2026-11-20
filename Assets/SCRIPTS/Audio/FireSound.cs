using UnityEngine;

namespace Capstone.Audio
{
    /// <summary>
    /// Looping 3D crackle for anything burning (torches, barrels, campfires). Add it to the fire object and drag a looping
    /// clip into <see cref="clip"/>; it plays through the SFX channel on every peer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireSound : MonoBehaviour
    {
        [SerializeField, Tooltip("Looping fire clip (empty = silent).")]
        private AudioClip clip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float maxDistance = 15f;
        [SerializeField, Tooltip("Start each loop at a random point so several fires don't play in unison.")]
        private bool randomStart = true;

        private AudioSource source;
        private AudioEchoFilter echo;
        private float volumeMultiplier = 1f;
        private float echoAmount;

        public AudioSource Source => source;
        public AudioClip Clip => clip;
        public float BaseVolume => volume;

        /// <summary>Scales the loop's volume (e.g. a fire burning out, the Santelmo pocket swelling); 1 = as authored.</summary>
        public float VolumeMultiplier
        {
            get => volumeMultiplier;
            set
            {
                volumeMultiplier = Mathf.Max(0f, value);
                if (source != null)
                    source.volume = Mathf.Clamp01(volume * volumeMultiplier);
            }
        }

        /// <summary>0..1 echo mixed into the loop (an AudioEchoFilter is added on first use).</summary>
        public float EchoAmount
        {
            get => echoAmount;
            set
            {
                echoAmount = Mathf.Clamp01(value);
                if (echoAmount > 0f && echo == null)
                {
                    echo = gameObject.AddComponent<AudioEchoFilter>();
                    echo.delay = 280f;
                    echo.decayRatio = 0.45f;
                    echo.dryMix = 1f;
                }
                if (echo != null)
                    echo.wetMix = echoAmount * 0.8f;
            }
        }

        /// <summary>Runtime setup (tests and code-created fires).</summary>
        public void Configure(AudioClip loop, float loopVolume, float min, float max)
        {
            clip = loop;
            volume = Mathf.Clamp01(loopVolume);
            minDistance = min;
            maxDistance = max;
            if (isActiveAndEnabled)
            {
                OnDisable();
                OnEnable();
            }
        }

        private void OnEnable()
        {
            if (clip == null)
                return;
            if (source == null)
                source = gameObject.AddComponent<AudioSource>();
            AudioManager.Configure(source, AudioChannel.Sfx, 1f, minDistance, maxDistance);
            source.clip = clip;
            source.loop = true;
            source.volume = Mathf.Clamp01(volume * volumeMultiplier);
            if (randomStart)
                source.time = Random.Range(0f, clip.length);
            source.Play();
        }

        private void OnDisable()
        {
            if (source != null)
                source.Stop();
        }
    }
}
