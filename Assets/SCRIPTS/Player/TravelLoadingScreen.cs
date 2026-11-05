using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local overlay used while travelling: a full-screen black fade and the loading screen (black background, animated
/// "Loading..." text, a random tip/lore line from a <see cref="LoadingTips"/> asset and a bottom row of animated
/// miniatures of the destination's enemies, laid out by a
/// HorizontalLayoutGroup). Owned by <see cref="TravelDirector"/>, which keeps a single instance alive across scenes.
/// </summary>
[DisallowMultipleComponent]
public sealed class TravelLoadingScreen : MonoBehaviour
{
    [SerializeField, Tooltip("Black full-screen Image drawn above everything else in this canvas.")]
    private Image fadeImage;
    [SerializeField, Tooltip("Shown while the destination builds and players get ready. Add custom loading visuals under it.")]
    private GameObject loadingRoot;
    [SerializeField, Tooltip("Label whose dots animate while loading.")]
    private Text loadingText;
    [SerializeField, Tooltip("Row (with a HorizontalLayoutGroup) that holds one miniature per enemy in the destination.")]
    private RectTransform enemyRow;
    [SerializeField, Tooltip("Inactive Image under the row, cloned for each enemy miniature.")]
    private Image miniatureTemplate;
    [SerializeField, Tooltip("Tips and lore lines (Create > Capstone > Loading Tips); one is picked at random per loading screen.")]
    private LoadingTips loadingTips;
    [SerializeField, Tooltip("Label between the loading text and the enemy row that shows the random tip.")]
    private Text tipText;

    [Header("Loading text")]
    [Tooltip("Text before the animated dots.")]
    public string loadingMessage = "Loading";
    [Range(1, 6), Tooltip("Number of dots at the end of the cycle.")]
    public int maxDots = 3;
    [Min(0.05f), Tooltip("Seconds between dot steps.")]
    public float dotInterval = 0.4f;

    [Header("Tips")]
    [Min(0f), Tooltip("Seconds before another random tip replaces the current one while loading; 0 = one tip per loading screen.")]
    public float tipChangeInterval = 0f;

    private readonly List<Image> figures = new List<Image>();
    private readonly List<EnemyLoadingMiniature> miniatures = new List<EnemyLoadingMiniature>();
    private float loadingStartedAt;
    private float tipShownAt;
    private int shownDots = -1;

    public bool LoadingVisible => loadingRoot != null && loadingRoot.activeSelf;
    public float FadeAlpha => fadeImage != null ? fadeImage.color.a : 0f;
    public Text LoadingText => loadingText;
    /// <summary>The loading text without the invisible padding dots, e.g. "Loading..".</summary>
    public string VisibleLoadingText => loadingMessage + new string('.', Mathf.Max(0, shownDots));
    public int DotCount => Mathf.Max(0, shownDots);
    public RectTransform EnemyRow => enemyRow;
    public IReadOnlyList<Image> EnemyFigures => figures;
    public Text TipText => tipText;
    public LoadingTips Tips => loadingTips;
    /// <summary>The tip currently shown, or null.</summary>
    public string CurrentTip { get; private set; }

    private void Awake()
    {
        if (fadeImage != null)
            fadeImage.raycastTarget = false;
        if (miniatureTemplate != null)
            miniatureTemplate.gameObject.SetActive(false);
        SetEnemies(null);
        HideLoading();
        SetFade(0f);
    }

    private void Update()
    {
        if (!LoadingVisible)
            return;
        float elapsed = Time.unscaledTime - loadingStartedAt;
        UpdateDots(elapsed);
        if (tipChangeInterval > 0f && Time.unscaledTime - tipShownAt >= tipChangeInterval)
            ShowRandomTip();
        for (int index = 0; index < figures.Count; index++)
        {
            if (figures[index] != null)
                figures[index].sprite = miniatures[index].FrameAt(elapsed);
        }
    }

    public void ShowLoading()
    {
        if (loadingRoot == null)
            return;
        if (!loadingRoot.activeSelf)
        {
            loadingStartedAt = Time.unscaledTime;
            shownDots = -1;
            ShowRandomTip();
        }
        loadingRoot.SetActive(true);
        UpdateDots(Time.unscaledTime - loadingStartedAt);
    }

    /// <summary>Replaces the shown tip with another random line from <see cref="Tips"/>; hides the label if there is none.</summary>
    public void ShowRandomTip()
    {
        tipShownAt = Time.unscaledTime;
        CurrentTip = loadingTips != null ? loadingTips.PickRandom(CurrentTip) : null;
        if (tipText == null)
            return;
        tipText.supportRichText = false;
        tipText.raycastTarget = false;
        tipText.text = CurrentTip ?? string.Empty;
        tipText.gameObject.SetActive(!string.IsNullOrEmpty(CurrentTip));
    }

    public void HideLoading()
    {
        if (loadingRoot != null)
            loadingRoot.SetActive(false);
    }

    /// <summary>Replaces the enemy miniatures; null/empty hides the row. Entries without frames are skipped.</summary>
    public void SetEnemies(IEnumerable<EnemyLoadingMiniature> enemies)
    {
        foreach (Image figure in figures)
        {
            if (figure != null)
                Destroy(figure.gameObject);
        }
        figures.Clear();
        miniatures.Clear();

        if (enemies != null && enemyRow != null && miniatureTemplate != null)
        {
            foreach (EnemyLoadingMiniature miniature in enemies)
            {
                if (miniature == null || !miniature.IsReady || miniatures.Contains(miniature))
                    continue;
                Image figure = Instantiate(miniatureTemplate, enemyRow);
                figure.name = miniature.name + " Miniature";
                figure.gameObject.SetActive(true);
                figure.raycastTarget = false;
                figure.preserveAspect = true;
                Sprite first = miniature.FrameAt(0f);
                figure.sprite = first;
                float aspect = first.rect.width / Mathf.Max(1f, first.rect.height);
                LayoutElement layout = figure.GetComponent<LayoutElement>();
                if (layout == null)
                    layout = figure.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = miniature.DisplayHeight;
                layout.preferredWidth = miniature.DisplayHeight * aspect;
                figures.Add(figure);
                miniatures.Add(miniature);
            }
        }

        if (enemyRow != null)
            enemyRow.gameObject.SetActive(figures.Count > 0);
    }

    public void SetFade(float alpha)
    {
        if (fadeImage == null)
            return;
        Color color = fadeImage.color;
        color.a = Mathf.Clamp01(alpha);
        fadeImage.color = color;
        fadeImage.enabled = color.a > 0f;
    }

    /// <summary>Fades the black overlay to <paramref name="alpha"/> using unscaled time.</summary>
    public IEnumerator FadeTo(float alpha, float duration)
    {
        float start = FadeAlpha;
        if (duration <= 0f)
        {
            SetFade(alpha);
            yield break;
        }

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            SetFade(Mathf.Lerp(start, alpha, elapsed / duration));
            yield return null;
        }
        SetFade(alpha);
    }

    private void UpdateDots(float elapsed)
    {
        int dots = Mathf.FloorToInt(Mathf.Max(0f, elapsed) / dotInterval) % (maxDots + 1);
        if (dots == shownDots || loadingText == null)
            return;
        shownDots = dots;
        // Unshown dots stay as invisible text so the centred word does not shift while the dots animate.
        loadingText.supportRichText = true;
        loadingText.text = loadingMessage + new string('.', dots) +
            (dots < maxDots ? "<color=#00000000>" + new string('.', maxDots - dots) + "</color>" : string.Empty);
    }
}