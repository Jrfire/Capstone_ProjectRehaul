using UnityEngine;

/// <summary>
/// Server-side free 3D flight with snappy (instant) speed changes; the root is synced by the NetworkTransform. Flies
/// straight at its goal while the way there is clear and otherwise climbs over walls/buildings in the way (stopping
/// horizontally until it is above them) instead of passing through them. Players and the enemy itself never block.
/// <see cref="ApproachPoint"/> lets chasers reach targets under roofs by dropping down beside the cover first;
/// <see cref="PursueTowards"/> also passes straight through the cover when the target is fully enclosed.
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
    [SerializeField, Min(1f), Tooltip("How far from a covered target (under a roof) it searches for open sky to drop down and fly in underneath.")]
    private float entrySearchRadius = 15f;
    [SerializeField, Min(1f), Tooltip("Height checked above a target for a roof or overhang covering it.")]
    private float coverCheckHeight = 40f;
    [SerializeField, Tooltip("When a covered target is fully enclosed (no open way in underneath), fly straight through the cover to it instead of hovering above it.")]
    private bool passThroughWhenEnclosed = true;

    private const int EntryDirections = 16;
    private static readonly RaycastHit[] hits = new RaycastHit[32];
    private static readonly Collider[] overlaps = new Collider[32];
    private Vector3 velocity;
    private bool hasEntry;
    private Vector3 entryPoint;
    private Vector3 entryGoal;
    private float nextEntrySearch;

    public override Vector3 Velocity => velocity;
    public override bool IsMoving => velocity.sqrMagnitude > 0.01f;
    public float ArriveDistance => arriveDistance;
    public float BodyCenterHeight => bodyCenterHeight;
    public float ObstacleClearance => obstacleClearance;
    public LayerMask ObstacleMask { get => obstacleMask; set => obstacleMask = value; }
    /// <summary>True while the last step climbed because the straight way to the goal was blocked.</summary>
    public bool IsAvoiding { get; private set; }
    /// <summary>
    /// True when the last <see cref="ApproachPoint"/> found the goal enclosed (covered, with no open way in) and
    /// <see cref="PassThroughWhenEnclosed"/> is on: callers should use <see cref="FlyThrough"/>.
    /// </summary>
    public bool IsEnclosedTarget { get; private set; }
    public bool PassThroughWhenEnclosed { get => passThroughWhenEnclosed; set => passThroughWhenEnclosed = value; }

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

    /// <summary>Server: moves one frame straight toward <paramref name="goal"/>, ignoring walls and roofs.</summary>
    public void FlyThrough(Vector3 goal, float speed, float deltaTime)
    {
        IsAvoiding = false;
        Vector3 toGoal = goal - transform.position;
        float distance = toGoal.magnitude;
        if (deltaTime <= 0f || distance <= 0.001f || speed <= 0f)
        {
            velocity = Vector3.zero;
            return;
        }
        velocity = toGoal / distance * (Mathf.Min(speed * deltaTime, distance) / deltaTime);
        transform.position += velocity * deltaTime;
    }

    /// <summary>
    /// Server: one chase step toward <paramref name="goal"/>: flies in under cover through an open side when it can,
    /// and straight through the cover when the goal is enclosed (see <see cref="PassThroughWhenEnclosed"/>).
    /// </summary>
    public void PursueTowards(Vector3 goal, float speed, float deltaTime)
    {
        Vector3 next = ApproachPoint(goal);
        if (IsEnclosedTarget)
            FlyThrough(goal, speed, deltaTime);
        else
            FlyTowards(next, speed, deltaTime);
    }

    /// <summary>
    /// Server: where to fly next to reach <paramref name="goal"/>. Normally the goal itself (climbing over anything in
    /// the way); when the goal is covered (under a roof or overhang) and cannot be reached in a straight line, an open-sky
    /// point beside the cover at the goal's height, from which the goal can be reached horizontally. Flying there first
    /// lets it drop down outside the roof and then slip underneath.
    /// </summary>
    public Vector3 ApproachPoint(Vector3 goal)
    {
        IsEnclosedTarget = false;
        Vector3 position = transform.position;
        Vector3 toGoal = goal - position;
        float distance = toGoal.magnitude;
        if (distance <= arriveDistance || !IsBlocked(position + Vector3.up * bodyCenterHeight, toGoal / distance, distance))
        {
            hasEntry = false;
            return goal;
        }
        if (!IsCovered(goal))
        {
            hasEntry = false;
            return goal;
        }

        // The entry is re-planned only when the goal moves noticeably or now and then, as the search costs many casts.
        if (!hasEntry || (goal - entryGoal).sqrMagnitude > 1f || Time.time >= nextEntrySearch)
        {
            hasEntry = TryFindEntry(goal, position, out entryPoint);
            entryGoal = goal;
            nextEntrySearch = Time.time + 0.5f;
        }
        IsEnclosedTarget = !hasEntry && passThroughWhenEnclosed;
        return hasEntry ? entryPoint : goal;
    }

    /// <summary>True when something solid is directly above the goal's body position (a roof, awning or overhang).</summary>
    public bool IsCovered(Vector3 goal) =>
        IsBlocked(goal + Vector3.up * bodyCenterHeight, Vector3.up, coverCheckHeight);

    private bool TryFindEntry(Vector3 goal, Vector3 from, out Vector3 entry)
    {
        entry = goal;
        float best = float.PositiveInfinity;
        Vector3 goalCenter = goal + Vector3.up * bodyCenterHeight;
        float step = Mathf.Max(0.5f, bodyRadius);
        for (int i = 0; i < EntryDirections; i++)
        {
            float angle = i * (360f / EntryDirections) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            for (float d = step; d <= entrySearchRadius + 0.001f; d += step)
            {
                Vector3 candidateCenter = goalCenter + direction * d;
                // The horizontal way back to the goal must be open; past the first wall this direction is useless.
                if (Overlaps(candidateCenter) || IsBlocked(candidateCenter, -direction, d))
                    break;
                if (IsBlocked(candidateCenter, Vector3.up, coverCheckHeight))
                    continue;
                Vector3 candidate = goal + direction * d;
                float cost = Vector3.Distance(from, candidate) + d;
                if (cost < best)
                {
                    best = cost;
                    entry = candidate;
                }
                break;
            }
        }
        return best < float.PositiveInfinity;
    }

    private bool Overlaps(Vector3 center)
    {
        int count = Physics.OverlapSphereNonAlloc(center, bodyRadius, overlaps, obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsIgnored(overlaps[i]))
                return true;
        }
        return false;
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
