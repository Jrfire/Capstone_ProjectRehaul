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
