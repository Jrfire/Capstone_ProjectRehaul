using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayerHud readout for the <see cref="RoundClock"/>: the top-right clock face (one sprite per o'clock hour), the
/// jeepney return countdown and the game over screen. The clock blinks and pulses once few hours remain.
/// </summary>
[DisallowMultipleComponent]
public sealed class RoundClockHud : MonoBehaviour
{
    [Header("Clock")]
    [SerializeField] private RectTransform clockPanel;
    [SerializeField] private Image clockImage;
    [SerializeField, Tooltip("Digital time under the clock face (12:00 to 12:00); blinks with it in the last hours.")]
    private Text timeText;
    [Tooltip("Index 0 = 12 o'clock, 1..11 = that hour (the ClockUI sheet's left-to-right order).")]
    [SerializeField] private Sprite[] hourSprites = new Sprite[12];
    [SerializeField, Min(0.1f)] private float lowTimeBlinksPerSecond = 2f;
    [SerializeField, Range(0f, 1f)] private float lowTimeMinimumAlpha = 0.35f;
    [SerializeField, Min(0f), Tooltip("Extra scale at the peak of the low-time pulse (0.12 = 12% larger).")]
    private float lowTimePulseScale = 0.12f;
    [SerializeField, Min(0.1f)] private float lowTimePulsesPerSecond = 1f;

    [Header("Return countdown")]
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private Text countdownLabel;

    [Header("Game over")]
    [SerializeField] private GameObject gameOverPanel;

    private Color clockColor = Color.white;
    private Color timeColor = Color.white;
    private Vector3 clockScale = Vector3.one;
    private bool configured;

    public bool ClockBlinking { get; private set; }
    public RectTransform ClockPanel => clockPanel;
    public Image ClockImage => clockImage;
    public Text TimeText => timeText;
    public GameObject CountdownPanel => countdownPanel;
    public Text CountdownLabel => countdownLabel;
    public GameObject GameOverPanel => gameOverPanel;

    private void Awake()
    {
        if (clockPanel == null || clockImage == null || countdownPanel == null || countdownLabel == null || gameOverPanel == null)
        {
            Debug.LogError("RoundClockHud needs a clock panel and Image, a countdown panel and label, and a game over panel.", this);
            enabled = false;
            return;
        }
        configured = true;
        clockColor = clockImage.color;
        if (timeText != null)
            timeColor = timeText.color;
        clockScale = clockPanel.localScale;
        Hide();
    }

    private void LateUpdate()
    {
        if (!configured)
            return;
        RoundClock clock = RoundClock.Instance != null && RoundClock.Instance.enabled ? RoundClock.Instance : null;
        if (clock == null || !NetworkClient.isConnected || NetworkClient.localPlayer == null)
        {
            Hide();
            return;
        }
        if (HudVisibility.Hidden)
        {
            // A menu hides the clock and countdown, but a game over still shows.
            Hide();
            gameOverPanel.SetActive(clock.Phase == RoundPhase.GameOver);
            return;
        }

        clockPanel.gameObject.SetActive(true);
        int frame = clock.ClockFrame;
        Sprite sprite = hourSprites != null && frame < hourSprites.Length ? hourSprites[frame] : null;
        clockImage.sprite = sprite;
        clockImage.enabled = sprite != null;

        ClockBlinking = clock.IsLowTime && clock.Phase != RoundPhase.GameOver;
        Color color = clockColor;
        Color textColor = timeColor;
        Vector3 scale = clockScale;
        if (ClockBlinking)
        {
            float blink = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * lowTimeBlinksPerSecond * Mathf.PI * 2f);
            float alpha = Mathf.Lerp(lowTimeMinimumAlpha, 1f, blink);
            color.a *= alpha;
            textColor.a *= alpha;
            float pulse = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * lowTimePulsesPerSecond * Mathf.PI * 2f);
            scale *= 1f + lowTimePulseScale * pulse;
        }
        clockImage.color = color;
        clockPanel.localScale = scale;
        if (timeText != null)
        {
            timeText.text = TimeString(clock.Elapsed, clock.HourSeconds, clock.ClockHours);
            timeText.color = textColor;
        }

        bool countdown = clock.Phase == RoundPhase.ReturnCountdown && !clock.InBase;
        countdownPanel.SetActive(countdown);
        if (countdown)
            countdownLabel.text = CountdownText(clock.IsMidnight, clock.CountdownRemaining, RoundClock.LocalPlayerAboard);
        gameOverPanel.SetActive(clock.Phase == RoundPhase.GameOver);
    }

    /// <summary>Clock time for the elapsed seconds since noon: "12:00" at the start, "9:00" after nine hours, "12:00" at midnight.</summary>
    public static string TimeString(float elapsedSeconds, float hourSeconds, int hours)
    {
        if (hourSeconds <= 0f)
            return "12:00";
        int totalMinutes = Mathf.Clamp(Mathf.FloorToInt(elapsedSeconds / hourSeconds * 60f + 0.001f), 0, Mathf.Max(1, hours) * 60);
        int hour = (totalMinutes / 60) % 12;
        return $"{(hour == 0 ? 12 : hour)}:{totalMinutes % 60:00}";
    }

    public static string CountdownText(bool midnight, float seconds, bool aboard)
    {
        string header = midnight ? "Midnight! Get back to the jeepney" : "The jeepney is leaving for base";
        string footer = aboard ? "Waiting for the others..." : "Press E on the back of the jeepney";
        return $"{header}: {Mathf.CeilToInt(Mathf.Max(0f, seconds))}\n{footer}";
    }

    private void Hide()
    {
        ClockBlinking = false;
        clockPanel.gameObject.SetActive(false);
        clockPanel.localScale = clockScale;
        countdownPanel.SetActive(false);
        gameOverPanel.SetActive(false);
    }

    private void OnDisable()
    {
        if (configured)
            Hide();
    }
}
