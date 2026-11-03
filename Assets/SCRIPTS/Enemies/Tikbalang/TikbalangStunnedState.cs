/// <summary>Stands still for <see cref="Duration"/> seconds, then walks home. Any chase target is dropped.</summary>
public sealed class TikbalangStunnedState : EnemyState
{
    private readonly TikbalangBrain brain;
    private float timeLeft;

    public TikbalangStunnedState(TikbalangBrain brain) => this.brain = brain;

    public override string Name => "Stunned";
    public float Duration { get; set; }

    public override void Enter()
    {
        brain.LookTarget = null;
        brain.ClearChaseTarget();
        brain.Motor.Stop();
        timeLeft = Duration;
    }

    public override void Tick(float deltaTime)
    {
        timeLeft -= deltaTime;
        if (timeLeft <= 0f)
            brain.ChangeState(brain.Returning);
    }
}
