using UnityEngine;

/// <summary>
/// Waits invisible for a random delay, then appears outside one edge of the map and flies a straight high pass over
/// the street blocks, looking down for players. The first player it sees starts a chase; reaching the far side
/// despawns it again until the next pass.
/// </summary>
public sealed class ManananggalHuntingState : EnemyState
{
    public enum HuntPhase
    {
        Waiting,
        Revealing,
        Flying
    }

    private readonly ManananggalBrain brain;
    private bool hasFlown;
    private float timer;

    public ManananggalHuntingState(ManananggalBrain brain) => this.brain = brain;

    public override string Name => "Hunting";
    public HuntPhase Phase { get; private set; }
    /// <summary>Seconds left before the next pass starts (Waiting) or before it appears (Revealing).</summary>
    public float TimeRemaining => timer;
    public Vector3 PassStart { get; private set; }
    public Vector3 PassEnd { get; private set; }
    public int PassCount { get; private set; }
    public int CompletedPassCount { get; private set; }

    public override void Enter()
    {
        brain.ApplyVision();
        brain.LookTarget = null;
        brain.ClearChaseTarget();
        brain.Flight.Stop();
        brain.SetHidden(true);
        Phase = HuntPhase.Waiting;
        timer = ManananggalProfile.RandomIn(hasFlown ? brain.Profile.respawnDelaySeconds : brain.Profile.firstPassDelaySeconds);
    }

    /// <summary>Skips the remaining wait (for tests/debugging).</summary>
    public void SkipWait() => timer = 0f;

    public override void Tick(float deltaTime)
    {
        switch (Phase)
        {
            case HuntPhase.Waiting:
                timer -= deltaTime;
                if (timer > 0f)
                    return;
                if (!brain.TryPlanPass(out Vector3 start, out Vector3 end))
                {
                    timer = 1f;
                    return;
                }
                PassStart = start;
                PassEnd = end;
                brain.Flight.Warp(start);
                brain.Flight.FaceTowards(end);
                hasFlown = true;
                PassCount++;
                Phase = HuntPhase.Revealing;
                timer = brain.Profile.revealDelaySeconds;
                return;

            case HuntPhase.Revealing:
                timer -= deltaTime;
                if (timer > 0f)
                    return;
                brain.SetHidden(false);
                Phase = HuntPhase.Flying;
                return;

            case HuntPhase.Flying:
                if (brain.TrySpotPlayer(out PlayerPersistence player))
                {
                    brain.StartChase(player);
                    return;
                }
                brain.Flight.FlyTowards(PassEnd, brain.Profile.huntSpeed, deltaTime);
                Vector3 left = PassEnd - brain.transform.position;
                left.y = 0f;
                if (left.sqrMagnitude <= brain.Flight.ArriveDistance * brain.Flight.ArriveDistance)
                {
                    CompletedPassCount++;
                    Enter();
                }
                return;
        }
    }
}
