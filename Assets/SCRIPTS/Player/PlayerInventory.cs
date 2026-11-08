using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Per-player inventory on NetworkPlayer: <see cref="BaseSlots"/> active slots (up to <see cref="MaxSlots"/> while a
/// passive item such as a backpack grants extra slots) plus one passive slot. Contents are server-authoritative SyncList/
/// SyncVar item ids resolved through the <see cref="ItemCatalog"/>, so they persist with the player across travel. The
/// owner picks the selected slot with the mouse wheel (Player/InventoryScroll) or 1-4 (Player/Slot1..Slot4); it is
/// applied locally at once and relayed to the server for future item use.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerInventory : NetworkBehaviour
{
    public const int BaseSlots = 2;
    public const int MaxSlots = 4;

    [SerializeField] private InputActionAsset inputActions;
    [SerializeField, Tooltip("Every item that can be held (Create > Capstone > Item Catalog).")]
    private ItemCatalog catalog;
    [SerializeField, Tooltip("Scrolling down selects the next slot; tick to reverse.")]
    private bool invertScroll;
    [SerializeField, Tooltip("Networked pickup spawned for dropped items (must be in the NetworkManager's spawn prefabs).")]
    private ItemPickup itemPickupPrefab;
    [SerializeField, Tooltip("Dropped items land this far in front of the player.")]
    private float dropDistance = 1f;
    [SerializeField, Tooltip("Host range check for picking up dropped items.")]
    private float pickupRange = 5f;

    private readonly SyncList<string> slots = new SyncList<string>();
    [SyncVar] private string passiveItem = string.Empty;
    [SyncVar] private int syncedSelectedSlot;

    private int localSelectedSlot;
    private InputAction scrollAction;
    private readonly InputAction[] slotActions = new InputAction[MaxSlots];
    private InputAction dropItemAction;
    private InputAction dropPassiveAction;
    private PlayerHealth health;

    public ItemCatalog Catalog => catalog;
    public ItemPickup ItemPickupPrefab => itemPickupPrefab;
    /// <summary>Active slots available right now: <see cref="BaseSlots"/> plus the passive item's extra slots.</summary>
    public int Capacity => Mathf.Clamp(BaseSlots + (PassiveItem != null ? PassiveItem.ExtraSlots : 0), BaseSlots, MaxSlots);
    /// <summary>Selected active slot (0-based): the owner's immediate choice, or the synced value on other peers.</summary>
    public int SelectedSlot => Mathf.Clamp(isOwned ? localSelectedSlot : syncedSelectedSlot, 0, Capacity - 1);
    public ItemDefinition PassiveItem => Resolve(passiveItem);
    public ItemDefinition SelectedItem => GetItem(SelectedSlot);

    private void Awake() => health = GetComponent<PlayerHealth>();

    public ItemDefinition GetItem(int slot) => slot >= 0 && slot < slots.Count ? Resolve(slots[slot]) : null;

    private ItemDefinition Resolve(string id) => catalog != null ? catalog.Find(id) : null;

    public override void OnStartServer()
    {
        while (slots.Count < MaxSlots)
            slots.Add(string.Empty);
    }

    public override void OnStartAuthority() => localSelectedSlot = syncedSelectedSlot;

    // ---------------------------------------------------------------- owner input

    public override void OnStartLocalPlayer()
    {
        scrollAction = CloneAction("Player/InventoryScroll", InputActionType.Value);
        for (int index = 0; index < MaxSlots; index++)
            slotActions[index] = CloneAction($"Player/Slot{index + 1}", InputActionType.Button);
        dropItemAction = CloneAction("Player/DropItem", InputActionType.Button);
        dropPassiveAction = CloneAction("Player/DropPassive", InputActionType.Button);
    }

    public override void OnStopLocalPlayer() => ReleaseInput();
    public override void OnStopClient() => ReleaseInput();

    private InputAction CloneAction(string path, InputActionType type)
    {
        InputAction configured = inputActions != null ? inputActions.FindAction(path) : null;
        if (configured == null || configured.type != type)
        {
            Debug.LogError($"PlayerInventory requires a {type} {path} action.", this);
            return null;
        }
        InputAction clone = GameSettings.TrackBindings(configured.Clone());
        if (isActiveAndEnabled)
            clone.Enable();
        return clone;
    }

    private void OnEnable()
    {
        if (!isLocalPlayer)
            return;
        scrollAction?.Enable();
        foreach (InputAction action in slotActions)
            action?.Enable();
        dropItemAction?.Enable();
        dropPassiveAction?.Enable();
    }

    private void OnDisable()
    {
        scrollAction?.Disable();
        foreach (InputAction action in slotActions)
            action?.Disable();
        dropItemAction?.Disable();
        dropPassiveAction?.Disable();
    }

    private void OnDestroy() => ReleaseInput();

    private void ReleaseInput()
    {
        scrollAction?.Dispose();
        scrollAction = null;
        for (int index = 0; index < slotActions.Length; index++)
        {
            slotActions[index]?.Dispose();
            slotActions[index] = null;
        }
        dropItemAction?.Dispose();
        dropItemAction = null;
        dropPassiveAction?.Dispose();
        dropPassiveAction = null;
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;
        if (localSelectedSlot >= Capacity)
            SelectSlot(Capacity - 1);
        if (!CanChangeSlots)
            return;

        if (dropItemAction != null && dropItemAction.WasPressedThisFrame())
            DropSelected();
        if (dropPassiveAction != null && dropPassiveAction.WasPressedThisFrame())
            UnequipPassive();

        for (int index = 0; index < slotActions.Length; index++)
        {
            if (slotActions[index] != null && slotActions[index].WasPressedThisFrame() && index < Capacity)
            {
                SelectSlot(index);
                return;
            }
        }

        float scroll = scrollAction != null ? scrollAction.ReadValue<float>() : 0f;
        if (Mathf.Abs(scroll) > 0.01f)
            ScrollSlots((scroll < 0f) != invertScroll ? 1 : -1);
    }

    private bool CanChangeSlots => !PlayerControlLock.IsLocked && (health == null || !health.IsDead) &&
                                   (SteamSession.Instance == null || !SteamSession.Instance.OverlayActive);

    /// <summary>Owner: moves the selection by <paramref name="steps"/>, wrapping around the available slots.</summary>
    public void ScrollSlots(int steps)
    {
        int capacity = Capacity;
        SelectSlot(((SelectedSlot + steps) % capacity + capacity) % capacity);
    }

    /// <summary>Owner: selects an active slot (clamped to <see cref="Capacity"/>).</summary>
    public void SelectSlot(int slot)
    {
        if (!isOwned)
            return;
        slot = Mathf.Clamp(slot, 0, Capacity - 1);
        localSelectedSlot = slot;
        if (syncedSelectedSlot != slot)
            CmdSelectSlot(slot);
    }

    [Command]
    private void CmdSelectSlot(int slot) => syncedSelectedSlot = Mathf.Clamp(slot, 0, Capacity - 1);

    // ---------------------------------------------------------------- drops and pick-ups

    /// <summary>Owner (G): drops the item in the selected slot.</summary>
    public void DropSelected()
    {
        if (isOwned && SelectedItem != null)
            CmdDropSlot(SelectedSlot);
    }

    /// <summary>Owner (Q): moves the passive item into the first free active slot, or drops it when they are full.</summary>
    public void UnequipPassive()
    {
        if (isOwned && PassiveItem != null)
            CmdUnequipPassive();
    }

    /// <summary>Client: asks the host to put a dropped item into this inventory.</summary>
    public void RequestPickUp(ItemPickup pickup)
    {
        if (isOwned && pickup != null && !pickup.Collected && pickup.netId != 0)
            CmdPickUp(pickup.netId);
    }

    [Command]
    private void CmdDropSlot(int slot)
    {
        if (health != null && health.IsDead)
            return;
        ServerDrop(ServerRemove(slot));
    }

    [Command]
    private void CmdUnequipPassive()
    {
        if (health != null && health.IsDead)
            return;
        ServerUnequipPassive();
    }

    [Command]
    private void CmdPickUp(uint pickupNetId)
    {
        if (health != null && health.IsDead)
            return;
        if (!NetworkServer.spawned.TryGetValue(pickupNetId, out NetworkIdentity identity) || identity == null ||
            !identity.TryGetComponent(out ItemPickup pickup))
            return;
        ServerPickUp(pickup);
    }

    /// <summary>
    /// Server: moves the passive item into the first free slot of the remaining capacity, otherwise drops it on the
    /// ground (items in slots the lost capacity no longer covers are dropped too). Returns true if anything changed.
    /// </summary>
    public bool ServerUnequipPassive()
    {
        ItemDefinition passive = PassiveItem;
        if (!NetworkServer.active || passive == null)
            return false;
        var overflow = new List<ItemDefinition>();
        ServerRemovePassive(overflow);
        foreach (ItemDefinition item in overflow)
            ServerDrop(item);
        int baseCapacity = Capacity;
        for (int index = 0; index < baseCapacity; index++)
        {
            if (GetItem(index) == null)
            {
                slots[index] = passive.Id;
                return true;
            }
        }
        ServerDrop(passive);
        return true;
    }

    /// <summary>Server: picks up a dropped item if it fits and the player is within range. Returns true on success.</summary>
    public bool ServerPickUp(ItemPickup pickup)
    {
        if (!NetworkServer.active || pickup == null || pickup.Collected)
            return false;
        Vector3 delta = pickup.transform.position - transform.position;
        if (delta.sqrMagnitude > pickupRange * pickupRange)
            return false;
        if (!ServerTryAdd(pickup.Item))
            return false;
        pickup.ServerCollect();
        return true;
    }

    /// <summary>Server: spawns a pickup for <paramref name="item"/> in front of the player.</summary>
    public ItemPickup ServerDrop(ItemDefinition item)
    {
        if (!NetworkServer.active || item == null)
            return null;
        if (itemPickupPrefab == null)
        {
            Debug.LogWarning($"PlayerInventory dropped {item.DisplayName} without an Item Pickup Prefab; it is lost.", this);
            return null;
        }
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        return ItemPickup.ServerSpawn(itemPickupPrefab, item, transform.position + forward.normalized * dropDistance);
    }

    /// <summary>Server: drops every held item (active and passive) around the player, e.g. on death.</summary>
    public int ServerDropAll()
    {
        if (!NetworkServer.active)
            return 0;
        var dropped = new List<ItemDefinition>();
        ItemDefinition passive = ServerRemovePassive(dropped);
        if (passive != null)
            dropped.Add(passive);
        for (int index = 0; index < slots.Count; index++)
        {
            ItemDefinition item = ServerRemove(index);
            if (item != null)
                dropped.Add(item);
        }
        for (int index = 0; index < dropped.Count; index++)
        {
            float angle = dropped.Count > 1 ? index * 360f / dropped.Count : 0f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (dropped.Count > 1 ? 0.6f : 0f);
            if (itemPickupPrefab != null)
                ItemPickup.ServerSpawn(itemPickupPrefab, dropped[index], transform.position + offset);
        }
        return dropped.Count;
    }

    // ---------------------------------------------------------------- server API

    /// <summary>
    /// Server: adds an item. Passive items take the passive slot when it is empty; active items take the first empty
    /// slot within <see cref="Capacity"/>. Returns false when there is no room (or the item is not in the catalog).
    /// </summary>
    public bool ServerTryAdd(ItemDefinition item)
    {
        if (!NetworkServer.active || item == null || Resolve(item.Id) == null)
            return false;
        if (item.Kind == ItemSlotKind.Passive)
        {
            if (PassiveItem != null)
                return false;
            passiveItem = item.Id;
            return true;
        }
        for (int index = 0; index < Capacity; index++)
        {
            if (GetItem(index) == null)
            {
                slots[index] = item.Id;
                return true;
            }
        }
        return false;
    }

    /// <summary>Server: removes and returns the item in an active slot (null when empty).</summary>
    public ItemDefinition ServerRemove(int slot)
    {
        ItemDefinition item = GetItem(slot);
        if (!NetworkServer.active || item == null)
            return null;
        slots[slot] = string.Empty;
        return item;
    }

    /// <summary>
    /// Server: removes the passive item. Items left in slots the lost capacity no longer covers are removed too and
    /// added to <paramref name="overflow"/> (e.g. for a future drop system). Returns the removed passive item.
    /// </summary>
    public ItemDefinition ServerRemovePassive(List<ItemDefinition> overflow = null)
    {
        ItemDefinition item = PassiveItem;
        if (!NetworkServer.active || item == null)
            return null;
        passiveItem = string.Empty;
        for (int index = Capacity; index < slots.Count; index++)
        {
            ItemDefinition dropped = ServerRemove(index);
            if (dropped != null)
                overflow?.Add(dropped);
        }
        if (syncedSelectedSlot >= Capacity)
            syncedSelectedSlot = Capacity - 1;
        return item;
    }

    /// <summary>Server: empties every slot (used by the game reset).</summary>
    public void ServerClear()
    {
        if (!NetworkServer.active)
            return;
        passiveItem = string.Empty;
        for (int index = 0; index < slots.Count; index++)
        {
            if (!string.IsNullOrEmpty(slots[index]))
                slots[index] = string.Empty;
        }
    }

    /// <summary>Item ids of the active slots, in slot order (empty string = empty slot), for save files.</summary>
    public string[] SlotIds()
    {
        var ids = new string[slots.Count];
        for (int index = 0; index < slots.Count; index++)
            ids[index] = slots[index] ?? string.Empty;
        return ids;
    }

    /// <summary>Raw passive item id (empty = none), for save files.</summary>
    public string PassiveItemId => passiveItem ?? string.Empty;

    /// <summary>
    /// Server: replaces the inventory with saved ids, keeping each item in its slot. Ids missing from the catalog and
    /// slots beyond the restored capacity are dropped.
    /// </summary>
    public void ServerRestore(string passiveId, IReadOnlyList<string> slotIds)
    {
        if (!NetworkServer.active)
            return;
        ServerClear();
        while (slots.Count < MaxSlots)
            slots.Add(string.Empty);
        ItemDefinition passive = Resolve(passiveId);
        if (passive != null && passive.Kind == ItemSlotKind.Passive)
            passiveItem = passive.Id;
        if (slotIds == null)
            return;
        for (int index = 0; index < Mathf.Min(slotIds.Count, Capacity); index++)
        {
            ItemDefinition item = Resolve(slotIds[index]);
            if (item != null && item.Kind != ItemSlotKind.Passive)
                slots[index] = item.Id;
        }
    }
}
