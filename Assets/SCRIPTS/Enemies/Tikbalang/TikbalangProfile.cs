using UnityEngine;

/// <summary>
/// Tunable numbers for one Tikbalang version. The default version uses these values; a harder version (later) is just
/// another profile asset assigned to a Tikbalang prefab variant.
/// </summary>
[CreateAssetMenu(fileName = "TikbalangProfile", menuName = "Capstone/Enemies/Tikbalang Profile")]
public sealed class TikbalangProfile : ScriptableObject
{
    [Header("Movement (m/s)")]
    [Min(0f)] public float walkSpeed = 2.5f;
    [Min(0f)] public float chaseSpeed = 6f;

    [Header("Vision (cone)")]
    [Min(0f)] public float eyeHeight = 2.2f;
    [Min(0f)] public float visionRange = 18f;
    [Range(1f, 359f), Tooltip("Full cone width while idle/returning. Narrower than a player's view.")]
    public float visionAngle = 40f;
    [Range(1f, 359f), Tooltip("Wider cone while searching for a noise.")]
    public float searchingVisionAngle = 80f;

    [Header("Hearing (dome)")]
    [Min(0f)] public float hearingRadius = 8f;

    [Header("Idle")]
    [Min(0f), Tooltip("Seconds it stands at each wander point.")]
    public float idleWaitSeconds = 10f;
    [Min(0.5f), Tooltip("Half size of the guarded area when it spawns without a street map.")]
    public Vector2 fallbackHomeHalfSize = new Vector2(10f, 10f);

    [Header("Searching")]
    [Min(0f), Tooltip("Total time spent looking around at the noise origin.")]
    public float lookAroundSeconds = 4f;
    [Tooltip("Snapped yaw offsets (degrees) it looks at, in order, while looking around.")]
    public float[] lookAroundAngles = { -90f, 90f, 180f, 0f };
    [Min(1f), Tooltip("Gives up and returns to Idle if it cannot reach the noise in time.")]
    public float searchTimeoutSeconds = 20f;

    [Header("Chase")]
    [Min(1f)] public float chaseDurationSeconds = 60f;
    [Min(0.05f)] public float repathInterval = 0.25f;
    [Min(0f), Tooltip("Stops this close to the target.")]
    public float catchDistance = 1.2f;

    [Header("Contact attack (Chase only)")]
    [Min(0), Tooltip("Health removed from the chased player on touch (2 = one heart).")]
    public int contactDamage = 2;
    [Min(0f), Tooltip("Horizontal distance from the chased player that counts as touching.")]
    public float attackRange = 1.4f;
    [Min(0f), Tooltip("Seconds the Tikbalang stands still after a hit before resuming the chase.")]
    public float hitPauseSeconds = 1.5f;
    [Min(1f), Tooltip("Straight-line distance at which it teleports to a hidden spot next to the target's block.")]
    public float teleportDistance = 35f;
    [Min(0f), Tooltip("A teleport destination must be at least this far from every player.")]
    public float teleportMinPlayerDistance = 10f;
    [Min(0f)] public float teleportCooldownSeconds = 5f;

    [Header("Stun")]
    [Min(0f)] public float defaultStunSeconds = 4f;
}
