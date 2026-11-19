using UnityEngine;

/// <summary>Tunable numbers for the Tiktik (stalks one chosen victim from the rooftops, then attacks from behind).</summary>
[CreateAssetMenu(fileName = "TiktikProfile", menuName = "Capstone/Enemies/Tiktik Profile")]
public sealed class TiktikProfile : ScriptableObject
{
    [Header("Choosing a victim")]
    [Min(0f), Tooltip("Shortest wait (hidden) after picking a victim before the first perch.")]
    public float firstPerchDelayMin = 5f;
    [Min(0f)] public float firstPerchDelayMax = 15f;

    [Header("Stalking (perched on rooftops)")]
    [Min(1f), Tooltip("Seconds after picking the victim when the stalking ends and the attack begins.")]
    public float attackAfterSeconds = 40f;
    [Min(0f), Tooltip("Seconds it stays visible on each perch, eyeing the victim.")]
    public float perchVisibleMin = 3f;
    [Min(0f)] public float perchVisibleMax = 6f;
    [Min(0f), Tooltip("Seconds hidden between perches (includes the fade out).")]
    public float perchGapMin = 2f;
    [Min(0f)] public float perchGapMax = 5f;
    [Min(0f), Tooltip("Fade in/out seconds on a perch.")]
    public float perchFadeSeconds = 0.6f;
    [Min(0f), Tooltip("Hidden pause after moving to a perch, so clients receive the new position before it appears.")]
    public float revealDelay = 0.5f;
    [Min(1f), Tooltip("Rooftops are searched within this radius of the victim.")]
    public float perchSearchRadius = 30f;
    [Min(0f), Tooltip("Closest horizontal distance of a perch to the victim.")]
    public float perchMinDistance = 8f;
    [Min(0.5f), Tooltip("Colliders at least this tall (and wide, see below) count as buildings to perch on.")]
    public float perchMinHeight = 2.5f;
    [Min(0.1f), Tooltip("Minimum horizontal size of a perch collider on both axes (rules out walls and fences).")]
    public float perchMinWidth = 1.5f;
    [Min(0f), Tooltip("How far inside the roof edge (toward the victim) it crouches.")]
    public float perchEdgeInset = 0.6f;
    [Min(0f), Tooltip("Inset from the block corner used when no rooftop is found.")]
    public float cornerInset = 1.5f;

    [Header("Attacking")]
    [Min(0f), Tooltip("Attack spawns are on the NavMesh this far from the victim, behind them or out of their sight.")]
    public float attackSpawnMinDistance = 6f;
    [Min(0f)] public float attackSpawnMaxDistance = 12f;
    [Min(0f), Tooltip("Seconds its opacity takes to rise when it appears for the attack.")]
    public float attackFadeInSeconds = 2f;
    [Min(0f)] public float rushSpeed = 7f;
    [Min(0.05f)] public float repathInterval = 0.2f;
    [Min(0.1f), Tooltip("Horizontal reach of an attack (height difference up to 2.5 m).")]
    public float attackRange = 1.4f;
    [Min(0), Tooltip("Damage per attack (2 = one heart).")]
    public int contactDamage = 2;
    [Min(0f), Tooltip("Seconds between attacks.")]
    public float attackIntervalSeconds = 3f;
    [Min(1f), Tooltip("Seconds the attack lasts before it fades away and picks a new victim.")]
    public float attackDurationSeconds = 60f;
    [Min(0f)] public float attackFadeOutSeconds = 1.5f;

    [Header("Flashlight (stops it and fades it away during the attack)")]
    [Range(1f, 90f), Tooltip("Half angle around the victim's view (camera yaw + flashlight pitch) that counts as looking at it.")]
    public float litHalfAngle = 25f;
    [Min(0f), Tooltip("Furthest distance at which the victim's flashlight affects it.")]
    public float litRange = 25f;
    [Min(0.1f), Tooltip("Seconds held in the victim's flashlight before it has faded away completely and teleports.")]
    public float litFadeSeconds = 2.5f;
    [Min(0.1f), Tooltip("Seconds to fully recover when the light leaves it before it vanished (it rushes again meanwhile).")]
    public float litRecoverSeconds = 1.5f;
    [Range(0f, 1f), Tooltip("Lowest flicker opacity while fully lit (the flicker grows harsher as it fades).")]
    public float litFlickerMinAlpha = 0.05f;
    [Min(0f), Tooltip("Flicker drop-outs per second while fully lit.")]
    public float litDropoutsPerSecond = 4f;
    [Min(1f), Tooltip("After vanishing it reappears at a random spot this far from the victim (their block or an openly reachable neighbouring block, out of their sight when possible).")]
    public float relocateMinDistance = 12f;
    [Min(1f)] public float relocateMaxDistance = 30f;
    [Range(0f, 180f), Tooltip("Spots inside this half angle directly behind the victim are never used.")]
    public float relocateRearHalfAngle = 50f;
    [Min(0f), Tooltip("The new spot is at least this far from where it vanished.")]
    public float relocateMinShift = 6f;
    [Min(0f), Tooltip("Fade-in after teleporting; it already rushes meanwhile.")]
    public float relocateFadeInSeconds = 0.8f;

    [Header("Flicker (ethereal opacity, local on every peer)")]
    [Range(0f, 1f)] public float flickerMinAlpha = 0.35f;
    [Min(0f)] public float flickerSpeed = 2.5f;
    [Min(0f)] public float flickerDropoutsPerSecond = 0.3f;
    [Min(0f)] public float flickerDropoutSeconds = 0.08f;

    [Header("Blood moon (fake clone rushes while attacking)")]
    [Tooltip("Random time (min, max) in seconds between harmless clone rushes on the victim during the attack.")]
    public Vector2 bloodMoonCloneIntervalSeconds = new Vector2(6f, 12f);
    [Min(2f), Tooltip("How far ahead of where the victim looks the clone appears (shortened in front of walls).")]
    public float cloneSpawnDistance = 14f;
    [Min(0.5f)] public float cloneRushSpeed = 9f;

    [Header("Misc")]
    [Min(0f)] public float defaultStunSeconds = 3f;
    [Min(0.5f), Tooltip("Half size of the area used when it spawns without a street map.")]
    public Vector2 fallbackHomeHalfSize = new Vector2(12f, 12f);
}
