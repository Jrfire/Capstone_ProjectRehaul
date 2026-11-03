using Mirror;
using UnityEngine;

/// <summary>
/// The still front-view image a creature shows on the victim's death screen. Put it on every spawnable enemy prefab
/// (next to its NetworkIdentity); <see cref="PlayerHealth"/> records the killer's asset id and the victim's client
/// looks the image up here. Sound effects can be added to this component later.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyJumpscare : MonoBehaviour
{
    [SerializeField, Tooltip("Front view of the creature that zooms into the screen when it kills the local player.")]
    private Sprite frontView;
    [SerializeField, Min(0.1f), Tooltip("Final size of the image as a multiple of the screen height (above 1 overflows the screen); higher values zoom closer to the Focus Point.")]
    private float finalScreenHeight = 1.15f;
    [SerializeField, Tooltip("Point on the sprite (0-1, from the bottom-left) kept at the screen centre; the zoom closes in on it. Click the Inspector preview to set it.")]
    private Vector2 focusPoint = new Vector2(0.5f, 0.5f);

    public Sprite FrontView => frontView;
    public float FinalScreenHeight => finalScreenHeight;
    public Vector2 FocusPoint => new Vector2(Mathf.Clamp01(focusPoint.x), Mathf.Clamp01(focusPoint.y));

    /// <summary>Finds the jumpscare of the registered prefab (or a spawned instance) with this Mirror asset id.</summary>
    public static EnemyJumpscare Find(uint assetId)
    {
        if (assetId == 0)
            return null;
        if (NetworkClient.prefabs.TryGetValue(assetId, out GameObject prefab) && prefab != null &&
            prefab.TryGetComponent(out EnemyJumpscare registered))
            return registered;
        foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
        {
            if (identity != null && identity.assetId == assetId && identity.TryGetComponent(out EnemyJumpscare spawned))
                return spawned;
        }
        return null;
    }
}
