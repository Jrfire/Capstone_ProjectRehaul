using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-side NavMeshAgent wrapper with snappy movement: instant acceleration, stops and turns. The prefab keeps the
/// agent disabled so clients never move enemies; the server enables it once it stands on a NavMesh.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyMotor : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float arriveDistance = 0.35f;
    [SerializeField, Min(0.1f), Tooltip("How far from its position the enemy may snap onto the NavMesh when it activates.")]
    private float activationSearchRadius = 4f;

    private NavMeshAgent agent;
    private NavMeshPath scratchPath;

    public NavMeshAgent Agent => agent;
    public bool IsActive => agent != null && agent.enabled && agent.isOnNavMesh;
    public Vector3 Velocity => IsActive ? agent.velocity : Vector3.zero;
    public bool IsMoving => IsActive && !agent.isStopped && agent.velocity.sqrMagnitude > 0.01f;
    public int AreaMask => agent != null ? agent.areaMask : NavMesh.AllAreas;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.acceleration = 1000f;
        agent.angularSpeed = 0f;
        agent.autoBraking = true;
        scratchPath = new NavMeshPath();
    }

    /// <summary>Enables the agent on the nearest NavMesh point. Returns false while no NavMesh exists nearby.</summary>
    public bool Activate()
    {
        if (IsActive)
            return true;
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, activationSearchRadius, AreaMask))
            return false;
        transform.position = hit.position;
        agent.enabled = true;
        agent.Warp(hit.position);
        return agent.isOnNavMesh;
    }

    public bool MoveTo(Vector3 destination, float speed)
    {
        if (!IsActive)
            return false;
        agent.speed = Mathf.Max(0f, speed);
        agent.isStopped = false;
        return agent.SetDestination(destination);
    }

    public void Stop()
    {
        if (!IsActive)
            return;
        agent.isStopped = true;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    public bool HasArrived =>
        !IsActive || (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, arriveDistance)));

    public void Warp(Vector3 position)
    {
        if (IsActive)
        {
            agent.ResetPath();
            agent.Warp(position);
            agent.velocity = Vector3.zero;
        }
        else
        {
            transform.position = position;
        }
    }

    /// <summary>Turns instantly (no smoothing) to look horizontally at <paramref name="point"/>.</summary>
    public void FaceTowards(Vector3 point) => FaceDirection(point - transform.position);

    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    public void FaceYaw(float yaw) => transform.rotation = Quaternion.Euler(0f, yaw, 0f);

    public bool HasCompletePath(Vector3 from, Vector3 to)
    {
        return NavMesh.CalculatePath(from, to, AreaMask, scratchPath) && scratchPath.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>Random NavMesh point inside <paramref name="area"/>, optionally reachable from the enemy.</summary>
    public bool TryRandomPoint(EnemyHomeArea area, int attempts, bool requireReachable, out Vector3 point)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (!NavMesh.SamplePosition(area.RandomPoint(), out NavMeshHit hit, 2f, AreaMask) || !area.Contains(hit.position))
                continue;
            if (requireReachable && IsActive && !HasCompletePath(transform.position, hit.position))
                continue;
            point = hit.position;
            return true;
        }
        point = default;
        return false;
    }
}
