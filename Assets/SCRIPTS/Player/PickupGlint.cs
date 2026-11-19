using Mirror;
using UnityEngine;

/// <summary>
/// A local sparkle that helps players find pickups (agimats, dropped items) in the dark: every
/// <see cref="Interval"/> seconds a four-point star flashes over the item's sprite, spinning and growing, with a faint
/// twinkle in between. It is sized by camera distance so it stays readable up to <see cref="MaxDistance"/>, and it is
/// independent of the item's <see cref="DistanceFadeSprite"/>, so it shows even where the item itself has faded.
/// Local only: every peer draws it toward its own camera.
/// </summary>
[DisallowMultipleComponent]
public sealed class PickupGlint : MonoBehaviour
{
    [SerializeField, Tooltip("Transparent sprite material for the star (Sprite-Unlit-Default). Empty = the SpriteRenderer default.")]
    private Material material;

    [SerializeField, Tooltip("Renderer the star sits on. Empty = the first child SpriteRenderer.")]
    private SpriteRenderer target;

    [Header("Timing")]
    [SerializeField, Min(0.2f), Tooltip("Seconds between flashes.")]
    private float interval = 2.2f;

    [SerializeField, Min(0.05f), Tooltip("Seconds one flash lasts.")]
    private float flashSeconds = 0.6f;

    [SerializeField, Range(0f, 1f), Tooltip("Twinkle opacity between flashes (0 = invisible).")]
    private float idleAlpha = 0.18f;

    [Header("Look")]
    [SerializeField, ColorUsage(false, true), Tooltip("Star colour (HDR: above 1 glows through the dark grading).")]
    private Color color = new Color(1.6f, 1.45f, 1.0f, 1f);

    [SerializeField, Min(0.01f), Tooltip("Star size in metres up close.")]
    private float baseSize = 0.35f;

    [SerializeField, Min(0f), Tooltip("Extra size per metre of distance, so far glints stay visible.")]
    private float sizePerMeter = 0.03f;

    [SerializeField, Min(0.01f), Tooltip("Largest star size in metres.")]
    private float maxSize = 1.1f;

    [SerializeField, Range(0f, 1f), Tooltip("Height on the target sprite (0 = bottom, 1 = top).")]
    private float heightOnSprite = 0.65f;

    [SerializeField, Tooltip("Degrees the star spins during a flash.")]
    private float spinDegrees = 90f;

    [Header("Distance")]
    [SerializeField, Min(0f), Tooltip("Hidden beyond this camera distance.")]
    private float maxDistance = 28f;

    [SerializeField, Min(0.01f), Tooltip("Metres before Max Distance over which it fades out.")]
    private float fadeRange = 6f;

    private static Sprite starSprite;

    private SpriteRenderer glint;
    private float phase;
    private NetworkIdentity cachedViewerOwner;
    private Camera cachedViewer;

    public SpriteRenderer GlintRenderer => glint;
    public float Interval { get => interval; set => interval = Mathf.Max(0.2f, value); }
    public float FlashSeconds { get => flashSeconds; set => flashSeconds = Mathf.Max(0.05f, value); }
    public float MaxDistance { get => maxDistance; set => maxDistance = Mathf.Max(0f, value); }
    /// <summary>0..1 brightness of the current flash (0 between flashes).</summary>
    public float Flash { get; private set; }
    /// <summary>Opacity the star is drawn with this frame.</summary>
    public float CurrentAlpha { get; private set; }

    private void Start()
    {
        // Created in Start, after DistanceFadeSprite collected its renderers in Awake, so the item's fade skips it.
        if (target == null)
            target = GetComponentInChildren<SpriteRenderer>(true);
        var go = new GameObject("Glint");
        go.transform.SetParent(transform, false);
        glint = go.AddComponent<SpriteRenderer>();
        glint.sprite = StarSprite();
        if (material != null)
            glint.sharedMaterial = material;
        glint.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glint.receiveShadows = false;
        glint.sortingOrder = 10;
        phase = Random.value * interval;
        LateUpdate();
    }

    private void OnDisable()
    {
        if (glint != null)
            glint.enabled = false;
    }

    /// <summary>Flash brightness at a time (seconds) for a given interval/flash length.</summary>
    public static float FlashAt(float time, float interval, float flashSeconds)
    {
        float t = Mathf.Repeat(time, Mathf.Max(0.2f, interval));
        float length = Mathf.Min(flashSeconds, interval);
        return t < length ? Mathf.Sin(t / length * Mathf.PI) : 0f;
    }

    private void LateUpdate()
    {
        if (glint == null)
            return;
        Camera viewer = ResolveViewer();
        bool hasTarget = target != null && target.sprite != null;
        if (viewer == null || !hasTarget)
        {
            glint.enabled = false;
            CurrentAlpha = 0f;
            return;
        }

        Bounds bounds = target.bounds;
        Vector3 anchor = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * heightOnSprite, bounds.center.z);
        Vector3 cameraPosition = viewer.transform.position;
        Vector3 toCamera = cameraPosition - anchor;
        float distance = toCamera.magnitude;
        float distanceAlpha = maxDistance <= 0f ? 0f : Mathf.Clamp01((maxDistance - distance) / fadeRange);

        float time = Time.time + phase;
        Flash = FlashAt(time, interval, flashSeconds);
        float twinkle = idleAlpha * (0.6f + 0.4f * Mathf.Sin(time * 5.3f));
        CurrentAlpha = Mathf.Max(Flash, twinkle) * distanceAlpha;
        glint.enabled = CurrentAlpha > 0.001f;
        if (!glint.enabled)
            return;

        // Slightly toward the camera so the item's own sprite never covers it.
        Vector3 forward = distance > 0.0001f ? toCamera / distance : Vector3.forward;
        Transform glintTransform = glint.transform;
        glintTransform.position = anchor + forward * 0.08f;
        float cycle = Mathf.Repeat(time, interval) / Mathf.Min(flashSeconds, interval);
        float spin = Flash > 0f ? Mathf.Clamp01(cycle) * spinDegrees : 0f;
        glintTransform.rotation = Quaternion.LookRotation(-forward, viewer.transform.up) * Quaternion.Euler(0f, 0f, spin);
        float size = Mathf.Min(maxSize, baseSize + distance * sizePerMeter) * Mathf.Lerp(0.55f, 1f, Mathf.Max(Flash, 0.3f));
        Vector3 parentScale = transform.lossyScale;
        float parent = Mathf.Max(0.0001f, Mathf.Abs(parentScale.x));
        glintTransform.localScale = Vector3.one * (size / parent);
        Color c = color;
        c.a = CurrentAlpha;
        glint.color = c;
    }

    private Camera ResolveViewer()
    {
        NetworkIdentity local = NetworkClient.localPlayer;
        if (local != null)
        {
            if (local != cachedViewerOwner)
            {
                cachedViewerOwner = local;
                cachedViewer = local.GetComponentInChildren<Camera>(true);
            }
            if (cachedViewer != null && cachedViewer.isActiveAndEnabled)
                return cachedViewer;
        }
        return Camera.main;
    }

    /// <summary>A generated 1-metre four-point star with a soft core (shared by every glint).</summary>
    public static Sprite StarSprite()
    {
        if (starSprite != null)
            return starSprite;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PickupGlintStar",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x - half) / half;
                float v = (y - half) / half;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Clamp01(1f - r / 0.35f);
                core *= core;
                float rayH = Mathf.Clamp01(1f - Mathf.Abs(v) / Mathf.Lerp(0.09f, 0.01f, Mathf.Abs(u))) * Mathf.Clamp01(1f - Mathf.Abs(u));
                float rayV = Mathf.Clamp01(1f - Mathf.Abs(u) / Mathf.Lerp(0.09f, 0.01f, Mathf.Abs(v))) * Mathf.Clamp01(1f - Mathf.Abs(v));
                float a = Mathf.Clamp01(core + rayH + rayV);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        starSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        starSprite.name = "PickupGlintStar";
        starSprite.hideFlags = HideFlags.DontSave;
        return starSprite;
    }
}
