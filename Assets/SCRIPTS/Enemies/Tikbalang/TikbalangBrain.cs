using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>
/// Tikbalang FSM brain. States: Idle (guard home block), Searching (investigate a heard noise), Chase (locked on the
/// first spotted player for a limited time), Returning (walk home ignoring players) and Stunned.
/// </summary>
[RequireComponent(typeof(EnemyMotor))]
public sealed class TikbalangBrain : EnemyBrain
{
    [SerializeField] private TikbalangProfile profile;

    [SyncVar] private NetworkIdentity chaseTarget;

    public TikbalangProfile Profile => profile;
    /// <summary>The player being chased (synced so clients can react, e.g. with fear effects later).</summary>
    public NetworkIdentity ChaseTarget => chaseTarget;
    public bool IsChasing => chaseTarget != null;

    public TikbalangIdleState Idle { get; private set; }
    public TikbalangSearchingState Searching { get; private set; }
    public TikbalangChaseState Chase { get; private set; }
    public TikbalangReturningState Returning { get; private set; }
    public TikbalangStunnedState Stunned { get; private set; }

    protected override Vector2 FallbackHomeHalfSize => profile.fallbackHomeHalfSize;

    protected override void Awake()
    {
        base.Awake();
        if (profile == null)
        {
            Debug.LogWarning($"'{name}' has no Tikbalang Profile; using default values.", this);
            profile = ScriptableObject.CreateInstance<TikbalangProfile>();
        }
        Idle = new TikbalangIdleState(this);
        Searching = new TikbalangSearchingState(this);
        Chase = new TikbalangChaseState(this);
        Returning = new TikbalangReturningState(this);
        Stunned = new TikbalangStunnedState(this);
        ApplyNormalVision();
    }

    protected override EnemyState CreateInitialState() => Idle;

    public void ApplyNormalVision()
    {
        Senses.EyeHeight = profile.eyeHeight;
        Senses.VisionRange = profile.visionRange;
        Senses.VisionAngle = profile.visionAngle;
        Senses.HearingRadius = profile.hearingRadius;
    }

    public void ApplySearchingVision()
    {
        ApplyNormalVision();
        Senses.VisionAngle = profile.searchingVisionAngle;
    }

    /// <summary>A visible player standing inside the home block (the guard rule for starting a chase).</summary>
    public bool TrySpotPlayerInHome(out PlayerPersistence player)
    {
        player = Senses.FindVisiblePlayer(candidate => Home.Contains(candidate.transform.position));
        return player != null;
    }

    public bool TrySpotAnyPlayer(out PlayerPersistence player)
    {
        player = Senses.FindVisiblePlayer();
        return player != null;
    }

    /// <summary>A noise whose origin lies inside the home block; noises elsewhere are ignored.</summary>
    public bool TryHearInHome(out Vector3 origin) => Senses.TryHear(point => Home.Contains(point), out origin);

    [Server]
    public void StartSearch(Vector3 origin)
    {
        Searching.SetOrigin(origin);
        if (CurrentState != Searching)
            ChangeState(Searching);
    }

    [Server]
    public void StartChase(PlayerPersistence player)
    {
        chaseTarget = player.netIdentity;
        ChangeState(Chase);
    }

    /// <summary>Server: stops the chase and drops the target, then walks home after <paramref name="seconds"/>.</summary>
    [Server]
    public void Stun(float seconds = -1f)
    {
        Stunned.Duration = seconds >= 0f ? seconds : profile.defaultStunSeconds;
        ChangeState(Stunned);
    }

    [Server]
    public void EndChase()
    {
        ChangeState(Returning);
    }

    internal void ClearChaseTarget() => chaseTarget = null;

    internal PlayerPersistence ChasedPlayer =>
        chaseTarget != null && chaseTarget.TryGetComponent(out PlayerPersistence player) && player.isActiveAndEnabled ? player : null;

    /// <summary>
    /// Teleports to a hidden spot in the block next to the target's block that is closest to the target (the target's
    /// own block is the last resort).
    /// </summary>
    [Server]
    public bool TryTeleportNear(PlayerPersistence target)
    {
        MapManager map = MapManager.Active;
        if (map == null || !map.IsBuilt || !map.TryGetBlockIndex(target.transform.position, out int targetBlock))
            return false;

        Vector3 targetPosition = target.transform.position;
        // Only blocks the target can walk to without passing a closed barricade, so the enemy is never sealed in.
        bool[] open = map.FindOpenlyReachable(targetBlock);
        IEnumerable<EnemyHomeArea> areas = map.GetNeighbours(targetBlock)
            .Where(index => open[index])
            .OrderBy(index => (map.GetBlock(index).transform.position - targetPosition).sqrMagnitude)
            .Select(index => EnemyHomeArea.FromBlock(map.GetBlock(index)))
            .Append(EnemyHomeArea.FromBlock(map.GetBlock(targetBlock)));
        if (!TryTeleportHidden(areas, profile.teleportMinPlayerDistance, targetPosition))
            return false;
        Motor.FaceTowards(targetPosition);
        return true;
    }
}
