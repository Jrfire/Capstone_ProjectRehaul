using Mirror;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class PlayerHud : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private GameObject healthPanel;
    [Tooltip("Left to right. Each heart represents two health points.")]
    [SerializeField] private Image[] hearts = new Image[3];
    [Header("Assign your heart sprites")]
    [SerializeField] private Sprite fullHeart;
    [SerializeField] private Sprite halfHeart;
    [SerializeField] private Sprite emptyHeart;

    [Header("Stamina")]
    [SerializeField] private GameObject staminaPanel;
    [Tooltip("A Filled Image whose Fill Amount shows the local player's stamina.")]
    [SerializeField] private Image staminaFill;
    [SerializeField, Range(0f, 1f), Tooltip("The fill blinks below this stamina fraction. After full depletion the whole bar blinks until stamina actions unlock.")]
    private float lowStaminaFraction = 0.3f;
    [SerializeField, Min(0.1f)] private float blinksPerSecond = 3f;
    [SerializeField, Range(0f, 1f), Tooltip("Alpha multiplier at the dimmest point of a blink.")]
    private float blinkMinimumAlpha = 0.2f;

    private static PlayerHud instance;
    private PlayerHealth observedHealth;
    private Color staminaFillColor;
    private Image staminaBackground;
    private Color staminaBackgroundColor;
    private bool configured;

    /// <summary>True while the stamina fill blinks (low or exhausted).</summary>
    public bool StaminaBlinking { get; private set; }
    /// <summary>True while the whole bar, including its background, blinks after full depletion.</summary>
    public bool StaminaPanelBlinking { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        if (transform.parent != null || healthPanel == null || healthPanel == gameObject ||
            hearts == null || hearts.Length != 3 ||
            hearts[0] == null || hearts[1] == null || hearts[2] == null ||
            staminaPanel == null || staminaPanel == gameObject || staminaFill == null)
        {
            Debug.LogError("PlayerHud needs a root Canvas, a health panel with three heart Images, and a stamina panel with a fill Image.", this);
            enabled = false;
            return;
        }

        configured = true;
        instance = this;
        staminaFillColor = staminaFill.color;
        staminaBackground = staminaPanel.GetComponent<Image>();
        if (staminaBackground != null)
            staminaBackgroundColor = staminaBackground.color;
        DontDestroyOnLoad(gameObject);
        healthPanel.SetActive(false);
        staminaPanel.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!configured)
            return;

        NetworkIdentity localPlayer = NetworkClient.isConnected && !HudVisibility.Hidden ? NetworkClient.localPlayer : null;
        PlayerHealth localHealth = localPlayer != null ? localPlayer.GetComponent<PlayerHealth>() : null;
        if (observedHealth != localHealth)
            Bind(localHealth);
        else if (localHealth == null)
            healthPanel.SetActive(false);

        RefreshStamina(localPlayer != null ? localPlayer.GetComponent<PlayerStamina>() : null);
    }

    private void Bind(PlayerHealth health)
    {
        if (observedHealth != null)
            observedHealth.HealthChanged -= Refresh;
        observedHealth = health;
        healthPanel.SetActive(health != null);
        if (health != null)
        {
            health.HealthChanged += Refresh;
            Refresh(health.CurrentHealth);
        }
    }

    private void Refresh(int health)
    {
        for (int index = 0; index < hearts.Length; index++)
        {
            int remaining = health - index * 2;
            hearts[index].sprite = remaining >= 2 ? fullHeart : remaining == 1 ? halfHeart : emptyHeart;
            hearts[index].enabled = hearts[index].sprite != null;
        }
    }

    private void RefreshStamina(PlayerStamina stamina)
    {
        staminaPanel.SetActive(stamina != null);
        float fraction = stamina != null ? stamina.NormalizedStamina : 1f;
        staminaFill.fillAmount = fraction;
        // Fully depleted stamina blinks the whole bar until it recovers; merely low stamina blinks only the fill.
        StaminaPanelBlinking = stamina != null && stamina.IsExhausted;
        StaminaBlinking = StaminaPanelBlinking || (stamina != null && fraction < lowStaminaFraction);
        float blinkAlpha = 1f;
        if (StaminaBlinking)
        {
            float wave = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * blinksPerSecond * Mathf.PI * 2f);
            blinkAlpha = Mathf.Lerp(blinkMinimumAlpha, 1f, wave);
        }

        Color color = staminaFillColor;
        color.a *= blinkAlpha;
        staminaFill.color = color;
        if (staminaBackground != null)
        {
            Color background = staminaBackgroundColor;
            if (StaminaPanelBlinking)
                background.a *= blinkAlpha;
            staminaBackground.color = background;
        }
    }

    private void OnDisable()
    {
        if (!configured)
            return;
        Bind(null);
        RefreshStamina(null);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
