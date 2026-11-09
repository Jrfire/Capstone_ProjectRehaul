using UnityEngine;

/// <summary>
/// Stops and stares at <see cref="Target"/>. Each second in sight fills the gaze (the player's view blurs and darkens);
/// a full gaze entrances them. Losing sight for longer than the grace lets the player escape (ignored for a while).
/// </summary>
public sealed class SantelmoGazingState : EnemyState
{
    private readonly SantelmoBrain brain;
    private float progress;
    private float lostFor;
    private PlayerTrance trance;

    public SantelmoGazingState(SantelmoBrain brain) => this.brain = brain;

    public override string Name => "Gazing";
    public PlayerPersistence Target { get; set; }
    public float Progress => progress;

    public override void Enter()
    {
        brain.Motor.Stop();
        progress = 0f;
        lostFor = 0f;
        trance = Target != null ? Target.GetComponent<PlayerTrance>() : null;
        brain.SetGaze(Target != null ? Target.netIdentity : null, 0f);
    }

    public override void Tick(float deltaTime)
    {
        if (Target == null || trance == null || !Target.isActiveAndEnabled || Target.IsAboard || trance.InTrance ||
            (Target.TryGetComponent(out PlayerHealth health) && health.IsDead))
        {
            brain.ChangeState(brain.Wandering);
            return;
        }

        brain.Motor.Stop();
        brain.LookTarget = Target.transform.position;
        if (brain.Senses.CanSee(Target))
            lostFor = 0f;
        else
        {
            lostFor += deltaTime;
            if (lostFor > brain.Profile.lostSightGraceSeconds)
            {
                // Escaped before the trance: leave them be for a while and drift on.
                brain.Ignore(Target, brain.Profile.escapeCooldownSeconds);
                brain.ChangeState(brain.Wandering);
            }
            return;
        }

        progress += deltaTime / brain.Profile.gazeSeconds;
        if (progress >= 1f)
        {
            PlayerPersistence victim = Target;
            if (trance.ServerStartTrance(brain.netIdentity))
                brain.Ignore(victim, brain.Profile.tranceCooldownSeconds);
            brain.ChangeState(brain.Wandering);
            return;
        }
        trance.ServerSetGaze(progress);
        brain.SetGaze(Target.netIdentity, progress);
    }

    public override void Exit()
    {
        if (trance != null && !trance.InTrance)
            trance.ServerSetGaze(0f);
        trance = null;
        Target = null;
        brain.LookTarget = null;
        brain.SetGaze(null, 0f);
    }
}
