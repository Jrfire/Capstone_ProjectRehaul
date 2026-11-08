using System;
using UnityEngine;

namespace Capstone.Audio
{
    /// <summary>Mixer channel a sound plays through. Master controls every other channel.</summary>
    public enum AudioChannel
    {
        Master,
        Sfx,
        UI,
        Music,
        Jumpscare
    }

    /// <summary>
    /// A list of interchangeable clips (e.g. single footsteps) with volume and random pitch. <see cref="Pick"/> avoids
    /// repeating the previous clip when there are several. Empty sets are silent, so clips can be dragged in later.
    /// </summary>
    [Serializable]
    public sealed class AudioClipSet
    {
        [Tooltip("Variations; one is picked at random each time (never the same twice in a row).")]
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random pitch range (1-1 = unchanged).")]
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

        [NonSerialized] private int lastIndex = -1;

        public bool HasClips
        {
            get
            {
                if (clips == null)
                    return false;
                foreach (AudioClip clip in clips)
                {
                    if (clip != null)
                        return true;
                }
                return false;
            }
        }

        public AudioClip Pick()
        {
            if (!HasClips)
                return null;
            int count = clips.Length;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int index = UnityEngine.Random.Range(0, count);
                if (clips[index] == null || (count > 1 && index == lastIndex && attempt < 7))
                    continue;
                lastIndex = index;
                return clips[index];
            }
            foreach (AudioClip clip in clips)
            {
                if (clip != null)
                    return clip;
            }
            return null;
        }

        public float PickPitch() =>
            UnityEngine.Random.Range(Mathf.Min(pitchRange.x, pitchRange.y), Mathf.Max(pitchRange.x, pitchRange.y));
    }
}
