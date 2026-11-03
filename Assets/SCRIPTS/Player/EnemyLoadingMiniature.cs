using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Animated miniature of a creature shown at the bottom of the travel loading screen when the destination contains it.
/// Put it on the enemy's networked prefab (next to its NetworkIdentity); clients look it up by the prefab's asset id.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyLoadingMiniature : MonoBehaviour
{
    [SerializeField, Tooltip("Looping frames in playback order (e.g. the sliced sub-sprites of the miniature sheet).")]
    private Sprite[] frames = new Sprite[0];
    [SerializeField, Min(0.1f), Tooltip("Playback speed of the frames.")]
    private float framesPerSecond = 8f;
    [SerializeField, Min(1f), Tooltip("Height of the figure on the loading screen in reference pixels (1920x1080); width follows the sprite's aspect.")]
    private float displayHeight = 140f;

    public IReadOnlyList<Sprite> Frames => frames;
    public float FramesPerSecond => framesPerSecond;
    public float DisplayHeight => displayHeight;
    public bool IsReady => frames != null && frames.Length > 0 && frames[0] != null;

    public Sprite FrameAt(float seconds)
    {
        if (!IsReady)
            return null;
        int index = Mathf.FloorToInt(Mathf.Max(0f, seconds) * framesPerSecond) % frames.Length;
        return frames[index] != null ? frames[index] : frames[0];
    }

    /// <summary>Finds the miniature of the registered prefab (or a spawned instance) with this Mirror asset id.</summary>
    public static EnemyLoadingMiniature Find(uint assetId)
    {
        if (assetId == 0)
            return null;
        if (NetworkClient.prefabs.TryGetValue(assetId, out GameObject prefab) && prefab != null &&
            prefab.TryGetComponent(out EnemyLoadingMiniature registered))
            return registered;
        foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
        {
            if (identity != null && identity.assetId == assetId && identity.TryGetComponent(out EnemyLoadingMiniature spawned))
                return spawned;
        }
        return null;
    }
}
