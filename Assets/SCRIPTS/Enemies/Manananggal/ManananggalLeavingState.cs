using UnityEngine;

/// <summary>
/// After a chase: climbs back to hunting altitude and flies straight out of the map (away from where it last was
/// chasing), ignoring players, then despawns and goes back to Hunting.
/// </summary>
public sealed class ManananggalLeavingState : EnemyState
{
    private readonly ManananggalBrain brain;
    private float elapsed;

    public ManananggalLeavingState(ManananggalBrain brain) => this.brain = brain;

    public override string Name => "Leaving";
    public Vector3 ExitPoint { get; private set; }
    /// <summary>When set (garlic/fire), the next Enter flies directly away from this point.</summary>
    public Vector3? AwayFrom { get; set; }

    public override void Enter()
    {
        elapsed = 0f;
        brain.LookTarget = null;
        Vector3 away = AwayFrom.HasValue ? brain.transform.position - AwayFrom.Value : brain.Flight.Velocity;
        AwayFrom = null;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = brain.transform.forward;
        ExitPoint = brain.GetExitPoint(brain.transform.position, away);
    }

    public override void Tick(float deltaTime)
    {
        if (brain.IsBloodMoon && !brain.IsRepelled)
        {
            brain.ChangeState(brain.Circling);
            return;
        }
        elapsed += deltaTime;
        brain.Flight.FlyTowards(ExitPoint, brain.Profile.leaveSpeed, deltaTime);
        if (brain.Flight.HasReached(ExitPoint) || elapsed >= brain.Profile.leaveTimeoutSeconds)
        {
            brain.Hunting.MarkFlown();
            brain.ChangeState(brain.Hunting);
        }
    }
}
