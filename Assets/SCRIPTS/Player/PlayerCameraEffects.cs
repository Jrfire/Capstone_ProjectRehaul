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
}
