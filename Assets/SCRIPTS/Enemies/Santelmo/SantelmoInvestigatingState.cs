using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// After a player escapes its gaze: drifts to where they were last seen, then hovers there looking around before
/// wandering on. It keeps watching the whole time, so any player it spots (including the escaped one) is gazed at
/// again. If the search finds nobody, the escaped player is ignored for the profile's escape cooldown.
/// </summary>
public sealed class SantelmoInvestigatingState : EnemyState
{
    private readonly SantelmoBrain brain;
    private PlayerPersistence escaped;
    private float elapsed;
    private float lookLeft;
    private float turnTimer;
    private bool arrived;

    public SantelmoInvestigatingState(SantelmoBrain brain) => this.brain = brain;

    public override string Name => "Investigating";
    public Vector3 LastKnownPosition { get; private set; }
    public bool HasArrived => arrived;

    internal void Begin(PlayerPersistence player, Vector3 lastKnown)
    {
        escaped = player;
        LastKnownPosition = lastKnown;
    }

    public override void Enter()
    {
        brain.ApplyVision();
        brain.LookTarget = null;
        brain.SetGaze(null, 0f);
        elapsed = 0f;
        arrived = false;
        lookLeft = brain.Profile.investigateLookSeconds;
        turnTimer = 0f;
        if (!NavMesh.SamplePosition(LastKnownPosition, out NavMeshHit hit, 3f, brain.Motor.AreaMask) ||
            !brain.Motor.MoveTo(hit.position, brain.InvestigateSpeed))
            Arrive();
    }

    public override void Tick(float deltaTime)
    {
        if (brain.TrySpotPlayer(out PlayerPersistence player))
        {
            brain.StartGaze(player);
            return;
        }

        elapsed += deltaTime;
        if (elapsed >= brain.Profile.investigateTimeoutSeconds)
        {
            GiveUp();
            return;
        }

        if (!arrived)
        {
            if (brain.Motor.HasArrived)
                Arrive();
            return;
        }

        brain.Motor.Stop();
        lookLeft -= deltaTime;
        turnTimer -= deltaTime;
        if (turnTimer <= 0f)
        {
            turnTimer = 0.75f;
            Vector3 look = Quaternion.Euler(0f, Random.Range(70f, 150f) * (Random.value < 0.5f ? -1f : 1f), 0f) * brain.transform.forward;
            brain.Motor.FaceTowards(brain.transform.position + look);
        }
        if (lookLeft <= 0f)
            GiveUp();
    }

    public override void Exit()
    {
        escaped = null;
    }

    private void Arrive()
    {
        arrived = true;
        brain.Motor.Stop();
    }

    private void GiveUp()
    {
        brain.Ignore(escaped, brain.Profile.escapeCooldownSeconds);
        brain.ChangeState(brain.Wandering);
    }
}
