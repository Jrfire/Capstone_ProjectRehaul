using UnityEngine;

namespace Capstone.Audio
{
    /// <summary>
    /// Plays a clip from <see cref="sounds"/> as a 3D sound from this object every <see cref="interval"/> (random min..max)
    /// seconds while enabled, e.g. the Santelmo's voice. Local on every peer. <see cref="IntervalMultiplier"/> lets code
    /// shorten the wait (the Santelmo pocket), <see cref="VolumeMultiplier"/> scales the volume.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntervalSound : MonoBehaviour
    {
        [SerializeField] private AudioClipSet sounds = new AudioClipSet();
        [SerializeField, Tooltip("Random seconds (min, max) between sounds.")]
        private Vector2 interval = new Vector2(12f, 30f);
        [SerializeField, Tooltip("Random seconds (min, max) before the first sound.")]
        private Vector2 firstDelay = new Vector2(2f, 8f);
        [SerializeField] private float minDistance = 2f;
        [SerializeField] private float maxDistance = 30f;
        [SerializeField] private AudioChannel channel = AudioChannel.Sfx;

        private float nextAt = -1f;

        public AudioClipSet Sounds => sounds;
        public Vector2 Interval { get => interval; set => interval = value; }
        public float IntervalMultiplier { get; set; } = 1f;
        public float VolumeMultiplier { get; set; } = 1f;
        public float MaxDistance { get => maxDistance; set => maxDistance = Mathf.Max(0.1f, value); }
        public int Played { get; private set; }

        private void OnEnable() => nextAt = Time.time + Random.Range(Mathf.Min(firstDelay.x, firstDelay.y), Mathf.Max(firstDelay.x, firstDelay.y));

        /// <summary>Plays one sound now and restarts the wait.</summary>
        public void PlayNow()
        {
            Played++;
            AudioSource played = AudioManager.PlayAt(sounds, transform.position, channel, minDistance, maxDistance);
            if (played != null)
                played.volume = Mathf.Clamp01(played.volume * VolumeMultiplier);
            Schedule();
        }

        /// <summary>Restarts the wait from now with the current multiplier.</summary>
        public void Schedule()
        {
            float min = Mathf.Max(0.1f, Mathf.Min(interval.x, interval.y));
            float max = Mathf.Max(min, interval.x, interval.y);
            nextAt = Time.time + Random.Range(min, max) * Mathf.Max(0.05f, IntervalMultiplier);
        }

        private void Update()
        {
            if (nextAt >= 0f && Time.time >= nextAt)
                PlayNow();
        }
    }
}
