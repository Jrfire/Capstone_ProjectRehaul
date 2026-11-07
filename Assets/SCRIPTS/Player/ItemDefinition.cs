using UnityEngine;

public enum ItemSlotKind
{
    /// <summary>Goes in one of the numbered inventory slots (healing items, stun weapons, keys, axes...).</summary>
    Active = 0,
    /// <summary>Goes in the single passive slot (backpack...).</summary>
    Passive = 1,
}

/// <summary>
/// An inventory item type. Add it to the <see cref="ItemCatalog"/> referenced by <see cref="PlayerInventory"/> so every
/// peer can look it up by <see cref="Id"/>. Item behaviour (healing, stunning, opening obstacles) is added per item later.
/// </summary>
[CreateAssetMenu(fileName = "Item", menuName = "Capstone/Item Definition")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField, Tooltip("Unique id synced over the network. Leave empty to use the asset name.")]
    private string itemId;
    [SerializeField] private string displayName;
    [SerializeField, Tooltip("Drawn on top of the slot background in the HUD.")]
    private Sprite icon;
    [SerializeField] private ItemSlotKind kind = ItemSlotKind.Active;
    [SerializeField, Range(0, PlayerInventory.MaxSlots - PlayerInventory.BaseSlots),
     Tooltip("Passive items only: extra inventory slots while equipped (a backpack adds 2).")]
    private int extraSlots;

    public string Id => string.IsNullOrEmpty(itemId) ? name : itemId;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public ItemSlotKind Kind => kind;
    public int ExtraSlots => kind == ItemSlotKind.Passive ? extraSlots : 0;

    /// <summary>Runtime setup for tests and code-created items.</summary>
    public void Configure(string id, string label, Sprite itemIcon, ItemSlotKind slotKind, int extra = 0)
    {
        itemId = id;
        displayName = label;
        icon = itemIcon;
        kind = slotKind;
        extraSlots = Mathf.Clamp(extra, 0, PlayerInventory.MaxSlots - PlayerInventory.BaseSlots);
    }
}
