using System;
using UnityEngine;

/// <summary>
/// Server-side eyes and ears. Vision is a cone from the eye along the enemy's forward axis, blocked by solid colliders;
/// hearing is a dome that hears any noise dome it intersects (see <see cref="NoiseSystem"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemySenses : MonoBehaviour
{
    [SerializeField, Min(0f)] private float eyeHeight = 2.2f;
    [SerializeField, Min(0f)] private float visionRange = 18f;
    [SerializeField, Range(1f, 359f), Tooltip("Full width of the vision cone in degrees.")]
    private float visionAngle = 40f;
    [SerializeField, Range(-89f, 89f), Tooltip("Tilts the vision cone downward by this many degrees (0 = level). Flyers look down at the street.")]
    private float visionPitch;
    [SerializeField, Min(0f)] private float hearingRadius = 8f;
    [SerializeField, Tooltip("Layers that block sight (walls, buildings, obstacles).")]
    private LayerMask occlusionMask = ~0;

    private static readonly float[] targetHeights = { 0.9f, 1.6f };
    private static readonly RaycastHit[] hits = new RaycastHit[32];

    public float EyeHeight { get => eyeHeight; set => eyeHeight = Mathf.Max(0f, value); }
    public float VisionRange { get => visionRange; set => visionRange = Mathf.Max(0f, value); }
    public float VisionAngle { get => visionAngle; set => visionAngle = Mathf.Clamp(value, 1f, 359f); }
    public float VisionPitch { get => visionPitch; set => visionPitch = Mathf.Clamp(value, -89f, 89f); }
    /// <summary>Centre of the vision cone: the body's forward tilted down by <see cref="VisionPitch"/>.</summary>
    public Vector3 VisionAxis => Quaternion.AngleAxis(visionPitch, transform.right) * transform.forward;
    public float HearingRadius { get => hearingRadius; set => hearingRadius = Mathf.Max(0f, value); }
    public LayerMask OcclusionMask { get => occlusionMask; set => occlusionMask = value; }
    public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;

    public bool CanSee(PlayerPersistence player)
    {
        if (player == null || !player.isActiveAndEnabled)
            return false;
        if (player.TryGetComponent(out PlayerHealth health) && health.CurrentHealth <= 0)
            return false;

        Vector3 eye = EyePosition;
        Vector3 axis = VisionAxis;
        foreach (float height in targetHeights)
        {
            Vector3 point = player.transform.position + Vector3.up * height;
            Vector3 toPoint = point - eye;
            float distance = toPoint.magnitude;
            if (distance > visionRange)
                continue;
            if (distance > 0.01f && Vector3.Angle(axis, toPoint) > visionAngle * 0.5f)
                continue;
            if (IsSegmentClear(eye, point, occlusionMask, transform, player.transform))
                return true;
        }
        return false;
    }

    /// <summary>Closest player that is visible and accepted by <paramref name="filter"/>.</summary>
    public PlayerPersistence FindVisiblePlayer(Func<PlayerPersistence, bool> filter = null)
    {
        PlayerPersistence best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (player.IsAboard || player.IsInTrance || IsDead(player) || (filter != null && !filter(player)))
                continue;
            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance < bestDistance && CanSee(player))
            {
                best = player;
                bestDistance = distance;
            }
        }
        return best;
    }

    public bool TryHear(Func<Vector3, bool> filter, out Vector3 origin) =>
        NoiseSystem.TryHear(transform.position, hearingRadius, filter, out origin);

    private static bool IsDead(PlayerPersistence player) =>
        player.TryGetComponent(out PlayerHealth health) && health.IsDead;

    /// <summary>True if no solid collider (other than ones under the ignored roots) lies between the points.</summary>
    public static bool IsSegmentClear(Vector3 from, Vector3 to, LayerMask mask, Transform ignoreA, Transform ignoreB)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return true;
        int count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = hits[i].collider.transform;
            if ((ignoreA != null && hit.IsChildOf(ignoreA)) || (ignoreB != null && hit.IsChildOf(ignoreB)))
                continue;
            return false;
        }
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 eye = EyePosition;
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Vector3 axis = VisionAxis;
        foreach (float sign in new[] { -1f, 1f })
        {
            Vector3 edge = Quaternion.AngleAxis(sign * visionAngle * 0.5f, Vector3.up) * axis;
            Gizmos.DrawLine(eye, eye + edge * visionRange);
            Vector3 pitchEdge = Quaternion.AngleAxis(sign * visionAngle * 0.5f, transform.right) * axis;
            Gizmos.DrawLine(eye, eye + pitchEdge * visionRange);
        }
        Gizmos.DrawLine(eye, eye + axis * visionRange);
    }
}
