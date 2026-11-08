using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayerHud panel (middle right, under the clock) listing every player and whether they have readied at the jeepney
/// (at base) or boarded it (away from base). Shown while a destination is selected at base, during the return
/// countdown, or while anyone is ready/aboard; hidden while <see cref="HudVisibility.Hidden"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class ReadyHud : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Text listText;
    [SerializeField] private string title = "Jeepney";
    [SerializeField] private string readyLabel = "Ready";
    [SerializeField] private string notReadyLabel = "Not ready";
    [SerializeField] private string deadLabel = "Dead";
    [SerializeField] private Color readyColor = new Color(0.45f, 0.9f, 0.45f);
    [SerializeField] private Color notReadyColor = new Color(0.8f, 0.8f, 0.8f);
    [SerializeField] private Color deadColor = new Color(0.85f, 0.25f, 0.25f);

    private readonly List<PlayerPersistence> sorted = new List<PlayerPersistence>();
    private readonly StringBuilder builder = new StringBuilder();

    public GameObject Panel => panel;
    public Text ListText => listText;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    private void LateUpdate()
    {
        if (panel == null || listText == null)
            return;
        bool show = ShouldShow();
        panel.SetActive(show);
        if (show)
            listText.text = BuildList();
    }

    public static bool ShouldShow()
    {
        if (!NetworkClient.isConnected || NetworkClient.localPlayer == null || HudVisibility.Hidden)
            return false;
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (player != null && (player.IsReady || player.IsAboard))
                return true;
        }
        RoundClock clock = RoundClock.Instance != null && RoundClock.Instance.enabled ? RoundClock.Instance : null;
        if (clock != null && !clock.InBase)
            return clock.Phase == RoundPhase.ReturnCountdown;
        TravelDirector travel = TravelDirector.Instance;
        return travel != null && !travel.IsTraveling && !string.IsNullOrEmpty(travel.SelectedDestination);
    }

    public string BuildList()
    {
        sorted.Clear();
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (player != null)
                sorted.Add(player);
        }
        sorted.Sort((a, b) => a.netId.CompareTo(b.netId));
        builder.Clear();
        builder.Append(title);
        foreach (PlayerPersistence player in sorted)
        {
            string name = player.TryGetComponent(out PlayerName playerName) ? playerName.DisplayName : $"Player {player.netId}";
            bool dead = player.TryGetComponent(out PlayerHealth health) && health.IsDead;
            bool ready = player.IsReady || player.IsAboard;
            string label = dead ? deadLabel : ready ? readyLabel : notReadyLabel;
            Color color = dead ? deadColor : ready ? readyColor : notReadyColor;
            builder.Append('\n').Append(name).Append("  <color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>')
                .Append(label).Append("</color>");
        }
        return builder.ToString();
    }
}
