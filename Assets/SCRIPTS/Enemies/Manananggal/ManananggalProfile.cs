using UnityEngine;

/// <summary>Tunable numbers for one Manananggal version (a harder version is another profile asset).</summary>
[CreateAssetMenu(fileName = "ManananggalProfile", menuName = "Capstone/Enemies/Manananggal Profile")]
public sealed class ManananggalProfile : ScriptableObject
{
    [Header("Flight (m/s, metres)")]
    [Min(0f), Tooltip("Speed of a hunting pass over the streets.")]
    public float huntSpeed = 8f;
    [Min(0f)] public float chaseSpeed = 6.5f;
    [Min(0f), Tooltip("Speed when flying away after a chase.")]
    public float leaveSpeed = 10f;
    [Min(0f), Tooltip("Hunting height above the street floor.")]
    public float huntAltitude = 14f;
    [Min(0f), Tooltip("Height of its root above the chased player's feet while closing in.")]
    public float chaseHoverHeight = 0.5f;

    [Header("Vision (cone looking down and a little forward)")]
    [Min(0f)] public float eyeHeight = 1.2f;
    [Min(0f)] public float visionRange = 30f;
    [Range(1f, 359f), Tooltip("Full cone width.")]
    public float visionAngle = 70f;
    [Range(0f, 89f), Tooltip("Degrees the cone is tilted down from horizontal (90 would be straight down).")]
    public float visionPitch = 50f;

    [Header("Hunting passes")]
    [Tooltip("Random delay (min, max) in seconds before the very first pass after it spawns.")]
    public Vector2 firstPassDelaySeconds = new Vector2(5f, 15f);
    [Tooltip("Random delay (min, max) in seconds after it despawns before the next pass.")]
    public Vector2 respawnDelaySeconds = new Vector2(15f, 40f);
    [Min(0f), Tooltip("Passes start and end this far outside the street blocks' outer edges.")]
    public float edgeMargin = 12f;
    [Range(0f, 90f), Tooltip("Pass directions are rounded to this many degrees (0 = any direction, 45 = edges and diagonals, 90 = map axes only).")]
    public float passDirectionStep = 0f;
    [Min(1), Tooltip("A pass lane must fly over at least this many street blocks. Lanes are random parallel lines anywhere across the map, including along its edges.")]
    public int minimumBlocksCrossed = 1;
    [Min(0f), Tooltip("Seconds it stays invisible at the pass start so every client sees it appear there, not mid-flight.")]
    public float revealDelaySeconds = 0.5f;
    [Min(1f), Tooltip("Half length of a pass when there is no street map.")]
    public float fallbackPassHalfLength = 40f;

    [Header("Chase")]
    [Min(1f)] public float chaseDurationSeconds = 20f;
    [Min(0), Tooltip("Health removed from the chased player on touch (2 = one heart).")]
    public int contactDamage = 2;
    [Min(0f), Tooltip("Horizontal distance from the chased player that counts as touching.")]
    public float attackRange = 1.4f;
    [Min(0f), Tooltip("Maximum height difference between its root and the player's feet that counts as touching.")]
    public float attackHeight = 2.5f;
    [Min(0f), Tooltip("Seconds it hovers still after a hit before resuming the chase.")]
    public float hitPauseSeconds = 2.5f;

    [Header("Leaving")]
    [Min(1f), Tooltip("Gives up flying away after this long and despawns anyway.")]
    public float leaveTimeoutSeconds = 15f;

    public static float RandomIn(Vector2 range) => Random.Range(Mathf.Max(0f, Mathf.Min(range.x, range.y)), Mathf.Max(0f, range.x, range.y));
}
