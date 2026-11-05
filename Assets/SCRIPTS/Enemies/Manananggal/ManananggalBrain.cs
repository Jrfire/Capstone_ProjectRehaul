using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Manananggal FSM brain: a flyer that roams the whole map instead of guarding a block. States: Hunting (invisible
/// waits between straight high passes over the street blocks, looking down for players), Chase (flies over buildings
/// at the first player it spots for a limited time) and Leaving (flies out of the map, then hunts again).
/// </summary>
[RequireComponent(typeof(EnemyFlightMotor))]
public sealed class ManananggalBrain : EnemyBrain
{
    [SerializeField] private ManananggalProfile profile;

    [SyncVar] private NetworkIdentity chaseTarget;
    // Despawned between passes: the networked object stays alive but is invisible and intangible on every peer.
    [SyncVar] private bool hidden = true;

    private readonly List<Collider> bodyColliders = new List<Collider>();
    private readonly List<GameObject> visuals = new List<GameObject>();
    private EnemyFearSource fearSource;
    private bool fearSourceWasEnabled;
    private bool appliedHidden;
    private bool hasApplied;

    public ManananggalProfile Profile => profile;
    public EnemyFlightMotor Flight { get; private set; }
    /// <summary>The player being chased (synced so clients can react later).</summary>
    public NetworkIdentity ChaseTarget => chaseTarget;
    public bool IsChasing => chaseTarget != null;
    /// <summary>True while despawned between passes (invisible, no collider, no fear effect).</summary>
    public bool IsHidden => hidden;
    public override bool RoamsWholeMap => true;

    public ManananggalHuntingState Hunting { get; private set; }
    public ManananggalChaseState Chase { get; private set; }
    public ManananggalLeavingState Leaving { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (profile == null)
        {
            Debug.LogWarning($"'{name}' has no Manananggal Profile; using default values.", this);
            profile = ScriptableObject.CreateInstance<ManananggalProfile>();
        }
        Flight = GetComponent<EnemyFlightMotor>();
        foreach (Collider body in GetComponents<Collider>())
            bodyColliders.Add(body);
        if (Presentation.WorldSprites != null)
            visuals.Add(Presentation.WorldSprites.gameObject);
        foreach (BlobShadow shadow in GetComponentsInChildren<BlobShadow>(true))
            visuals.Add(shadow.gameObject);
        fearSource = GetComponent<EnemyFearSource>();
        fearSourceWasEnabled = fearSource != null && fearSource.enabled;

        Hunting = new ManananggalHuntingState(this);
        Chase = new ManananggalChaseState(this);
        Leaving = new ManananggalLeavingState(this);
        ApplyVision();
        ApplyHidden();
    }

    protected override EnemyState CreateInitialState() => Hunting;

    public void ApplyVision()
    {
        Senses.EyeHeight = profile.eyeHeight;
        Senses.VisionRange = profile.visionRange;
        Senses.VisionAngle = profile.visionAngle;
        Senses.VisionPitch = profile.visionPitch;
        Senses.HearingRadius = 0f;
    }

    private void LateUpdate()
    {
        if (!hasApplied || appliedHidden != hidden)
            ApplyHidden();
    }

    /// <summary>Server: despawns (hides) or reappears on every peer.</summary>
    [Server]
    public void SetHidden(bool value)
    {
        hidden = value;
        ApplyHidden();
    }

    private void ApplyHidden()
    {
        hasApplied = true;
        appliedHidden = hidden;
        foreach (GameObject visual in visuals)
        {
            if (visual != null)
                visual.SetActive(!hidden);
        }
        foreach (Collider body in bodyColliders)
        {
            if (body != null)
                body.enabled = !hidden;
        }
        if (fearSource != null)
            fearSource.enabled = fearSourceWasEnabled && !hidden;
        if (!hidden)
            Presentation.SnapNow();
    }

    public bool TrySpotPlayer(out PlayerPersistence player)
    {
        player = hidden ? null : Senses.FindVisiblePlayer();
        return player != null;
    }

    [Server]
    public void StartChase(PlayerPersistence player)
    {
        chaseTarget = player.netIdentity;
        ChangeState(Chase);
    }

    [Server]
    public void EndChase()
    {
        ChangeState(Leaving);
    }

    internal void ClearChaseTarget() => chaseTarget = null;

    internal PlayerPersistence ChasedPlayer =>
        chaseTarget != null && chaseTarget.TryGetComponent(out PlayerPersistence player) && player.isActiveAndEnabled ? player : null;

    /// <summary>
    /// A straight pass at hunting altitude from outside one side of the map to outside the opposite side. Each pass
    /// picks a random direction (see <see cref="ManananggalProfile.passDirectionStep"/>) and a random parallel lane
    /// anywhere across the map, including along its edges, as long as it flies over enough street blocks.
    /// Without a map it crosses the fallback home area instead.
    /// </summary>
    public bool TryPlanPass(out Vector3 start, out Vector3 end)
    {
        MapManager map = MapManager.Active;
        if (map != null && map.IsBuilt && TryGetMapBounds(map, out Bounds bounds))
        {
            float altitude = bounds.min.y + profile.huntAltitude;
            int required = Mathf.Min(Mathf.Max(1, profile.minimumBlocksCrossed), Mathf.Max(1, map.BlockCount));
            for (int attempt = 0; attempt < 32; attempt++)
            {
                PlanLane(bounds, RandomPassYaw(), Random.value, altitude, profile.edgeMargin, out start, out end);
                if (CountCrossedBlocks(map, start, end) >= required)
                    return true;
            }

            // Rare fallback: a random direction through a random block centre always crosses at least that block.
            float yaw = RandomPassYaw();
            Vector3 across = Vector3.Cross(Vector3.up, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            Vector3 offset = map.GetBlock(Random.Range(0, map.BlockCount)).transform.position - bounds.center;
            float extent = Mathf.Abs(across.x) * bounds.extents.x + Mathf.Abs(across.z) * bounds.extents.z;
            float lane = extent > 0.0001f ? Mathf.InverseLerp(-extent, extent, Vector3.Dot(offset, across)) : 0.5f;
            PlanLane(bounds, yaw, lane, altitude, profile.edgeMargin, out start, out end);
            return true;
        }

        if (!Home.IsValid)
        {
            start = end = default;
            return false;
        }
        Vector3 direction = Quaternion.Euler(0f, RandomPassYaw(), 0f) * Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Vector2 half = Home.HalfSize;
        Vector3 center = Home.Center + Vector3.up * profile.huntAltitude
            + side * Random.Range(-Mathf.Min(half.x, half.y), Mathf.Min(half.x, half.y));
        start = center - direction * profile.fallbackPassHalfLength;
        end = center + direction * profile.fallbackPassHalfLength;
        return true;
    }

    private float RandomPassYaw()
    {
        float yaw = Random.Range(0f, 360f);
        float step = profile.passDirectionStep;
        return step > 0.01f ? Mathf.Round(yaw / step) * step : yaw;
    }

    /// <summary>
    /// Straight line heading <paramref name="yaw"/> across <paramref name="bounds"/> (XZ). <paramref name="lane"/> 0..1
    /// picks the parallel lane from one side of the map to the other; both ends lie <paramref name="margin"/> outside it.
    /// </summary>
    public static void PlanLane(Bounds bounds, float yaw, float lane, float altitude, float margin, out Vector3 start, out Vector3 end)
    {
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 across = Vector3.Cross(Vector3.up, direction);
        Vector3 extents = bounds.extents;
        float along = Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.z) * extents.z;
        float wide = Mathf.Abs(across.x) * extents.x + Mathf.Abs(across.z) * extents.z;
        Vector3 center = new Vector3(bounds.center.x, altitude, bounds.center.z) + across * Mathf.Lerp(-wide, wide, Mathf.Clamp01(lane));
        start = center - direction * (along + margin);
        end = center + direction * (along + margin);
    }

    /// <summary>Hunting-altitude point outside the map (or fallback area) straight along <paramref name="direction"/>.</summary>
    public Vector3 GetExitPoint(Vector3 from, Vector3 direction)
    {
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        MapManager map = MapManager.Active;
        float altitude;
        float distance;
        if (map != null && map.IsBuilt && TryGetMapBounds(map, out Bounds bounds))
        {
            altitude = bounds.min.y + profile.huntAltitude;
            float tx = ExitDistance(from.x, direction.x, bounds.min.x, bounds.max.x);
            float tz = ExitDistance(from.z, direction.z, bounds.min.z, bounds.max.z);
            distance = Mathf.Max(0f, Mathf.Min(tx, tz)) + profile.edgeMargin;
        }
        else
        {
            altitude = Home.Center.y + profile.huntAltitude;
            Vector3 offset = from - Home.Center;
            offset.y = 0f;
            distance = Mathf.Max(0f, profile.fallbackPassHalfLength - Vector3.Dot(offset, direction)) + profile.edgeMargin;
        }
        Vector3 exit = from + direction * distance;
        exit.y = altitude;
        return exit;
    }

    public static bool TryGetMapBounds(MapManager map, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        for (int i = 0; i < map.BlockCount; i++)
        {
            StreetBlock block = map.GetBlock(i);
            if (block == null)
                continue;
            Vector2 half = block.Size * 0.5f;
            foreach (Vector3 corner in new[] { new Vector3(-half.x, 0f, -half.y), new Vector3(half.x, 0f, -half.y),
                         new Vector3(-half.x, 0f, half.y), new Vector3(half.x, 0f, half.y) })
            {
                Vector3 world = block.transform.TransformPoint(corner);
                if (!any)
                    bounds = new Bounds(world, Vector3.zero);
                else
                    bounds.Encapsulate(world);
                any = true;
            }
        }
        return any;
    }

    private static int CountCrossedBlocks(MapManager map, Vector3 start, Vector3 end)
    {
        var crossed = new HashSet<int>();
        float length = Vector3.Distance(start, end);
        for (float t = 0f; t <= length; t += 1.5f)
        {
            if (map.TryGetBlockIndex(Vector3.Lerp(start, end, t / length), out int index))
                crossed.Add(index);
        }
        return crossed.Count;
    }

    private static float ExitDistance(float from, float direction, float min, float max)
    {
        if (Mathf.Abs(direction) < 0.0001f)
            return float.PositiveInfinity;
        return ((direction > 0f ? max : min) - from) / direction;
    }
}
