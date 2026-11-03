using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public enum ObstacleRequirement
{
    Key,
    Destruction,
    KeyAndDestruction
}

/// <summary>
/// Blocks a street block entrance until its requirement is met. Key and destruction tools call
/// <see cref="TryUseKey"/> and <see cref="TryDestroy"/>; with KeyAndDestruction both are needed, in any order.
/// State is local for now and will be networked together with level generation.
/// </summary>
[DisallowMultipleComponent]
public sealed class EntranceObstacle : MonoBehaviour
{
    [SerializeField] private ObstacleRequirement requirement = ObstacleRequirement.Key;
    [SerializeField, Tooltip("Deactivate this obstacle once its requirement is met.")]
    private bool deactivateWhenCleared = true;
    [Tooltip("Invoked once when the requirement is met.")]
    public UnityEvent cleared = new UnityEvent();

    public ObstacleRequirement Requirement => requirement;
    public bool RequiresKey => requirement != ObstacleRequirement.Destruction;
    public bool RequiresDestruction => requirement != ObstacleRequirement.Key;
    public bool KeyUsed { get; private set; }
    public bool Destroyed { get; private set; }
    public bool IsCleared => (!RequiresKey || KeyUsed) && (!RequiresDestruction || Destroyed);

    /// <summary>Applies a key. Returns false if this obstacle does not need one or already received it.</summary>
    public bool TryUseKey()
    {
        if (!RequiresKey || KeyUsed || IsCleared)
            return false;

        KeyUsed = true;
        CheckCleared();
        return true;
    }

    /// <summary>Applies destruction (e.g. an axe). Returns false if this obstacle cannot be destroyed or already was.</summary>
    public bool TryDestroy()
    {
        if (!RequiresDestruction || Destroyed || IsCleared)
            return false;

        Destroyed = true;
        CheckCleared();
        return true;
    }

    private void CheckCleared()
    {
        if (!IsCleared)
            return;

        cleared.Invoke();
        // The NavMesh "fills in" once the entrance opens, whether or not the obstacle object stays visible.
        foreach (NavMeshObstacle carver in GetComponentsInChildren<NavMeshObstacle>(true))
            carver.enabled = false;
        if (deactivateWhenCleared)
            gameObject.SetActive(false);
    }
}
