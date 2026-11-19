using System.Collections.Generic;
using UnityEngine;

/// <summary>Every <see cref="ItemDefinition"/> the game knows, so synced item ids can be resolved on every peer.</summary>
[CreateAssetMenu(fileName = "ItemCatalog", menuName = "Capstone/Item Catalog")]
public sealed class ItemCatalog : ScriptableObject
{
    /// <summary>Item id prefix of generated keys: "key:G1" is "Key G1".</summary>
    public const string KeyIdPrefix = "key:";

    [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();
    [SerializeField, Tooltip("Template for map keys (icon, durability...). Key G1, G2... are created from it at runtime, one per generated gate.")]
    private ItemDefinition keyTemplate;

    private readonly Dictionary<string, ItemDefinition> keys = new Dictionary<string, ItemDefinition>();

    public IReadOnlyList<ItemDefinition> Items => items;
    public ItemDefinition KeyTemplate => keyTemplate;

    public static string KeyItemId(KeyCategory category, string code) =>
        KeyIdPrefix + (category == KeyCategory.Chest ? "chest:" : string.Empty) + code;

    public ItemDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        foreach (ItemDefinition item in items)
        {
            if (item != null && item.Id == id)
                return item;
        }
        return id.StartsWith(KeyIdPrefix) ? FindKey(id) : null;
    }

    /// <summary>The key item that opens gate (or chest) <paramref name="code"/>, e.g. "G1" gives "Key G1".</summary>
    public ItemDefinition GetKey(string code, KeyCategory category = KeyCategory.Gate) =>
        string.IsNullOrEmpty(code) ? null : Find(KeyItemId(category, code));

    public void SetKeyTemplate(ItemDefinition template)
    {
        keyTemplate = template;
        keys.Clear();
    }

    private ItemDefinition FindKey(string id)
    {
        if (keys.TryGetValue(id, out ItemDefinition key) && key != null)
            return key;
        string code = id.Substring(KeyIdPrefix.Length);
        KeyCategory category = KeyCategory.Gate;
        if (code.StartsWith("chest:"))
        {
            category = KeyCategory.Chest;
            code = code.Substring("chest:".Length);
        }
        if (string.IsNullOrEmpty(code))
            return null;
        key = keyTemplate != null ? Instantiate(keyTemplate) : CreateInstance<ItemDefinition>();
        key.name = $"Key {code}";
        key.hideFlags = HideFlags.DontSave;
        key.Configure(id, $"Key {code}", keyTemplate != null ? keyTemplate.Icon : null, ItemSlotKind.Active);
        key.ConfigureKey(category, code);
        key.ConfigureLoot(0f);
        keys[id] = key;
        return key;
    }

    /// <summary>Adds an item at runtime (tests, code-created items); ignored when its id is already listed.</summary>
    public void Register(ItemDefinition item)
    {
        if (item != null && Find(item.Id) == null)
            items.Add(item);
    }
}
