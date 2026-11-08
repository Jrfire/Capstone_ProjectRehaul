using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// "Select your character" screen (persistent root in the base scene). Whenever the local player exists without a chosen
/// character (<see cref="PlayerCharacterSelection.HasSelected"/> false: on starting a game or joining one) it opens,
/// locks controls (<see cref="PlayerControlLock"/>) and hides the HUD; each button shows its option's front sprite and
/// sends the choice. It closes once the selection syncs back.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterSelectScreen : MonoBehaviour
{
    [SerializeField, Tooltip("Everything shown while the screen is open.")]
    private GameObject screenRoot;
    [SerializeField] private Text titleText;
    [SerializeField] private string title = "Select your character";
    [SerializeField, Tooltip("One button per PlayerCharacterSelection option, in the same order.")]
    private Button[] buttons = new Button[0];
    [SerializeField, Tooltip("Front-sprite Image on each button (same order as Buttons).")]
    private Image[] portraits = new Image[0];
    [SerializeField, Tooltip("Optional name label on each button (same order as Buttons).")]
    private Text[] labels = new Text[0];

    private static CharacterSelectScreen instance;
    private PlayerCharacterSelection boundSelection;
    private bool pending;

    public static CharacterSelectScreen Current => instance;
    public bool IsOpen => screenRoot != null && screenRoot.activeSelf;
    public Button[] Buttons => buttons;
    public Image[] Portraits => portraits;
    public Text TitleText => titleText;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);
        for (int index = 0; index < buttons.Length; index++)
        {
            int option = index;
            if (buttons[index] != null)
                buttons[index].onClick.AddListener(() => Choose(option));
        }
        if (titleText != null)
            titleText.text = title;
        SetOpen(false);
    }

    private void OnDisable() => SetOpen(false);

    private void OnDestroy()
    {
        if (instance != this)
            return;
        PlayerControlLock.Release(this);
        HudVisibility.Show(this);
        instance = null;
    }

    private void Update()
    {
        PlayerCharacterSelection selection = LocalSelection();
        bool open = selection != null && !selection.HasSelected && selection.Options.Length > 0;
        if (open && selection != boundSelection)
        {
            boundSelection = selection;
            pending = false;
            Populate(selection);
        }
        if (!open)
            pending = false;
        SetOpen(open);
    }

    private static PlayerCharacterSelection LocalSelection()
    {
        NetworkIdentity local = NetworkClient.localPlayer;
        return local != null && local.TryGetComponent(out PlayerCharacterSelection selection) ? selection : null;
    }

    private void Populate(PlayerCharacterSelection selection)
    {
        for (int index = 0; index < buttons.Length; index++)
        {
            bool available = index < selection.Options.Length && selection.Options[index].definition != null;
            if (buttons[index] != null)
            {
                buttons[index].gameObject.SetActive(index < selection.Options.Length);
                buttons[index].interactable = available;
            }
            if (index >= selection.Options.Length)
                continue;
            CharacterOption option = selection.Options[index];
            if (index < portraits.Length && portraits[index] != null)
            {
                portraits[index].sprite = option.Portrait;
                portraits[index].enabled = portraits[index].sprite != null;
                portraits[index].preserveAspect = true;
            }
            if (index < labels.Length && labels[index] != null)
                labels[index].text = option.displayName;
        }
    }

    /// <summary>Picks option <paramref name="index"/> for the local player.</summary>
    public void Choose(int index)
    {
        PlayerCharacterSelection selection = LocalSelection();
        if (selection == null || pending)
            return;
        pending = true;
        selection.Select(index);
    }

    private void SetOpen(bool open)
    {
        if (open && screenRoot != null && !screenRoot.activeSelf && FindAnyObjectByType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(transform, false);
        }
        if (screenRoot != null && screenRoot.activeSelf != open)
            screenRoot.SetActive(open);
        if (open)
        {
            PlayerControlLock.Acquire(this);
            HudVisibility.Hide(this);
        }
        else
        {
            PlayerControlLock.Release(this);
            HudVisibility.Show(this);
        }
    }
}
