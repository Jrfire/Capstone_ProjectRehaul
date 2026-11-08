using System;
using System.Collections.Generic;
using Capstone.Audio;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;

/// <summary>
/// Player settings saved in PlayerPrefs: graphics (resolution, world pixel height, brightness) and key binding
/// overrides. Volumes are stored by <see cref="AudioManager"/>. Settings apply immediately and on every launch.
/// </summary>
public static class GameSettings
{
    public const int MinPixelHeight = 60;
    public const int MaxPixelHeight = 170;
    public const int DefaultPixelHeight = 170;
    /// <summary>Brightness is a post-exposure offset in EV stops.</summary>
    public const float MinBrightness = -1f;
    public const float MaxBrightness = 1.5f;

    private const string PixelHeightKey = "Capstone.PixelHeight";
    private const string BrightnessKey = "Capstone.Brightness";
    private const string ResolutionWidthKey = "Capstone.ResolutionWidth";
    private const string ResolutionHeightKey = "Capstone.ResolutionHeight";
    private const string BindingsKey = "Capstone.Bindings";

    /// <summary>Raised whenever a graphics setting or a binding changes.</summary>
    public static event Action Changed;

    /// <summary>True once the player has chosen a pixel height; until then cameras keep their prefab value.</summary>
    public static bool HasPixelHeight => PlayerPrefs.HasKey(PixelHeightKey);

    public static int PixelHeight
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(PixelHeightKey, DefaultPixelHeight), MinPixelHeight, MaxPixelHeight);
        set
        {
            PlayerPrefs.SetInt(PixelHeightKey, Mathf.Clamp(value, MinPixelHeight, MaxPixelHeight));
            Changed?.Invoke();
        }
    }

    public static float Brightness
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(BrightnessKey, 0f), MinBrightness, MaxBrightness);
        set
        {
            PlayerPrefs.SetFloat(BrightnessKey, Mathf.Clamp(value, MinBrightness, MaxBrightness));
            BrightnessVolume.Refresh();
            Changed?.Invoke();
        }
    }

    // ---------------------------------------------------------------- resolution

    /// <summary>The monitor's native resolution.</summary>
    public static Vector2Int NativeResolution
    {
        get
        {
            int width = Display.main != null ? Display.main.systemWidth : Screen.currentResolution.width;
            int height = Display.main != null ? Display.main.systemHeight : Screen.currentResolution.height;
            return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
        }
    }

    /// <summary>The saved resolution, or the native one when none is saved.</summary>
    public static Vector2Int Resolution
    {
        get
        {
            if (!PlayerPrefs.HasKey(ResolutionWidthKey) || !PlayerPrefs.HasKey(ResolutionHeightKey))
                return NativeResolution;
            return new Vector2Int(PlayerPrefs.GetInt(ResolutionWidthKey), PlayerPrefs.GetInt(ResolutionHeightKey));
        }
    }

    /// <summary>Distinct screen sizes offered on the Graphics tab (largest first), always including native.</summary>
    public static List<Vector2Int> AvailableResolutions()
    {
        var sizes = new List<Vector2Int>();
        foreach (UnityEngine.Resolution resolution in Screen.resolutions)
        {
            var size = new Vector2Int(resolution.width, resolution.height);
            if (!sizes.Contains(size))
                sizes.Add(size);
        }
        Vector2Int native = NativeResolution;
        if (!sizes.Contains(native))
            sizes.Add(native);
        sizes.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
        return sizes;
    }

    public static void SetResolution(Vector2Int size)
    {
        PlayerPrefs.SetInt(ResolutionWidthKey, size.x);
        PlayerPrefs.SetInt(ResolutionHeightKey, size.y);
        ApplyResolution();
        Changed?.Invoke();
    }

    /// <summary>Applies the saved (or native) resolution; the editor's Game view is left alone.</summary>
    public static void ApplyResolution()
    {
        if (Application.isEditor)
            return;
        Vector2Int size = Resolution;
        if (Screen.width != size.x || Screen.height != size.y)
            Screen.SetResolution(size.x, size.y, Screen.fullScreenMode);
    }

    // ---------------------------------------------------------------- volume

    public static float GetVolume(AudioChannel channel) =>
        AudioManager.Instance != null ? AudioManager.Instance.GetVolume(channel) : PlayerPrefs.GetFloat(AudioManager.PrefsKey(channel), 1f);

    /// <summary>Saves a channel's linear volume and applies it to the running AudioManager or, without one, the mixer.</summary>
    public static void SetVolume(AudioChannel channel, float linear, AudioMixer fallbackMixer = null)
    {
        linear = Mathf.Clamp01(linear);
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetVolume(channel, linear);
            return;
        }
        PlayerPrefs.SetFloat(AudioManager.PrefsKey(channel), linear);
        if (fallbackMixer != null)
            fallbackMixer.SetFloat(AudioManager.VolumeParameter(channel), AudioManager.LinearToDecibels(linear));
    }

    // ---------------------------------------------------------------- bindings

    [Serializable]
    private sealed class BindingOverride
    {
        public string id;
        public string path;
    }

    [Serializable]
    private sealed class BindingOverrideList
    {
        public List<BindingOverride> overrides = new List<BindingOverride>();
    }

    private static Dictionary<string, string> cachedOverrides;

    private static Dictionary<string, string> Overrides
    {
        get
        {
            if (cachedOverrides != null)
                return cachedOverrides;
            cachedOverrides = new Dictionary<string, string>();
            string json = PlayerPrefs.GetString(BindingsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    BindingOverrideList list = JsonUtility.FromJson<BindingOverrideList>(json);
                    if (list?.overrides != null)
                    {
                        foreach (BindingOverride entry in list.overrides)
                        {
                            if (!string.IsNullOrEmpty(entry.id) && !string.IsNullOrEmpty(entry.path))
                                cachedOverrides[entry.id] = entry.path;
                        }
                    }
                }
                catch (ArgumentException)
                {
                    Debug.LogWarning("Ignoring unreadable saved key bindings.");
                }
            }
            return cachedOverrides;
        }
    }

    public static int BindingOverrideCount => Overrides.Count;

    /// <summary>Applies saved rebinds to an action (e.g. a player's runtime clone). Matching is by binding id.</summary>
    public static void ApplyBindingOverrides(InputAction action)
    {
        if (action == null)
            return;
        bool wasEnabled = action.enabled;
        if (wasEnabled)
            action.Disable();
        for (int index = 0; index < action.bindings.Count; index++)
        {
            InputBinding binding = action.bindings[index];
            if (Overrides.TryGetValue(binding.id.ToString(), out string path))
                action.ApplyBindingOverride(index, path);
            else if (!string.IsNullOrEmpty(binding.overridePath))
                action.RemoveBindingOverride(index);
        }
        if (wasEnabled)
            action.Enable();
    }

    /// <summary>Applies saved rebinds to every action of an asset (or a runtime instance of one).</summary>
    public static void ApplyBindingOverrides(InputActionAsset asset)
    {
        if (asset == null)
            return;
        foreach (InputActionMap map in asset.actionMaps)
        {
            foreach (InputAction action in map.actions)
                ApplyBindingOverrides(action);
        }
    }

    private static readonly List<WeakReference<InputAction>> trackedActions = new List<WeakReference<InputAction>>();
    private static readonly List<WeakReference<InputActionAsset>> trackedAssets = new List<WeakReference<InputActionAsset>>();

    /// <summary>
    /// Applies saved rebinds to a gameplay action clone and keeps it updated when bindings change later (e.g. from
    /// the pause menu). Call right after cloning an action.
    /// </summary>
    public static InputAction TrackBindings(InputAction action)
    {
        if (action == null)
            return null;
        ApplyBindingOverrides(action);
        trackedActions.Add(new WeakReference<InputAction>(action));
        return action;
    }

    /// <summary>As <see cref="TrackBindings(InputAction)"/> for a runtime instance of an actions asset.</summary>
    public static InputActionAsset TrackBindings(InputActionAsset asset)
    {
        if (asset == null)
            return null;
        ApplyBindingOverrides(asset);
        trackedAssets.Add(new WeakReference<InputActionAsset>(asset));
        return asset;
    }

    private static void RefreshTrackedBindings()
    {
        for (int index = trackedActions.Count - 1; index >= 0; index--)
        {
            if (!trackedActions[index].TryGetTarget(out InputAction action) || !TryApply(() => ApplyBindingOverrides(action)))
                trackedActions.RemoveAt(index);
        }
        for (int index = trackedAssets.Count - 1; index >= 0; index--)
        {
            // Destroyed assets compare equal to null through UnityEngine.Object.
            if (!trackedAssets[index].TryGetTarget(out InputActionAsset asset) || asset == null ||
                !TryApply(() => ApplyBindingOverrides(asset)))
                trackedAssets.RemoveAt(index);
        }
    }

    private static bool TryApply(Action apply)
    {
        try
        {
            apply();
            return true;
        }
        catch (Exception exception) when (exception is ObjectDisposedException || exception is InvalidOperationException ||
                                          exception is NullReferenceException)
        {
            // Disposed clones (despawned players) are dropped.
            return false;
        }
    }

    /// <summary>Saves an action's current binding override (set after an interactive rebind).</summary>
    public static void SaveBindingOverride(InputAction action, int bindingIndex)
    {
        InputBinding binding = action.bindings[bindingIndex];
        string id = binding.id.ToString();
        if (string.IsNullOrEmpty(binding.overridePath) || binding.overridePath == binding.path)
            Overrides.Remove(id);
        else
            Overrides[id] = binding.overridePath;
        WriteOverrides();
    }

    /// <summary>Clears every saved rebind and the asset's overrides.</summary>
    public static void ResetBindings(InputActionAsset asset)
    {
        Overrides.Clear();
        WriteOverrides();
        asset?.RemoveAllBindingOverrides();
    }

    private static void WriteOverrides()
    {
        var list = new BindingOverrideList();
        foreach (KeyValuePair<string, string> pair in Overrides)
            list.overrides.Add(new BindingOverride { id = pair.Key, path = pair.Value });
        PlayerPrefs.SetString(BindingsKey, JsonUtility.ToJson(list));
        PlayerPrefs.Save();
        RefreshTrackedBindings();
        Changed?.Invoke();
    }

    /// <summary>Forgets cached values so the next read comes from PlayerPrefs (tests).</summary>
    public static void ReloadFromPrefs() => cachedOverrides = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnLaunch()
    {
        ApplyResolution();
        BrightnessVolume.Refresh();
    }
}
