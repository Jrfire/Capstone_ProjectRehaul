/// <summary>
/// Hit by a player's weapon: drops the chase and hovers still for <see cref="Duration"/> seconds, then flies out of
/// the map (Leaving) and hunts again later.
/// </summary>
public sealed class ManananggalStunnedState : EnemyState
{
    private readonly ManananggalBrain brain;
    private float timeLeft;

    public ManananggalStunnedState(ManananggalBrain brain) => this.brain = brain;

    public override string Name => "Stunned";
    public float Duration { get; set; }

    public override void Enter()
    {
        brain.LookTarget = null;
        brain.ClearChaseTarget();
        brain.Flight.Stop();
        timeLeft = Duration;
    }

    public override void Tick(float deltaTime)
    {
        brain.Flight.Stop();
        timeLeft -= deltaTime;
        if (timeLeft <= 0f)
            brain.ChangeState(brain.Leaving);
    }
}
