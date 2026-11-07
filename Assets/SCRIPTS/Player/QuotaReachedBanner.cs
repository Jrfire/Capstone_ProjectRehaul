using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD banner that slams "Quota Reached" into the middle of the screen whenever the round advances (the agimat quota was
/// met), holds it briefly and fades it out. Local only: every client watches <see cref="RoundClock.Round"/>, so it plays
/// for everyone. It shows even while menus hide the rest of the HUD (e.g. the Albularyo screen).
/// </summary>
[DisallowMultipleComponent]
public sealed class QuotaReachedBanner : MonoBehaviour
{
    [SerializeField, Tooltip("Root that is shown while the banner plays (scaled and faded).")]
    private RectTransform banner;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Text label;
    [SerializeField] private string message = "Quota Reached";
    [SerializeField, Min(1f), Tooltip("Scale the text starts at before slamming down to normal size.")]
    private float slamStartScale = 3f;
    [SerializeField, Min(0.01f), Tooltip("Seconds the slam (scale down and fade in) takes.")]
    private float slamSeconds = 0.15f;
    [SerializeField, Min(0f), Tooltip("Impact shake amplitude in reference pixels.")]
    private float shakeAmplitude = 14f;
    [SerializeField, Min(0f)] private float shakeSeconds = 0.3f;
    [SerializeField, Min(0f), Tooltip("Seconds the text stays fully visible after the slam.")]
    private float holdSeconds = 1.4f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.8f;

    private int lastRound = -1;
    private float startTime = -1f;
    private Vector2 basePosition;

    public RectTransform Banner => banner;
    public Text Label => label;
    public bool IsPlaying => startTime >= 0f;
    public float TotalSeconds => slamSeconds + holdSeconds + fadeSeconds;

    private void Awake()
    {
        if (banner != null)
        {
            basePosition = banner.anchoredPosition;
            banner.gameObject.SetActive(false);
        }
        if (label != null)
            label.text = message;
    }

    private void OnDisable() => Stop();

    /// <summary>Plays the banner from the start (also used by tests).</summary>
    public void Play()
    {
        if (banner == null)
            return;
        if (label != null)
            label.text = message;
        startTime = Time.unscaledTime;
        banner.gameObject.SetActive(true);
        Apply(0f);
    }

    private void Stop()
    {
        startTime = -1f;
        if (banner != null)
        {
            banner.anchoredPosition = basePosition;
            banner.localScale = Vector3.one;
            banner.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        WatchRound();
        if (startTime < 0f)
            return;
        float t = Time.unscaledTime - startTime;
        if (t >= TotalSeconds)
        {
            Stop();
            return;
        }
        Apply(t);
    }

    private void WatchRound()
    {
        RoundClock clock = RoundClock.Instance;
        if (clock == null || !NetworkClient.isConnected)
        {
            lastRound = -1;
            return;
        }
        int round = clock.Round;
        if (lastRound > 0 && round > lastRound)
            Play();
        lastRound = round;
    }

    private void Apply(float t)
    {
        float slam = Mathf.Clamp01(t / slamSeconds);
        // Ease-in so the text accelerates into the screen like an impact.
        float scale = Mathf.Lerp(slamStartScale, 1f, slam * slam);
        banner.localScale = new Vector3(scale, scale, 1f);

        Vector2 offset = Vector2.zero;
        float sinceImpact = t - slamSeconds;
        if (sinceImpact >= 0f && sinceImpact < shakeSeconds && shakeAmplitude > 0f)
        {
            float strength = shakeAmplitude * (1f - sinceImpact / shakeSeconds);
            offset = new Vector2(Mathf.Sin(sinceImpact * 90f), Mathf.Cos(sinceImpact * 73f)) * strength;
        }
        banner.anchoredPosition = basePosition + offset;

        float alpha = slam;
        float fadeStart = slamSeconds + holdSeconds;
        if (t > fadeStart)
            alpha = 1f - Mathf.Clamp01((t - fadeStart) / fadeSeconds);
        if (canvasGroup != null)
            canvasGroup.alpha = alpha;
    }
}
