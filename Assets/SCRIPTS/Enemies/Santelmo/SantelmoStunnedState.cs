/// <summary>Hovers still for <see cref="Duration"/> seconds (any gaze is broken), then wanders on.</summary>
public sealed class SantelmoStunnedState : EnemyState
{
    private readonly SantelmoBrain brain;
    private float timeLeft;

    public SantelmoStunnedState(SantelmoBrain brain) => this.brain = brain;

    public override string Name => "Stunned";
    public float Duration { get; set; }

    public override void Enter()
    {
        brain.LookTarget = null;
        brain.SetGaze(null, 0f);
        brain.Motor.Stop();
        timeLeft = Duration;
    }

    public override void Tick(float deltaTime)
    {
        timeLeft -= deltaTime;
        if (timeLeft <= 0f)
            brain.ChangeState(brain.Wandering);
    }
}
