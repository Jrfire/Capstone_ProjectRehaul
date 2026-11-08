using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Local sacrifice screen opened by an <see cref="AlbularyoInteractable"/>: the Albularyo's dialogue at the top and,
/// below it, the shared agimats (gold, silver, bronze) each with a Sacrifice button, the round's turned-in count
/// (bottom left) and the reward (bottom right). While open, controls are locked and the gameplay HUD is hidden
/// (<see cref="HudVisibility"/>). One instance is shared and survives scene changes; Escape or Leave closes it.
/// </summary>
[DisallowMultipleComponent]
public sealed class AlbularyoScreen : MonoBehaviour
{
    [SerializeField, Tooltip("Everything shown while the screen is open.")]
    private GameObject screenRoot;
    [SerializeField, Tooltip("The Albularyo's line at the top.")]
    private Text dialogueText;
    [SerializeField, Tooltip("The Albularyo's bust on the left of the dialogue box. Assign its artwork on this Image.")]
    private Image portraitImage;
    [SerializeField] private Text goldCountText;
    [SerializeField] private Text silverCountText;
    [SerializeField] private Text bronzeCountText;
    [SerializeField] private Button goldButton;
    [SerializeField] private Button silverButton;
    [SerializeField] private Button bronzeButton;
    [SerializeField, Tooltip("Bottom-left \"Turned in: 0x\" label, followed by the agimat icon.")]
    private Text turnedInText;
    [SerializeField, Tooltip("Bottom-right reward label.")]
    private Text rewardText;
    [SerializeField] private Button leaveButton;

    [Header("Dialogue")]
    [SerializeField, Tooltip("Random request and quota-met lines (Create > Capstone > Albularyo Dialogue).")]
    private AlbularyoDialogue dialogue;
    [SerializeField, Min(0f), Tooltip("Seconds between new random request lines while the screen stays open; 0 = one line per visit.")]
    private float lineChangeInterval;
    [SerializeField, Min(0f), Tooltip("Seconds the quota-met line stays before a request line returns.")]
    private float quotaMetSeconds = 4f;

    [Header("Texts")]
    [SerializeField, Tooltip("Count under each agimat; {0} = amount held.")]
    private string countFormat = "x{0}";
    [SerializeField, Tooltip("{0} = agimats turned in this round.")]
    private string turnedInFormat = "Turned in: {0}x";
    [SerializeField, Tooltip("Bottom-right label; {0} = pesos earned from this round's sacrifices, e.g. \"₱40\".")]
    private string rewardFormat = "Reward: {0}";
    [SerializeField, Tooltip("Sacrifice label colour while none of that agimat is held.")]
    private Color unavailableTextColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

    private static AlbularyoScreen instance;
    private int lastRound = -1;
    private float quotaMetUntil;
    private float nextLineChange;
    private string requestLine;
    private string quotaMetLine;

    public static AlbularyoScreen Current => instance;
    public bool IsOpen => screenRoot != null && screenRoot.activeSelf;
    public Text DialogueText => dialogueText;
    public Image PortraitImage => portraitImage;
    public AlbularyoDialogue Dialogue => dialogue;
    /// <summary>The unformatted line currently shown.</summary>
    public string CurrentLine => Time.unscaledTime < quotaMetUntil ? quotaMetLine : requestLine;
    public Text TurnedInText => turnedInText;
    public Text RewardText => rewardText;
    public Button GoldButton => goldButton;
    public Button SilverButton => silverButton;
    public Button BronzeButton => bronzeButton;
    public Text GoldCountText => goldCountText;
    public Text SilverCountText => silverCountText;
    public Text BronzeCountText => bronzeCountText;

    public static AlbularyoScreen Open(AlbularyoScreen prefab)
    {
        if (instance == null)
        {
            if (prefab == null)
                return null;
            instance = Instantiate(prefab);
            instance.name = prefab.name;
            DontDestroyOnLoad(instance.gameObject);
        }
        instance.Show();
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
        if (goldButton != null)
            goldButton.onClick.AddListener(() => Sacrifice(AgimatType.Gold));
        if (silverButton != null)
            silverButton.onClick.AddListener(() => Sacrifice(AgimatType.Silver));
        if (bronzeButton != null)
            bronzeButton.onClick.AddListener(() => Sacrifice(AgimatType.Bronze));
        if (leaveButton != null)
            leaveButton.onClick.AddListener(Close);
        if (screenRoot != null)
            screenRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;
        PlayerControlLock.Release(this);
        HudVisibility.Show(this);
        AgimatBank.TalkRefused -= OnTalkRefused;
        instance = null;
    }

    public void Show()
    {
        EnsureEventSystem();
        screenRoot.SetActive(true);
        PlayerControlLock.Acquire(this);
        HudVisibility.Hide(this);
        lastRound = CurrentRound;
        quotaMetUntil = 0f;
        NextRequestLine();
        Refresh();
        // Only one player may talk at a time: the host refuses (and this screen closes) when someone else already is.
        AgimatBank.TalkRefused -= OnTalkRefused;
        AgimatBank.TalkRefused += OnTalkRefused;
        if (AgimatBank.Instance != null)
            AgimatBank.Instance.RequestTalk(true);
    }

    private void OnTalkRefused()
    {
        if (IsOpen)
            Close();
    }

    public void Close()
    {
        bool wasOpen = IsOpen;
        if (screenRoot != null)
            screenRoot.SetActive(false);
        PlayerControlLock.Release(this);
        HudVisibility.Show(this);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
        AgimatBank.TalkRefused -= OnTalkRefused;
        if (wasOpen && AgimatBank.Instance != null)
            AgimatBank.Instance.RequestTalk(false);
    }

    public void Sacrifice(AgimatType type)
    {
        if (AgimatBank.Instance != null)
            AgimatBank.Instance.RequestSacrifice(type);
    }

    private void Update()
    {
        if (!IsOpen)
            return;
        bool gameOver = RoundClock.Instance != null && RoundClock.Instance.enabled && RoundClock.Instance.Phase == RoundPhase.GameOver;
        if ((NetworkClient.active && NetworkClient.localPlayer == null) || AgimatBank.Instance == null || gameOver ||
            AgimatBank.Instance.SomeoneElseTalking || PlayerPersistence.LocalReadied ||
            (TravelDirector.Instance != null && TravelDirector.Instance.IsTraveling))
        {
            Close();
            return;
        }
        if (MenuEscape.TryConsume())
        {
            Close();
            return;
        }
        Refresh();
    }

    private static int CurrentRound => RoundClock.Instance != null ? RoundClock.Instance.Round : 1;

    private void Refresh()
    {
        AgimatBank bank = AgimatBank.Instance;
        if (bank == null)
            return;

        int round = CurrentRound;
        if (lastRound >= 0 && round > lastRound)
        {
            quotaMetLine = dialogue != null ? dialogue.PickQuotaMet(quotaMetLine) : null;
            quotaMetUntil = Time.unscaledTime + quotaMetSeconds;
            // A fresh request line waits behind the quota-met line for the new round.
            NextRequestLine();
            nextLineChange = quotaMetUntil + lineChangeInterval;
        }
        lastRound = round;
        bool showingQuotaMet = Time.unscaledTime < quotaMetUntil;
        if (!showingQuotaMet && lineChangeInterval > 0f && Time.unscaledTime >= nextLineChange)
            NextRequestLine();

        SetCount(goldCountText, goldButton, bank.Count(AgimatType.Gold));
        SetCount(silverCountText, silverButton, bank.Count(AgimatType.Silver));
        SetCount(bronzeCountText, bronzeButton, bank.Count(AgimatType.Bronze));
        SetText(turnedInText, string.Format(turnedInFormat, bank.TurnedIn));
        SetText(rewardText, string.Format(rewardFormat, AgimatBank.FormatPeso(bank.RoundReward)));
        SetText(dialogueText, AlbularyoDialogue.Format(showingQuotaMet ? quotaMetLine : requestLine,
            Mathf.Max(0, bank.Quota - bank.TurnedIn), bank.Quota, bank.TurnedIn));
    }

    private void NextRequestLine()
    {
        requestLine = dialogue != null ? dialogue.PickRequest(requestLine) : null;
        nextLineChange = Time.unscaledTime + lineChangeInterval;
    }

    private void SetCount(Text label, Button button, int count)
    {
        SetText(label, string.Format(countFormat, count));
        if (button == null)
            return;
        button.interactable = count > 0;
        Text buttonLabel = button.GetComponentInChildren<Text>(true);
        if (buttonLabel != null)
            buttonLabel.color = count > 0 ? Color.white : unavailableTextColor;
    }

    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystem.transform.SetParent(transform, false);
    }
}
