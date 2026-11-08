using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// A networked dropped item (spawned by the server when a player drops an item with G/Q or dies holding items). Its
/// item id is a SyncVar resolved through the <see cref="ItemCatalog"/>, which picks the billboarded icon and nametag on
/// every peer. E (<see cref="ItemPickupInteractable"/>) asks the host, through the player's
/// <see cref="PlayerInventory"/>, to put it back into an inventory.
/// </summary>
[DisallowMultipleComponent]
public sealed class ItemPickup : NetworkBehaviour
{
    [SerializeField] private ItemCatalog catalog;
    [SerializeField, Tooltip("Billboarded sprite that shows the item's icon.")]
    private SpriteRenderer worldSprite;
    [SerializeField, Tooltip("Optional nametag renamed to the item's display name.")]
    private Nametag nametag;

    [SyncVar(hook = nameof(OnItemChanged))]
    private string itemId = string.Empty;

    private static readonly List<ItemPickup> active = new List<ItemPickup>();

    public static IReadOnlyList<ItemPickup> Active => active;
    public string ItemId => itemId;
    public ItemDefinition Item => catalog != null ? catalog.Find(itemId) : null;
    public ItemCatalog Catalog => catalog;
    public SpriteRenderer WorldSprite => worldSprite;
    public bool Collected { get; private set; }

    /// <summary>Server: spawns a pickup for <paramref name="item"/> on the ground near <paramref name="position"/>.</summary>
    public static ItemPickup ServerSpawn(ItemPickup prefab, ItemDefinition item, Vector3 position)
    {
        if (!NetworkServer.active || prefab == null || item == null)
            return null;
        if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            position = hit.point;
        ItemPickup pickup = Instantiate(prefab, position, Quaternion.identity);
        pickup.name = $"{prefab.name} ({item.DisplayName})";
        pickup.itemId = item.Id;
        pickup.Apply();
        NetworkServer.Spawn(pickup.gameObject);
        return pickup;
    }

    private void Awake() => Apply();

    private void OnEnable() => active.Add(this);

    private void OnDisable() => active.Remove(this);

    public override void OnStartClient() => Apply();

    private void OnItemChanged(string _, string __) => Apply();

    private void Apply()
    {
        ItemDefinition item = Item;
        if (worldSprite != null)
            worldSprite.sprite = item != null ? item.Icon : null;
        if (nametag != null)
            nametag.DisplayName = item != null ? item.DisplayName : "Item";
    }

    /// <summary>Server: marks the pickup taken and destroys it everywhere. Returns false if it was already taken.</summary>
    public bool ServerCollect()
    {
        if (!NetworkServer.active || Collected)
            return false;
        Collected = true;
        if (isServer)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
        return true;
    }
}
