using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Local travel map opened by a <see cref="MapTerminal"/>: a background map, one button per destination, and a
/// "Travel to location?" confirmation. One instance is shared by every terminal and survives scene changes.
/// </summary>
[DisallowMultipleComponent]
public sealed class TravelMapScreen : MonoBehaviour
{
    [SerializeField, Tooltip("Everything shown while the map is open.")]
    private GameObject screenRoot;
    [SerializeField, Tooltip("Full-screen background map; assign the map artwork sprite here.")]
    private Image mapBackground;
    [SerializeField, Tooltip("Parent that receives one button per destination.")]
    private RectTransform destinationList;
    [SerializeField, Tooltip("Inactive button cloned for each destination; its child Text shows the scene name.")]
    private Button destinationButtonTemplate;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private Text confirmLabel;
    [SerializeField, Tooltip("Confirmation line showing the destination's rolled enemy (\"Enemies: Tikbalang\").")]
    private Text enemiesLabel;
    [SerializeField, Tooltip("Confirmation line showing the round-trip time cost (\"Travel Time: 1 hour\").")]
    private Text travelTimeLabel;
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;
    [SerializeField, Tooltip("Label colour of unavailable destinations (such as the current scene).")]
    private Color unavailableTextColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

    private static TravelMapScreen instance;
    private readonly List<Button> destinationButtons = new List<Button>();
    private string pendingDestination;

    public static TravelMapScreen Current => instance;
    public bool IsOpen => screenRoot != null && screenRoot.activeSelf;
    public bool ConfirmationOpen => confirmPanel != null && confirmPanel.activeSelf;
    public string PendingDestination => pendingDestination;
    public IReadOnlyList<Button> DestinationButtons => destinationButtons;
    public Image MapBackground => mapBackground;
    public Text EnemiesLabel => enemiesLabel;
    public Text TravelTimeLabel => travelTimeLabel;
    public Button YesButton => yesButton;

    public static TravelMapScreen Open(TravelMapScreen prefab, MapTerminal terminal)
    {
        if (instance == null)
        {
            if (prefab == null)
                return null;
            instance = Instantiate(prefab);
            instance.name = prefab.name;
            DontDestroyOnLoad(instance.gameObject);
        }

        instance.Show(terminal.Destinations);
        return instance;
    }

    public static void CloseIfOpen()
    {
        if (instance != null && instance.IsOpen)
            instance.Close();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (destinationButtonTemplate != null)
            destinationButtonTemplate.gameObject.SetActive(false);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
        if (yesButton != null)
            yesButton.onClick.AddListener(ConfirmTravel);
        if (noButton != null)
            noButton.onClick.AddListener(HideConfirmation);
        if (screenRoot != null)
            screenRoot.SetActive(false);
        if (confirmPanel != null)
            confirmPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;
        PlayerControlLock.Release(this);
        instance = null;
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (NetworkLocalPlayerMissing() || (TravelDirector.Instance != null && TravelDirector.Instance.IsTraveling))
        {
            Close();
            return;
        }

        if (MenuEscape.TryConsume())
        {
            if (ConfirmationOpen)
                HideConfirmation();
            else
                Close();
        }
        else if (ConfirmationOpen)
            RefreshEnemies();
    }

    public void Show(IEnumerable<string> destinations)
    {
        ClearButtons();
        string currentScene = SceneManager.GetActiveScene().name;
        if (destinations != null)
        {
            foreach (string entry in destinations)
            {
                string sceneName = entry?.Trim();
                if (string.IsNullOrEmpty(sceneName) || destinationButtons.Exists(b => b.name == sceneName))
                    continue;
                AddButton(sceneName, sceneName != currentScene && TravelDirector.CanLoadScene(sceneName));
            }
        }

        HideConfirmation();
        EnsureEventSystem();
        screenRoot.SetActive(true);
        PlayerControlLock.Acquire(this);
    }

    public void Close()
    {
        HideConfirmation();
        ClearButtons();
        if (screenRoot != null)
            screenRoot.SetActive(false);
        PlayerControlLock.Release(this);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void ShowConfirmation(string sceneName)
    {
        pendingDestination = sceneName;
        if (confirmLabel != null)
            confirmLabel.text = $"Travel to {sceneName}?";
        RefreshEnemies();
        confirmPanel.SetActive(true);
    }

    /// <summary>"Enemies: [Enemy]" text for a destination, using the host's roll shared with every client.</summary>
    public static string EnemiesText(string sceneName)
    {
        string rolled = TravelDirector.Instance != null ? TravelDirector.Instance.RolledEnemyName(sceneName) : null;
        if (rolled == null)
            return "Enemies: Unknown";
        return $"Enemies: {(rolled.Length == 0 ? "None" : rolled)}";
    }

    private void RefreshEnemies()
    {
        if (enemiesLabel != null)
            enemiesLabel.text = string.IsNullOrEmpty(pendingDestination) ? string.Empty : EnemiesText(pendingDestination);

        bool affordable = string.IsNullOrEmpty(pendingDestination) || RoundClock.Instance == null ||
            !RoundClock.Instance.enabled || RoundClock.Instance.CanAfford(pendingDestination);
        if (travelTimeLabel != null)
            travelTimeLabel.text = string.IsNullOrEmpty(pendingDestination) ? string.Empty : TravelTimeText(pendingDestination);
        if (yesButton != null)
            yesButton.interactable = affordable;
    }

    /// <summary>"Travel Time: 1 hour (round trip)", or a warning when the trip would reach midnight.</summary>
    public static string TravelTimeText(string sceneName)
    {
        string text = TravelTimeManager.FormatHours(TravelTimeManager.CostHours(sceneName));
        if (RoundClock.Instance != null && RoundClock.Instance.enabled && !RoundClock.Instance.CanAfford(sceneName))
            return text + " - not enough time before midnight";
        return TravelTimeManager.CostHours(sceneName) > 0f ? text + " (round trip)" : text;
    }

    public void HideConfirmation()
    {
        pendingDestination = null;
        if (confirmPanel != null)
            confirmPanel.SetActive(false);
    }

    public void ConfirmTravel()
    {
        string sceneName = pendingDestination;
        Close();
        if (string.IsNullOrEmpty(sceneName))
            return;
        if (TravelDirector.Instance == null)
            Debug.LogError("Travel needs a TravelDirector on the NetworkManager object.", this);
        else
            TravelDirector.Instance.RequestDestination(sceneName);
    }

    private void AddButton(string sceneName, bool available)
    {
        Button button = Instantiate(destinationButtonTemplate, destinationList);
        button.name = sceneName;
        button.gameObject.SetActive(true);
        button.interactable = available;
        Text label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = sceneName;
            if (!available)
                label.color = unavailableTextColor;
        }
        if (available)
            button.onClick.AddListener(() => ShowConfirmation(sceneName));
        destinationButtons.Add(button);
    }

    private void ClearButtons()
    {
        foreach (Button button in destinationButtons)
        {
            if (button != null)
            {
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
        }
        destinationButtons.Clear();
    }

    private static bool NetworkLocalPlayerMissing() =>
        Mirror.NetworkClient.active && Mirror.NetworkClient.localPlayer == null;

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystem.transform.SetParent(transform, false);
    }
}
