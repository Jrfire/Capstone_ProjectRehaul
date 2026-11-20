using UnityEngine;

namespace Capstone.Audio
{
    /// <summary>
    /// Map ambience (put one in every map scene): every <see cref="interval"/> (random) seconds one clip from a random
    /// non-empty <see cref="sets"/> entry (crickets, wind, owl hoots, wolf howls) plays as a 3D sound at a random spot
    /// <see cref="distance"/> metres around the active AudioListener, so it sounds far away but stays audible. Local on
    /// every peer: everyone hears ambience, each from their own random directions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientSoundscape : MonoBehaviour
    {
        [SerializeField, Tooltip("Groups of ambience clips; each sound picks a random group, then a random clip in it.")]
        private AudioClipSet[] sets = new AudioClipSet[0];
        [SerializeField, Tooltip("Random seconds (min, max) between ambience sounds.")]
        private Vector2 interval = new Vector2(8f, 20f);
        [SerializeField, Tooltip("Random seconds (min, max) before the first sound.")]
        private Vector2 firstDelay = new Vector2(3f, 8f);
        [SerializeField, Tooltip("Random horizontal distance (min, max) from the listener.")]
        private Vector2 distance = new Vector2(25f, 45f);
        [SerializeField, Tooltip("Random height (min, max) above the listener.")]
        private Vector2 height = new Vector2(2f, 10f);
        [SerializeField, Tooltip("3D fade: full volume within this distance of the sound...")]
        private float minDistance = 20f;
        [SerializeField, Tooltip("...silent beyond this distance (keep it above Distance max so far sounds stay audible).")]
        private float maxDistance = 90f;

        private float nextAt = -1f;
        private AudioListener listener;

        public AudioClipSet[] Sets => sets;
        public Vector2 Distance => distance;
        public int Played { get; private set; }
        public Vector3 LastPosition { get; private set; }

        private void OnEnable() => nextAt = Time.time + Random.Range(Mathf.Min(firstDelay.x, firstDelay.y), Mathf.Max(firstDelay.x, firstDelay.y));

        /// <summary>Plays one ambience sound now around the listener (false when there is no listener or no clip).</summary>
        public bool PlayNow()
        {
            ScheduleNext();
            Transform around = FindListener();
            AudioClipSet set = PickSet();
            if (around == null || set == null)
                return false;
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(Mathf.Min(distance.x, distance.y), Mathf.Max(distance.x, distance.y));
            Vector3 position = around.position + new Vector3(Mathf.Cos(angle) * radius,
                Random.Range(Mathf.Min(height.x, height.y), Mathf.Max(height.x, height.y)), Mathf.Sin(angle) * radius);
            LastPosition = position;
            Played++;
            AudioManager.PlayAt(set, position, AudioChannel.Sfx, minDistance, maxDistance);
            return true;
        }

        private void Update()
        {
            if (nextAt >= 0f && Time.time >= nextAt)
                PlayNow();
        }

        private void ScheduleNext()
        {
            float min = Mathf.Max(0.5f, Mathf.Min(interval.x, interval.y));
            nextAt = Time.time + Random.Range(min, Mathf.Max(min, interval.x, interval.y));
        }

        private AudioClipSet PickSet()
        {
            int count = 0;
            foreach (AudioClipSet set in sets)
            {
                if (set != null && set.HasClips)
                    count++;
            }
            if (count == 0)
                return null;
            int pick = Random.Range(0, count);
            foreach (AudioClipSet set in sets)
            {
                if (set == null || !set.HasClips)
                    continue;
                if (pick-- == 0)
                    return set;
            }
            return null;
        }

        private Transform FindListener()
        {
            if (listener == null || !listener.isActiveAndEnabled)
            {
                listener = null;
                foreach (AudioListener candidate in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                {
                    if (candidate.isActiveAndEnabled)
                    {
                        listener = candidate;
                        break;
                    }
                }
            }
            return listener != null ? listener.transform : null;
        }
    }
}
