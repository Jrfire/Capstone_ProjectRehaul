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
    public float gazeSeconds = 7f;
    [Min(0f), Tooltip("Brief losses of sight shorter than this keep the gaze going.")]
    public float lostSightGraceSeconds = 0.75f;
    [Min(0f), Tooltip("Seconds it ignores a player who escaped its gaze once its search of their last known location finds nobody.")]
    public float escapeCooldownSeconds = 8f;
    [Min(0f), Tooltip("Seconds it ignores a player after entrancing them (counted from the trance start).")]
    public float tranceCooldownSeconds = 90f;

    [Header("Investigating (after a player escapes its gaze)")]
    [Min(0f), Tooltip("Speed while drifting to the escaped player's last known location.")]
    public float investigateSpeed = 3f;
    [Min(0f), Tooltip("Seconds it hovers there looking around before wandering on.")]
    public float investigateLookSeconds = 3f;
    [Min(1f), Tooltip("Gives up the search after this long.")]
    public float investigateTimeoutSeconds = 20f;

    [Header("Blood moon")]
    [Min(0f), Tooltip("Wander and investigate speed during a blood moon.")]
    public float bloodMoonWanderSpeed = 4.5f;
    [Min(0.5f), Tooltip("Gaze time during a blood moon (never longer than Gaze Seconds).")]
    public float bloodMoonGazeSeconds = 4f;

    [Header("Molotov fire (it is made of fire)")]
    [Min(0f), Tooltip("Wandering, it heads for any burning molotov fire within this distance (0 = anywhere on the map).")]
    public float fireAttractionRange = 0f;
    [Min(1f), Tooltip("Speed multiplier while empowered by a fire's core.")]
    public float fireCoreSpeedMultiplier = 1.5f;
    [Range(0.1f, 1f), Tooltip("Gaze time multiplier while empowered by a fire's core (0.5 = entrances twice as fast).")]
    public float fireCoreGazeMultiplier = 0.5f;
    [Min(0f), Tooltip("Seconds the empowerment lasts after leaving the core.")]
    public float fireEmpowerSeconds = 8f;

    [Header("Stun")]
    [Min(0f)] public float defaultStunSeconds = 3f;
}
