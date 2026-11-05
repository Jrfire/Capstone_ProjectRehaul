using System;
using UnityEngine;

/// <summary>
/// Scene-local moon: put it on the scene's directional moon light. While <see cref="alignMoonToLight"/> is on, the Night
/// Sky moon (and the fog/pixelation moon masks) sits opposite this light's forward direction, so rotating the light moves
/// the moon and the moonlight always comes from it (also in Edit Mode). In Play Mode it applies the normal or blood-moon
/// skybox material and tints this light (and therefore Night Fog's light shafts) red during a blood moon.
/// <see cref="BloodMoon"/> is shared game state that persists across scenes; difficulty code sets it (on every peer)
/// before the scene is shown.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class MoonSky : MonoBehaviour
{
    private static readonly int MoonLightId = Shader.PropertyToID("_NightSkyMoonLight");
    private static MoonSky aligned;
    private static bool bloodMoon;

    [SerializeField, Tooltip("Place the sky's moon where this light shines from. Off = use the material's Moon Azimuth/Elevation.")]
    private bool alignMoonToLight = true;
    [SerializeField, Tooltip("Skybox used on normal nights (e.g. NightSky).")]
    private Material normalSky;
    [SerializeField, Tooltip("Skybox used during a blood moon (e.g. BloodMoonSky).")]
    private Material bloodMoonSky;
    [SerializeField, Tooltip("Moon light color during a blood moon. The normal color is the Light's own color.")]
    private Color bloodMoonLightColor = new Color(1f, 0.12f, 0.08f);
    [SerializeField, Min(0f), Tooltip("Multiplies the Light's own intensity during a blood moon.")]
    private float bloodMoonIntensityMultiplier = 1.5f;

    private Light moonLight;
    private Color normalLightColor;
    private float normalIntensity;
    private bool captured;

    /// <summary>Raised when <see cref="BloodMoon"/> changes.</summary>
    public static event Action<bool> BloodMoonChanged;

    /// <summary>True while the blood moon is up. Persists across scene loads; every MoonSky applies it immediately.</summary>
    public static bool BloodMoon
    {
        get => bloodMoon;
        set
        {
            if (bloodMoon == value)
                return;
            bloodMoon = value;
            BloodMoonChanged?.Invoke(value);
        }
    }

    public Material NormalSky => normalSky;
    public Material BloodMoonSky => bloodMoonSky;
    /// <summary>World direction towards the moon (opposite the light's forward).</summary>
    public Vector3 MoonDirection => -transform.forward;

    private void OnEnable()
    {
        moonLight = GetComponent<Light>();
        UpdateAlignment();
        if (!Application.isPlaying)
            return;
        if (!captured)
        {
            normalLightColor = moonLight.color;
            normalIntensity = moonLight.intensity;
            captured = true;
        }
        BloodMoonChanged += Apply;
        Apply(bloodMoon);
    }

    private void LateUpdate() => UpdateAlignment();

    private void OnValidate()
    {
        if (isActiveAndEnabled)
            UpdateAlignment();
    }

    private void OnDisable()
    {
        if (aligned == this)
        {
            aligned = null;
            Shader.SetGlobalVector(MoonLightId, Vector4.zero);
        }
        if (!Application.isPlaying)
            return;
        BloodMoonChanged -= Apply;
        if (moonLight != null)
        {
            moonLight.color = normalLightColor;
            moonLight.intensity = normalIntensity;
        }
    }

    private void UpdateAlignment()
    {
        if (!alignMoonToLight)
        {
            if (aligned == this)
            {
                aligned = null;
                Shader.SetGlobalVector(MoonLightId, Vector4.zero);
            }
            return;
        }
        aligned = this;
        Vector3 direction = MoonDirection;
        Shader.SetGlobalVector(MoonLightId, new Vector4(direction.x, direction.y, direction.z, 1f));
    }

    private void Apply(bool blood)
    {
        Material sky = blood ? bloodMoonSky : normalSky;
        if (sky != null)
            RenderSettings.skybox = sky;
        moonLight.color = blood ? bloodMoonLightColor : normalLightColor;
        moonLight.intensity = blood ? normalIntensity * bloodMoonIntensityMultiplier : normalIntensity;
    }

    [ContextMenu("Toggle Blood Moon (Play Mode)")]
    private void ToggleBloodMoon() => BloodMoon = !BloodMoon;
}
