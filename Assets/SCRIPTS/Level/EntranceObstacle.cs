using Mirror;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public enum ObstacleRequirement
{
    Key,
    Destruction,
    KeyAndDestruction
}

/// <summary>
/// Blocks a street block entrance until its requirement is met. Key and destruction tools call
/// <see cref="TryUseKey"/> and <see cref="TryDestroy"/>; with KeyAndDestruction both are needed, in any order.
/// Networked gameplay must call these methods on the server; offline use is also supported.
/// </summary>
[DisallowMultipleComponent]
public sealed class EntranceObstacle : MonoBehaviour
{
    [SerializeField] private ObstacleRequirement requirement = ObstacleRequirement.Key;
    [SerializeField, Tooltip("Deactivate this obstacle once its requirement is met.")]
    private bool deactivateWhenCleared = true;
    [SerializeField, Tooltip("Optional opened/broken prefab. Replaces the closed visuals instead of deactivating. Use the same local origin, orientation and scale; do not include EntranceObstacle or NetworkIdentity.")]
    private GameObject clearedPrefab;
    [Tooltip("Invoked once when the requirement is met.")]
    public UnityEvent cleared = new UnityEvent();
    [Header("Sounds")]
    [SerializeField, Tooltip("Played where the obstacle stands when it is opened with a key (gates).")]
    private Capstone.Audio.AudioClipSet openedSound = new Capstone.Audio.AudioClipSet();
    [SerializeField, Tooltip("Played where the obstacle stands when it is destroyed (barricades). Used for KeyAndDestruction too.")]
    private Capstone.Audio.AudioClipSet destroyedSound = new Capstone.Audio.AudioClipSet();
    [SerializeField, Min(0.1f)] private float soundMinDistance = 2f;
    [SerializeField, Min(1f)] private float soundMaxDistance = 35f;

    public Capstone.Audio.AudioClipSet OpenedSound => openedSound;
    public Capstone.Audio.AudioClipSet DestroyedSound => destroyedSound;
    /// <summary>The set chosen for this obstacle's clearing: destruction for barricades, otherwise the opening sound.</summary>
    public Capstone.Audio.AudioClipSet ClearSound => RequiresDestruction ? destroyedSound : openedSound;

    public ObstacleRequirement Requirement => requirement;
    public bool RequiresKey => requirement != ObstacleRequirement.Destruction;
    public bool RequiresDestruction => requirement != ObstacleRequirement.Key;
    public bool KeyUsed { get; private set; }
    public bool Destroyed { get; private set; }
    public bool IsCleared => (!RequiresKey || KeyUsed) && (!RequiresDestruction || Destroyed);
    public GameObject ClearedPrefab => clearedPrefab;
    public GameObject ClearedInstance { get; private set; }

    private bool clearApplied;

    /// <summary>Applies a key. Returns false if this obstacle does not need one or already received it.</summary>
    public bool TryUseKey()
    {
        if (!CanChangeState())
            return false;
        if (!RequiresKey || KeyUsed || IsCleared)
            return false;
        if (!ValidateClearedPrefab())
            return false;

        KeyUsed = true;
        CheckCleared();
        EntranceObstacleNetwork.Broadcast(this);
        return true;
    }

    /// <summary>Applies destruction (e.g. an axe). Returns false if this obstacle cannot be destroyed or already was.</summary>
    public bool TryDestroy()
    {
        if (!CanChangeState())
            return false;
        if (!RequiresDestruction || Destroyed || IsCleared)
            return false;
        if (!ValidateClearedPrefab())
            return false;

        Destroyed = true;
        CheckCleared();
        EntranceObstacleNetwork.Broadcast(this);
        return true;
    }

    private bool CanChangeState()
    {
        if (!NetworkClient.active || NetworkServer.active)
            return true;
        Debug.LogWarning("Entrance obstacles can only be opened by server gameplay in a networked session.", this);
        return false;
    }

    private bool ValidateClearedPrefab()
    {
        if (clearedPrefab == null ||
            (clearedPrefab.GetComponentInChildren<EntranceObstacle>(true) == null &&
             clearedPrefab.GetComponentInChildren<NetworkIdentity>(true) == null))
            return true;
        Debug.LogError("Cleared Prefab must not contain EntranceObstacle or NetworkIdentity components.", this);
        return false;
    }

    internal void ApplyNetworkState(bool keyUsed, bool destroyed)
    {
        KeyUsed |= keyUsed;
        Destroyed |= destroyed;
        CheckCleared();
    }

    private void CheckCleared()
    {
        if (!IsCleared || clearApplied || !ValidateClearedPrefab())
            return;

        clearApplied = true;
        if (Application.isPlaying)
            Capstone.Audio.AudioManager.PlayAt(ClearSound, transform.position + Vector3.up, Capstone.Audio.AudioChannel.Sfx,
                soundMinDistance, soundMaxDistance);
        // The NavMesh "fills in" once the entrance opens, whether or not the obstacle object stays visible.
        foreach (NavMeshObstacle carver in GetComponentsInChildren<NavMeshObstacle>(true))
            carver.enabled = false;
        foreach (Collider solid in GetComponentsInChildren<Collider>(true))
            solid.enabled = false;
        if (clearedPrefab != null)
        {
            foreach (Renderer visual in GetComponentsInChildren<Renderer>(true))
                visual.enabled = false;
            ClearedInstance = Instantiate(clearedPrefab, transform, false);
            ClearedInstance.transform.localPosition = Vector3.zero;
            ClearedInstance.transform.localRotation = Quaternion.identity;
            ClearedInstance.transform.localScale = Vector3.one;
            MapManager map = GetComponentInParent<MapManager>();
            if (Application.isPlaying && map != null && map.HasNavMesh)
                map.AddClearedObstacleCarvers(ClearedInstance.transform);
        }
        else if (deactivateWhenCleared)
            gameObject.SetActive(false);
        cleared.Invoke();
    }
}
