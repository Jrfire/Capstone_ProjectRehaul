using UnityEngine;

public enum ItemSlotKind
{
    /// <summary>Goes in one of the numbered inventory slots (healing items, stun weapons, keys, axes...).</summary>
    Active = 0,
    /// <summary>Goes in the single passive slot (backpack...).</summary>
    Passive = 1,
}

public enum ItemUseKind
{
    /// <summary>Left mouse does nothing (keys, axes used on obstacles, or items without an action yet).</summary>
    None = 0,
    /// <summary>Left mouse uses it up (healing items).</summary>
    Consumable = 1,
    /// <summary>Left mouse swings it: stuns the enemy in front (and breaks barricades if it Breaks Obstacles).</summary>
    Weapon = 2,
}

/// <summary>
/// An inventory item type. Add it to the <see cref="ItemCatalog"/> referenced by <see cref="PlayerInventory"/> so every
/// peer can look it up by <see cref="Id"/>. Active items in the selected slot are used with Player/UseItem (left mouse)
/// through <see cref="PlayerItemUse"/> according to <see cref="UseKind"/>.
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

    [Header("Use (left mouse, active items)")]
    [SerializeField, Tooltip("What left mouse does while this item is in the selected slot.")]
    private ItemUseKind useKind = ItemUseKind.None;
    [SerializeField, Min(0), Tooltip("Consumables: health restored (2 = one heart).")]
    private int healAmount;
    [SerializeField, Tooltip("Consumables: removed from the slot after use.")]
    private bool consumeOnUse = true;

    [Header("Weapon (Use Kind = Weapon)")]
    [SerializeField, Min(0.1f), Tooltip("Reach of the swing from the camera, in metres.")]
    private float attackRange = 2.5f;
    [SerializeField, Min(0f), Tooltip("Radius of the swing (sphere cast), so the aim does not need to be pixel perfect.")]
    private float attackRadius = 0.35f;
    [SerializeField, Min(0f), Tooltip("Seconds a hit enemy is stunned (0 = the enemy's own default stun).")]
    private float stunSeconds = 3f;
    [SerializeField, Tooltip("Hitting a Destruction entrance obstacle (barricade) breaks it, like an itak/axe.")]
    private bool breaksObstacles;

    public string Id => string.IsNullOrEmpty(itemId) ? name : itemId;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public ItemSlotKind Kind => kind;
    public int ExtraSlots => kind == ItemSlotKind.Passive ? extraSlots : 0;
    public ItemUseKind UseKind => kind == ItemSlotKind.Active ? useKind : ItemUseKind.None;
    public int HealAmount => healAmount;
    public bool ConsumeOnUse => consumeOnUse;
    public float AttackRange => attackRange;
    public float AttackRadius => attackRadius;
    public float StunSeconds => stunSeconds;
    public bool BreaksObstacles => breaksObstacles;

    /// <summary>Runtime setup of a weapon for tests and code-created items.</summary>
    public void ConfigureWeapon(float range, float stun, bool breaks, float radius = 0.35f)
    {
        useKind = ItemUseKind.Weapon;
        attackRange = Mathf.Max(0.1f, range);
        stunSeconds = Mathf.Max(0f, stun);
        breaksObstacles = breaks;
        attackRadius = Mathf.Max(0f, radius);
    }

    /// <summary>Runtime setup of the left mouse behaviour for tests and code-created items.</summary>
    public void ConfigureUse(ItemUseKind use, int heal = 0, bool consume = true)
    {
        useKind = use;
        healAmount = Mathf.Max(0, heal);
        consumeOnUse = consume;
    }

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
