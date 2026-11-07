using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scrolling compass strip at the top of the PlayerHud. It scrolls a repeating heading texture (N...E...S...W...) so the
/// heading the local camera faces sits under the centre. Directions match the Tab map: north = world +Z (map up),
/// east = +X (map right). Local only; hidden without a local player or while the HUD is hidden.
/// </summary>
[DisallowMultipleComponent]
public sealed class CompassHud : MonoBehaviour
{
    [SerializeField, Tooltip("Shown while a local player exists; hidden with the HUD.")]
    private GameObject content;
    [SerializeField, Tooltip("RawImage showing the compass texture. Its texture's Wrap Mode U must be Repeat.")]
    private RawImage strip;
    [SerializeField, Range(0f, 1f), Tooltip("Horizontal texture position (0-1) of the N mark's centre.")]
    private float northU = 0.0104f;
    [SerializeField, Tooltip("Derive the visible arc from the strip's aspect ratio so the letters keep the texture's proportions.")]
    private bool matchTextureAspect = true;
    [SerializeField, Range(10f, 360f), Tooltip("Degrees of heading visible across the strip when Match Texture Aspect is off.")]
    private float visibleDegrees = 180f;
    [SerializeField, Tooltip("Reverse the scroll direction if the texture's headings run right-to-left.")]
    private bool reverse;

    public GameObject Content => content;
    public RawImage Strip => strip;
    /// <summary>Heading (0 = north/+Z, 90 = east/+X) shown under the centre last frame.</summary>
    public float Heading { get; private set; }

    /// <summary>Fraction of the texture's width visible across the strip.</summary>
    public float VisibleFraction
    {
        get
        {
            if (matchTextureAspect && strip != null && strip.texture != null)
            {
                Rect rect = strip.rectTransform.rect;
                if (rect.height > 0f && strip.texture.width > 0)
                    return Mathf.Clamp(rect.width / rect.height * strip.texture.height / strip.texture.width, 0.02f, 1f);
            }
            return visibleDegrees / 360f;
        }
    }

    private void LateUpdate()
    {
        Transform view = ViewTransform();
        bool show = view != null && !HudVisibility.Hidden;
        if (content != null && content.activeSelf != show)
            content.SetActive(show);
        if (show)
            SetHeading(view.eulerAngles.y);
    }

    /// <summary>Scrolls the strip so <paramref name="yaw"/> (degrees, 0 = north/+Z) sits under the centre.</summary>
    public void SetHeading(float yaw)
    {
        Heading = Mathf.Repeat(yaw, 360f);
        if (strip == null)
            return;
        float width = VisibleFraction;
        float center = northU + (reverse ? -Heading : Heading) / 360f;
        strip.uvRect = new Rect(Mathf.Repeat(center - width * 0.5f, 1f), 0f, width, 1f);
    }

    private static Transform ViewTransform()
    {
        NetworkIdentity player = NetworkClient.active ? NetworkClient.localPlayer : null;
        if (player == null)
            return null;
        Camera camera = player.GetComponentInChildren<Camera>();
        return camera != null ? camera.transform : player.transform;
    }
}
