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

    public override void Enter()
    {
        elapsed = 0f;
        brain.LookTarget = null;
        Vector3 away = brain.Flight.Velocity;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
            away = brain.transform.forward;
        ExitPoint = brain.GetExitPoint(brain.transform.position, away);
    }

    public override void Tick(float deltaTime)
    {
        elapsed += deltaTime;
        brain.Flight.FlyTowards(ExitPoint, brain.Profile.leaveSpeed, deltaTime);
        if (brain.Flight.HasReached(ExitPoint) || elapsed >= brain.Profile.leaveTimeoutSeconds)
            brain.ChangeState(brain.Hunting);
    }
}
