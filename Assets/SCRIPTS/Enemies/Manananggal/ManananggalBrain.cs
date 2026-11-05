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
    /// A straight pass over the street blocks at hunting altitude, from outside one edge of the map to outside the
    /// opposite edge. Lines along a row/column that crosses many blocks are preferred, so it flies down the streets.
    /// Without a map it crosses the fallback home area instead.
    /// </summary>
    public bool TryPlanPass(out Vector3 start, out Vector3 end)
    {
        MapManager map = MapManager.Active;
        if (map != null && map.IsBuilt && TryGetMapBounds(map, out Bounds bounds))
        {
            var candidates = new List<(Vector3 anchor, bool alongX, int crossed)>();
            int best = 0;
            for (int i = 0; i < map.BlockCount; i++)
            {
                Vector3 anchor = map.GetBlock(i).transform.position;
                foreach (bool alongX in new[] { true, false })
                {
                    int crossed = CountCrossedBlocks(map, bounds, anchor, alongX);
                    candidates.Add((anchor, alongX, crossed));
                    best = Mathf.Max(best, crossed);
                }
            }
            candidates.RemoveAll(candidate => candidate.crossed < Mathf.Max(1, Mathf.CeilToInt(best * 0.6f)));
            if (candidates.Count > 0)
            {
                var pick = candidates[Random.Range(0, candidates.Count)];
                float altitude = bounds.min.y + profile.huntAltitude;
                Vector3 axis = pick.alongX ? Vector3.right : Vector3.forward;
                float min = (pick.alongX ? bounds.min.x : bounds.min.z) - profile.edgeMargin;
                float max = (pick.alongX ? bounds.max.x : bounds.max.z) + profile.edgeMargin;
                Vector3 across = new Vector3(pick.anchor.x, altitude, pick.anchor.z);
                Vector3 a = Vector3.Scale(across, Vector3.one - axis) + axis * min;
                Vector3 b = Vector3.Scale(across, Vector3.one - axis) + axis * max;
                bool reverse = Random.value < 0.5f;
                start = reverse ? b : a;
                end = reverse ? a : b;
                return true;
            }
        }

        if (!Home.IsValid)
        {
            start = end = default;
            return false;
        }
        Vector3 center = Home.Center + Vector3.up * profile.huntAltitude;
        Vector3 direction = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f) * Vector3.forward;
        start = center - direction * profile.fallbackPassHalfLength;
        end = center + direction * profile.fallbackPassHalfLength;
        return true;
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

    private static int CountCrossedBlocks(MapManager map, Bounds bounds, Vector3 anchor, bool alongX)
    {
        var crossed = new HashSet<int>();
        float min = alongX ? bounds.min.x : bounds.min.z;
        float max = alongX ? bounds.max.x : bounds.max.z;
        for (float t = min; t <= max; t += 1.5f)
        {
            Vector3 point = alongX ? new Vector3(t, anchor.y, anchor.z) : new Vector3(anchor.x, anchor.y, t);
            if (map.TryGetBlockIndex(point, out int index))
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
