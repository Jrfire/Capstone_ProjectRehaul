/// <summary>
/// Drifts between random points across the whole map, hovering a moment at each, until it spots a player. A burning
/// molotov fire draws it in: it heads there and hovers in the flames (empowered, see SantelmoBrain) until the fire dies.
/// </summary>
public sealed class SantelmoWanderingState : EnemyState
{
    private readonly SantelmoBrain brain;
    private bool waiting;
    private float waitLeft;
    private FireZone headingTo;

    public SantelmoWanderingState(SantelmoBrain brain) => this.brain = brain;

    public override string Name => "Wandering";
    public bool IsWaiting => waiting;
    /// <summary>The fire it is heading to or hovering in, or null.</summary>
    public FireZone HeadingTo => headingTo;

    public override void Enter()
    {
        brain.ApplyVision();
        brain.LookTarget = null;
        brain.SetGaze(null, 0f);
        brain.Motor.Stop();
        headingTo = null;
        PickNextPoint();
    }

    public override void Tick(float deltaTime)
    {
        if (brain.TrySpotPlayer(out PlayerPersistence player))
        {
            brain.StartGaze(player);
            return;
        }
        if (FollowFire())
            return;
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

    private bool FollowFire()
    {
        FireZone fire = brain.AttractingFire;
        if (fire == null)
        {
            if (headingTo != null)
            {
                headingTo = null;
                PickNextPoint();
            }
            return false;
        }
        if (fire.InCore(brain.transform.position))
        {
            brain.Motor.Stop(); // hover in the flames
            waiting = false;
        }
        else if (headingTo != fire || brain.Motor.HasArrived)
        {
            if (!brain.Motor.MoveTo(fire.transform.position, brain.WanderSpeed))
                return false;
            waiting = false;
        }
        headingTo = fire;
        return true;
    }

    private void PickNextPoint()
    {
        if (brain.TryGetWanderPoint(out UnityEngine.Vector3 point) && brain.Motor.MoveTo(point, brain.WanderSpeed))
        {
            waiting = false;
            return;
        }
        waiting = true;
        waitLeft = 1f;
    }
}
