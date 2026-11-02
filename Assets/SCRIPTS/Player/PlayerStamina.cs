using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerStamina : NetworkBehaviour
{
    public const float MaxStamina = 100f;

    [SerializeField, Min(0f)] private float sprintDrainPerSecond = 15f;
    [SerializeField, Min(0f)] private float regenPerSecond = 10f;
    [SerializeField, Min(0f), Tooltip("Seconds to wait after stamina was last used before it regenerates.")]
    private float regenDelay = 0.8f;
    [SerializeField, Range(0f, 1f), Tooltip("After running out, stamina actions stay locked until this fraction is refilled.")]
    private float exhaustionRecoveryFraction = 0.3f;

    [SyncVar] private float currentStamina = MaxStamina;
    [SyncVar] private bool exhausted;

    private float regenCooldown;
    private int lastUseFrame = -1;

    public float CurrentStamina => currentStamina;
    public float NormalizedStamina => currentStamina / MaxStamina;
    public bool IsExhausted => exhausted;
    public bool CanUseStamina => !exhausted && currentStamina > 0f;
    public float ExhaustionRecoveryFraction => exhaustionRecoveryFraction;
    private bool HasControl => isOwned && NetworkClient.active;

    private void Awake()
    {
        // The owning client simulates stamina like its movement; Mirror relays it to the server and other players.
        syncDirection = SyncDirection.ClientToServer;
    }

    /// <summary>Spends stamina for an owner action. Returns false while exhausted or when not the owner.</summary>
    public bool TryUseStamina(float amount)
    {
        if (amount <= 0f || !HasControl || !CanUseStamina)
            return false;

        Spend(amount);
        return true;
    }

    internal bool TrySprint(float deltaTime)
    {
        return TryUseStamina(sprintDrainPerSecond * deltaTime);
    }

    private void Spend(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        regenCooldown = regenDelay;
        lastUseFrame = Time.frameCount;
        if (currentStamina <= 0f)
            exhausted = true;
    }

    private void Update()
    {
        if (HasControl && lastUseFrame != Time.frameCount)
            Regenerate(Time.deltaTime);
    }

    private void Regenerate(float deltaTime)
    {
        if (regenCooldown > 0f)
        {
            regenCooldown -= deltaTime;
            if (regenCooldown > 0f)
                return;
            deltaTime = -regenCooldown;
            regenCooldown = 0f;
        }

        if (currentStamina < MaxStamina)
            currentStamina = Mathf.Min(MaxStamina, currentStamina + regenPerSecond * deltaTime);
        if (exhausted && currentStamina >= MaxStamina * exhaustionRecoveryFraction)
            exhausted = false;
    }
}
