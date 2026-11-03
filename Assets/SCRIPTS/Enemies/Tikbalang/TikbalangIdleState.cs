/// <summary>Guards the home block: walks to random points in it and stands at each for the profile's wait time.</summary>
public sealed class TikbalangIdleState : EnemyState
{
    private readonly TikbalangBrain brain;
    private bool waiting;
    private float waitLeft;

    public TikbalangIdleState(TikbalangBrain brain) => this.brain = brain;

    public override string Name => "Idle";
    public bool IsWaiting => waiting;

    public override void Enter()
    {
        brain.ApplyNormalVision();
        brain.LookTarget = null;
        brain.Motor.Stop();
        PickNextPoint();
    }

    public override void Tick(float deltaTime)
    {
        if (brain.TrySpotPlayerInHome(out PlayerPersistence player))
        {
            brain.StartChase(player);
            return;
        }
        if (brain.TryHearInHome(out UnityEngine.Vector3 origin))
        {
            brain.StartSearch(origin);
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
            waitLeft = brain.Profile.idleWaitSeconds;
        }
    }

    private void PickNextPoint()
    {
        if (brain.TryGetHomePoint(out UnityEngine.Vector3 point) && brain.Motor.MoveTo(point, brain.Profile.walkSpeed))
        {
            waiting = false;
            return;
        }
        // Nowhere reachable right now; try again shortly.
        waiting = true;
        waitLeft = 1f;
    }
}
