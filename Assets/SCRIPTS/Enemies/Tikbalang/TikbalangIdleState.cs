/// <summary>
/// Guards the home block: walks to random points in it and stands at each for the profile's wait time. During a blood
/// moon it hunts instead: any visible player or heard noise anywhere counts, and it walks to points in the nearest
/// player's block, pausing only briefly.
/// </summary>
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
        bool hunting = brain.IsBloodMoon;
        if (hunting ? brain.TrySpotAnyPlayer(out PlayerPersistence player) : brain.TrySpotPlayerInHome(out player))
        {
            brain.StartChase(player);
            return;
        }
        if (hunting ? brain.TryHearAny(out UnityEngine.Vector3 origin) : brain.TryHearInHome(out origin))
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
            waitLeft = hunting ? brain.Profile.bloodMoonHuntWaitSeconds : brain.Profile.idleWaitSeconds;
        }
    }

    private void PickNextPoint()
    {
        bool hunting = brain.IsBloodMoon;
        UnityEngine.Vector3 point;
        bool found = hunting ? brain.TryGetHuntPoint(out point) : brain.TryGetHomePoint(out point);
        float speed = brain.SpeedFor(hunting ? brain.Profile.bloodMoonHuntSpeed : brain.Profile.walkSpeed);
        if (found && brain.Motor.MoveTo(point, speed))
        {
            waiting = false;
            return;
        }
        // Nowhere reachable right now; try again shortly.
        waiting = true;
        waitLeft = 1f;
    }
}
