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
    public float SlotSize => slotSize;
    public float SelectedSlotSize => selectedSlotSize;
    public float PulseScale => pulseScale;
    /// <summary>True while the bar is enlarged after a slot change.</summary>
    public bool Pulsing => Time.unscaledTime < pulseUntil;

    private void Awake()
    {
        if (bar != null)
            bar.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        NetworkIdentity hudPlayer = !HudVisibility.Hidden ? PlayerDeathScreen.HudPlayer : null;
        PlayerInventory inventory = hudPlayer != null ? hudPlayer.GetComponent<PlayerInventory>() : null;
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
