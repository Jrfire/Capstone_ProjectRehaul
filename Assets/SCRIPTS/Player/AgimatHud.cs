using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD column (left side, below health and stamina) showing the shared agimat inventory from <see cref="AgimatBank"/>:
/// one icon + "x0" row each for gold, silver and bronze, then the quota row ("turned in/quota"). Hidden while no local
/// player exists or a menu hides the HUD (<see cref="HudVisibility"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class AgimatHud : MonoBehaviour
{
    [SerializeField, Tooltip("Panel holding every row; hidden together.")]
    private GameObject panel;
    [SerializeField] private Text goldText;
    [SerializeField] private Text silverText;
    [SerializeField] private Text bronzeText;
    [SerializeField, Tooltip("Shows turned-in/quota, e.g. \"0/5\".")]
    private Text quotaText;
    [SerializeField, Tooltip("Format for each type's count; {0} = amount.")]
    private string countFormat = "x{0}";

    public GameObject Panel => panel;
    public Text GoldText => goldText;
    public Text SilverText => silverText;
    public Text BronzeText => bronzeText;
    public Text QuotaText => quotaText;

    public static string QuotaLabel(int turnedIn, int quota) => $"{turnedIn}/{quota}";

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    private void LateUpdate()
    {
        AgimatBank bank = AgimatBank.Instance != null && AgimatBank.Instance.enabled ? AgimatBank.Instance : null;
        bool visible = bank != null && NetworkClient.isConnected && NetworkClient.localPlayer != null && !HudVisibility.Hidden;
        if (panel != null && panel.activeSelf != visible)
            panel.SetActive(visible);
        if (!visible)
            return;

        SetText(goldText, string.Format(countFormat, bank.Count(AgimatType.Gold)));
        SetText(silverText, string.Format(countFormat, bank.Count(AgimatType.Silver)));
        SetText(bronzeText, string.Format(countFormat, bank.Count(AgimatType.Bronze)));
        SetText(quotaText, QuotaLabel(bank.TurnedIn, bank.Quota));
    }

    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }
}
