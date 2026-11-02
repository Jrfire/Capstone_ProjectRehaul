using System;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerHealth : NetworkBehaviour
{
    public const int MaxHealth = 6;

    [SyncVar(hook = nameof(OnHealthChanged))]
    [SerializeField, Range(0, MaxHealth)] private int currentHealth = MaxHealth;

    public int CurrentHealth => currentHealth;
    public event Action<int> HealthChanged;

    public override void OnStartServer()
    {
        currentHealth = MaxHealth;
    }

    [Server]
    public void TakeDamage(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Damage must be positive.");

        currentHealth -= Mathf.Min(amount, currentHealth);
    }

    [Server]
    public void Heal(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Healing must be positive.");

        currentHealth += Mathf.Min(amount, MaxHealth - currentHealth);
    }

    private void OnHealthChanged(int previous, int current)
    {
        HealthChanged?.Invoke(current);
    }
}
