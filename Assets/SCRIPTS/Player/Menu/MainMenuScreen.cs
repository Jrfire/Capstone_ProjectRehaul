using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The main menu (MainMenu scene): Play (Host = choose one of the <see cref="SaveSystem.SlotCount"/> save slots, then
/// Play or Delete it; Join = Steam friends list), Settings (the shared <see cref="SettingsPanel"/>) and Quit. A pending
/// <see cref="MenuLaunch.TakeNotice"/> message (kicked, host disconnected) is shown on arrival.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuScreen : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject playPanel;
    [SerializeField] private GameObject savesPanel;
    [SerializeField] private GameObject slotActionsPanel;

    [Header("Main")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Play")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button playBackButton;
    [SerializeField, Tooltip("Shows Steam problems and join instructions.")]
    private Text playStatusText;
    [SerializeField] private string joinHint = "Pick a friend in Steam and choose Join Game.";

    [Header("Save slots")]
    [SerializeField] private Button[] slotButtons = new Button[SaveSystem.SlotCount];
    [SerializeField] private Text[] slotTexts = new Text[SaveSystem.SlotCount];
    [SerializeField] private Button savesBackButton;
    [SerializeField] private Text slotTitleText;
    [SerializeField] private Button slotPlayButton;
    [SerializeField] private Button slotDeleteButton;
    [SerializeField] private Text slotDeleteText;
    [SerializeField] private Button slotBackButton;

    [Header("Settings")]
    [SerializeField, Tooltip("Nested SettingsPanel prefab; its Back returns to the main panel.")]
    private SettingsPanel settings;

    [Header("Notice")]
    [SerializeField, Tooltip("Shown when the game returned here for a reason (kicked, host disconnected).")]
    private GameObject noticePanel;
    [SerializeField] private Text noticeText;
    [SerializeField] private Button noticeOkButton;

    private int selectedSlot = -1;
    private bool deleteArmed;

    public int SelectedSlot => selectedSlot;

    private void Awake()
    {
        Bind(playButton, ShowPlay);
        Bind(settingsButton, ShowSettings);
        Bind(quitButton, Quit);
        Bind(hostButton, ShowSaves);
        Bind(joinButton, Join);
        Bind(playBackButton, ShowMain);
        for (int index = 0; index < slotButtons.Length; index++)
        {
            int slot = index;
            Bind(slotButtons[index], () => SelectSlot(slot));
        }
        Bind(savesBackButton, ShowPlay);
        Bind(slotPlayButton, PlaySelectedSlot);
        Bind(slotDeleteButton, DeleteSelectedSlot);
        Bind(slotBackButton, ShowSaves);
        Bind(noticeOkButton, HideNotice);
        if (settings != null)
            settings.BackRequested += ShowMain;
    }

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShowMain();
        HideNotice();
        ShowNotice(MenuLaunch.TakeNotice());
    }

    private void OnDestroy()
    {
        if (settings != null)
            settings.BackRequested -= ShowMain;
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    private GameObject SettingsObject => settings != null ? settings.gameObject : null;

    // ---------------------------------------------------------------- navigation

    private void ShowOnly(GameObject panel)
    {
        if (settings != null)
            settings.CancelRebind();
        foreach (GameObject candidate in new[] { mainPanel, playPanel, savesPanel, slotActionsPanel, SettingsObject })
        {
            if (candidate != null)
                candidate.SetActive(candidate == panel);
        }
    }

    public void ShowMain() => ShowOnly(mainPanel);

    public void ShowPlay()
    {
        ShowOnly(playPanel);
        SetStatus(SteamBootstrap.SteamReady ? string.Empty : SteamBootstrap.FailureMessage);
    }

    public void ShowSaves()
    {
        ShowOnly(savesPanel);
        RefreshSlots();
    }

    public void ShowSettings()
    {
        ShowOnly(SettingsObject);
        if (settings != null)
            settings.Show();
    }

    public GameObject ActivePanel
    {
        get
        {
            foreach (GameObject candidate in new[] { mainPanel, playPanel, savesPanel, slotActionsPanel, SettingsObject })
            {
                if (candidate != null && candidate.activeSelf)
                    return candidate;
            }
            return null;
        }
    }

    private void SetStatus(string message)
    {
        if (playStatusText != null)
            playStatusText.text = message ?? string.Empty;
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Join()
    {
        if (!SteamBootstrap.SteamReady)
        {
            SetStatus(SteamBootstrap.FailureMessage ?? "Steam is not running.");
            return;
        }
        SetStatus(SteamBootstrap.OpenFriendsList() ? joinHint : "Could not open the Steam overlay. Enable it in Steam.");
    }

    // ---------------------------------------------------------------- save slots

    public void RefreshSlots()
    {
        for (int index = 0; index < slotTexts.Length; index++)
        {
            if (slotTexts[index] != null)
                slotTexts[index].text = SaveSystem.Describe(index);
        }
    }

    public void SelectSlot(int slot)
    {
        if (!SaveSystem.IsValidSlot(slot))
            return;
        selectedSlot = slot;
        deleteArmed = false;
        ShowOnly(slotActionsPanel);
        if (slotTitleText != null)
            slotTitleText.text = SaveSystem.Describe(slot);
        if (slotDeleteButton != null)
            slotDeleteButton.interactable = SaveSystem.Exists(slot);
        if (slotDeleteText != null)
            slotDeleteText.text = "Delete";
    }

    public void PlaySelectedSlot()
    {
        if (!SaveSystem.IsValidSlot(selectedSlot))
            return;
        MenuLaunch.Host(selectedSlot);
    }

    /// <summary>First press arms the button ("Confirm Delete"), the second deletes the file.</summary>
    public void DeleteSelectedSlot()
    {
        if (!SaveSystem.Exists(selectedSlot))
            return;
        if (!deleteArmed)
        {
            deleteArmed = true;
            if (slotDeleteText != null)
                slotDeleteText.text = "Confirm Delete";
            return;
        }
        SaveSystem.Delete(selectedSlot);
        ShowSaves();
    }

    // ---------------------------------------------------------------- settings (forwarded for tests and tools)

    public SettingsPanel Settings => settings;
    public bool IsRebinding => settings != null && settings.IsRebinding;
    public int BindingRowCount => settings != null ? settings.BindingRowCount : 0;
    public InputActionAsset RuntimeActions => settings != null ? settings.RuntimeActions : null;
    public void ApplyRebind(int rowIndex, string path) => settings?.ApplyRebind(rowIndex, path);
    public string BindingText(int rowIndex) => settings != null ? settings.BindingText(rowIndex) : null;
    public void ResetBindings() => settings?.ResetBindings();
    public void StartRebind(int rowIndex) => settings?.StartRebind(rowIndex);

    // ---------------------------------------------------------------- notice

    /// <summary>Shows a message (e.g. why the game ended) over the menu until OK is pressed.</summary>
    public void ShowNotice(string message)
    {
        if (noticePanel == null || string.IsNullOrEmpty(message))
            return;
        if (noticeText != null)
            noticeText.text = message;
        noticePanel.SetActive(true);
        noticePanel.transform.SetAsLastSibling();
    }

    public void HideNotice()
    {
        if (noticePanel != null)
            noticePanel.SetActive(false);
    }

    public bool NoticeShown => noticePanel != null && noticePanel.activeSelf;
    public string NoticeMessage => noticeText != null ? noticeText.text : null;
}
