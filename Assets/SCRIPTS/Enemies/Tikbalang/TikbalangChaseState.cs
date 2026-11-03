using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chases the first player it locked onto, anywhere on the map, until stunned or the chase time runs out. Eyes stay
/// on the target while the sprite shows the direction it actually moves. When the target is too far away it teleports
/// to a hidden spot next to the target's block.
/// </summary>
public sealed class TikbalangChaseState : EnemyState
{
    private readonly TikbalangBrain brain;
    private float elapsed;
    private float repathTimer;
    private float teleportCheckTimer;
    private float nextTeleportTime;
    private float pauseRemaining;

    public TikbalangChaseState(TikbalangBrain brain) => this.brain = brain;

    public override string Name => "Chase";
    public float Elapsed => elapsed;
    public int TeleportCount { get; private set; }
    public int HitCount { get; private set; }
    public bool IsPausedAfterHit => pauseRemaining > 0f;

    public override void Enter()
    {
        brain.ApplyNormalVision();
        elapsed = 0f;
        pauseRemaining = 0f;
        repathTimer = 0f;
        teleportCheckTimer = 0f;
        nextTeleportTime = 0f;
    }

    public override void Exit()
    {
        brain.LookTarget = null;
        brain.ClearChaseTarget();
    }

    public override void Tick(float deltaTime)
    {
        PlayerPersistence target = brain.ChasedPlayer;
        if (target == null || target.IsAboard || (target.TryGetComponent(out PlayerHealth health) && health.CurrentHealth <= 0))
        {
            brain.EndChase();
            return;
        }

        elapsed += deltaTime;
        if (elapsed >= brain.Profile.chaseDurationSeconds)
        {
            brain.EndChase();
            return;
        }

        Vector3 targetPosition = target.transform.position;
        brain.LookTarget = targetPosition;

        if (pauseRemaining > 0f)
        {
            pauseRemaining -= deltaTime;
            if (pauseRemaining > 0f)
                return;
            repathTimer = 0f;
        }

        if (health != null && IsTouching(targetPosition))
        {
            health.TakeDamage(brain.Profile.contactDamage, brain.netIdentity);
            HitCount++;
            brain.Motor.Stop();
            pauseRemaining = brain.Profile.hitPauseSeconds;
            if (health.CurrentHealth <= 0)
                brain.EndChase();
            return;
        }

        teleportCheckTimer -= deltaTime;
        if (teleportCheckTimer <= 0f)
        {
            teleportCheckTimer = 1f;
            if (Time.time >= nextTeleportTime &&
                (targetPosition - brain.transform.position).sqrMagnitude > brain.Profile.teleportDistance * brain.Profile.teleportDistance &&
                brain.TryTeleportNear(target))
            {
                TeleportCount++;
                nextTeleportTime = Time.time + brain.Profile.teleportCooldownSeconds;
                repathTimer = 0f;
            }
        }

        repathTimer -= deltaTime;
        if (repathTimer > 0f)
            return;
        repathTimer = brain.Profile.repathInterval;
        if ((targetPosition - brain.transform.position).sqrMagnitude <= brain.Profile.catchDistance * brain.Profile.catchDistance)
            brain.Motor.Stop();
        else if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, 3f, brain.Motor.AreaMask))
            brain.Motor.MoveTo(hit.position, brain.Profile.chaseSpeed);
    }

    private bool IsTouching(Vector3 targetPosition)
    {
        if (brain.Profile.contactDamage <= 0)
            return false;
        Vector3 offset = targetPosition - brain.transform.position;
        if (Mathf.Abs(offset.y) > 2.5f)
            return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= brain.Profile.attackRange * brain.Profile.attackRange;
    }
}
