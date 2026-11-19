using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Local shop screen opened by a <see cref="ShopkeeperInteractable"/>, laid out like the <see cref="AlbularyoScreen"/>:
/// the shopkeeper's line at the top and, below it, this quota's <see cref="ShopStock"/> (icon, name, price and a Buy
/// button per item), the team's money (bottom left) and the last purchase's outcome (bottom right). While open, controls
/// are locked and the gameplay HUD is hidden (<see cref="HudVisibility"/>). One instance is shared and survives scene
/// changes; Escape or Leave closes it.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShopScreen : MonoBehaviour
{
    [SerializeField, Tooltip("Everything shown while the screen is open.")]
    private GameObject screenRoot;
    [SerializeField, Tooltip("The shopkeeper's line at the top.")]
    private Text dialogueText;
    [SerializeField, Tooltip("The shopkeeper's bust on the left of the dialogue box. Assign its artwork on this Image.")]
    private Image portraitImage;
    [SerializeField, Tooltip("One column per stock entry, left to right; columns without stock are hidden.")]
    private GameObject[] columns = new GameObject[2];
    [SerializeField] private Image[] itemIcons = new Image[2];
    [SerializeField] private Text[] itemNames = new Text[2];
    [SerializeField] private Text[] itemPrices = new Text[2];
    [SerializeField] private Button[] buyButtons = new Button[2];
    [SerializeField, Tooltip("Bottom-left money label.")]
    private Text moneyText;
    [SerializeField, Tooltip("Bottom-right label showing the outcome of the last purchase.")]
    private Text statusText;
    [SerializeField] private Button leaveButton;

    [Header("Texts")]
    [SerializeField, Tooltip("One is picked at random each visit.")]
    private string[] greetingLines =
    {
        "Looking for something? Pay in pesos, nothing else.",
        "Fresh stock, just for this night. Take a look.",
        "Out there you'll need more than luck. Buy something.",
    };
    [SerializeField, Tooltip("Shown instead of a greeting while nothing is in stock.")]
    private string emptyLine = "Nothing left to sell tonight. Come back after the next offering.";
    [SerializeField, Tooltip("{0} = the team's money, e.g. \"₱40\".")]
    private string moneyFormat = "Money: {0}";
    [SerializeField, Tooltip("Price label; {0} = price, e.g. \"₱25\".")]
    private string priceFormat = "{0}";
    [SerializeField] private string buyLabel = "Buy";
    [SerializeField] private string soldLabel = "Sold";
    [SerializeField] private string boughtMessage = "Bought {0}.";
    [SerializeField] private string notEnoughMoneyMessage = "Not enough money.";
    [SerializeField] private string inventoryFullMessage = "Your inventory is full.";
    [SerializeField] private string soldOutMessage = "That's already sold.";
    [SerializeField] private string refusedMessage = "Can't buy that right now.";
    [SerializeField, Tooltip("Buy label colour while the item cannot be bought.")]
    private Color unavailableTextColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

    private static ShopScreen instance;
    private string greeting;
    private string status = string.Empty;

    public static ShopScreen Current => instance;
    public bool IsOpen => screenRoot != null && screenRoot.activeSelf;
    public Text DialogueText => dialogueText;
    public Image PortraitImage => portraitImage;
    public GameObject[] Columns => columns;
    public Image[] ItemIcons => itemIcons;
    public Text[] ItemNames => itemNames;
    public Text[] ItemPrices => itemPrices;
    public Button[] BuyButtons => buyButtons;
    public Text MoneyText => moneyText;
    public Text StatusText => statusText;
    public string[] GreetingLines => greetingLines;
    public string EmptyLine => emptyLine;

    public static ShopScreen Open(ShopScreen prefab)
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
        for (int index = 0; index < buyButtons.Length; index++)
        {
            int entry = index;
            if (buyButtons[index] != null)
                buyButtons[index].onClick.AddListener(() => Buy(entry));
        }
        if (leaveButton != null)
            leaveButton.onClick.AddListener(Close);
        if (screenRoot != null)
            screenRoot.SetActive(false);
    }

    private void OnEnable() => ShopStock.BuyAnswered += OnBuyAnswered;

    private void OnDisable() => ShopStock.BuyAnswered -= OnBuyAnswered;

    private void OnDestroy()
    {
        if (instance != this)
            return;
        PlayerControlLock.Release(this);
        HudVisibility.Show(this);
        instance = null;
    }

    public void Show()
    {
        EnsureEventSystem();
        screenRoot.SetActive(true);
        PlayerControlLock.Acquire(this);
        HudVisibility.Hide(this);
        greeting = greetingLines != null && greetingLines.Length > 0
            ? greetingLines[Random.Range(0, greetingLines.Length)]
            : string.Empty;
        status = string.Empty;
        Refresh();
    }

    public void Close()
    {
        if (screenRoot != null)
            screenRoot.SetActive(false);
        PlayerControlLock.Release(this);
        HudVisibility.Show(this);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Asks the host to buy stock entry <paramref name="index"/>.</summary>
    public void Buy(int index)
    {
        ShopStock stock = ShopStock.Instance;
        if (stock == null)
            return;
        AgimatBank bank = AgimatBank.Instance;
        if (bank != null && bank.Money < stock.GetPrice(index))
        {
            status = notEnoughMoneyMessage;
            return;
        }
        stock.RequestBuy(index);
    }

    private void OnBuyAnswered(int index, ShopBuyResult result)
    {
        switch (result)
        {
            case ShopBuyResult.Bought:
                ItemDefinition item = ShopStock.Instance != null ? ShopStock.Instance.GetItem(index) : null;
                status = string.Format(boughtMessage, item != null ? item.DisplayName : "it");
                break;
            case ShopBuyResult.NotEnoughMoney: status = notEnoughMoneyMessage; break;
            case ShopBuyResult.InventoryFull: status = inventoryFullMessage; break;
            case ShopBuyResult.SoldOut: status = soldOutMessage; break;
            default: status = refusedMessage; break;
        }
    }

    private void Update()
    {
        if (!IsOpen)
            return;
        bool gameOver = RoundClock.Instance != null && RoundClock.Instance.enabled && RoundClock.Instance.Phase == RoundPhase.GameOver;
        if ((NetworkClient.active && NetworkClient.localPlayer == null) || ShopStock.Instance == null || gameOver ||
            PlayerPersistence.LocalReadied || (TravelDirector.Instance != null && TravelDirector.Instance.IsTraveling))
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

    private void Refresh()
    {
        ShopStock stock = ShopStock.Instance;
        AgimatBank bank = AgimatBank.Instance;
        int money = bank != null ? bank.Money : 0;
        int count = stock != null ? stock.Count : 0;
        bool anyForSale = false;
        for (int index = 0; index < columns.Length; index++)
        {
            ItemDefinition item = index < count ? stock.GetItem(index) : null;
            bool show = item != null;
            if (columns[index] != null && columns[index].activeSelf != show)
                columns[index].SetActive(show);
            if (!show)
                continue;
            bool sold = stock.IsSold(index);
            int price = stock.GetPrice(index);
            anyForSale |= !sold;
            if (index < itemIcons.Length && itemIcons[index] != null)
            {
                itemIcons[index].sprite = item.Icon;
                itemIcons[index].enabled = item.Icon != null;
                itemIcons[index].color = sold ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            }
            if (index < itemNames.Length)
                SetText(itemNames[index], item.DisplayName);
            if (index < itemPrices.Length)
                SetText(itemPrices[index], string.Format(priceFormat, AgimatBank.FormatPeso(price)));
            if (index < buyButtons.Length && buyButtons[index] != null)
            {
                bool canBuy = !sold && money >= price;
                buyButtons[index].interactable = !sold;
                Text label = buyButtons[index].GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    SetText(label, sold ? soldLabel : buyLabel);
                    label.color = canBuy ? Color.white : unavailableTextColor;
                }
            }
        }
        SetText(dialogueText, anyForSale ? greeting : emptyLine);
        SetText(moneyText, string.Format(moneyFormat, AgimatBank.FormatPeso(money)));
        SetText(statusText, status);
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
