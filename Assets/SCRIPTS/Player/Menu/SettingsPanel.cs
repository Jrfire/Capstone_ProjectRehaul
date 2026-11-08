using System;
using System.Collections.Generic;
using Capstone.Audio;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shared settings UI (<c>Assets/PREFABS/SettingsPanel.prefab</c>, nested in the main menu and the pause menu):
/// Sounds / Controls / Graphics tabs stored through <see cref="GameSettings"/>. Rebinding rows are generated from
/// <see cref="inputActions"/>' Player map; saved rebinds also update the live player's actions immediately.
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsPanel : MonoBehaviour
{
    [Header("Tabs")]
    [SerializeField] private Button soundsTabButton;
    [SerializeField] private Button controlsTabButton;
    [SerializeField] private Button graphicsTabButton;
    [SerializeField] private GameObject soundsPage;
    [SerializeField] private GameObject controlsPage;
    [SerializeField] private GameObject graphicsPage;
    [SerializeField, Tooltip("Raises BackRequested; the owning menu decides where to go.")]
    private Button backButton;

    [Header("Sounds")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider uiSlider;
    [SerializeField] private Slider jumpscareSlider;

    [Header("Controls")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string actionMapName = "Player";
    [SerializeField, Tooltip("Parent of the generated rows (a ScrollRect's content).")]
    private RectTransform bindingRowParent;
    [SerializeField, Tooltip("Inactive row: a Label Text, then a primary and an alternate Button whose child Text shows the key.")]
    private GameObject bindingRowTemplate;
    [SerializeField, Tooltip("Shown on an alternate slot with no key assigned.")]
    private string unboundText = "None";
    [SerializeField] private Button resetBindingsButton;
    [SerializeField] private string waitingForKeyText = "Press a key... (Esc cancels)";
    [SerializeField, Tooltip("Actions not offered for rebinding (pointer motion).")]
    private string[] hiddenActions = { "Look", "InventoryScroll" };

    [Header("Graphics")]
    [SerializeField] private Dropdown resolutionDropdown;
    [SerializeField] private Slider pixelSlider;
    [SerializeField] private Text pixelValueText;
    [SerializeField] private Slider brightnessSlider;
    [SerializeField] private Text brightnessValueText;

    private sealed class BindingRow
    {
        public InputAction action;
        public int bindingIndex;
        public Text keyText;
        public Button button;
    }

    private static readonly Dictionary<string, string> ActionLabels = new Dictionary<string, string>
    {
        { "UseItem", "Use / Attack" },
    };

    /// <summary>Every rebindable binding (primary and alternate slots), in row order.</summary>
    private readonly List<BindingRow> bindingRows = new List<BindingRow>();
    private int controlRowCount;
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    private InputActionAsset runtimeActions;
    private InputActionRebindingExtensions.RebindingOperation rebinding;
    private bool initialized;
    private int rebindEndFrame = -10;

    /// <summary>The Back button was pressed.</summary>
    public event Action BackRequested;

    public bool IsRebinding => rebinding != null;

    /// <summary>True while rebinding or on the frames right after it ended (its Escape must not also close menus).</summary>
    public bool ConsumesEscape => rebinding != null || Time.frameCount - rebindEndFrame <= 1;

    public int BindingRowCount
    {
        get
        {
            EnsureInitialized();
            return bindingRows.Count;
        }
    }

    /// <summary>Rows on the Controls page (one per action or movement direction, each with primary + alternate).</summary>
    public int ControlRowCount
    {
        get
        {
            EnsureInitialized();
            return controlRowCount;
        }
    }

    /// <summary>Binding slot index for an action (and composite part, e.g. "up"), or -1.</summary>
    public int FindBindingSlot(string actionName, bool alternate, string part = null)
    {
        EnsureInitialized();
        int found = 0;
        for (int index = 0; index < bindingRows.Count; index++)
        {
            BindingRow row = bindingRows[index];
            InputBinding binding = row.action.bindings[row.bindingIndex];
            if (row.action.name != actionName || (part != null && binding.name != part))
                continue;
            if (found++ == (alternate ? 1 : 0))
                return index;
        }
        return -1;
    }

    public InputActionAsset RuntimeActions
    {
        get
        {
            EnsureInitialized();
            return runtimeActions;
        }
    }

    public Button BackButton => backButton;
    public GameObject SoundsPage => soundsPage;
    public GameObject ControlsPage => controlsPage;
    public GameObject GraphicsPage => graphicsPage;

    private void Awake() => EnsureInitialized();

    private void OnEnable()
    {
        EnsureInitialized();
        RefreshValues();
    }

    private void OnDisable() => CancelRebind();

    private void OnDestroy()
    {
        CancelRebind();
        if (runtimeActions != null)
            Destroy(runtimeActions);
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;
        initialized = true;
        Bind(soundsTabButton, () => ShowTab(soundsPage));
        Bind(controlsTabButton, () => ShowTab(controlsPage));
        Bind(graphicsTabButton, () => ShowTab(graphicsPage));
        Bind(backButton, () =>
        {
            CancelRebind();
            BackRequested?.Invoke();
        });
        Bind(resetBindingsButton, ResetBindings);

        BindSlider(masterSlider, AudioChannel.Master);
        BindSlider(sfxSlider, AudioChannel.Sfx);
        BindSlider(musicSlider, AudioChannel.Music);
        BindSlider(uiSlider, AudioChannel.UI);
        BindSlider(jumpscareSlider, AudioChannel.Jumpscare);

        if (pixelSlider != null)
        {
            pixelSlider.wholeNumbers = true;
            pixelSlider.minValue = GameSettings.MinPixelHeight;
            pixelSlider.maxValue = GameSettings.MaxPixelHeight;
            pixelSlider.onValueChanged.AddListener(value =>
            {
                GameSettings.PixelHeight = Mathf.RoundToInt(value);
                RefreshGraphicsTexts();
            });
        }
        if (brightnessSlider != null)
        {
            brightnessSlider.wholeNumbers = false;
            brightnessSlider.minValue = GameSettings.MinBrightness;
            brightnessSlider.maxValue = GameSettings.MaxBrightness;
            brightnessSlider.onValueChanged.AddListener(value =>
            {
                GameSettings.Brightness = value;
                RefreshGraphicsTexts();
            });
        }
        SetupResolutions();
        BuildBindingRows();
        RefreshValues();
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    private void BindSlider(Slider slider, AudioChannel channel)
    {
        if (slider == null)
            return;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(value => GameSettings.SetVolume(channel, value, audioMixer));
        if (audioMixer != null)
            audioMixer.SetFloat(AudioManager.VolumeParameter(channel), AudioManager.LinearToDecibels(GameSettings.GetVolume(channel)));
    }

    /// <summary>Re-reads every stored value (another settings panel may have changed them).</summary>
    public void RefreshValues()
    {
        SetSlider(masterSlider, GameSettings.GetVolume(AudioChannel.Master));
        SetSlider(sfxSlider, GameSettings.GetVolume(AudioChannel.Sfx));
        SetSlider(musicSlider, GameSettings.GetVolume(AudioChannel.Music));
        SetSlider(uiSlider, GameSettings.GetVolume(AudioChannel.UI));
        SetSlider(jumpscareSlider, GameSettings.GetVolume(AudioChannel.Jumpscare));
        SetSlider(pixelSlider, GameSettings.PixelHeight);
        SetSlider(brightnessSlider, GameSettings.Brightness);
        if (resolutionDropdown != null)
        {
            int index = resolutions.IndexOf(GameSettings.Resolution);
            if (index >= 0)
                resolutionDropdown.SetValueWithoutNotify(index);
        }
        RefreshGraphicsTexts();
        if (runtimeActions != null)
            GameSettings.ApplyBindingOverrides(runtimeActions);
        RefreshBindingTexts();
    }

    private static void SetSlider(Slider slider, float value)
    {
        if (slider != null)
            slider.SetValueWithoutNotify(value);
    }

    // ---------------------------------------------------------------- tabs

    /// <summary>Activates the panel on its first (Sounds) tab.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        EnsureInitialized();
        ShowTab(soundsPage);
    }

    public void Hide()
    {
        CancelRebind();
        gameObject.SetActive(false);
    }

    public void ShowTab(GameObject page)
    {
        CancelRebind();
        foreach (GameObject candidate in new[] { soundsPage, controlsPage, graphicsPage })
        {
            if (candidate != null)
                candidate.SetActive(candidate == page);
        }
        if (page == controlsPage)
            RefreshBindingTexts();
    }

    // ---------------------------------------------------------------- graphics

    private void SetupResolutions()
    {
        if (resolutionDropdown == null)
            return;
        resolutions.Clear();
        resolutions.AddRange(GameSettings.AvailableResolutions());
        Vector2Int current = GameSettings.Resolution;
        if (!resolutions.Contains(current))
            resolutions.Insert(0, current);
        Vector2Int native = GameSettings.NativeResolution;
        var options = new List<string>();
        foreach (Vector2Int size in resolutions)
            options.Add(size == native ? $"{size.x} x {size.y} (Native)" : $"{size.x} x {size.y}");
        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(options);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, resolutions.IndexOf(current)));
        resolutionDropdown.onValueChanged.AddListener(index =>
        {
            if (index >= 0 && index < resolutions.Count)
                GameSettings.SetResolution(resolutions[index]);
        });
    }

    private void RefreshGraphicsTexts()
    {
        if (pixelValueText != null)
            pixelValueText.text = GameSettings.PixelHeight.ToString();
        if (brightnessValueText != null)
        {
            float brightness = GameSettings.Brightness;
            brightnessValueText.text = (brightness >= 0f ? "+" : string.Empty) + brightness.ToString("0.0");
        }
    }

    // ---------------------------------------------------------------- controls

    private void BuildBindingRows()
    {
        bindingRows.Clear();
        controlRowCount = 0;
        if (inputActions == null || bindingRowParent == null || bindingRowTemplate == null)
            return;
        runtimeActions = Instantiate(inputActions);
        GameSettings.ApplyBindingOverrides(runtimeActions);
        InputActionMap map = runtimeActions.FindActionMap(actionMapName);
        if (map == null)
            return;
        bindingRowTemplate.SetActive(false);
        foreach (InputAction action in map.actions)
        {
            if (Array.IndexOf(hiddenActions, action.name) >= 0)
                continue;
            // Keyboard/mouse bindings grouped per composite part (or the whole action): first = primary, second = alternate.
            var groups = new List<KeyValuePair<string, List<int>>>();
            for (int index = 0; index < action.bindings.Count; index++)
            {
                InputBinding binding = action.bindings[index];
                if (binding.isComposite || !IsKeyboardMouse(binding))
                    continue;
                string part = binding.isPartOfComposite ? binding.name : string.Empty;
                int groupIndex = groups.FindIndex(group => group.Key == part);
                if (groupIndex < 0)
                {
                    groups.Add(new KeyValuePair<string, List<int>>(part, new List<int>()));
                    groupIndex = groups.Count - 1;
                }
                groups[groupIndex].Value.Add(index);
            }
            foreach (KeyValuePair<string, List<int>> group in groups)
            {
                string label = ActionLabels.TryGetValue(action.name, out string custom) ? custom : Nicify(action.name);
                if (group.Key.Length > 0)
                    label += " " + Nicify(group.Key);
                CreateRow(action, group.Value[0], group.Value.Count > 1 ? group.Value[1] : -1, label);
            }
        }
        RefreshBindingTexts();
    }

    private static bool IsKeyboardMouse(InputBinding binding)
    {
        if (!string.IsNullOrEmpty(binding.groups) && binding.groups.Contains("Keyboard&Mouse"))
            return true;
        string path = binding.effectivePath ?? binding.path ?? string.Empty;
        return path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>");
    }

    private static string Nicify(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;
        var builder = new System.Text.StringBuilder();
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (index > 0 && ((char.IsUpper(character) && !char.IsUpper(name[index - 1])) ||
                              (char.IsDigit(character) && !char.IsDigit(name[index - 1]))))
                builder.Append(' ');
            builder.Append(index == 0 ? char.ToUpperInvariant(character) : character);
        }
        return builder.ToString();
    }

    private void CreateRow(InputAction action, int primaryIndex, int alternateIndex, string label)
    {
        GameObject rowObject = Instantiate(bindingRowTemplate, bindingRowParent);
        rowObject.name = $"Binding {label}";
        rowObject.SetActive(true);
        controlRowCount++;
        Button[] buttons = rowObject.GetComponentsInChildren<Button>(true);
        foreach (Text text in rowObject.GetComponentsInChildren<Text>(true))
        {
            if (Array.TrueForAll(buttons, button => !text.transform.IsChildOf(button.transform)))
            {
                text.text = label;
                break;
            }
        }
        AddSlot(action, primaryIndex, buttons.Length > 0 ? buttons[0] : null);
        Button alternateButton = buttons.Length > 1 ? buttons[1] : null;
        if (alternateIndex >= 0)
            AddSlot(action, alternateIndex, alternateButton);
        else if (alternateButton != null)
        {
            alternateButton.interactable = false;
            Text text = alternateButton.GetComponentInChildren<Text>(true);
            if (text != null)
                text.text = "-";
        }
    }

    private void AddSlot(InputAction action, int bindingIndex, Button button)
    {
        Text keyText = button != null ? button.GetComponentInChildren<Text>(true) : null;
        var row = new BindingRow { action = action, bindingIndex = bindingIndex, keyText = keyText, button = button };
        bindingRows.Add(row);
        if (button != null)
            button.onClick.AddListener(() => StartRebind(row));
    }

    public void RefreshBindingTexts()
    {
        foreach (BindingRow row in bindingRows)
        {
            if (row.keyText == null)
                continue;
            string display = row.action.GetBindingDisplayString(row.bindingIndex);
            row.keyText.text = string.IsNullOrEmpty(display) ? unboundText : display;
        }
    }

    /// <summary>Starts rebinding a generated row (index in generation order).</summary>
    public void StartRebind(int rowIndex)
    {
        EnsureInitialized();
        if (rowIndex >= 0 && rowIndex < bindingRows.Count)
            StartRebind(bindingRows[rowIndex]);
    }

    private void StartRebind(BindingRow row)
    {
        CancelRebind();
        row.action.Disable();
        if (row.keyText != null)
            row.keyText.text = waitingForKeyText;
        rebinding = row.action.PerformInteractiveRebinding(row.bindingIndex)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(operation =>
            {
                GameSettings.SaveBindingOverride(row.action, row.bindingIndex);
                FinishRebind();
            })
            .OnCancel(operation => FinishRebind())
            .Start();
    }

    /// <summary>Applies a rebind without waiting for input (tests and tools).</summary>
    public void ApplyRebind(int rowIndex, string path)
    {
        EnsureInitialized();
        if (rowIndex < 0 || rowIndex >= bindingRows.Count)
            return;
        BindingRow row = bindingRows[rowIndex];
        row.action.ApplyBindingOverride(row.bindingIndex, path);
        GameSettings.SaveBindingOverride(row.action, row.bindingIndex);
        RefreshBindingTexts();
    }

    public string BindingText(int rowIndex)
    {
        EnsureInitialized();
        return rowIndex >= 0 && rowIndex < bindingRows.Count && bindingRows[rowIndex].keyText != null
            ? bindingRows[rowIndex].keyText.text
            : null;
    }

    private void FinishRebind()
    {
        rebinding?.Dispose();
        rebinding = null;
        rebindEndFrame = Time.frameCount;
        RefreshBindingTexts();
    }

    public void CancelRebind()
    {
        if (rebinding == null)
            return;
        rebinding.Cancel();
        rebinding?.Dispose();
        rebinding = null;
        rebindEndFrame = Time.frameCount;
        RefreshBindingTexts();
    }

    public void ResetBindings()
    {
        EnsureInitialized();
        CancelRebind();
        GameSettings.ResetBindings(runtimeActions);
        RefreshBindingTexts();
    }
}
