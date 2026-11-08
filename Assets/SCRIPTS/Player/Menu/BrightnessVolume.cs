using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies <see cref="GameSettings.Brightness"/>: a persistent, high-priority global Volume whose Color Adjustments
/// post exposure is the scene's own post exposure plus the brightness offset. Inactive while the offset is zero, so
/// scenes look exactly as authored by default.
/// </summary>
public sealed class BrightnessVolume : MonoBehaviour
{
    private const float Priority = 1000f;

    private static BrightnessVolume instance;
    private Volume volume;
    private VolumeProfile profile;
    private ColorAdjustments adjustments;

    /// <summary>The post exposure currently applied by the override (scene value plus offset).</summary>
    public static float AppliedExposure => instance != null && instance.adjustments != null ? instance.adjustments.postExposure.value : 0f;
    public static bool Active => instance != null && instance.volume != null && instance.volume.enabled;

    /// <summary>Re-reads the brightness setting and the scene's post exposure.</summary>
    public static void Refresh()
    {
        if (!Application.isPlaying)
            return;
        if (instance == null)
        {
            if (Mathf.Approximately(GameSettings.Brightness, 0f))
                return;
            var host = new GameObject("BrightnessVolume") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(host);
            instance = host.AddComponent<BrightnessVolume>();
        }
        instance.Apply();
    }

    private void Awake()
    {
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Brightness (runtime)";
        adjustments = profile.Add<ColorAdjustments>();
        adjustments.postExposure.overrideState = true;
        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.sharedProfile = profile;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (profile != null)
            Destroy(profile);
        if (instance == this)
            instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    private void Apply()
    {
        float offset = GameSettings.Brightness;
        adjustments.postExposure.value = SceneExposure() + offset;
        volume.enabled = !Mathf.Approximately(offset, 0f);
    }

    /// <summary>The post exposure of the highest-priority other global volume that overrides it (0 when none).</summary>
    private float SceneExposure()
    {
        float best = 0f;
        float bestPriority = float.NegativeInfinity;
        foreach (Volume other in FindObjectsByType<Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (other == volume || !other.isGlobal || !other.enabled || other.priority < bestPriority)
                continue;
            VolumeProfile otherProfile = other.sharedProfile;
            if (otherProfile != null && otherProfile.TryGet(out ColorAdjustments color) && color.active &&
                color.postExposure.overrideState)
            {
                best = color.postExposure.value;
                bestPriority = other.priority;
            }
        }
        return best;
    }
}
