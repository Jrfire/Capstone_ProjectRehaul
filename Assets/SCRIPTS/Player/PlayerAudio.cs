using Capstone.Audio;
using Mirror;
using UnityEngine;

/// <summary>
/// Player sounds (NetworkPlayer, every peer). Footsteps: single-step clips from the <see cref="solidSteps"/> or
/// <see cref="dirtSteps"/> set picked by the <see cref="GroundSurface"/> under the player, spaced by
/// <see cref="walkStepInterval"/> or the faster <see cref="runStepInterval"/> while sprinting, as 3D sounds at the
/// feet so others hear them too. Damaged: <see cref="damagedSound"/> whenever health drops. Attack: when the owner
/// attacks with a weapon (<see cref="PlayerItemUse"/>, left mouse) <see cref="attackSound"/> plays locally and for everyone else.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerAudio : NetworkBehaviour
{
    [Header("Footsteps")]
    [SerializeField, Tooltip("Single footsteps on concrete, asphalt and other hard floors.")]
    private AudioClipSet solidSteps = new AudioClipSet();
    [SerializeField, Tooltip("Single footsteps on dirt, grass and soft floors.")]
    private AudioClipSet dirtSteps = new AudioClipSet();
    [SerializeField, Tooltip("Floors without a GroundSurface use this set.")]
    private GroundSurfaceType defaultSurface = GroundSurfaceType.Solid;
    [SerializeField, Min(0.05f), Tooltip("Seconds between steps while walking.")]
    private float walkStepInterval = 0.5f;
    [SerializeField, Min(0.05f), Tooltip("Seconds between steps while sprinting.")]
    private float runStepInterval = 0.32f;
    [SerializeField, Tooltip("Horizontal speed (m/s) that counts as moving.")]
    private float movingSpeed = 0.5f;
    [SerializeField] private float stepMinDistance = 1f;
    [SerializeField] private float stepMaxDistance = 18f;

    [Header("Attack")]
    [SerializeField, Tooltip("Played when a weapon is used with left mouse.")]
    private AudioClipSet attackSound = new AudioClipSet();

    [Header("Damage")]
    [SerializeField, Tooltip("Played whenever the player takes damage (shared by every character).")]
    private AudioClipSet damagedSound = new AudioClipSet();

    private PlayerHealth health;
    private PlayerMovement movement;
    private PlayerNoise noise;
    private PlayerPersistence persistence;
    private Vector3 lastPosition;
    private float smoothedSpeed;
    private float stepTimer;
    private int lastHealth = PlayerHealth.MaxHealth;

    public bool IsWalkingAudibly { get; private set; }
    public bool IsRunningAudibly { get; private set; }
    public float CurrentStepInterval => IsRunningAudibly ? runStepInterval : walkStepInterval;
    public int StepsPlayed { get; private set; }
    public GroundSurfaceType LastSurface { get; private set; }
    public int AttacksPlayed { get; private set; }
    public int DamageSoundsPlayed { get; private set; }

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        movement = GetComponent<PlayerMovement>();
        noise = GetComponent<PlayerNoise>();
        persistence = GetComponent<PlayerPersistence>();
        lastPosition = transform.position;
    }

    private void OnEnable()
    {
        lastPosition = transform.position;
        if (health != null)
        {
            lastHealth = health.CurrentHealth;
            health.HealthChanged += OnHealthChanged;
        }
    }

    private void OnDisable()
    {
        if (health != null)
            health.HealthChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int current)
    {
        if (current < lastHealth)
        {
            DamageSoundsPlayed++;
            AudioManager.PlayAt(damagedSound, transform.position + Vector3.up, AudioChannel.Sfx, 1f, 20f);
        }
        lastHealth = current;
    }

    private void Update()
    {
        UpdateFootsteps();
    }

    /// <summary>Owner: plays the attack sound here and for everyone else.</summary>
    public void Attack()
    {
        PlayAttackSound();
        if (isOwned && NetworkClient.active)
            CmdAttack();
    }

    [Command]
    private void CmdAttack()
    {
        if (health == null || !health.IsDead)
            RpcAttack();
    }

    [ClientRpc(includeOwner = false)]
    private void RpcAttack() => PlayAttackSound();

    private void PlayAttackSound()
    {
        AttacksPlayed++;
        AudioManager.PlayAt(attackSound, transform.position + Vector3.up, AudioChannel.Sfx, 1f, 18f);
    }

    private void UpdateFootsteps()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        delta.y = 0f;
        float speed = delta.magnitude / dt;
        if (speed > 30f)
            speed = 0f; // teleports and respawns
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-12f * dt));

        bool alive = health == null || !health.IsDead;
        bool frozen = persistence != null && persistence.IsAboard;
        IsWalkingAudibly = alive && !frozen && smoothedSpeed > movingSpeed;
        IsRunningAudibly = IsWalkingAudibly && (isLocalPlayer && movement != null ? movement.IsSprinting : noise != null && noise.IsRunning);
        if (!IsWalkingAudibly)
        {
            stepTimer = Mathf.Min(stepTimer, CurrentStepInterval * 0.5f);
            return;
        }
        stepTimer -= dt;
        if (stepTimer > 0f)
            return;
        stepTimer = CurrentStepInterval;
        PlayStep();
    }

    private void PlayStep()
    {
        GroundSurfaceType surface = defaultSurface;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.5f, ~0,
                QueryTriggerInteraction.Ignore) && hit.collider.transform.root != transform.root)
            surface = GroundSurface.Of(hit.collider, defaultSurface);
        LastSurface = surface;
        StepsPlayed++;
        AudioManager.PlayAt(surface == GroundSurfaceType.Dirt ? dirtSteps : solidSteps, transform.position,
            AudioChannel.Sfx, stepMinDistance, stepMaxDistance);
    }
}
