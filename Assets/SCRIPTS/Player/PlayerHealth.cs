using System;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerHealth : NetworkBehaviour
{
    public const int MaxHealth = 6;

    // Declared before currentHealth so clients receive the killer no later than the fatal health value.
    [SyncVar] private uint killerAssetId;

    [SyncVar(hook = nameof(OnHealthChanged))]
    [SerializeField, Range(0, MaxHealth)] private int currentHealth = MaxHealth;

    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;
    /// <summary>Mirror asset id of the spawned prefab that dealt the fatal damage; 0 when nobody killed this player.</summary>
    public uint KillerAssetId => killerAssetId;
    public event Action<int> HealthChanged;

    public override void OnStartServer()
    {
        currentHealth = MaxHealth;
        killerAssetId = 0;
    }

    /// <summary>Server: deals damage. Pass the attacker's identity so a fatal hit records the killer for its jumpscare.</summary>
    [Server]
    public void TakeDamage(int amount, NetworkIdentity source = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Damage must be positive.");
        if (currentHealth <= 0)
            return;

        int remaining = currentHealth - Mathf.Min(amount, currentHealth);
        if (remaining == 0)
            killerAssetId = source != null ? source.assetId : 0;
        currentHealth = remaining;
    }

    [Server]
    public void Heal(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Healing must be positive.");

        killerAssetId = 0;
        currentHealth += Mathf.Min(amount, MaxHealth - currentHealth);
    }

    /// <summary>Server: sets health directly (clamped to 0..6), e.g. 1 heart after being left behind or full on a reset. Setting 0 records no killer.</summary>
    [Server]
    public void SetHealth(int value)
    {
        killerAssetId = 0;
        currentHealth = Mathf.Clamp(value, 0, MaxHealth);
    }

    private void OnHealthChanged(int previous, int current)
    {
        HealthChanged?.Invoke(current);
    }
}
