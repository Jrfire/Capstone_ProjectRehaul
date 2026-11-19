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
    [SerializeField, Min(0.01f), Tooltip("Largest side of the item's sprite in the world, in metres (an agimat pickup is about 0.41 m). The sprite is scaled to fit whatever its texture size.")]
    private float worldSize = 0.41f;

    [SyncVar(hook = nameof(OnItemChanged))]
    private string itemId = string.Empty;

    [SyncVar] private int uses;

    private static readonly List<ItemPickup> active = new List<ItemPickup>();

    public static IReadOnlyList<ItemPickup> Active => active;
    public string ItemId => itemId;
    public ItemDefinition Item => catalog != null ? catalog.Find(itemId) : null;
    public ItemCatalog Catalog => catalog;
    public SpriteRenderer WorldSprite => worldSprite;
    public float WorldSize => worldSize;
    public bool Collected { get; private set; }
    /// <summary>Remaining durability of a dropped weapon (0 = full).</summary>
    public int Uses => uses;

    /// <summary>Server: spawns a pickup for <paramref name="item"/> on the ground near <paramref name="position"/>.</summary>
    public static ItemPickup ServerSpawn(ItemPickup prefab, ItemDefinition item, Vector3 position, int uses = 0)
    {
        if (!NetworkServer.active || prefab == null || item == null)
            return null;
        if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            position = hit.point;
        ItemPickup pickup = Instantiate(prefab, position, Quaternion.identity);
        pickup.name = $"{prefab.name} ({item.DisplayName})";
        pickup.itemId = item.Id;
        pickup.uses = Mathf.Max(0, uses);
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
        {
            worldSprite.sprite = item != null ? item.Icon : null;
            worldSprite.transform.localScale = Vector3.one * ScaleFor(worldSprite.sprite, worldSize);
        }
        if (nametag != null)
            nametag.DisplayName = item != null ? item.DisplayName : "Item";
    }

    /// <summary>Uniform scale that makes the sprite's largest side <paramref name="size"/> metres (1 without a sprite).</summary>
    public static float ScaleFor(Sprite sprite, float size)
    {
        if (sprite == null)
            return 1f;
        Vector3 bounds = sprite.bounds.size;
        float largest = Mathf.Max(bounds.x, bounds.y);
        return largest > 0.0001f ? size / largest : 1f;
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
