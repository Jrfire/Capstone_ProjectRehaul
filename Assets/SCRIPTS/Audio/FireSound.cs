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

        public AudioSource Source => source;

        private void OnEnable()
        {
            if (clip == null)
                return;
            if (source == null)
                source = gameObject.AddComponent<AudioSource>();
            AudioManager.Configure(source, AudioChannel.Sfx, 1f, minDistance, maxDistance);
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
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
