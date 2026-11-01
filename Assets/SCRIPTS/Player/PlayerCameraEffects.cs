using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class PlayerCameraEffects : MonoBehaviour
{
    [Tooltip("Pixelation affects the world, not Screen Space Overlay canvases.")]
    public bool pixelation = true;
    [Min(1), Tooltip("Virtual vertical resolution. Lower values produce larger pixels.")]
    public int pixelHeight = 270;
}
