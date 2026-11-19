using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The attack: appears (fading in) behind the victim or out of their sight, then rushes them. While the victim's
/// flashlight is on it, it stops, flickers and fades away (<see cref="TiktikProfile.litFadeSeconds"/>); fully faded it
/// teleports close to the victim (out of their sight when possible) and rushes again. Each touch deals <see cref="TiktikProfile.contactDamage"/>, at most once every
/// <see cref="TiktikProfile.attackIntervalSeconds"/>. After <see cref="TiktikProfile.attackDurationSeconds"/> (or when
/// the victim dies, boards or is entranced) it fades away and waits for a new victim.
/// </summary>
public sealed class TiktikAttackingState : EnemyState
{
    public enum Phase
    {
        Spawning,
        FadingIn,
        Rushing,
        FadingOut,
        Relocating,
    }

    private const float RelaxSpawnAfterSeconds = 4f;

    private readonly TiktikBrain brain;
    private float elapsed;
    private float timeLeft;
    private float repathTimer;
    private float spawnSearchSeconds;
    private float nextAttackTime;
    private float stunRemaining;
    private float banish;
    private bool relocateLanded;
    private float relocateSearchSeconds;
    private Vector3 vanishedAt;

    public TiktikAttackingState(TiktikBrain brain) => this.brain = brain;

    public override string Name => "Attacking";
    public Phase Current { get; private set; }
    public float Elapsed => elapsed;
    /// <summary>True while the victim's flashlight holds it still.</summary>
    public bool IsFrozen { get; private set; }
    public bool IsStunned => stunRemaining > 0f;
    public int HitCount { get; private set; }
    public Vector3 SpawnPoint { get; private set; }
    /// <summary>How often the flashlight faded it away (each time it teleported).</summary>
    public int Banishments { get; private set; }
    /// <summary>Where it last vanished in the flashlight.</summary>
    public Vector3 VanishedAt => vanishedAt;

    public override void Enter()
    {
        brain.SetVisible(false, 0f);
        brain.SetAttacking(true);
        Current = Phase.Spawning;
        elapsed = 0f;
        timeLeft = 0f;
        spawnSearchSeconds = 0f;
        repathTimer = 0f;
        nextAttackTime = 0f;
        stunRemaining = 0f;
        IsFrozen = false;
        SetBanish(0f);
        brain.ResetBloodMoonClone();
    }

    public override void Exit()
    {
        brain.LookTarget = null;
        brain.SetAttacking(false);
        IsFrozen = false;
        SetBanish(0f);
    }

    /// <summary>Server: stands still (no attacks) for <paramref name="seconds"/>; the attack time keeps running.</summary>
    public void Stun(float seconds)
    {
        stunRemaining = Mathf.Max(stunRemaining, seconds);
        brain.Motor.Stop();
    }

    public override void Tick(float deltaTime)
    {
        TiktikProfile profile = brain.Profile;
        elapsed += deltaTime;
        timeLeft -= deltaTime;
        PlayerPersistence victim = brain.VictimPlayer;

        if (Current == Phase.FadingOut)
        {
            if (timeLeft <= 0f)
            {
                brain.Motor.Suspend();
                brain.ChangeState(brain.Waiting);
            }
            return;
        }

        if (!TiktikBrain.IsValidVictim(victim) || elapsed >= profile.attackDurationSeconds)
        {
            BeginFadeOut();
            return;
        }

        if (Current != Phase.Spawning)
            brain.TickBloodMoonClone(victim, deltaTime);

        Vector3 target = victim.transform.position;
        switch (Current)
        {
            case Phase.Spawning:
                if (timeLeft > 0f)
                    return;
                spawnSearchSeconds += 0.5f;
                bool requireHidden = spawnSearchSeconds < RelaxSpawnAfterSeconds;
                if (!brain.TryFindAttackSpawn(victim, requireHidden, out Vector3 spawn) || !brain.LandAt(spawn))
                {
                    timeLeft = 0.5f;
                    return;
                }
                SpawnPoint = spawn;
                brain.LookTarget = target;
                brain.SetVisible(true, profile.attackFadeInSeconds);
                Current = Phase.FadingIn;
                timeLeft = profile.attackFadeInSeconds;
                return;

            case Phase.FadingIn:
                brain.LookTarget = target;
                if (timeLeft > 0f)
                    return;
                Current = Phase.Rushing;
                repathTimer = 0f;
                return;

            case Phase.Rushing:
                Rush(victim, target, deltaTime);
                return;

            case Phase.Relocating:
                Relocate(victim, target);
                return;
        }
    }

    private void Rush(PlayerPersistence victim, Vector3 target, float deltaTime)
    {
        TiktikProfile profile = brain.Profile;
        brain.LookTarget = target;
        if (stunRemaining > 0f)
        {
            stunRemaining -= deltaTime;
            brain.Motor.Stop();
            return;
        }

        IsFrozen = brain.IsLitBy(victim);
        if (IsFrozen)
        {
            brain.Motor.Stop();
            repathTimer = 0f;
            SetBanish(banish + deltaTime / Mathf.Max(0.1f, profile.litFadeSeconds));
            if (banish >= 1f)
                BeginRelocate();
            return;
        }
        if (banish > 0f)
            SetBanish(banish - deltaTime / Mathf.Max(0.1f, profile.litRecoverSeconds));

        if (IsTouching(target) && Time.time >= nextAttackTime && victim.TryGetComponent(out PlayerHealth health))
        {
            health.TakeDamage(profile.contactDamage, brain.netIdentity);
            HitCount++;
            nextAttackTime = Time.time + profile.attackIntervalSeconds;
            if (health.IsDead)
            {
                BeginFadeOut();
                return;
            }
        }

        repathTimer -= deltaTime;
        if (repathTimer > 0f)
            return;
        repathTimer = profile.repathInterval;
        Vector3 offset = target - brain.transform.position;
        offset.y = 0f;
        if (offset.magnitude <= profile.attackRange * 0.6f)
            brain.Motor.Stop();
        else if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, brain.Motor.AreaMask))
            brain.Motor.MoveTo(hit.position, profile.rushSpeed);
    }

    private void SetBanish(float value)
    {
        banish = Mathf.Clamp01(value);
        brain.SetBanish(banish);
    }

    private void BeginRelocate()
    {
        Banishments++;
        vanishedAt = brain.transform.position;
        brain.Motor.Stop();
        brain.SetVisible(false, 0f);
        SetBanish(0f);
        IsFrozen = false;
        relocateLanded = false;
        relocateSearchSeconds = 0f;
        Current = Phase.Relocating;
        timeLeft = 0f;
    }

    private void Relocate(PlayerPersistence victim, Vector3 target)
    {
        TiktikProfile profile = brain.Profile;
        if (timeLeft > 0f)
            return;
        if (!relocateLanded)
        {
            relocateSearchSeconds += 0.25f;
            bool requireHidden = relocateSearchSeconds < 1.5f;
            // Random spots in the victim's/neighbouring blocks first; later searches widen to any block (3 s) and then
            // allow the rear cone (5 s), but never come closer than the relocate minimum distance. Until a spot is
            // found it simply stays hidden and keeps searching.
            bool anyBlock = relocateSearchSeconds >= 3f;
            bool allowRear = relocateSearchSeconds >= 5f;
            bool found = brain.TryFindRelocateSpot(victim, requireHidden, vanishedAt, profile.relocateMinShift,
                out Vector3 spot, anyBlock, allowRear);
            if (!found || !brain.LandAt(spot))
            {
                timeLeft = 0.25f;
                return;
            }
            SpawnPoint = spot;
            relocateLanded = true;
            brain.LookTarget = target;
            // Hidden for a moment so clients receive the new position before it shows.
            timeLeft = profile.revealDelay;
            return;
        }
        brain.SetVisible(true, profile.relocateFadeInSeconds);
        Current = Phase.Rushing;
        repathTimer = 0f;
    }

    private bool IsTouching(Vector3 target)
    {
        if (brain.Profile.contactDamage <= 0)
            return false;
        Vector3 offset = target - brain.transform.position;
        if (Mathf.Abs(offset.y) > 2.5f)
            return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= brain.Profile.attackRange * brain.Profile.attackRange;
    }

    private void BeginFadeOut()
    {
        brain.SetAttacking(false);
        brain.LookTarget = null;
        brain.Motor.Stop();
        IsFrozen = false;
        SetBanish(0f);
        brain.SetVisible(false, brain.Profile.attackFadeOutSeconds);
        Current = Phase.FadingOut;
        timeLeft = brain.Profile.attackFadeOutSeconds + 0.1f;
    }
}
