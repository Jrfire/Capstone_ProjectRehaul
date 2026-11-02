using Mirror;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class PlayerHealthHUD : MonoBehaviour
{
    [SerializeField] private GameObject healthPanel;
    [Tooltip("Left to right. Each heart represents two health points.")]
    [SerializeField] private Image[] hearts = new Image[3];
    [Header("Assign your heart sprites")]
    [SerializeField] private Sprite fullHeart;
    [SerializeField] private Sprite halfHeart;
    [SerializeField] private Sprite emptyHeart;

    private static PlayerHealthHUD instance;
    private PlayerHealth observedHealth;
    private bool configured;

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
            hearts[0] == null || hearts[1] == null || hearts[2] == null)
        {
            Debug.LogError("PlayerHealthHUD needs a root Canvas, a child health panel, and three heart Images.", this);
            enabled = false;
            return;
        }

        configured = true;
        instance = this;
        DontDestroyOnLoad(gameObject);
        healthPanel.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!configured)
            return;

        PlayerHealth localHealth = NetworkClient.isConnected && NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<PlayerHealth>() : null;
        if (observedHealth != localHealth)
            Bind(localHealth);
        else if (localHealth == null)
            healthPanel.SetActive(false);
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

    private void OnDisable()
    {
        if (configured)
            Bind(null);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
