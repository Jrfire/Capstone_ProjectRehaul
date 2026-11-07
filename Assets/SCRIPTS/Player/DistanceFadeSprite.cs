using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fades and darkens billboarded world sprites with distance from the local viewer, so bright (unlit) sprites cannot
/// be seen beyond the night fog. Local only: every peer evaluates its own camera and changes only its own renderers and
/// material copies; nothing is networked. Add it to enemies, items or any other object with billboarded sprites.
/// While faded, Capstone/Solid Sprite renderers use a local transparent copy of their material (no depth writes),
/// so the fade blends smoothly instead of dithering; fully opaque sprites keep the depth-writing solid material.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class DistanceFadeSprite : MonoBehaviour
{
    private const float OpaqueThreshold = 0.999f;
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly Dictionary<Material, Material> fadedMaterials = new Dictionary<Material, Material>();

    [SerializeField, Tooltip("Sprites to fade. Leave empty to use every SpriteRenderer in this object's children.")]
    private SpriteRenderer[] renderers = new SpriteRenderer[0];

    [Header("Opacity")]
    [SerializeField, Min(0f), Tooltip("Up to this camera distance (metres) the sprites are completely opaque.")]
    private float solidDistance = 12f;
    [SerializeField, Min(0f), Tooltip("From this camera distance (metres) on the sprites stay at Minimum Opacity.")]
    private float fadedDistance = 30f;
    [SerializeField, Range(0f, 1f), Tooltip("Opacity at and beyond Faded Distance.")]
    private float minimumOpacity = 0.01f;

    [Header("Darkening")]
    [SerializeField, Min(0f), Tooltip("Up to this camera distance (metres) the sprites keep their full colour.")]
    private float darkenStartDistance = 4f;
    [SerializeField, Min(0f), Tooltip("From this camera distance (metres) on the colour stays at Minimum Brightness.")]
    private float darkestDistance = 25f;
    [SerializeField, Range(0f, 1f), Tooltip("Colour multiplier at and beyond Darkest Distance (0 = black).")]
    private float minimumBrightness = 0.15f;

    private readonly List<SpriteRenderer> targets = new List<SpriteRenderer>();
    private readonly List<Color> baseColors = new List<Color>();
    private readonly List<Material> solidMaterials = new List<Material>();
    private NetworkIdentity cachedViewerOwner;
    private Camera cachedViewer;

    public float SolidDistance => solidDistance;
    public float FadedDistance => fadedDistance;
    public float MinimumOpacity => minimumOpacity;
    public float DarkenStartDistance => darkenStartDistance;
    public float DarkestDistance => darkestDistance;
    public float MinimumBrightness => minimumBrightness;
    /// <summary>The opacity applied on the last refresh (1 when no viewer exists).</summary>
    public float Opacity { get; private set; } = 1f;
    /// <summary>The colour multiplier applied on the last refresh (1 when no viewer exists).</summary>
    public float Brightness { get; private set; } = 1f;
    /// <summary>Extra 0-1 opacity factor set by other scripts (e.g. the White Lady's flicker); multiplies the distance fade.</summary>
    public float OpacityMultiplier { get; set; } = 1f;

    private void Awake()
    {
        if (renderers != null && renderers.Length > 0)
        {
            foreach (SpriteRenderer target in renderers)
            {
                if (target != null && !targets.Contains(target))
                    targets.Add(target);
            }
        }
        else
        {
            targets.AddRange(GetComponentsInChildren<SpriteRenderer>(true));
        }
        foreach (SpriteRenderer target in targets)
        {
            baseColors.Add(target.color);
            solidMaterials.Add(target.sharedMaterial);
        }
    }

    private void OnEnable() => Refresh();

    private void LateUpdate() => Refresh();

    private void OnDisable()
    {
        // Restore the original look fully, ignoring any external multiplier.
        float multiplier = OpacityMultiplier;
        OpacityMultiplier = 1f;
        Apply(1f, 1f);
        OpacityMultiplier = multiplier;
    }

    private void OnValidate()
    {
        fadedDistance = Mathf.Max(fadedDistance, solidDistance);
        darkestDistance = Mathf.Max(darkestDistance, darkenStartDistance);
    }

    /// <summary>Eases from 1 (at or below <paramref name="start"/>) to <paramref name="minimum"/> (at or beyond <paramref name="end"/>).</summary>
    public static float EvaluateFalloff(float distance, float start, float end, float minimum)
    {
        if (end <= start)
            return distance <= start ? 1f : minimum;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, distance));
        return Mathf.Lerp(1f, minimum, t);
    }

    /// <summary>Opacity for a viewer distance: solid up to <paramref name="solid"/>, eased down to <paramref name="minimum"/> at <paramref name="faded"/>.</summary>
    public static float EvaluateOpacity(float distance, float solid, float faded, float minimum) =>
        EvaluateFalloff(distance, solid, faded, minimum);

    /// <summary>Re-evaluates the distance to the local viewer and applies the opacity and darkening immediately.</summary>
    public void Refresh()
    {
        Camera viewer = ResolveViewer();
        if (viewer == null)
        {
            Apply(1f, 1f);
            return;
        }
        Vector3 eye = viewer.transform.position;
        float distance = float.PositiveInfinity;
        foreach (SpriteRenderer target in targets)
        {
            if (target != null)
                distance = Mathf.Min(distance, Vector3.Distance(eye, target.bounds.center));
        }
        if (float.IsPositiveInfinity(distance))
        {
            Apply(1f, 1f);
            return;
        }
        Apply(EvaluateFalloff(distance, solidDistance, fadedDistance, minimumOpacity),
            EvaluateFalloff(distance, darkenStartDistance, darkestDistance, minimumBrightness));
    }

    private void Apply(float opacity, float brightness)
    {
        opacity *= Mathf.Clamp01(OpacityMultiplier);
        Opacity = opacity;
        Brightness = brightness;
        bool faded = opacity < OpaqueThreshold;
        for (int i = 0; i < targets.Count; i++)
        {
            SpriteRenderer target = targets[i];
            if (target == null)
                continue;
            Color baseColor = baseColors[i];
            target.color = new Color(baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness,
                baseColor.a * opacity);
            Material material = faded ? GetFadedMaterial(solidMaterials[i]) : solidMaterials[i];
            if (target.sharedMaterial != material)
                target.sharedMaterial = material;
        }
    }

    /// <summary>
    /// A transparent copy of a depth-writing Solid Sprite material (shared by every faded renderer that uses it). Other
    /// shaders are returned unchanged because they already blend the renderer colour's alpha.
    /// </summary>
    public static Material GetFadedMaterial(Material solid)
    {
        if (solid == null || !solid.HasProperty(ZWriteId))
            return solid;
        if (fadedMaterials.TryGetValue(solid, out Material faded) && faded != null)
            return faded;
        faded = new Material(solid)
        {
            name = solid.name + " (Distance Faded)",
            hideFlags = HideFlags.DontSave,
            renderQueue = (int)RenderQueue.Transparent,
        };
        faded.SetFloat(ZWriteId, 0f);
        faded.SetOverrideTag("RenderType", "Transparent");
        faded.SetShaderPassEnabled("DepthOnly", false);
        faded.SetShaderPassEnabled("DepthNormalsOnly", false);
        fadedMaterials[solid] = faded;
        return faded;
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
}
