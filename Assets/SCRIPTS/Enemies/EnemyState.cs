/// <summary>One behaviour of an <see cref="EnemyBrain"/>. Each concrete state lives in its own script.</summary>
public abstract class EnemyState
{
    public abstract string Name { get; }

    public virtual void Enter()
    {
    }

    public virtual void Tick(float deltaTime)
    {
    }

    public virtual void Exit()
    {
    }
}
