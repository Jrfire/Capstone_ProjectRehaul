/// <summary>
/// Hidden: picks a victim (another one than last time when possible) and waits
/// <see cref="TiktikProfile.firstPerchDelayMin"/>..Max seconds before stalking them.
/// </summary>
public sealed class TiktikWaitingState : EnemyState
{
    private readonly TiktikBrain brain;
    private float timeLeft;
    private float retryTimer;

    public TiktikWaitingState(TiktikBrain brain) => this.brain = brain;

    public override string Name => "Waiting";
    public float TimeLeft => timeLeft;

    public override void Enter()
    {
        brain.LookTarget = null;
        brain.SetAttacking(false);
        brain.SetVisible(false, 0f);
        brain.Locomotion.Stop();
        brain.ClearVictim();
        timeLeft = UnityEngine.Random.Range(brain.Profile.firstPerchDelayMin,
            UnityEngine.Mathf.Max(brain.Profile.firstPerchDelayMin, brain.Profile.firstPerchDelayMax));
        retryTimer = 0f;
    }

    public override void Tick(float deltaTime)
    {
        if (!brain.VictimIsValid)
        {
            retryTimer -= deltaTime;
            if (retryTimer > 0f)
                return;
            retryTimer = 1f;
            if (!brain.TryPickVictim())
                return;
        }
        timeLeft -= deltaTime;
        if (timeLeft <= 0f)
            brain.ChangeState(brain.Stalking);
    }
}
