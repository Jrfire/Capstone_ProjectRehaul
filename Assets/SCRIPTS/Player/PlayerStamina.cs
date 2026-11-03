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
    // Set only by the owner's travel handoff; guests restore it when their player is respawned in the new scene.
    [SyncVar] private float carriedRegenCooldown;

    private float regenCooldown;
    private int lastUseFrame = -1;

    /// <summary>
    /// Pauses stamina simulation on this client while a scene travel is in progress, so every player arrives with
    /// exactly the stamina (and regeneration delay) they had when travel began.
    /// </summary>
    public static bool TravelPaused { get; set; }

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
        if (amount <= 0f || !HasControl || TravelPaused || !CanUseStamina)
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

    public override void OnStartAuthority()
    {
        // A guest's player is respawned after a scene change; the server copy carries the owner's departure state.
        regenCooldown = carriedRegenCooldown;
    }

    /// <summary>
    /// Owner only: sends this player's exact stamina to the server at the start of travel. Guests' player objects
    /// are recreated from the server's copy in the new scene, so this keeps each player's own values exact.
    /// </summary>
    public void CommitForTravel()
    {
        if (HasControl)
            CmdCommitForTravel(currentStamina, exhausted, regenCooldown);
    }

    [Command]
    private void CmdCommitForTravel(float stamina, bool isExhausted, float cooldown)
    {
        ApplyCarriedState(stamina, isExhausted, cooldown);
    }

    /// <summary>Server: refills the owner's stamina and clears exhaustion (the owner simulates stamina, so it applies it).</summary>
    [Server]
    public void ServerRestore()
    {
        ApplyCarriedState(MaxStamina, false, 0f);
        if (connectionToClient != null)
            TargetRestore(connectionToClient);
    }

    [TargetRpc]
    private void TargetRestore(NetworkConnectionToClient target)
    {
        currentStamina = MaxStamina;
        exhausted = false;
        regenCooldown = 0f;
    }

    private void ApplyCarriedState(float stamina, bool isExhausted, float cooldown)
    {
        currentStamina = Mathf.Clamp(stamina, 0f, MaxStamina);
        exhausted = isExhausted || currentStamina <= 0f;
        carriedRegenCooldown = Mathf.Clamp(cooldown, 0f, regenDelay);
    }

    private void Update()
    {
        if (HasControl && !TravelPaused && lastUseFrame != Time.frameCount)
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
