using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayerHud overlay shown on every client when a quota is met (after the player who turned the agimats in leaves the
/// Albularyo): the screen fades to black, "NIGHT: [round]" fades in and out, then the black fades away. Controls are
/// locked while it plays.
/// </summary>
[DisallowMultipleComponent]
public sealed class NightTransitionScreen : MonoBehaviour
{
    [SerializeField] private Image blackout;
    [SerializeField] private Text label;
    [SerializeField, Tooltip("{0} = the new round.")] private string labelFormat = "NIGHT: {0}";
    [SerializeField, Min(0f)] private float fadeToBlackSeconds = 0.8f;
    [SerializeField, Min(0f)] private float textFadeInSeconds = 0.8f;
    [SerializeField, Min(0f)] private float holdSeconds = 1.6f;
    [SerializeField, Min(0f)] private float textFadeOutSeconds = 0.6f;
    [SerializeField, Min(0f)] private float fadeFromBlackSeconds = 1f;

    private Coroutine routine;

    public bool IsPlaying => routine != null;
    public Image Blackout => blackout;
    public Text Label => label;

    public static string FormatLabel(string format, int round) => string.Format(format ?? "NIGHT: {0}", round);

    private void Awake() => SetVisible(0f, 0f);

    private void OnEnable() => AgimatBank.NightTransitionStarted += Play;

    private void OnDisable()
    {
        AgimatBank.NightTransitionStarted -= Play;
        Stop();
    }

    public void Play(int round)
    {
        if (blackout == null || label == null)
            return;
        Stop();
        label.text = FormatLabel(labelFormat, round);
        routine = StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        PlayerControlLock.Acquire(this);
        yield return Fade(fadeToBlackSeconds, t => SetVisible(t, 0f));
        yield return Fade(textFadeInSeconds, t => SetVisible(1f, t));
        float end = Time.unscaledTime + holdSeconds;
        while (Time.unscaledTime < end)
            yield return null;
        yield return Fade(textFadeOutSeconds, t => SetVisible(1f, 1f - t));
        PlayerControlLock.Release(this);
        yield return Fade(fadeFromBlackSeconds, t => SetVisible(1f - t, 0f));
        SetVisible(0f, 0f);
        routine = null;
    }

    private static IEnumerator Fade(float seconds, System.Action<float> apply)
    {
        float start = Time.unscaledTime;
        while (seconds > 0f && Time.unscaledTime - start < seconds)
        {
            apply(Mathf.Clamp01((Time.unscaledTime - start) / seconds));
            yield return null;
        }
        apply(1f);
    }

    private void Stop()
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
        PlayerControlLock.Release(this);
        SetVisible(0f, 0f);
    }

    private void SetVisible(float black, float text)
    {
        if (blackout != null)
        {
            Color color = blackout.color;
            color.a = black;
            blackout.color = color;
            blackout.enabled = black > 0f;
        }
        if (label != null)
        {
            Color color = label.color;
            color.a = text;
            label.color = color;
            label.enabled = text > 0f;
        }
    }

    private void OnDestroy() => PlayerControlLock.Release(this);
}
