using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Main finite-state machine of an enemy. Runs only on the server: it ticks the current <see cref="EnemyState"/>, then
/// publishes motion to <see cref="EnemyPresentation"/>. The displayed direction always follows actual movement
/// (e.g. sidestepping around an obstacle) while the enemy's eyes may stay locked on a target.
/// </summary>
/// <remarks>Ground enemies need an <see cref="EnemyMotor"/>; flying ones an <see cref="EnemyFlightMotor"/>.</remarks>
[RequireComponent(typeof(EnemySenses), typeof(EnemyPresentation))]
public abstract class EnemyBrain : NetworkBehaviour
{
    private static readonly float[] bodyHeights = { 0.3f, 1.4f, 2.6f };

    [SyncVar] private string stateName = string.Empty;

    private bool hasHome;
    private EnemyHomeArea home;

    /// <summary>Whatever moves this enemy (NavMesh walker or flyer).</summary>
    public EnemyLocomotion Locomotion { get; private set; }
    /// <summary>The NavMesh motor; null for enemies that do not walk on the NavMesh.</summary>
    public EnemyMotor Motor { get; private set; }
    public EnemySenses Senses { get; private set; }
    public EnemyPresentation Presentation { get; private set; }
    public EnemyState CurrentState { get; private set; }
    /// <summary>Name of the current state, synchronized to clients for debugging/UI.</summary>
    public string StateName => stateName;
    public EnemyHomeArea Home => home;
    public int HomeBlockIndex { get; private set; } = -1;
    /// <summary>When set, the body keeps facing this point (vision lock) instead of turning with movement.</summary>
    public Vector3? LookTarget { get; set; }

    protected virtual void Awake()
    {
        Motor = GetComponent<EnemyMotor>();
        Locomotion = Motor != null ? Motor : GetComponent<EnemyLocomotion>();
        if (Locomotion == null)
            Debug.LogError($"'{name}' needs an EnemyMotor or EnemyFlightMotor.", this);
        Senses = GetComponent<EnemySenses>();
        Presentation = GetComponent<EnemyPresentation>();
    }

    protected abstract EnemyState CreateInitialState();

    /// <summary>Half size of the guarded box used when the enemy spawns without a street map.</summary>
    protected virtual Vector2 FallbackHomeHalfSize => new Vector2(10f, 10f);

    /// <summary>Enemies that roam the whole map (e.g. flyers) instead of guarding a home block.</summary>
    public virtual bool RoamsWholeMap => false;

    /// <summary>Server: makes a placed street block this enemy's home. Call before <c>NetworkServer.Spawn</c>.</summary>
    public void AssignHomeBlock(MapManager map, int blockIndex)
    {
        home = EnemyHomeArea.FromBlock(map.GetBlock(blockIndex));
        HomeBlockIndex = blockIndex;
        hasHome = true;
    }

    public void AssignHomeArea(Vector3 center, Vector2 halfSize)
    {
        home = EnemyHomeArea.FromBox(center, halfSize);
        HomeBlockIndex = -1;
        hasHome = true;
    }

    [Server]
    public void ChangeState(EnemyState next)
    {
        CurrentState?.Exit();
        CurrentState = next;
        stateName = next != null ? next.Name : string.Empty;
        next?.Enter();
    }

    protected virtual void Update()
    {
        if (!isServer || Locomotion == null || !Locomotion.Activate())
            return;

        if (CurrentState == null)
        {
            ResolveHome();
            ChangeState(CreateInitialState());
        }
        CurrentState?.Tick(Time.deltaTime);
        PublishMotion();
    }

    private void ResolveHome()
    {
        if (hasHome)
            return;
        MapManager map = MapManager.Active;
        if (map != null && map.IsBuilt && map.TryGetBlockIndex(transform.position, out int index))
            AssignHomeBlock(map, index);
        else
            AssignHomeArea(transform.position, FallbackHomeHalfSize);
    }

    private void PublishMotion()
    {
        bool moving = Locomotion.IsMoving;
        Vector3 velocity = Locomotion.Velocity;
        velocity.y = 0f;
        if (LookTarget.HasValue)
            Locomotion.FaceTowards(LookTarget.Value);
        else if (moving)
            Locomotion.FaceDirection(velocity);

        float yaw = moving && velocity.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(velocity, Vector3.up).eulerAngles.y
            : transform.eulerAngles.y;
        Presentation.SetMotion(moving, yaw);
    }

    /// <summary>Random reachable NavMesh point inside the home area.</summary>
    public bool TryGetHomePoint(out Vector3 point)
    {
        if (Motor != null)
            return Motor.TryRandomPoint(home, 30, true, out point);
        point = home.RandomPoint();
        return home.IsValid;
    }

    /// <summary>
    /// Warps to a NavMesh point in the first of <paramref name="areas"/> that has one hidden from every player (behind
    /// cover or beyond their camera's far clip), at least <paramref name="minPlayerDistance"/> from them and, if given,
    /// with a complete path to <paramref name="pathTarget"/>.
    /// </summary>
    [Server]
    public bool TryTeleportHidden(IEnumerable<EnemyHomeArea> areas, float minPlayerDistance, Vector3? pathTarget, int attemptsPerArea = 24)
    {
        if (Motor == null)
            return false;
        foreach (EnemyHomeArea area in areas)
        {
            for (int i = 0; i < attemptsPerArea; i++)
            {
                if (!NavMesh.SamplePosition(area.RandomPoint(), out NavMeshHit hit, 2f, Motor.AreaMask) || !area.Contains(hit.position))
                    continue;
                Vector3 point = hit.position;
                if (!IsHiddenFromPlayers(point, minPlayerDistance))
                    continue;
                if (pathTarget.HasValue && !Motor.HasCompletePath(point, pathTarget.Value))
                    continue;

                Motor.Warp(point);
                Presentation.SnapNow();
                return true;
            }
        }
        return false;
    }

    public bool IsHiddenFromPlayers(Vector3 point, float minPlayerDistance)
    {
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if ((player.transform.position - point).sqrMagnitude < minPlayerDistance * minPlayerDistance)
                return false;

            Camera eye = player.GetComponentInChildren<Camera>(true);
            Vector3 eyePosition = eye != null ? eye.transform.position : player.transform.position + Vector3.up * 1.6f;
            float viewDistance = eye != null ? eye.farClipPlane : float.PositiveInfinity;
            foreach (float height in bodyHeights)
            {
                Vector3 body = point + Vector3.up * height;
                if ((body - eyePosition).sqrMagnitude <= viewDistance * viewDistance &&
                    EnemySenses.IsSegmentClear(eyePosition, body, Senses.OcclusionMask, player.transform, transform))
                    return false;
            }
        }
        return true;
    }
}
