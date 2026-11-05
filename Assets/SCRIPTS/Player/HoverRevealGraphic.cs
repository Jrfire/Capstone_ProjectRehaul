using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Keeps a UI background (by default the Button's Target Graphic) invisible until the cursor hovers over it. The graphic
/// stays a raycast target while invisible, so hovering and clicking still work; child labels are unaffected. The
/// Selectable's own color tint still applies on top while it is visible.
/// </summary>
[DisallowMultipleComponent]
public sealed class HoverRevealGraphic : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField, Tooltip("Background to reveal. Empty = the Selectable's Target Graphic, else this object's Graphic.")]
    private Graphic target;
    [SerializeField, Range(0f, 1f), Tooltip("Alpha of the background while hovered.")]
    private float hoverAlpha = 1f;
    [SerializeField, Min(0f), Tooltip("Seconds to fade in/out; 0 = instant.")]
    private float fadeSeconds = 0.08f;
    [SerializeField, Tooltip("Also reveal while the Selectable is not interactable (e.g. unavailable destinations).")]
    private bool revealWhenNotInteractable;

    private Selectable selectable;
    private bool hovered;

    public Graphic Target => target;
    public bool Hovered => hovered;
    public float CurrentAlpha => target != null ? target.color.a : 0f;
    public float HoverAlpha => hoverAlpha;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        if (target == null)
            target = selectable != null && selectable.targetGraphic != null ? selectable.targetGraphic : GetComponent<Graphic>();
    }

    private void OnEnable()
    {
        hovered = false;
        SetAlpha(0f);
    }

    private void OnDisable()
    {
        hovered = false;
        SetAlpha(0f);
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;

    public void OnPointerExit(PointerEventData eventData) => hovered = false;

    private void Update()
    {
        if (target == null)
            return;
        bool reveal = hovered && (revealWhenNotInteractable || selectable == null || selectable.IsInteractable());
        float goal = reveal ? hoverAlpha : 0f;
        float alpha = target.color.a;
        if (Mathf.Approximately(alpha, goal))
            return;
        SetAlpha(fadeSeconds <= 0f ? goal : Mathf.MoveTowards(alpha, goal, Time.unscaledDeltaTime * Mathf.Max(hoverAlpha, 0.01f) / fadeSeconds));
    }

    private void SetAlpha(float alpha)
    {
        if (target == null)
            return;
        Color color = target.color;
        color.a = alpha;
        target.color = color;
    }
}
