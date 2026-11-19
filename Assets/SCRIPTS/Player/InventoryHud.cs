using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-centre inventory bar on the PlayerHud: the passive slot (left) followed by the local player's active slots in
/// a HorizontalLayoutGroup. Slots beyond <see cref="PlayerInventory.Capacity"/> are hidden. Item icons are drawn over
/// each slot's background. The selected slot eases to <see cref="selectedSlotSize"/>; changing slots briefly enlarges
/// the whole bar, which settles back <see cref="pulseHoldSeconds"/> after the last change. Hidden while no local player
/// exists or a menu hides the HUD (<see cref="HudVisibility"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryHud : MonoBehaviour
{
    [SerializeField, Tooltip("The bar (HorizontalLayoutGroup); shown, hidden and scaled as a whole.")]
    private RectTransform bar;
    [SerializeField, Tooltip("Passive slot (PassiveUI background) on the left of the bar.")]
    private LayoutElement passiveSlot;
    [SerializeField] private Image passiveIcon;
    [SerializeField, Tooltip("Active slots left to right (SlotUI backgrounds); one per possible slot.")]
    private LayoutElement[] slots = new LayoutElement[PlayerInventory.MaxSlots];
    [SerializeField, Tooltip("Item icon Image on top of each slot's background, in slot order.")]
    private Image[] slotIcons = new Image[PlayerInventory.MaxSlots];
    [SerializeField, Tooltip("Small heart below each weapon icon; the template cloned into one heart per durability use.")]
    private Image[] durabilityIcons = new Image[PlayerInventory.MaxSlots];
    [SerializeField, Tooltip("Remaining-use count, shown next to a single heart only when the weapon has more than Max Durability Hearts uses.")]
    private Text[] durabilityText = new Text[PlayerInventory.MaxSlots];
    [SerializeField, Tooltip("Heart for each remaining use (empty = the PlayerHud's Full Heart).")]
    private Sprite durabilityHeartSprite;
    [SerializeField, Tooltip("Heart for each used-up use (empty = the PlayerHud's Empty Heart; without one, spent hearts are dimmed).")]
    private Sprite durabilityEmptyHeartSprite;
    [SerializeField, Min(1), Tooltip("Weapons with up to this many uses show one small heart per use; more show a heart and a count.")]
    private int maxDurabilityHearts = 5;
    [SerializeField, Min(0f), Tooltip("Gap between the small durability hearts, in pixels.")]
    private float durabilityHeartSpacing = 1f;
    [SerializeField, Tooltip("Tint of spent hearts when no empty heart sprite exists.")]
    private Color spentHeartTint = new Color(0.25f, 0.25f, 0.25f, 0.6f);

    [Header("Selected item name")]
    [SerializeField, Tooltip("Bottom-left label showing the selected slot's item name; hidden while that slot is empty.")]
    private Text selectedNameText;

    [Header("Sizes")]
    [SerializeField, Min(8f)] private float slotSize = 64f;
    [SerializeField, Min(8f), Tooltip("Size of the selected slot.")]
    private float selectedSlotSize = 80f;
    [SerializeField, Min(0.01f), Tooltip("How quickly a slot eases to its new size (higher = faster).")]
    private float resizeSpeed = 14f;

    [Header("Slot change pulse")]
    [SerializeField, Min(1f), Tooltip("Scale of the whole bar while the player is changing slots.")]
    private float pulseScale = 1.12f;
    [SerializeField, Min(0f), Tooltip("Seconds after the last slot change before the bar settles back.")]
    private float pulseHoldSeconds = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("How quickly the bar grows and settles (higher = faster).")]
    private float pulseSpeed = 10f;

    private PlayerInventory bound;
    private int lastSelected = -1;
    private float pulseUntil = float.NegativeInfinity;

    public RectTransform Bar => bar;
    public LayoutElement PassiveSlot => passiveSlot;
    public Image PassiveIcon => passiveIcon;
    public LayoutElement[] Slots => slots;
    public Image[] SlotIcons => slotIcons;
    private PlayerHud parentHud;

    public Image[] DurabilityIcons => durabilityIcons;

    /// <summary>The durability heart: the assigned sprite, else the PlayerHud's Full Heart (null = number only).</summary>
    public Sprite DurabilitySprite
    {
        get
        {
            if (durabilityHeartSprite != null)
                return durabilityHeartSprite;
            if (parentHud == null)
                parentHud = GetComponentInParent<PlayerHud>(true);
            return parentHud != null ? parentHud.FullHeart : null;
        }
    }

    /// <summary>The spent durability heart: the assigned sprite, else the PlayerHud's Empty Heart (null = dimmed full heart).</summary>
    public Sprite DurabilityEmptySprite
    {
        get
        {
            if (durabilityEmptyHeartSprite != null)
                return durabilityEmptyHeartSprite;
            if (parentHud == null)
                parentHud = GetComponentInParent<PlayerHud>(true);
            return parentHud != null ? parentHud.EmptyHeart : null;
        }
    }

    public int MaxDurabilityHearts => maxDurabilityHearts;

    private sealed class HeartRow
    {
        public readonly List<Image> hearts = new List<Image>();
        public Vector2 templatePivot;
        public Vector2 templatePosition;
        public Color templateColor;
    }

    private HeartRow[] heartRows;

    /// <summary>Visible durability hearts of <paramref name="slot"/> (the template first).</summary>
    public IReadOnlyList<Image> GetDurabilityHearts(int slot)
    {
        var visible = new List<Image>();
        if (heartRows == null || slot < 0 || slot >= heartRows.Length || heartRows[slot] == null)
            return visible;
        foreach (Image heart in heartRows[slot].hearts)
        {
            if (heart != null && heart.enabled && heart.gameObject.activeSelf)
                visible.Add(heart);
        }
        return visible;
    }

    private HeartRow RowFor(int index)
    {
        if (heartRows == null || heartRows.Length != durabilityIcons.Length)
            heartRows = new HeartRow[durabilityIcons.Length];
        if (heartRows[index] == null)
        {
            Image template = durabilityIcons[index];
            var row = new HeartRow
            {
                templatePivot = template.rectTransform.pivot,
                templatePosition = template.rectTransform.anchoredPosition,
                templateColor = template.color
            };
            row.hearts.Add(template);
            heartRows[index] = row;
        }
        return heartRows[index];
    }

    /// <summary>
    /// One small heart per use (full = remaining, empty = spent) centred below the icon; above
    /// <see cref="maxDurabilityHearts"/> uses a single heart plus the remaining count.
    /// </summary>
    private void UpdateDurability(int index, bool show, int remaining, int maximum)
    {
        Image template = durabilityIcons[index];
        HeartRow row = RowFor(index);
        Text count = index < durabilityText.Length ? durabilityText[index] : null;
        Sprite full = DurabilitySprite;
        bool asRow = show && full != null && maximum <= maxDurabilityHearts;
        int shown = !show || full == null ? 0 : asRow ? maximum : 1;

        while (row.hearts.Count < shown)
        {
            Image clone = Instantiate(template, template.transform.parent);
            clone.name = $"{template.name} {row.hearts.Count + 1}";
            row.hearts.Add(clone);
        }

        Sprite empty = DurabilityEmptySprite;
        float width = template.rectTransform.sizeDelta.x;
        float total = shown * width + Mathf.Max(0, shown - 1) * durabilityHeartSpacing;
        for (int heart = 0; heart < row.hearts.Count; heart++)
        {
            Image image = row.hearts[heart];
            bool visible = heart < shown;
            if (image.gameObject.activeSelf != visible)
                image.gameObject.SetActive(visible);
            image.enabled = visible;
            if (!visible)
                continue;
            RectTransform rect = image.rectTransform;
            if (asRow)
            {
                bool spent = heart >= remaining;
                image.sprite = spent && empty != null ? empty : full;
                image.color = spent && empty == null ? spentHeartTint : row.templateColor;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-total * 0.5f + width * 0.5f + heart * (width + durabilityHeartSpacing),
                    row.templatePosition.y);
            }
            else
            {
                image.sprite = full;
                image.color = row.templateColor;
                rect.pivot = row.templatePivot;
                rect.anchoredPosition = row.templatePosition;
            }
        }

        // Weapons with many uses (or no heart sprite at all) show the remaining count instead.
        bool showCount = show && !asRow;
        if (count != null)
        {
            count.text = showCount ? remaining.ToString() : string.Empty;
            if (count.gameObject.activeSelf != showCount)
                count.gameObject.SetActive(showCount);
        }
    }
    public Text[] DurabilityText => durabilityText;
    public Text SelectedNameText => selectedNameText;
    public float SlotSize => slotSize;
    public float SelectedSlotSize => selectedSlotSize;
    public float PulseScale => pulseScale;
    /// <summary>True while the bar is enlarged after a slot change.</summary>
    public bool Pulsing => Time.unscaledTime < pulseUntil;

    private void Awake()
    {
        if (bar != null)
            bar.gameObject.SetActive(false);
        UpdateSelectedName(null);
    }

    private void UpdateSelectedName(ItemDefinition item)
    {
        if (selectedNameText == null)
            return;
        bool show = item != null;
        if (show && selectedNameText.text != item.DisplayName)
            selectedNameText.text = item.DisplayName;
        if (selectedNameText.gameObject.activeSelf != show)
            selectedNameText.gameObject.SetActive(show);
    }

    private void LateUpdate()
    {
        NetworkIdentity hudPlayer = !HudVisibility.Hidden ? PlayerDeathScreen.HudPlayer : null;
        PlayerInventory inventory = hudPlayer != null ? hudPlayer.GetComponent<PlayerInventory>() : null;
        UpdateSelectedName(inventory != null ? inventory.SelectedItem : null);
        if (bar == null)
            return;
        if (bar.gameObject.activeSelf != (inventory != null))
            bar.gameObject.SetActive(inventory != null);
        if (inventory == null)
        {
            bound = null;
            return;
        }

        bool snap = inventory != bound;
        int selected = inventory.SelectedSlot;
        if (snap)
        {
            bound = inventory;
            pulseUntil = float.NegativeInfinity;
        }
        else if (selected != lastSelected)
        {
            pulseUntil = Time.unscaledTime + pulseHoldSeconds;
        }
        lastSelected = selected;

        float step = snap ? 1f : 1f - Mathf.Exp(-resizeSpeed * Time.unscaledDeltaTime);
        SetIcon(passiveIcon, inventory.PassiveItem);
        if (passiveSlot != null)
            Resize(passiveSlot, slotSize, 1f);
        int capacity = inventory.Capacity;
        for (int index = 0; index < slots.Length; index++)
        {
            LayoutElement slot = slots[index];
            if (slot == null)
                continue;
            bool available = index < capacity;
            if (slot.gameObject.activeSelf != available)
                slot.gameObject.SetActive(available);
            Resize(slot, index == selected ? selectedSlotSize : slotSize, step);
            if (index < slotIcons.Length)
                SetIcon(slotIcons[index], inventory.GetItem(index));
            ItemDefinition item = inventory.GetItem(index);
            int remaining = inventory.GetDurability(index);
            int maximum = inventory.GetMaxDurability(index);
            bool showDurability = item != null && item.DurabilityEnabled && maximum > 0;
            if (index < durabilityIcons.Length && durabilityIcons[index] != null)
                UpdateDurability(index, showDurability, remaining, maximum);
            else if (index < durabilityText.Length && durabilityText[index] != null)
            {
                durabilityText[index].text = showDurability ? remaining.ToString() : string.Empty;
                durabilityText[index].gameObject.SetActive(showDurability);
            }
        }

        float targetScale = Pulsing ? pulseScale : 1f;
        float scaleStep = snap ? 1f : 1f - Mathf.Exp(-pulseSpeed * Time.unscaledDeltaTime);
        float scale = Mathf.Lerp(bar.localScale.x, targetScale, scaleStep);
        if (Mathf.Abs(scale - targetScale) < 0.0005f)
            scale = targetScale;
        bar.localScale = new Vector3(scale, scale, 1f);
    }

    private static void Resize(LayoutElement slot, float target, float step)
    {
        float size = Mathf.Lerp(slot.preferredWidth, target, step);
        if (Mathf.Abs(size - target) < 0.05f)
            size = target;
        if (!Mathf.Approximately(slot.preferredWidth, size))
        {
            slot.preferredWidth = size;
            slot.preferredHeight = size;
        }
    }

    private static void SetIcon(Image icon, ItemDefinition item)
    {
        if (icon == null)
            return;
        Sprite sprite = item != null ? item.Icon : null;
        if (icon.sprite != sprite)
            icon.sprite = sprite;
        bool show = sprite != null;
        if (icon.enabled != show)
            icon.enabled = show;
    }
}
