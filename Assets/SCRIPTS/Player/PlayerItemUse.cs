using System;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Left mouse (Player/UseItem, shared by "use" and "attack") on NetworkPlayer: the owner uses the item in the selected
/// active slot according to its <see cref="ItemDefinition.UseKind"/>. Consumables are applied by the server (heal, then
/// removed if <see cref="ItemDefinition.ConsumeOnUse"/>). Weapons play the attack sound for everyone (PlayerAudio) and
/// the server swings along the owner's camera aim (<see cref="ServerAttack"/>): the first <see cref="IWeaponTarget"/>
/// (enemies: stunned) or, for weapons that break obstacles, a Destruction <see cref="EntranceObstacle"/> within reach is
/// hit; walls in front block the swing and other players are passed through. Empty slots and items without a use do nothing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerItemUse : NetworkBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField, Min(0f), Tooltip("Seconds between uses.")]
    private float useCooldown = 0.35f;
    [SerializeField, Min(0.5f), Tooltip("Server: furthest the reported aim origin may be from the player's root (anti-cheat).")]
    private float maxAimOriginOffset = 3f;

    private const float DefaultEyeHeight = 1.6f;
    private static readonly RaycastHit[] attackHits = new RaycastHit[32];

    private PlayerInventory inventory;
    private PlayerHealth health;
    private PlayerAudio playerAudio;
    private PlayerPersistence persistence;
    private Camera playerCamera;
    private InputAction useAction;
    private float nextUseTime;

    /// <summary>Server: an item was used (player, item, slot). Consumables are already applied; weapons already swung.</summary>
    public static event Action<PlayerItemUse, ItemDefinition, int> ServerItemUsed;
    /// <summary>Server: a weapon swing hit something it affected (player, weapon, hit object: an enemy or an obstacle).</summary>
    public static event Action<PlayerItemUse, ItemDefinition, GameObject> ServerWeaponHit;

    public int ServerUseCount { get; private set; }
    /// <summary>Server: what the last weapon swing affected, or null for a miss / a swing blocked by a wall.</summary>
    public GameObject LastWeaponHit { get; private set; }

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        health = GetComponent<PlayerHealth>();
        playerAudio = GetComponent<PlayerAudio>();
        persistence = GetComponent<PlayerPersistence>();
        playerCamera = GetComponentInChildren<Camera>(true);
    }

    public override void OnStartLocalPlayer()
    {
        InputAction configured = inputActions != null ? inputActions.FindAction("Player/UseItem") : null;
        if (configured == null)
        {
            Debug.LogError("PlayerItemUse requires a Player/UseItem action.", this);
            return;
        }
        useAction = GameSettings.TrackBindings(configured.Clone());
        if (isActiveAndEnabled)
            useAction.Enable();
    }

    public override void OnStopLocalPlayer() => ReleaseInput();

    private void OnEnable()
    {
        if (isLocalPlayer)
            useAction?.Enable();
    }

    private void OnDisable() => useAction?.Disable();

    private void OnDestroy() => ReleaseInput();

    private void ReleaseInput()
    {
        useAction?.Dispose();
        useAction = null;
    }

    private void Update()
    {
        if (isLocalPlayer && useAction != null && useAction.WasPressedThisFrame())
            TryUseSelected();
    }

    private bool Alive => health == null || !health.IsDead;

    /// <summary>True when the owner may use the selected item right now.</summary>
    public bool CanUse
    {
        get
        {
            ItemDefinition item = inventory != null ? inventory.SelectedItem : null;
            return item != null && item.UseKind != ItemUseKind.None && Alive && !PlayerControlLock.IsLocked &&
                   (persistence == null || (!persistence.IsAboard && !persistence.IsInTrance)) && Time.time >= nextUseTime &&
                   (SteamSession.Instance == null || !SteamSession.Instance.OverlayActive);
        }
    }

    /// <summary>Owner: uses the selected item (as left mouse does). Returns false if nothing happened.</summary>
    public bool TryUseSelected()
    {
        if (!CanUse)
            return false;
        nextUseTime = Time.time + useCooldown;
        ItemDefinition item = inventory.SelectedItem;
        if (item.UseKind == ItemUseKind.Weapon && playerAudio != null)
            playerAudio.Attack();
        GetAim(out Vector3 aimOrigin, out Vector3 aimDirection);
        if (NetworkClient.active)
            CmdUse(inventory.SelectedSlot, item.Id, aimOrigin, aimDirection);
        return true;
    }

    /// <summary>The owner's camera ray (or eye height and body facing without a camera).</summary>
    public void GetAim(out Vector3 origin, out Vector3 direction)
    {
        if (playerCamera != null)
        {
            origin = playerCamera.transform.position;
            direction = playerCamera.transform.forward;
            return;
        }
        origin = transform.position + Vector3.up * DefaultEyeHeight;
        direction = transform.forward;
    }

    [Command]
    private void CmdUse(int slot, string itemId, Vector3 aimOrigin, Vector3 aimDirection) =>
        ServerUse(slot, itemId, aimOrigin, aimDirection);

    /// <summary>Server: applies the item in <paramref name="slot"/> (it must still be <paramref name="itemId"/> if given), aiming weapons with the player's own camera.</summary>
    [Server]
    public bool ServerUse(int slot, string itemId = null)
    {
        GetAim(out Vector3 origin, out Vector3 direction);
        return ServerUse(slot, itemId, origin, direction);
    }

    /// <summary>Server: applies the item in <paramref name="slot"/>; weapons swing along the given aim.</summary>
    [Server]
    public bool ServerUse(int slot, string itemId, Vector3 aimOrigin, Vector3 aimDirection)
    {
        ItemDefinition item = inventory.GetItem(slot);
        if (item == null || (!string.IsNullOrEmpty(itemId) && item.Id != itemId) || !Alive ||
            (persistence != null && (persistence.IsAboard || persistence.IsInTrance)))
            return false;
        switch (item.UseKind)
        {
            case ItemUseKind.Consumable:
                if (item.HealAmount > 0)
                {
                    if (health == null || health.CurrentHealth >= PlayerHealth.MaxHealth)
                        return false; // don't waste healing at full health
                    health.Heal(item.HealAmount);
                }
                if (item.ConsumeOnUse)
                    inventory.ServerRemove(slot);
                break;
            case ItemUseKind.Weapon:
                ServerAttack(item, aimOrigin, aimDirection);
                break;
            default:
                return false;
        }
        ServerUseCount++;
        ServerItemUsed?.Invoke(this, item, slot);
        return true;
    }

    /// <summary>
    /// Server: swings <paramref name="weapon"/> along the aim (a sphere cast of its Attack Radius up to its Attack Range).
    /// The nearest solid collider decides: an <see cref="IWeaponTarget"/> (enemy) takes the hit, a Destruction
    /// <see cref="EntranceObstacle"/> breaks if the weapon Breaks Obstacles, anything else (walls) blocks the swing.
    /// The attacker's own colliders and other players are ignored. Returns the affected object, or null.
    /// </summary>
    [Server]
    public GameObject ServerAttack(ItemDefinition weapon, Vector3 aimOrigin, Vector3 aimDirection)
    {
        LastWeaponHit = null;
        if (weapon == null)
            return null;
        if (!IsFinite(aimOrigin) || !IsFinite(aimDirection) || aimDirection.sqrMagnitude < 0.0001f ||
            (aimOrigin - transform.position).sqrMagnitude > maxAimOriginOffset * maxAimOriginOffset)
            GetAim(out aimOrigin, out aimDirection);
        aimDirection.Normalize();

        int count = Physics.SphereCastNonAlloc(aimOrigin, weapon.AttackRadius, aimDirection, attackHits, weapon.AttackRange,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(attackHits, 0, count, HitDistanceComparer.Instance);
        for (int index = 0; index < count; index++)
        {
            Collider hit = attackHits[index].collider;
            if (hit == null || hit.transform.IsChildOf(transform) || hit.GetComponentInParent<PlayerPersistence>() != null)
                continue;
            GameObject affected = null;
            IWeaponTarget target = hit.GetComponentInParent<IWeaponTarget>();
            if (target != null)
            {
                if (target.ServerTakeWeaponHit(weapon, netIdentity))
                    affected = ((Component)target).gameObject;
            }
            else
            {
                EntranceObstacle obstacle = hit.GetComponentInParent<EntranceObstacle>();
                if (obstacle != null && weapon.BreaksObstacles && obstacle.TryDestroy())
                    affected = obstacle.gameObject;
            }
            if (affected != null)
            {
                LastWeaponHit = affected;
                ServerWeaponHit?.Invoke(this, weapon, affected);
            }
            return affected;
        }
        return null;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

    private sealed class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }
}
