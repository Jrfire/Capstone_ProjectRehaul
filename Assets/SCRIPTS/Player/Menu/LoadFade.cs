using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen black overlay used when leaving the main menu: fades to black, runs the load action, then (surviving the
/// scene change) fades back out over the new scene and destroys itself.
/// </summary>
public sealed class LoadFade : MonoBehaviour
{
    public const int SortingOrder = 32000;

    private Image image;

    /// <summary>The fade currently running, if any.</summary>
    public static LoadFade Active { get; private set; }

    public float Alpha => image != null ? image.color.a : 0f;

    /// <summary>Fades to black over <paramref name="fadeOutSeconds"/>, calls <paramref name="load"/>, then fades in.</summary>
    public static LoadFade Run(Action load, float fadeOutSeconds = 0.8f, float fadeInSeconds = 0.8f)
    {
        if (Active != null)
            return Active;
        var root = new GameObject("LoadFade", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        DontDestroyOnLoad(root);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var cover = new GameObject("Black", typeof(RectTransform), typeof(Image));
        cover.transform.SetParent(root.transform, false);
        var rect = (RectTransform)cover.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        LoadFade fade = root.AddComponent<LoadFade>();
        fade.image = cover.GetComponent<Image>();
        fade.image.color = new Color(0f, 0f, 0f, 0f);
        fade.image.raycastTarget = true;
        Active = fade;
        fade.StartCoroutine(fade.Play(load, fadeOutSeconds, fadeInSeconds));
        return fade;
    }

    private IEnumerator Play(Action load, float fadeOutSeconds, float fadeInSeconds)
    {
        yield return FadeTo(1f, fadeOutSeconds);
        // One fully black frame is presented before the (blocking) load starts.
        yield return null;
        load?.Invoke();
        yield return null;
        yield return new WaitForSecondsRealtime(0.25f);
        image.raycastTarget = false;
        yield return FadeTo(0f, fadeInSeconds);
        Destroy(gameObject);
    }

    private IEnumerator FadeTo(float target, float seconds)
    {
        float start = image.color.a;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetAlpha(Mathf.Lerp(start, target, t / seconds));
            yield return null;
        }
        SetAlpha(target);
    }

    private void SetAlpha(float alpha) => image.color = new Color(0f, 0f, 0f, alpha);

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }
}
