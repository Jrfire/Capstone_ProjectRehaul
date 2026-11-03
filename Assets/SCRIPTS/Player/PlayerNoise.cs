using Mirror;
using UnityEngine;

/// <summary>
/// Noise a player makes. While sprinting, the player emits a dome of <see cref="RunNoiseRadius"/> that enemies' hearing
/// domes can intersect. The owner reports sprinting to the server (like movement); enemies listen on the server.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity), typeof(PlayerMovement))]
public sealed class PlayerNoise : NetworkBehaviour, INoiseSource
{
    [SerializeField, Min(0f), Tooltip("Radius in metres of the noise dome emitted while sprinting.")]
    private float runNoiseRadius = 6f;

    [SyncVar] private bool running;

    private PlayerMovement movement;
    private PlayerHealth health;

    public float RunNoiseRadius => runNoiseRadius;
    public bool IsRunning => running;
    public bool IsMakingNoise => isActiveAndEnabled && running && (health == null || !health.IsDead);
    public Vector3 NoisePosition => transform.position;
    public float NoiseRadius => runNoiseRadius;

    private void Awake()
    {
        syncDirection = SyncDirection.ClientToServer;
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
    }

    private void OnEnable() => NoiseSystem.Register(this);

    private void OnDisable() => NoiseSystem.Unregister(this);

    private void Update()
    {
        if (isOwned && NetworkClient.active)
            running = movement.IsSprinting && !PlayerStamina.TravelPaused;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = running ? new Color(1f, 0.5f, 0f, 0.8f) : new Color(1f, 0.5f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, runNoiseRadius);
    }
}
