using UnityEngine;

/// <summary>Tunable numbers for the Santelmo (a floating fire orb that entrances players who hold its gaze).</summary>
[CreateAssetMenu(fileName = "SantelmoProfile", menuName = "Capstone/Enemies/Santelmo Profile")]
public sealed class SantelmoProfile : ScriptableObject
{
    [Header("Wandering (whole map)")]
    [Min(0f)] public float wanderSpeed = 2f;
    [Min(0f), Tooltip("Seconds it hovers at each wander point before drifting to another block.")]
    public float wanderPauseSeconds = 3f;
    [Min(0.5f), Tooltip("Half size of the roamed area when it spawns without a street map.")]
    public Vector2 fallbackHomeHalfSize = new Vector2(12f, 12f);

    [Header("Vision (cone)")]
    [Min(0f), Tooltip("Eye height above the NavMesh (the orb floats; match WorldSprites' height).")]
    public float eyeHeight = 1.4f;
    [Min(0f)] public float visionRange = 16f;
    [Range(1f, 359f)] public float visionAngle = 70f;

    [Header("Gaze")]
    [Min(0.5f), Tooltip("Seconds a player must stay in its sight before the trance starts.")]
    public float gazeSeconds = 10f;
    [Min(0f), Tooltip("Brief losses of sight shorter than this keep the gaze going.")]
    public float lostSightGraceSeconds = 0.75f;
    [Min(0f), Tooltip("Seconds it ignores a player who escaped its gaze.")]
    public float escapeCooldownSeconds = 8f;
    [Min(0f), Tooltip("Seconds it ignores a player after entrancing them (counted from the trance start).")]
    public float tranceCooldownSeconds = 90f;

    [Header("Stun")]
    [Min(0f)] public float defaultStunSeconds = 3f;
}
