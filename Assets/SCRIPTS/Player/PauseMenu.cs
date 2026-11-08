using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game pause menu (<c>Assets/PREFABS/PauseMenu.prefab</c>, a persistent root in SampleScene). Escape opens it
/// whenever a session runs and no other menu used that Escape press: a dark overlay with Resume, Options (the shared
/// <see cref="SettingsPanel"/>) and Quit, plus the connected players on the right. The host sees an X beside every
/// guest that kicks them. The game keeps running (multiplayer); while open the local player's controls are locked.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class PauseMenu : MonoBehaviour
{
    [SerializeField, Tooltip("Overlay and panels; inactive while closed.")]
    private GameObject screenRoot;
    [SerializeField, Tooltip("Resume / Options / Quit and the player list.")]
    private GameObject mainPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private SettingsPanel settings;

    [Header("Players")]
    [SerializeField, Tooltip("Parent of the generated player rows (a vertical layout).")]
    private RectTransform playerRowParent;
    [SerializeField, Tooltip("Inactive row: a Name Text and a Kick Button (shown only to the host, never on the host's own row).")]
    private GameObject playerRowTemplate;
    [SerializeField, Min(0.1f)] private float listRefreshSeconds = 0.5f;

    private sealed class PlayerRow
    {
        public GameObject root;
        public Text nameText;
        public Button kickButton;
        public NetworkIdentity identity;
    }

    private readonly List<PlayerRow> rows = new List<PlayerRow>();
    private float nextListRefresh;

    public static PauseMenu Instance { get; private set; }

    public bool IsOpen => screenRoot != null && screenRoot.activeSelf;
    public bool OptionsOpen => IsOpen && settings != null && settings.gameObject.activeSelf;
    public SettingsPanel Settings => settings;
    public int PlayerRowCount => rows.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);
        if (playerRowTemplate != null)
            playerRowTemplate.SetActive(false);
        Bind(resumeButton, Close);
        Bind(optionsButton, ShowOptions);
        Bind(quitButton, Quit);
        if (settings != null)
            settings.BackRequested += ShowMain;
        SetVisible(false);
    }

    private void OnDisable()
    {
        if (Instance == this)
            SetVisible(false);
    }

    private void OnDestroy()
    {
        if (settings != null)
            settings.BackRequested -= ShowMain;
        if (Instance != this)
            return;
        PlayerControlLock.Release(this);
        Instance = null;
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    /// <summary>A multiplayer session (host or guest) is running and the game is not returning to the menu.</summary>
    public static bool SessionActive => (NetworkClient.active || NetworkServer.active) && !MenuLaunch.ReturningToMenu;

    private void Update()
    {
        if (IsOpen && !SessionActive)
        {
            Close();
            return;
        }

        bool settingsUsedEscape = settings != null && settings.ConsumesEscape;
        if (!settingsUsedEscape && MenuEscape.PressedThisFrame && !MenuEscape.ConsumedThisFrame &&
            (IsOpen || SessionActive))
        {
            MenuEscape.Consume();
            if (OptionsOpen)
                ShowMain();
            else if (IsOpen)
                Close();
            else
                Open();
        }

        if (IsOpen && Time.unscaledTime >= nextListRefresh)
            RefreshPlayers();
    }

    public void Open()
    {
        if (!SessionActive)
            return;
        EnsureEventSystem();
        SetVisible(true);
        ShowMain();
        RefreshPlayers();
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            return;
        var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        eventSystem.transform.SetParent(transform, false);
    }

    public void Close() => SetVisible(false);

    private void SetVisible(bool visible)
    {
        if (!visible && settings != null)
            settings.Hide();
        if (screenRoot != null)
            screenRoot.SetActive(visible);
        if (visible)
        {
            PlayerControlLock.Acquire(this);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            PlayerControlLock.Release(this);
        }
    }

    public void ShowMain()
    {
        if (settings != null)
            settings.Hide();
        if (mainPanel != null)
            mainPanel.SetActive(true);
    }

    public void ShowOptions()
    {
        if (settings == null)
            return;
        if (mainPanel != null)
            mainPanel.SetActive(false);
        settings.Show();
    }

    /// <summary>Back to the main menu: the host saves and shuts the server down; a guest disconnects.</summary>
    public void Quit()
    {
        Close();
        MenuLaunch.LeaveGame();
    }

    // ---------------------------------------------------------------- players

    /// <summary>Rebuilds the player list from the spawned players (sorted by netId).</summary>
    public void RefreshPlayers()
    {
        nextListRefresh = Time.unscaledTime + listRefreshSeconds;
        var players = new List<NetworkIdentity>();
        IEnumerable<NetworkIdentity> spawned = NetworkServer.active
            ? (IEnumerable<NetworkIdentity>)NetworkServer.spawned.Values
            : NetworkClient.spawned.Values;
        foreach (NetworkIdentity identity in spawned)
        {
            if (identity != null && identity.GetComponent<PlayerName>() != null)
                players.Add(identity);
        }
        players.Sort((left, right) => left.netId.CompareTo(right.netId));

        while (rows.Count < players.Count)
            rows.Add(CreateRow());
        for (int index = 0; index < rows.Count; index++)
        {
            PlayerRow row = rows[index];
            bool used = index < players.Count;
            if (row.root != null)
                row.root.SetActive(used);
            if (!used)
            {
                row.identity = null;
                continue;
            }
            NetworkIdentity identity = players[index];
            row.identity = identity;
            if (row.nameText != null)
                row.nameText.text = identity.GetComponent<PlayerName>().DisplayName;
            if (row.kickButton != null)
                row.kickButton.gameObject.SetActive(CanKick(identity));
        }
        rows.RemoveAll(row => row.root == null);
    }

    private PlayerRow CreateRow()
    {
        var row = new PlayerRow();
        if (playerRowTemplate == null || playerRowParent == null)
            return row;
        row.root = Instantiate(playerRowTemplate, playerRowParent);
        row.root.name = "PlayerRow";
        row.root.SetActive(true);
        row.kickButton = row.root.GetComponentInChildren<Button>(true);
        foreach (Text text in row.root.GetComponentsInChildren<Text>(true))
        {
            if (row.kickButton == null || !text.transform.IsChildOf(row.kickButton.transform))
            {
                row.nameText = text;
                break;
            }
        }
        if (row.kickButton != null)
            row.kickButton.onClick.AddListener(() => Kick(row.identity));
        return row;
    }

    /// <summary>Only the host kicks, and never its own player.</summary>
    public static bool CanKick(NetworkIdentity identity) =>
        NetworkServer.active && identity != null && !identity.isLocalPlayer &&
        identity.connectionToClient != null && !(identity.connectionToClient is LocalConnectionToClient);

    public bool Kick(NetworkIdentity identity)
    {
        if (!CanKick(identity) || !(NetworkManager.singleton is SteamNetworkManager manager))
            return false;
        bool kicked = manager.ServerKick(identity.connectionToClient);
        if (kicked)
            RefreshPlayers();
        return kicked;
    }

    /// <summary>Displayed player names in list order (tests).</summary>
    public List<string> PlayerNames()
    {
        var names = new List<string>();
        foreach (PlayerRow row in rows)
        {
            if (row.root != null && row.root.activeSelf && row.nameText != null)
                names.Add(row.nameText.text);
        }
        return names;
    }

    /// <summary>Whether the row at <paramref name="index"/> shows its kick button (tests).</summary>
    public bool KickVisible(int index) =>
        index >= 0 && index < rows.Count && rows[index].kickButton != null && rows[index].kickButton.gameObject.activeSelf;
}
