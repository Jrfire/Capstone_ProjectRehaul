/// <summary>Drifts between random points across the whole map, hovering a moment at each, until it spots a player.</summary>
public sealed class SantelmoWanderingState : EnemyState
{
    private readonly SantelmoBrain brain;
    private bool waiting;
    private float waitLeft;

    public SantelmoWanderingState(SantelmoBrain brain) => this.brain = brain;

    public override string Name => "Wandering";
    public bool IsWaiting => waiting;

    public override void Enter()
    {
        brain.ApplyVision();
        brain.LookTarget = null;
        brain.SetGaze(null, 0f);
        brain.Motor.Stop();
        PickNextPoint();
    }

    public override void Tick(float deltaTime)
    {
        if (brain.TrySpotPlayer(out PlayerPersistence player))
        {
            brain.StartGaze(player);
            return;
        }
        if (waiting)
        {
            waitLeft -= deltaTime;
            if (waitLeft <= 0f)
                PickNextPoint();
        }
        else if (brain.Motor.HasArrived)
        {
            brain.Motor.Stop();
            waiting = true;
            waitLeft = brain.Profile.wanderPauseSeconds;
        }
    }

    private void PickNextPoint()
    {
        if (brain.TryGetWanderPoint(out UnityEngine.Vector3 point) && brain.Motor.MoveTo(point, brain.Profile.wanderSpeed))
        {
            waiting = false;
            return;
        }
        waiting = true;
        waitLeft = 1f;
    }
}
