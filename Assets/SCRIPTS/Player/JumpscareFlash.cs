using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local, split-second jumpscare flash: a black screen with an enemy's <see cref="EnemyJumpscare"/> front view, plus its
/// jumpscare sound. Used by blood-moon abilities (the Tikbalang's control scramble, the Santelmo in the trance pocket).
/// Creates its own persistent overlay canvas on first use.
/// </summary>
public sealed class JumpscareFlash : MonoBehaviour
{
    public const int SortingOrder = 560;

    private static JumpscareFlash instance;

    private Canvas canvas;
    private Image blackout;
    private Image image;
    private float hideAt;

    /// <summary>True while a flash is on screen.</summary>
    public static bool Showing => instance != null && instance.canvas != null && instance.canvas.enabled;
    /// <summary>Number of flashes shown since start (tests).</summary>
    public static int FlashCount { get; private set; }
    public static Sprite CurrentSprite => instance != null && instance.image != null ? instance.image.sprite : null;

    /// <summary>Flashes the front view of the enemy prefab with <paramref name="assetId"/> for <paramref name="seconds"/>.</summary>
    public static void Show(uint assetId, float seconds = 0.18f, bool playSound = true) =>
        Show(EnemyJumpscare.Find(assetId), seconds, playSound);

    public static void Show(EnemyJumpscare jumpscare, float seconds = 0.18f, bool playSound = true)
    {
        if (instance == null)
        {
            var root = new GameObject("JumpscareFlash");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<JumpscareFlash>();
            instance.Build();
        }
        instance.Flash(jumpscare, seconds, playSound);
    }

    private void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        blackout = CreateImage("Blackout", Color.black);
        RectTransform black = blackout.rectTransform;
        black.anchorMin = Vector2.zero;
        black.anchorMax = Vector2.one;
        black.offsetMin = black.offsetMax = Vector2.zero;
        image = CreateImage("Jumpscare", Color.white);
        image.preserveAspect = true;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        canvas.enabled = false;
    }

    private Image CreateImage(string objectName, Color color)
    {
        var child = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        child.transform.SetParent(transform, false);
        var result = child.GetComponent<Image>();
        result.color = color;
        result.raycastTarget = false;
        return result;
    }

    private void Flash(EnemyJumpscare jumpscare, float seconds, bool playSound)
    {
        FlashCount++;
        Sprite sprite = jumpscare != null ? jumpscare.FrontView : null;
        image.sprite = sprite;
        image.enabled = sprite != null;
        if (sprite != null)
        {
            float height = 1080f * jumpscare.FinalScreenHeight;
            float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
            image.rectTransform.sizeDelta = new Vector2(height * aspect, height);
            image.rectTransform.pivot = jumpscare.FocusPoint;
        }
        if (playSound && jumpscare != null)
            Capstone.Audio.AudioManager.Play2D(jumpscare.JumpscareSound, Capstone.Audio.AudioChannel.Jumpscare);
        canvas.enabled = true;
        hideAt = Time.unscaledTime + Mathf.Max(0.02f, seconds);
    }

    private void Update()
    {
        if (canvas != null && canvas.enabled && Time.unscaledTime >= hideAt)
            canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
