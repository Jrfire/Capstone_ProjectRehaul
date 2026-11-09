using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class PlayerCameraEffects : MonoBehaviour
{
    [Header("World pixelation")]
    [Tooltip("Pixelation affects the world, not Screen Space Overlay canvases.")]
    public bool pixelation = true;
    [Min(1), Tooltip("Virtual vertical resolution: 135 gives larger pixels, 270 is the default, 540 gives finer pixels. Changes apply immediately; overlay UI stays crisp.")]
    public int pixelHeight = 270;
    [Range(1f, 4f), Tooltip("Renderers marked with ReducedPixelation (e.g. enemy sprites) use Pixel Height times this value. 1 pixelates them like the world; 2 makes their pixels half as large.")]
    public float enemyPixelDetail = 2f;

    // Runtime-only screen overrides for this camera (e.g. the Santelmo trance); never serialized or shared.
    /// <summary>0..1 world blur.</summary>
    [System.NonSerialized] public float TranceBlur;
    /// <summary>Colour grade towards this colour; alpha is the amount.</summary>
    [System.NonSerialized] public Color TranceTint = Color.clear;
    /// <summary>Full-screen fade of the world image (black/white); alpha is the amount. Overlay UI stays on top.</summary>
    [System.NonSerialized] public Color ScreenFade = Color.clear;
    /// <summary>Night Fog ambient colour override; alpha is the blend weight.</summary>
    [System.NonSerialized] public Color FogAmbientOverride = Color.clear;
    [System.NonSerialized] public float FogDensityMultiplier = 1f;

    public bool HasScreenEffects => TranceBlur > 0f || TranceTint.a > 0f || ScreenFade.a > 0f;

    public void ClearScreenOverrides()
    {
        TranceBlur = 0f;
        TranceTint = Color.clear;
        ScreenFade = Color.clear;
        FogAmbientOverride = Color.clear;
        FogDensityMultiplier = 1f;
    }

    private void OnEnable()
    {
        GameSettings.Changed += ApplySettings;
        ApplySettings();
    }

    private void OnDisable() => GameSettings.Changed -= ApplySettings;

    /// <summary>Uses the Graphics tab's pixel height once the player has set one; otherwise keeps the prefab value.</summary>
    private void ApplySettings()
    {
        if (Application.isPlaying && GameSettings.HasPixelHeight)
            pixelHeight = GameSettings.PixelHeight;
    }
}
