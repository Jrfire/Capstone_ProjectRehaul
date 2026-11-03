using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flashes a full-screen red Image whenever the local player's health drops, holds briefly, then fades it out.
/// Lives on the persistent PlayerHud; it only watches NetworkClient.localPlayer, so other players never see it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerDamageFlash : MonoBehaviour
{
    [SerializeField] private Image flashImage;
    [Range(0f, 1f), Tooltip("Alpha of the red overlay at the moment of the hit.")]
    public float peakAlpha = 0.6f;
    [Min(0f), Tooltip("Seconds the overlay stays at full strength before fading.")]
    public float holdSeconds = 0.08f;
    [Min(0.01f), Tooltip("Seconds the overlay takes to fade away after the hold.")]
    public float fadeSeconds = 0.6f;

    private PlayerHealth observedHealth;
    private int lastHealth;
    private float flashAge = float.PositiveInfinity;

    public Image FlashImage => flashImage;
    public float CurrentAlpha => flashImage != null ? flashImage.color.a : 0f;

    private void Awake()
    {
        if (flashImage == null)
            flashImage = GetComponent<Image>();
        if (flashImage != null)
            flashImage.raycastTarget = false;
        SetAlpha(0f);
    }

    private void OnDisable()
    {
        observedHealth = null;
        flashAge = float.PositiveInfinity;
        SetAlpha(0f);
    }

    private void LateUpdate()
    {
        NetworkIdentity localPlayer = NetworkClient.isConnected ? NetworkClient.localPlayer : null;
        PlayerHealth health = localPlayer != null ? localPlayer.GetComponent<PlayerHealth>() : null;
        if (health != observedHealth)
        {
            // A new or replaced local player starts from its current value, so spawning never flashes.
            observedHealth = health;
            lastHealth = health != null ? health.CurrentHealth : 0;
            flashAge = float.PositiveInfinity;
        }
        else if (health != null)
        {
            if (health.CurrentHealth < lastHealth)
                Flash();
            lastHealth = health.CurrentHealth;
        }

        flashAge += Time.unscaledDeltaTime;
        SetAlpha(EvaluateAlpha(flashAge));
    }

    public void Flash() => flashAge = 0f;

    public float EvaluateAlpha(float age)
    {
        if (float.IsInfinity(age) || age < 0f)
            return 0f;
        if (age <= holdSeconds)
            return peakAlpha;
        return peakAlpha * (1f - Mathf.Clamp01((age - holdSeconds) / fadeSeconds));
    }

    private void SetAlpha(float alpha)
    {
        if (flashImage == null)
            return;
        Color color = flashImage.color;
        color.a = alpha;
        flashImage.color = color;
        flashImage.enabled = alpha > 0f;
    }
}
