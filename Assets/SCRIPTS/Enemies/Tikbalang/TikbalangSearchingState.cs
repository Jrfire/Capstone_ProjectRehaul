using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Investigates a noise heard inside the home block: widens the vision cone, turns toward the origin, walks there and
/// looks around in snapped turns. Spotting a player starts a chase; otherwise it returns to Idle.
/// </summary>
public sealed class TikbalangSearchingState : EnemyState
{
    private readonly TikbalangBrain brain;
    private Vector3 origin;
    private bool lookingAround;
    private int lookIndex;
    private float lookTimer;
    private float baseYaw;
    private float elapsed;

    public TikbalangSearchingState(TikbalangBrain brain) => this.brain = brain;

    public override string Name => "Searching";
    public Vector3 Origin => origin;
    public bool IsLookingAround => lookingAround;

    public void SetOrigin(Vector3 noiseOrigin)
    {
        origin = noiseOrigin;
        if (brain.CurrentState == this)
            WalkToOrigin();
    }

    public override void Enter()
    {
        brain.ApplySearchingVision();
        brain.LookTarget = null;
        elapsed = 0f;
        WalkToOrigin();
    }

    public override void Exit() => brain.ApplyNormalVision();

    public override void Tick(float deltaTime)
    {
        if (brain.TrySpotAnyPlayer(out PlayerPersistence player))
        {
            brain.StartChase(player);
            return;
        }

        elapsed += deltaTime;
        if (elapsed >= brain.Profile.searchTimeoutSeconds)
        {
            brain.ChangeState(brain.Idle);
            return;
        }

        if (brain.TryHearInHome(out Vector3 newOrigin) && (newOrigin - origin).sqrMagnitude > 4f)
        {
            SetOrigin(newOrigin);
            return;
        }

        if (!lookingAround)
        {
            if (brain.Motor.HasArrived)
                StartLookingAround();
            return;
        }

        float[] angles = brain.Profile.lookAroundAngles;
        float step = brain.Profile.lookAroundSeconds / Mathf.Max(1, angles.Length);
        lookTimer += deltaTime;
        if (lookTimer < step)
            return;
        lookTimer = 0f;
        lookIndex++;
        if (lookIndex >= angles.Length)
            brain.ChangeState(brain.Idle);
        else
            brain.Motor.FaceYaw(baseYaw + angles[lookIndex]);
    }

    private void WalkToOrigin()
    {
        lookingAround = false;
        brain.Motor.FaceTowards(origin);
        if (NavMesh.SamplePosition(origin, out NavMeshHit hit, 3f, brain.Motor.AreaMask) &&
            brain.Motor.MoveTo(hit.position, brain.Profile.walkSpeed))
            return;
        StartLookingAround();
    }

    private void StartLookingAround()
    {
        brain.Motor.Stop();
        lookingAround = true;
        lookIndex = 0;
        lookTimer = 0f;
        baseYaw = brain.transform.eulerAngles.y;
        float[] angles = brain.Profile.lookAroundAngles;
        if (angles.Length > 0)
            brain.Motor.FaceYaw(baseYaw + angles[0]);
    }
}
