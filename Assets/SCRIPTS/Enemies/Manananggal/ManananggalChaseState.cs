using UnityEngine;

/// <summary>
/// Flies at the first player it spotted, climbing over walls and buildings instead of going around them (and dropping
/// down beside a roof to fly in underneath when the target hides under one, or straight through the cover when the
/// target is fully enclosed), until the chase time runs out or the target dies/boards. Each touch removes one heart and is followed by a hovering pause.
/// </summary>
public sealed class ManananggalChaseState : EnemyState
{
    private readonly ManananggalBrain brain;
    private float elapsed;
    private float pauseRemaining;

    public ManananggalChaseState(ManananggalBrain brain) => this.brain = brain;

    public override string Name => "Chase";
    public float Elapsed => elapsed;
    public int HitCount { get; private set; }
    public bool IsPausedAfterHit => pauseRemaining > 0f;

    public override void Enter()
    {
        brain.SetHidden(false);
        elapsed = 0f;
        pauseRemaining = 0f;
    }

    public override void Exit()
    {
        brain.LookTarget = null;
        brain.ClearChaseTarget();
    }

    public override void Tick(float deltaTime)
    {
        PlayerPersistence target = brain.ChasedPersistence;
        if (target == null || target.IsAboard || target.IsInTrance || (target.TryGetComponent(out PlayerHealth health) && health.CurrentHealth <= 0))
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
            brain.Flight.Stop();
            return;
        }

        if (health != null && IsTouching(targetPosition))
        {
            health.TakeDamage(brain.Profile.contactDamage, brain.netIdentity);
            HitCount++;
            brain.Flight.Stop();
            pauseRemaining = brain.Profile.hitPauseSeconds;
            if (health.CurrentHealth <= 0)
                brain.EndChase();
            return;
        }

        Vector3 goal = targetPosition + Vector3.up * brain.Profile.chaseHoverHeight;
        brain.Flight.PursueTowards(goal, brain.Profile.chaseSpeed, deltaTime);
    }

    private bool IsTouching(Vector3 targetPosition)
    {
        if (brain.Profile.contactDamage <= 0)
            return false;
        Vector3 offset = brain.transform.position - targetPosition;
        if (Mathf.Abs(offset.y) > brain.Profile.attackHeight)
            return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= brain.Profile.attackRange * brain.Profile.attackRange;
    }
}
