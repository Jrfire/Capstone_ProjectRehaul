using UnityEngine;

/// <summary>
/// Server-side free 3D flight with snappy (instant) speed changes; the root is synced by the NetworkTransform. Flies
/// straight at its goal while the way there is clear and otherwise climbs over walls/buildings in the way (stopping
/// horizontally until it is above them) instead of passing through them. Players and the enemy itself never block.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyFlightMotor : EnemyLocomotion
{
    [SerializeField, Min(0.01f)] private float arriveDistance = 0.5f;
    [SerializeField, Min(0.05f), Tooltip("Radius of the flying body used for obstacle checks.")]
    private float bodyRadius = 0.6f;
    [SerializeField, Min(0f), Tooltip("Height of the body centre above the root, used for obstacle checks.")]
    private float bodyCenterHeight = 1.2f;
    [SerializeField, Min(0f), Tooltip("Height kept above the top of a wall/building it flies over.")]
    private float obstacleClearance = 2f;
    [SerializeField, Min(0.5f), Tooltip("How far ahead it looks for rooftops to climb over.")]
    private float lookAhead = 6f;
    [SerializeField, Min(0.1f), Tooltip("Maximum vertical speed while climbing over obstacles (m/s).")]
    private float climbSpeed = 10f;
    [SerializeField, Tooltip("Layers it cannot fly through (walls, buildings, obstacles).")]
    private LayerMask obstacleMask = Physics.DefaultRaycastLayers;

    private static readonly RaycastHit[] hits = new RaycastHit[32];
    private Vector3 velocity;

    public override Vector3 Velocity => velocity;
    public override bool IsMoving => velocity.sqrMagnitude > 0.01f;
    public float ArriveDistance => arriveDistance;
    public float BodyCenterHeight => bodyCenterHeight;
    public float ObstacleClearance => obstacleClearance;
    public LayerMask ObstacleMask { get => obstacleMask; set => obstacleMask = value; }
    /// <summary>True while the last step climbed because the straight way to the goal was blocked.</summary>
    public bool IsAvoiding { get; private set; }

    /// <summary>Flying needs no NavMesh.</summary>
    public override bool Activate() => true;

    public override void Stop() => velocity = Vector3.zero;

    public override void Warp(Vector3 position)
    {
        velocity = Vector3.zero;
        transform.position = position;
    }

    public bool HasReached(Vector3 goal) => (goal - transform.position).sqrMagnitude <= arriveDistance * arriveDistance;

    /// <summary>Server: moves one frame toward <paramref name="goal"/> (root position) at <paramref name="speed"/>.</summary>
    public void FlyTowards(Vector3 goal, float speed) => FlyTowards(goal, speed, Time.deltaTime);

    public void FlyTowards(Vector3 goal, float speed, float deltaTime)
    {
        Vector3 position = transform.position;
        Vector3 toGoal = goal - position;
        float distance = toGoal.magnitude;
        if (deltaTime <= 0f || distance <= 0.001f || speed <= 0f)
        {
            velocity = Vector3.zero;
            IsAvoiding = false;
            return;
        }

        float step = Mathf.Min(speed * deltaTime, distance);
        Vector3 bodyCenter = position + Vector3.up * bodyCenterHeight;
        IsAvoiding = IsBlocked(bodyCenter, toGoal / distance, distance);
        if (!IsAvoiding)
        {
            velocity = toGoal / distance * (step / deltaTime);
            transform.position = position + velocity * deltaTime;
            return;
        }

        // Climb above the highest roof between here and a little ahead, moving forward only while nothing is in front.
        Vector3 flat = new Vector3(toGoal.x, 0f, toGoal.z);
        float flatDistance = flat.magnitude;
        Vector3 forward = flatDistance > 0.001f ? flat / flatDistance : Vector3.zero;
        float desiredY = goal.y;
        if (TryFindHighestTop(position, forward, Mathf.Min(flatDistance, lookAhead), out float top))
            desiredY = Mathf.Max(desiredY, top + obstacleClearance);

        float verticalStep = Mathf.Clamp(desiredY - position.y, -climbSpeed * deltaTime, climbSpeed * deltaTime);
        Vector3 horizontal = Vector3.zero;
        if (flatDistance > 0.001f)
        {
            float horizontalStep = Mathf.Min(speed * deltaTime, flatDistance);
            if (!IsBlocked(bodyCenter + Vector3.up * verticalStep, forward, horizontalStep + bodyRadius * 0.5f))
                horizontal = forward * horizontalStep;
        }
        velocity = (horizontal + Vector3.up * verticalStep) / deltaTime;
        transform.position = position + velocity * deltaTime;
    }

    private bool IsBlocked(Vector3 from, Vector3 direction, float distance)
    {
        int count = Physics.SphereCastNonAlloc(from, bodyRadius, direction, hits, distance, obstacleMask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsIgnored(hits[i].collider))
                return true;
        }
        return false;
    }

    /// <summary>Highest solid surface under the strip from just behind the body to <paramref name="ahead"/> metres ahead.</summary>
    private bool TryFindHighestTop(Vector3 position, Vector3 forward, float ahead, out float top)
    {
        top = float.NegativeInfinity;
        float start = -bodyRadius;
        float end = ahead + bodyRadius;
        float rayTop = position.y + 200f;
        for (float offset = start; offset <= end + 0.001f; offset += Mathf.Max(0.25f, bodyRadius * 0.5f))
        {
            foreach (float side in new[] { -bodyRadius, 0f, bodyRadius })
            {
                Vector3 sideways = new Vector3(forward.z, 0f, -forward.x) * side;
                Vector3 origin = new Vector3(position.x, rayTop, position.z) + forward * offset + sideways;
                int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 400f, obstacleMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    if (!IsIgnored(hits[i].collider))
                        top = Mathf.Max(top, hits[i].point.y);
                }
            }
        }
        return top > float.NegativeInfinity;
    }

    private bool IsIgnored(Collider collider) =>
        collider.transform.IsChildOf(transform) || collider.GetComponentInParent<PlayerPersistence>() != null;
}
