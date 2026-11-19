using System;
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
    [SerializeField, Tooltip("Gate identity such as G1. MapManager names kept key gates G1, G2... (Gate G1).")]
    private string gateId;
    [SerializeField, Tooltip("Key code this gate needs, e.g. G1 for Key G1 (a key item whose Key Id matches). Empty = any key.")]
    private string requiredKeyId;
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
    public string GateId => gateId;
    public string RequiredKeyId => requiredKeyId;
    public bool RequiresKey => requirement != ObstacleRequirement.Destruction;
    public bool RequiresDestruction => requirement != ObstacleRequirement.Key;
    public bool KeyUsed { get; private set; }
    public bool Destroyed { get; private set; }
    public bool IsCleared => (!RequiresKey || KeyUsed) && (!RequiresDestruction || Destroyed);
    public GameObject ClearedPrefab => clearedPrefab;
    public GameObject ClearedInstance { get; private set; }

    private bool clearApplied;

    /// <summary>
    /// Names this gate (e.g. "G1") and, unless <paramref name="keyCode"/> says otherwise, requires the key with the same
    /// code ("Key G1"). Call identically on every peer (MapManager does this for its seeded layout).
    /// </summary>
    public void SetGateIdentity(string id, string keyCode = null)
    {
        gateId = id;
        requiredKeyId = keyCode ?? id;
    }

    public void SetRequiredKey(string keyCode) => requiredKeyId = keyCode;

    /// <summary>Display name of the gate, e.g. "Gate G1" (empty without an id).</summary>
    public string GateName => string.IsNullOrEmpty(gateId) ? string.Empty : $"Gate {gateId}";

    /// <summary>Nametag for an uncleared obstacle: "Blocked" for barricades, else "Locked. Requires Key G1" (or "Locked").</summary>
    public string Label =>
        IsCleared ? string.Empty
        : !RequiresKey || KeyUsed ? "Blocked"
        : string.IsNullOrEmpty(requiredKeyId) ? "Locked"
        : $"Locked. Requires Key {requiredKeyId}";

    /// <summary>Applies a key. Returns false if this obstacle does not need one or already received it.</summary>
    public bool TryUseKey()
    {
        return TryUseKey(string.Empty);
    }

    /// <summary>Applies a matching key. An empty key id is accepted by legacy gates.</summary>
    public bool TryUseKey(string keyId)
    {
        if (!CanChangeState())
            return false;
        if (!RequiresKey || KeyUsed || IsCleared)
            return false;
        if (!string.IsNullOrEmpty(requiredKeyId) &&
            (string.IsNullOrEmpty(keyId) || !string.Equals(requiredKeyId, keyId, StringComparison.OrdinalIgnoreCase)))
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
