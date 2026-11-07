using System.Collections.Generic;
using UnityEngine;

/// <summary>Every <see cref="ItemDefinition"/> the game knows, so synced item ids can be resolved on every peer.</summary>
[CreateAssetMenu(fileName = "ItemCatalog", menuName = "Capstone/Item Catalog")]
public sealed class ItemCatalog : ScriptableObject
{
    [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();

    public IReadOnlyList<ItemDefinition> Items => items;

    public ItemDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        foreach (ItemDefinition item in items)
        {
            if (item != null && item.Id == id)
                return item;
        }
        return null;
    }

    /// <summary>Adds an item at runtime (tests, code-created items); ignored when its id is already listed.</summary>
    public void Register(ItemDefinition item)
    {
        if (item != null && Find(item.Id) == null)
            items.Add(item);
    }
}
