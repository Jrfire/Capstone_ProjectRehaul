using UnityEngine;

/// <summary>
/// Perches on rooftops near the victim (block corners when there are none): moves there hidden, appears and eyes the
/// victim for a few seconds, fades away, waits, and repeats. Once <see cref="TiktikProfile.attackAfterSeconds"/> have
/// passed since the victim was picked (and it is hidden again) the attack starts.
/// </summary>
public sealed class TiktikStalkingState : EnemyState
{
    public enum Phase
    {
        Gap,
        Revealing,
        Perched,
    }

    private readonly TiktikBrain brain;
    private float timeLeft;

    public TiktikStalkingState(TiktikBrain brain) => this.brain = brain;

    public override string Name => "Stalking";
    public Phase Current { get; private set; }
    public int PerchCount { get; private set; }
    public Vector3 LastPerch { get; private set; }

    public override void Enter()
    {
        brain.SetAttacking(false);
        brain.Motor.Suspend();
        Current = Phase.Gap;
        timeLeft = 0f;
    }

    public override void Exit()
    {
        brain.LookTarget = null;
    }

    public override void Tick(float deltaTime)
    {
        PlayerPersistence victim = brain.VictimPlayer;
        if (!TiktikBrain.IsValidVictim(victim))
        {
            brain.ChangeState(brain.Waiting);
            return;
        }

        TiktikProfile profile = brain.Profile;
        bool attackDue = brain.SinceVictimPicked >= profile.attackAfterSeconds;
        timeLeft -= deltaTime;
        switch (Current)
        {
            case Phase.Gap:
                if (timeLeft > 0f)
                    return;
                if (attackDue)
                {
                    brain.ChangeState(brain.Attacking);
                    return;
                }
                if (!brain.TryFindPerch(victim, out Vector3 perch))
                {
                    timeLeft = 1f;
                    return;
                }
                brain.PerchAt(perch, victim.transform.position);
                LastPerch = perch;
                Current = Phase.Revealing;
                timeLeft = profile.revealDelay;
                return;

            case Phase.Revealing:
                brain.LookTarget = victim.transform.position;
                if (attackDue)
                {
                    Current = Phase.Gap;
                    timeLeft = 0f;
                    return;
                }
                if (timeLeft > 0f)
                    return;
                brain.SetVisible(true, profile.perchFadeSeconds);
                PerchCount++;
                Current = Phase.Perched;
                timeLeft = Random.Range(profile.perchVisibleMin, Mathf.Max(profile.perchVisibleMin, profile.perchVisibleMax));
                return;

            case Phase.Perched:
                brain.LookTarget = victim.transform.position;
                if (timeLeft > 0f && !attackDue)
                    return;
                brain.SetVisible(false, profile.perchFadeSeconds);
                Current = Phase.Gap;
                // The gap always covers the fade out, so it never moves while still showing.
                timeLeft = Mathf.Max(profile.perchFadeSeconds + 0.1f,
                    attackDue ? 0f : Random.Range(profile.perchGapMin, Mathf.Max(profile.perchGapMin, profile.perchGapMax)));
                return;
        }
    }
}
