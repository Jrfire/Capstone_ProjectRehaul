using UnityEngine;

/// <summary>Walks back to the home block at walk speed, ignoring every player, then resumes Idle.</summary>
public sealed class TikbalangReturningState : EnemyState
{
    private readonly TikbalangBrain brain;

    public TikbalangReturningState(TikbalangBrain brain) => this.brain = brain;

    public override string Name => "Returning";

    public override void Enter()
    {
        brain.ApplyNormalVision();
        brain.LookTarget = null;
        brain.Motor.Stop();
        if (!brain.Home.Contains(brain.transform.position))
            HeadHome();
    }

    public override void Tick(float deltaTime)
    {
        if (brain.Home.Contains(brain.transform.position))
        {
            brain.ChangeState(brain.Idle);
            return;
        }
        if (brain.Motor.HasArrived)
            HeadHome();
    }

    private void HeadHome()
    {
        if (brain.TryGetHomePoint(out Vector3 point) && brain.Motor.MoveTo(point, brain.Profile.walkSpeed))
            return;
        // No walkable route (e.g. an entrance obstacle closed behind it): reappear somewhere hidden at home.
        brain.TryTeleportHidden(new[] { brain.Home }, brain.Profile.teleportMinPlayerDistance, null);
    }
}
