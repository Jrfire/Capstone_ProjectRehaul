using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>Two placed blocks joined through one entrance each (indices into <see cref="MapManager.Layout"/>).</summary>
public readonly struct MapJoint
{
    public MapJoint(int blockA, int entranceA, int blockB, int entranceB, bool barricaded)
    {
        BlockA = blockA;
        EntranceA = entranceA;
        BlockB = blockB;
        EntranceB = entranceB;
        Barricaded = barricaded;
    }

    public int BlockA { get; }
    public int EntranceA { get; }
    public int BlockB { get; }
    public int EntranceB { get; }
    /// <summary>An entrance obstacle was kept in this joint.</summary>
    public bool Barricaded { get; }
}

/// <summary>
/// Scene-local street builder. Picks blocks from its pools with a seed and connects their entrances so none stay open.
/// In a networked session every peer builds the same map from the seed shared by the travel system.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapManager : MonoBehaviour
{
    private const string GeneratedRootName = "Generated Map";

    [Header("Block pools")]
    [SerializeField, Tooltip("Blocks with 2+ entrances that make up the street.")]
    private List<StreetBlock> connectorBlocks = new List<StreetBlock>();
    [SerializeField, Tooltip("Single-entrance blocks that close every remaining open entrance.")]
    private List<StreetBlock> endCapBlocks = new List<StreetBlock>();
    [SerializeField, Tooltip("Single-entrance blocks containing the player spawn points; one is placed at the street's edge.")]
    private List<StreetBlock> spawnBlocks = new List<StreetBlock>();

    [Header("Generation")]
    [SerializeField, Min(0), Tooltip("Number of connector blocks. End caps and the spawn block are added on top of this.")]
    private int connectorBlockCount = 6;
    [FormerlySerializedAs("maxConnectorBlockCount")]
    [SerializeField, Min(0), Tooltip("Most blocks in total (connectors, end caps and the spawn block) the map grows to when the host adds blocks per quota met (Extra Connectors). Connector Block Count itself is never reduced, so a base layout above this is kept.")]
    private int maxBlockCount = 15;
    [SerializeField, Min(1), Tooltip("Layouts retried (deterministically from the seed) before generation fails.")]
    private int maxAttempts = 100;
    [SerializeField, Tooltip("Build automatically when the scene starts.")]
    private bool buildOnStart = true;
    [SerializeField, Tooltip("Offline only: use Fixed Seed instead of a random one. Networked sessions always use the host's seed.")]
    private bool useFixedSeed;
    [SerializeField] private int fixedSeed;

    [SerializeField, Range(0f, 1f), Tooltip("Chance, each time a connector is added, to place one that joins two or more open entrances and so closes a loop. Loops give players routes around barricades.")]
    private float loopChance = 0.75f;

    [Header("Town layout")]
    [SerializeField, Tooltip("Town connector blocks (2+ entrances, generated with Town Block ticked). In a town layout they replace Connector Blocks and pack into a compact cluster; their edge walls are removed wherever two town blocks touch, while the map's outskirts and end caps stay walled.")]
    private List<StreetBlock> townBlocks = new List<StreetBlock>();
    [SerializeField, Range(0f, 1f), Tooltip("Chance (rolled from the seed, so every peer agrees) that a map with at least Town Min Connector Count connectors is built as a town. Falls back to a normal layout if the town cannot be planned.")]
    private float townLayoutChance = 0.5f;
    [SerializeField, Min(0), Tooltip("Town layouts are only rolled when the effective connector count (Connector Block Count plus per-quota extras, capped by Max Block Count) reaches this.")]
    private int townMinConnectorCount = 10;

    [Header("Barricades")]
    [SerializeField, Tooltip("Keep entrance obstacles that seal off end cap blocks (the only places that may be fully blocked, e.g. for loot behind them).")]
    private bool barricadeEndCaps = true;
    [SerializeField, Tooltip("Keep other entrance obstacles only where the street still has a route around them; turn off to remove every non-end-cap obstacle.")]
    private bool barricadeLoops = true;
    [SerializeField, Range(0f, 1f), Tooltip("Chance that an end cap joint without a baked obstacle gets one placed by the map. Uses the end cap generator's Obstacle Prefabs, else the other block's, else Fallback Obstacle Prefabs.")]
    private float endCapObstacleChance = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Chance that a connector-to-connector joint without a baked obstacle gets one placed by the map; only where a route around it remains. The spawn block's entrance never gets one.")]
    private float loopObstacleChance = 0.35f;
    [SerializeField, Tooltip("Obstacle prefabs (with EntranceObstacle, front = +Z into the block) used when neither block's generator lists any.")]
    private List<GameObject> fallbackObstaclePrefabs = new List<GameObject>();

    [Header("Player spawns")]
    [SerializeField, Tooltip("Give the placed spawn block generated player spawn points on open, reachable floor, unless it already has a PlayerSpawnPoints with points.")]
    private bool generateSpawnPoints = true;
    [SerializeField, Range(1, 6), Tooltip("Spawn points generated in the spawn block (host plus five guests = 6).")]
    private int spawnPointCount = 6;
    [SerializeField, Min(0.5f), Tooltip("Minimum distance between generated spawn points.")]
    private float spawnPointSpacing = 1.5f;
    [SerializeField, Min(0.1f), Tooltip("Radius of the player-sized capsule that must be free of colliders at each spawn point.")]
    private float spawnClearanceRadius = 0.45f;
    [SerializeField, Min(0.5f), Tooltip("Height of the player-sized capsule that must be free of colliders at each spawn point.")]
    private float spawnClearanceHeight = 2f;
    [SerializeField, Tooltip("Vehicle (e.g. the jeepney with its TravelInteractable back, back = local -Z) parked in the placed spawn block on free, reachable floor, away from the entrance with its back towards the block centre, so players can head home. Empty = none.")]
    private GameObject jeepneyPrefab;
    [SerializeField, Min(0.5f), Tooltip("Open, reachable floor kept behind the jeepney's back so players can board it.")]
    private float jeepneyRearClearance = 2f;

    [Header("Filler blocks")]
    [SerializeField, Tooltip("Blocks with no entrances placed in every empty grid cell around the street (same cell size) so the map does not float in a void. Fillers may overhang the outer margin; cells no filler fits are plugged (see Plug Gaps).")]
    private List<StreetBlock> fillerBlocks = new List<StreetBlock>();
    [SerializeField, Min(0), Tooltip("Grid cells of filler added beyond the street's bounding box on every side.")]
    private int fillerMarginCells = 2;
    [SerializeField, Tooltip("Cells no filler block fits (e.g. one- or two-cell gaps between street blocks) get the smallest filler's floor tiles plus one Gap Fill Prefab, so nobody sees under the map.")]
    private bool plugGaps = true;
    [SerializeField, Tooltip("Prefabs (e.g. small buildings) placed centred in plugged cells with a random 90-degree turn; ones larger than a cell are skipped. Empty = the smallest filler's generator Filler/Middle/Building Prefabs.")]
    private List<GameObject> gapFillPrefabs = new List<GameObject>();

    [Header("Navigation")]
    [SerializeField, Tooltip("Bake a NavMesh on every block's floor after building (play mode only).")]
    private bool buildNavMesh = true;
    [SerializeField, Tooltip("NavMesh agent type (Navigation window > Agents) the street is baked for. 0 = Humanoid.")]
    private int navMeshAgentTypeId;
    [SerializeField, Tooltip("Give every solid non-floor collider (buildings, walls, decorations, entrance obstacles) a carving NavMeshObstacle.")]
    private bool carveObstacles = true;
    [SerializeField, Min(0f), Tooltip("Agent radius the street NavMesh is baked with. Carving expands every obstacle by this on each side and walls keep this clearance. 0 = the agent type's own radius (Humanoid 0.5).")]
    private float bakeAgentRadius = 0.3f;
    [SerializeField, Range(0.05f, 1f), Tooltip("Horizontal scale of decoration carving boxes (their colliders often include canopies/overhangs). Use NavMeshCarveOverride on a prefab for an exact footprint or no carving.")]
    private float decorationCarveScale = 0.6f;

    private static readonly Dictionary<string, int> networkSeeds = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> networkExtraConnectors = new Dictionary<string, int>();
    private Transform generatedRoot;
    private readonly List<StreetBlock> placedBlocks = new List<StreetBlock>();
    private readonly List<StreetBlock> placedFillers = new List<StreetBlock>();
    private readonly List<Vector3[]> pluggedCells = new List<Vector3[]>();
    private readonly List<List<int>> neighbours = new List<List<int>>();
    private readonly List<MapJoint> joints = new List<MapJoint>();
    private readonly List<EntranceObstacle> jointBarricades = new List<EntranceObstacle>();
    private readonly List<Vector3[]> openSeams = new List<Vector3[]>();
    private List<Vector3>[] reachableFloor;
    private NavMeshDataInstance navMeshInstance;

    public static MapManager Active { get; private set; }

    /// <summary>When true (set by the networking layer), managers wait for <see cref="SetNetworkSeed"/> instead of rolling a seed.</summary>
    public static bool WaitForNetworkSeed { get; set; }

    public event Action<MapManager> Built;

    public IReadOnlyList<StreetBlock> ConnectorBlocks => connectorBlocks;
    public IReadOnlyList<StreetBlock> EndCapBlocks => endCapBlocks;
    public IReadOnlyList<StreetBlock> SpawnBlocks => spawnBlocks;
    public int ConnectorBlockCount => connectorBlockCount;
    public int MaxBlockCount => maxBlockCount;
    /// <summary>Connector blocks added on top of Connector Block Count (set by the host for every peer, e.g. 2 per quota met).</summary>
    public int ExtraConnectors { get; set; }
    /// <summary>Upper bound for connectors tried: Connector Block Count plus Extra Connectors, capped at Max Block Count. The built layout may use fewer so the total (with end caps and spawn) stays within Max Block Count.</summary>
    public int EffectiveConnectorBlockCount => EffectiveConnectorCount(connectorBlockCount, ExtraConnectors, maxBlockCount);
    /// <summary>Connector blocks in the last built layout.</summary>
    public int PlacedConnectorCount { get; private set; }

    public static int EffectiveConnectorCount(int baseCount, int extra, int max) =>
        Mathf.Max(baseCount, Mathf.Min(baseCount + Mathf.Max(0, extra), max));

    /// <summary>
    /// Plans with up to <paramref name="baseCount"/> + <paramref name="extra"/> connectors such that the whole layout (connectors,
    /// end caps and the spawn block) has at most <paramref name="maxBlocks"/> blocks: each connector count from the highest down
    /// is tried (deterministically from <paramref name="seed"/>) until one fits. Extras never push a map past the cap, but the
    /// base count is always kept, even when its own layout exceeds the cap.
    /// </summary>
    public static bool TryPlanCapped(IEnumerable<StreetBlock> connectors, IEnumerable<StreetBlock> endCaps, IEnumerable<StreetBlock> spawns,
        int baseCount, int extra, int maxBlocks, int seed, int maxAttempts, float loopChance,
        out List<MapBlockPlacement> layout, out int connectorCount, out string error, bool compact = false)
    {
        int highest = EffectiveConnectorCount(baseCount, extra, maxBlocks);
        layout = null;
        error = null;
        connectorCount = 0;
        for (int count = highest; count >= baseCount; count--)
        {
            if (!MapLayoutPlanner.TryPlan(connectors, endCaps, spawns, count, seed, maxAttempts, out List<MapBlockPlacement> plan,
                    out string planError, loopChance, compact))
            {
                error ??= planError;
                continue;
            }
            if (plan.Count <= maxBlocks || count == baseCount)
            {
                layout = plan;
                connectorCount = count;
                error = null;
                return true;
            }
        }
        return false;
    }
    public int Seed { get; private set; }
    public bool IsBuilt { get; private set; }
    public bool BuildFailed { get; private set; }
    /// <summary>True once building finished, successfully or not.</summary>
    public bool IsDone => IsBuilt || BuildFailed;
    public IReadOnlyList<MapBlockPlacement> Layout { get; private set; } = Array.Empty<MapBlockPlacement>();
    public Transform GeneratedRoot => generatedRoot;
    /// <summary>Number of placed blocks; indices match <see cref="Layout"/>.</summary>
    public int BlockCount => placedBlocks.Count;
    public bool HasNavMesh => navMeshInstance.valid;

    public StreetBlock GetBlock(int index) => placedBlocks[index];

    public IReadOnlyList<StreetBlock> FillerBlocks => fillerBlocks;
    /// <summary>Placed filler blocks (not part of <see cref="Layout"/>, the NavMesh or block neighbours).</summary>
    public int FillerCount => placedFillers.Count;
    public StreetBlock GetFiller(int index) => placedFillers[index];
    /// <summary>Grid cells no filler block fitted, covered by gap plugs (floor tiles plus a gap prefab).</summary>
    public int PluggedCellCount { get; private set; }
    /// <summary>World-space corners (clockwise from the -X/-Z corner) of every plugged gap cell.</summary>
    public IReadOnlyList<Vector3[]> PluggedCells => pluggedCells;
    public int SpawnPointCount => spawnPointCount;
    public float SpawnPointSpacing => spawnPointSpacing;
    public float SpawnClearanceRadius => spawnClearanceRadius;
    public float SpawnClearanceHeight => spawnClearanceHeight;
    public GameObject JeepneyPrefab => jeepneyPrefab;
    public float JeepneyRearClearance => jeepneyRearClearance;
    /// <summary>The jeepney parked in the spawn block by the last build, or null.</summary>
    public GameObject PlacedJeepney { get; private set; }

    public IReadOnlyList<StreetBlock> TownBlocks => townBlocks;
    public float TownLayoutChance => townLayoutChance;
    public int TownMinConnectorCount => townMinConnectorCount;
    /// <summary>True if the last build used the town layout (town connectors with walls removed between them).</summary>
    public bool IsTownLayout { get; private set; }
    /// <summary>World-space segments (two points at floor height) where touching town blocks had their edge walls removed.</summary>
    public IReadOnlyList<Vector3[]> OpenSeams => openSeams;

    /// <summary>Deterministic town roll shared by every peer: true with probability <paramref name="chance"/> for <paramref name="seed"/>.</summary>
    public static bool RollTownLayout(int seed, float chance) =>
        chance > 0f && new System.Random(seed ^ 0x70C1A7E5).NextDouble() < chance;

    /// <summary>True if a build with <paramref name="seed"/> would try the town layout (pool assigned, enough connectors, roll passed).</summary>
    public bool WantsTownLayout(int seed)
    {
        bool hasTownBlock = false;
        foreach (StreetBlock block in townBlocks)
            hasTownBlock |= block != null;
        return hasTownBlock && EffectiveConnectorBlockCount >= townMinConnectorCount && RollTownLayout(seed, townLayoutChance);
    }

    private bool IsTown(int blockIndex) =>
        blockIndex >= 0 && blockIndex < placedBlocks.Count && placedBlocks[blockIndex] != null && placedBlocks[blockIndex].IsTownBlock;

    public MapBlockRole GetRole(int index) => Layout[index].Role;

    /// <summary>Blocks whose footprints share an edge with block <paramref name="index"/>.</summary>
    public IReadOnlyList<int> GetNeighbours(int index) => neighbours[index];

    /// <summary>Every pair of joined entrances, and whether a barricade stands in it.</summary>
    public IReadOnlyList<MapJoint> Joints => joints;

    /// <summary>True unless joint <paramref name="joint"/> holds a barricade that has not been cleared yet.</summary>
    public bool IsJointOpen(int joint)
    {
        EntranceObstacle barricade = joint < jointBarricades.Count ? jointBarricades[joint] : null;
        return barricade == null || barricade.IsCleared;
    }

    /// <summary>
    /// Blocks reachable from <paramref name="fromBlock"/> through joined entrances without passing an uncleared barricade
    /// (e.g. a gated end cap is not reachable until its gate is opened).
    /// </summary>
    public bool[] FindOpenlyReachable(int fromBlock)
    {
        var reached = new bool[placedBlocks.Count];
        if (fromBlock < 0 || fromBlock >= reached.Length)
            return reached;
        var queue = new Queue<int>();
        reached[fromBlock] = true;
        queue.Enqueue(fromBlock);
        while (queue.Count > 0)
        {
            int block = queue.Dequeue();
            for (int j = 0; j < joints.Count; j++)
            {
                MapJoint joint = joints[j];
                if ((joint.BlockA != block && joint.BlockB != block) || !IsJointOpen(j))
                    continue;
                int other = joint.BlockA == block ? joint.BlockB : joint.BlockA;
                if (!reached[other])
                {
                    reached[other] = true;
                    queue.Enqueue(other);
                }
            }
        }
        return reached;
    }

    /// <summary>Blocks players can currently walk to from the spawn block (all false if there is none).</summary>
    public bool[] FindOpenlyReachableFromSpawn()
    {
        for (int i = 0; i < placedBlocks.Count; i++)
        {
            if (GetRole(i) == MapBlockRole.Spawn)
                return FindOpenlyReachable(i);
        }
        return new bool[placedBlocks.Count];
    }

    /// <summary>Independent loops in the street (joints - blocks + 1 for a connected map).</summary>
    public int LoopCount => Mathf.Max(0, joints.Count - placedBlocks.Count + 1);

    /// <summary>Finds the placed block whose footprint contains <paramref name="worldPosition"/> (height ignored).</summary>
    public bool TryGetBlockIndex(Vector3 worldPosition, out int index)
    {
        for (index = 0; index < placedBlocks.Count; index++)
        {
            if (placedBlocks[index] != null && placedBlocks[index].ContainsXZ(worldPosition, 0.001f))
                return true;
        }
        index = -1;
        return false;
    }

    public static void SetNetworkSeed(string sceneName, int seed, int extraConnectors = 0)
    {
        networkSeeds[sceneName] = seed;
        networkExtraConnectors[sceneName] = Mathf.Max(0, extraConnectors);
        if (Active != null && !Active.IsDone && Active.buildOnStart && Active.gameObject.scene.name == sceneName)
        {
            Active.ExtraConnectors = Mathf.Max(0, extraConnectors);
            Active.Build(seed);
        }
    }

    public static bool TryGetNetworkSeed(string sceneName, out int seed) => networkSeeds.TryGetValue(sceneName, out seed);

    public static void ClearNetworkSeeds()
    {
        networkSeeds.Clear();
        networkExtraConnectors.Clear();
    }

    private void Awake()
    {
        if (!Application.isPlaying)
            return;
        if (Active != null && Active != this)
            Debug.LogWarning("Only one MapManager should exist at a time; the newest one is used for travel readiness.", this);
        Active = this;
    }

    private void Start()
    {
        if (!buildOnStart || IsDone)
            return;

        if (TryGetNetworkSeed(gameObject.scene.name, out int seed))
        {
            ExtraConnectors = networkExtraConnectors.TryGetValue(gameObject.scene.name, out int extra) ? extra : 0;
            Build(seed);
        }
        else if (!WaitForNetworkSeed)
            Build(useFixedSeed ? fixedSeed : Environment.TickCount);
    }

    private void OnDestroy()
    {
        if (navMeshInstance.valid)
            NavMesh.RemoveNavMeshData(navMeshInstance);
        navMeshInstance = default;
        if (Active == this)
            Active = null;
    }

    /// <summary>Clears any previous result and builds the street for <paramref name="seed"/>. Returns false on failure.</summary>
    public bool Build(int seed)
    {
        Clear();
        Seed = seed;
        List<MapBlockPlacement> layout = null;
        int placedConnectors = 0;
        string error = null;
        bool planned = false;
        if (WantsTownLayout(seed))
        {
            planned = TryPlanCapped(townBlocks, endCapBlocks, spawnBlocks, connectorBlockCount, ExtraConnectors, maxBlockCount, seed,
                maxAttempts, loopChance, out layout, out placedConnectors, out error, compact: true);
            if (planned)
                IsTownLayout = true;
            else
                Debug.LogWarning($"Town layout could not be planned ({error}); building a normal street instead.", this);
        }
        if (!planned && !TryPlanCapped(connectorBlocks, endCapBlocks, spawnBlocks, connectorBlockCount, ExtraConnectors, maxBlockCount, seed,
                maxAttempts, loopChance, out layout, out placedConnectors, out error))
        {
            Debug.LogError(error, this);
            BuildFailed = true;
            Built?.Invoke(this);
            return false;
        }

        PlacedConnectorCount = placedConnectors;
        generatedRoot = new GameObject(GeneratedRootName).transform;
        generatedRoot.SetParent(transform, false);
        for (int i = 0; i < layout.Count; i++)
        {
            MapBlockPlacement placement = layout[i];
            GameObject block = Instantiate(placement.Prefab.gameObject, generatedRoot);
            block.name = $"{i:00} {placement.Role} {placement.Prefab.name}";
            block.transform.localPosition = placement.Position;
            block.transform.localRotation = placement.Rotation;
            placedBlocks.Add(block.GetComponent<StreetBlock>());
        }

        Layout = layout;
        FindNeighbours(layout);
        OpenTownSeams(layout);
        ClearSpawnEntrances(layout);
        ResolveBarricades(layout);
        PlaceFillers(layout);
        if (generateSpawnPoints || jeepneyPrefab != null)
            GenerateSpawnPoints(layout);
        if (buildNavMesh && Application.isPlaying)
            BuildNavigation();
        IsBuilt = true;
        if (Application.isPlaying)
            EntranceObstacleNetwork.RequestSnapshot();
        Built?.Invoke(this);
        return true;
    }

    /// <summary>
    /// End caps may be sealed off completely (loot can wait behind them). Every other barricade must leave a route
    /// around it: obstacles are kept one by one only while the spawn block can still reach every non-end-cap block
    /// without passing a kept barricade. Each joint keeps at most one obstacle.
    /// </summary>
    private void ResolveBarricades(IReadOnlyList<MapBlockPlacement> layout)
    {
        const float joinTolerance = 0.1f;
        joints.Clear();
        for (int a = 0; a < placedBlocks.Count; a++)
        {
            for (int b = a + 1; b < placedBlocks.Count; b++)
            {
                if (placedBlocks[a] == null || placedBlocks[b] == null)
                    continue;
                for (int ea = 0; ea < placedBlocks[a].Entrances.Count; ea++)
                {
                    Vector3 pa = EntranceWorld(a, ea);
                    for (int eb = 0; eb < placedBlocks[b].Entrances.Count; eb++)
                    {
                        Vector3 offset = EntranceWorld(b, eb) - pa;
                        if (new Vector2(offset.x, offset.z).magnitude <= joinTolerance)
                            joints.Add(new MapJoint(a, ea, b, eb, false));
                    }
                }
            }
        }

        // Every obstacle under the map belongs to the joint whose entrance references it, or else the nearest opening.
        var jointObstacles = new List<EntranceObstacle>[joints.Count];
        for (int j = 0; j < joints.Count; j++)
            jointObstacles[j] = new List<EntranceObstacle>();
        foreach (EntranceObstacle obstacle in generatedRoot.GetComponentsInChildren<EntranceObstacle>(true))
        {
            StreetBlock hostBlock = obstacle.GetComponentInParent<StreetBlock>(true);
            if (hostBlock != null && hostBlock.IsTownBlock)
            {
                // Town blocks have no walls to frame an obstacle.
                RemoveObstacle(obstacle);
                continue;
            }
            int owner = -1;
            float best = float.MaxValue;
            for (int j = 0; j < joints.Count && owner < 0; j++)
            {
                if (Entrance(joints[j].BlockA, joints[j].EntranceA).Obstacle == obstacle ||
                    Entrance(joints[j].BlockB, joints[j].EntranceB).Obstacle == obstacle)
                    owner = j;
            }
            for (int j = 0; owner < 0 && j < joints.Count; j++)
            {
                Vector3 offset = obstacle.transform.position - EntranceWorld(joints[j].BlockA, joints[j].EntranceA);
                float distance = new Vector2(offset.x, offset.z).magnitude;
                if (distance <= Entrance(joints[j].BlockA, joints[j].EntranceA).Width * 0.5f + 1f && distance < best)
                {
                    best = distance;
                    owner = j;
                }
            }
            if (owner >= 0)
                jointObstacles[owner].Add(obstacle);
        }

        var blocked = new bool[joints.Count];
        jointBarricades.Clear();
        for (int j = 0; j < joints.Count; j++)
            jointBarricades.Add(null);
        var random = new System.Random(Seed ^ 0x0B57AC1E);
        bool warnedNoPrefabs = false;
        for (int j = 0; j < joints.Count; j++)
        {
            List<EntranceObstacle> obstacles = jointObstacles[j];
            bool endCap = layout[joints[j].BlockA].Role == MapBlockRole.EndCap || layout[joints[j].BlockB].Role == MapBlockRole.EndCap;
            if (IsTown(joints[j].BlockA) && IsTown(joints[j].BlockB))
            {
                // Between two town blocks there is no wall to hold an obstacle.
                foreach (EntranceObstacle obstacle in obstacles)
                    RemoveObstacle(obstacle);
                continue;
            }
            if (obstacles.Count == 0)
            {
                bool spawnJoint = layout[joints[j].BlockA].Role == MapBlockRole.Spawn || layout[joints[j].BlockB].Role == MapBlockRole.Spawn;
                float chance = endCap ? endCapObstacleChance : loopObstacleChance;
                if (spawnJoint || chance <= 0f || random.NextDouble() >= chance)
                    continue;
                if (!(endCap ? barricadeEndCaps : barricadeLoops && StaysConnected(layout, blocked, j)))
                    continue;
                EntranceObstacle placedObstacle = PlaceJointObstacle(layout, j, random);
                jointBarricades[j] = placedObstacle;
                if (placedObstacle == null)
                {
                    if (!warnedNoPrefabs)
                        Debug.LogWarning("A joint rolled an obstacle but no Obstacle Prefabs are assigned on either block's generator or the MapManager's Fallback Obstacle Prefabs.", this);
                    warnedNoPrefabs = true;
                    continue;
                }
                blocked[j] = true;
                joints[j] = new MapJoint(joints[j].BlockA, joints[j].EntranceA, joints[j].BlockB, joints[j].EntranceB, true);
                continue;
            }
            bool keep = endCap ? barricadeEndCaps : barricadeLoops && StaysConnected(layout, blocked, j);
            int keepIndex = -1;
            if (keep)
            {
                // Prefer the end cap's own obstacle, otherwise the first one.
                keepIndex = 0;
                for (int k = 0; endCap && k < obstacles.Count; k++)
                {
                    StreetBlock owner = obstacles[k].GetComponentInParent<StreetBlock>();
                    int index = owner != null ? placedBlocks.IndexOf(owner) : -1;
                    if (index >= 0 && layout[index].Role == MapBlockRole.EndCap)
                    {
                        keepIndex = k;
                        break;
                    }
                }
                blocked[j] = true;
                jointBarricades[j] = obstacles[keepIndex];
            }
            for (int k = 0; k < obstacles.Count; k++)
            {
                if (k != keepIndex)
                    RemoveObstacle(obstacles[k]);
            }
            joints[j] = new MapJoint(joints[j].BlockA, joints[j].EntranceA, joints[j].BlockB, joints[j].EntranceB, blocked[j]);
        }
    }

    /// <summary>
    /// Places a random obstacle inside one block of joint <paramref name="joint"/> (the end cap's, else either block), on the
    /// block edge, centred on the entrance and facing inward, exactly like the street block generator does.
    /// </summary>
    private EntranceObstacle PlaceJointObstacle(IReadOnlyList<MapBlockPlacement> layout, int joint, System.Random random)
    {
        MapJoint j = joints[joint];
        bool townA = IsTown(j.BlockA);
        bool townB = IsTown(j.BlockB);
        // A town block never hosts the obstacle: its walled neighbour does.
        bool aIsHost = townA != townB
            ? townB
            : layout[j.BlockA].Role == MapBlockRole.EndCap ||
              (layout[j.BlockB].Role != MapBlockRole.EndCap && random.Next(2) == 0);
        int hostIndex = aIsHost ? j.BlockA : j.BlockB;
        int otherIndex = aIsHost ? j.BlockB : j.BlockA;
        StreetBlock host = placedBlocks[hostIndex];
        StreetBlockEntrance entrance = host.Entrances[aIsHost ? j.EntranceA : j.EntranceB];

        List<GameObject> pool = ObstaclePool(host);
        if (pool.Count == 0)
            pool = ObstaclePool(placedBlocks[otherIndex]);
        if (pool.Count == 0)
            pool = ValidObstacles(fallbackObstaclePrefabs);
        if (pool.Count == 0)
            return null;
        GameObject prefab = pool[random.Next(pool.Count)];

        Transform parent = host.transform.Find("Generated/Obstacles");
        if (parent == null)
        {
            parent = new GameObject("Obstacles").transform;
            Transform generated = host.transform.Find("Generated");
            parent.SetParent(generated != null ? generated : host.transform, false);
        }
        GameObject instance = Instantiate(prefab, parent);
        instance.name = $"{prefab.name} ({entrance.Side} entrance)";
        instance.SetActive(true);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.Euler(0f, entrance.Side.InwardYaw(), 0f);

        StreetBlockGenerator generator = host.GetComponent<StreetBlockGenerator>();
        if ((generator == null || generator.addMissingColliders) && instance.GetComponentInChildren<Collider>(true) == null &&
            TryMeasure(instance, instance.transform, out Bounds own))
        {
            BoxCollider box = instance.AddComponent<BoxCollider>();
            box.center = own.center;
            box.size = own.size;
        }

        if (TryMeasure(instance, host.transform, out Bounds bounds))
        {
            // Outer face on the block edge, centred along the entrance, bottom on the floor.
            Vector3 outward = entrance.Side.Outward();
            float depth = Mathf.Abs(Vector3.Dot(bounds.size, outward));
            Vector3 target = entrance.LocalCenter - outward * (depth * 0.5f);
            Vector3 shift = new Vector3(target.x - bounds.center.x, entrance.LocalCenter.y - bounds.min.y, target.z - bounds.center.z);
            instance.transform.position += host.transform.TransformVector(shift);
        }
        else
        {
            instance.transform.localPosition = parent.InverseTransformPoint(host.transform.TransformPoint(entrance.LocalCenter));
        }

        EntranceObstacle obstacle = instance.GetComponentInChildren<EntranceObstacle>(true);
        entrance.AssignObstacle(obstacle);
        return obstacle;
    }

    private static List<GameObject> ObstaclePool(StreetBlock block)
    {
        StreetBlockGenerator generator = block != null ? block.GetComponent<StreetBlockGenerator>() : null;
        return ValidObstacles(generator != null ? generator.obstaclePrefabs : null);
    }

    private static List<GameObject> ValidObstacles(IEnumerable<GameObject> prefabs)
    {
        var result = new List<GameObject>();
        if (prefabs == null)
            return result;
        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null && prefab.GetComponentInChildren<EntranceObstacle>(true) != null)
                result.Add(prefab);
        }
        return result;
    }

    /// <summary>Renderer and solid collider bounds of <paramref name="instance"/> in <paramref name="space"/>.</summary>
    private static bool TryMeasure(GameObject instance, Transform space, out Bounds total)
    {
        bool any = false;
        total = default;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                continue;
            Encapsulate(ref total, ref any, renderer.localBounds, renderer.transform, space);
        }
        Physics.SyncTransforms();
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger)
                continue;
            if (collider is BoxCollider box)
                Encapsulate(ref total, ref any, new Bounds(box.center, box.size), box.transform, space);
            else if (collider.enabled && collider.gameObject.activeInHierarchy)
                Encapsulate(ref total, ref any, collider.bounds, null, space);
        }
        return any;
    }

    private static void Encapsulate(ref Bounds total, ref bool any, Bounds local, Transform from, Transform space)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
            Vector3 point = local.center + Vector3.Scale(local.extents, sign);
            if (from != null)
                point = from.TransformPoint(point);
            point = space.InverseTransformPoint(point);
            if (!any)
            {
                total = new Bounds(point, Vector3.zero);
                any = true;
            }
            else
            {
                total.Encapsulate(point);
            }
        }
    }

    /// <summary>True if the spawn block still reaches every non-end-cap block with joint <paramref name="extra"/> blocked too.</summary>
    private bool StaysConnected(IReadOnlyList<MapBlockPlacement> layout, bool[] blocked, int extra)
    {
        int start = -1;
        int required = 0;
        for (int i = 0; i < layout.Count; i++)
        {
            if (layout[i].Role == MapBlockRole.EndCap)
                continue;
            required++;
            if (start < 0 || layout[i].Role == MapBlockRole.Spawn)
                start = i;
        }
        if (start < 0)
            return true;

        var visited = new bool[layout.Count];
        var queue = new Queue<int>();
        visited[start] = true;
        queue.Enqueue(start);
        int reached = 0;
        while (queue.Count > 0)
        {
            int block = queue.Dequeue();
            reached++;
            for (int j = 0; j < joints.Count; j++)
            {
                if (blocked[j] || j == extra)
                    continue;
                int other = joints[j].BlockA == block ? joints[j].BlockB : joints[j].BlockB == block ? joints[j].BlockA : -1;
                if (other < 0 || visited[other] || layout[other].Role == MapBlockRole.EndCap)
                    continue;
                visited[other] = true;
                queue.Enqueue(other);
            }
        }
        return reached == required;
    }

    private StreetBlockEntrance Entrance(int block, int entrance) => placedBlocks[block].Entrances[entrance];

    private Vector3 EntranceWorld(int block, int entrance) =>
        placedBlocks[block].transform.TransformPoint(Entrance(block, entrance).LocalCenter);

    private void RemoveObstacle(EntranceObstacle obstacle)
    {
        foreach (StreetBlock block in placedBlocks)
        {
            if (block == null)
                continue;
            foreach (StreetBlockEntrance entrance in block.Entrances)
            {
                if (entrance.Obstacle == obstacle)
                {
                    entrance.RemoveObstacle();
                    return;
                }
            }
        }
        DestroyImmediate(obstacle.gameObject);
    }

    /// <summary>
    /// Players must always be able to leave the spawn block: removes any obstacle on a spawn block's entrance, on the
    /// neighbouring entrance it joins, or otherwise standing in that opening.
    /// </summary>
    private void ClearSpawnEntrances(IReadOnlyList<MapBlockPlacement> layout)
    {
        const float joinTolerance = 1f;
        var openings = new List<(Vector3 center, float radius)>();
        for (int i = 0; i < layout.Count; i++)
        {
            if (layout[i].Role != MapBlockRole.Spawn || placedBlocks[i] == null)
                continue;
            foreach (StreetBlockEntrance entrance in placedBlocks[i].Entrances)
            {
                entrance.RemoveObstacle();
                openings.Add((placedBlocks[i].transform.TransformPoint(entrance.LocalCenter), entrance.Width * 0.5f + joinTolerance));
            }
        }
        if (openings.Count == 0)
            return;

        foreach (StreetBlock block in placedBlocks)
        {
            if (block == null)
                continue;
            foreach (StreetBlockEntrance entrance in block.Entrances)
            {
                if (entrance.Obstacle != null && NearOpening(block.transform.TransformPoint(entrance.LocalCenter), openings, joinTolerance))
                    entrance.RemoveObstacle();
            }
        }
        foreach (EntranceObstacle obstacle in generatedRoot.GetComponentsInChildren<EntranceObstacle>(true))
        {
            if (obstacle != null && NearOpening(obstacle.transform.position, openings, 0f))
                DestroyImmediate(obstacle.gameObject);
        }
    }

    private static bool NearOpening(Vector3 position, List<(Vector3 center, float radius)> openings, float fixedRadius)
    {
        foreach ((Vector3 center, float radius) in openings)
        {
            Vector2 offset = new Vector2(position.x - center.x, position.z - center.z);
            if (offset.magnitude <= (fixedRadius > 0f ? fixedRadius : radius))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Fills every empty grid cell in the street's bounding box (plus the margin) with filler blocks, largest first.
    /// Uses its own random stream so the street layout for a seed never changes.
    /// </summary>
    private void PlaceFillers(IReadOnlyList<MapBlockPlacement> layout)
    {
        if (fillerBlocks.Count == 0 || layout.Count == 0)
            return;

        Vector2 cell = layout[0].Prefab.CellSize;
        var valid = new List<StreetBlock>();
        foreach (StreetBlock filler in fillerBlocks)
        {
            if (filler == null)
                continue;
            Vector2 cells = new Vector2(filler.Size.x / cell.x, filler.Size.y / cell.y);
            if (filler.Entrances.Count != 0)
                Debug.LogError($"Filler block '{filler.name}' has entrances; fillers must have none. It is skipped.", this);
            else if (Vector2.Distance(filler.CellSize, cell) > 0.01f || cells.x < 0.99f || cells.y < 0.99f ||
                     Mathf.Abs(cells.x - Mathf.Round(cells.x)) > 0.01f || Mathf.Abs(cells.y - Mathf.Round(cells.y)) > 0.01f)
                Debug.LogError($"Filler block '{filler.name}' must use the street's {cell.x}x{cell.y} m cell size. It is skipped.", this);
            else
                valid.Add(filler);
        }
        if (valid.Count == 0)
            return;

        Vector2 origin = Footprint(layout[0]).min;
        var occupied = new HashSet<Vector2Int>();
        Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
        Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);
        foreach (MapBlockPlacement placement in layout)
        {
            Rect footprint = Footprint(placement);
            Vector2Int start = ToCell(footprint.min - origin, cell);
            Vector2Int size = ToCell(footprint.size, cell);
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                    occupied.Add(new Vector2Int(start.x + x, start.y + y));
            }
            min = Vector2Int.Min(min, start);
            max = Vector2Int.Max(max, start + size - Vector2Int.one);
        }
        min -= Vector2Int.one * fillerMarginCells;
        max += Vector2Int.one * fillerMarginCells;

        var random = new System.Random(Seed ^ 0x5F1A7E3);
        var fillersRoot = new GameObject("Fillers").transform;
        fillersRoot.SetParent(generatedRoot, false);
        float height = layout[0].Position.y;
        var holes = new List<Vector2Int>();
        var candidates = new List<(StreetBlock prefab, int turns, Vector2Int size, int order)>();
        for (int y = min.y; y <= max.y; y++)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                var corner = new Vector2Int(x, y);
                if (occupied.Contains(corner))
                    continue;

                candidates.Clear();
                foreach (StreetBlock filler in valid)
                {
                    int turns = random.Next(4);
                    Vector2Int size = ToCell(filler.Size, cell);
                    if (turns % 2 != 0)
                        size = new Vector2Int(size.y, size.x);
                    candidates.Add((filler, turns, size, random.Next()));
                }
                // Largest filler first so one-cell fillers only plug the leftover holes; ties are random.
                candidates.Sort((a, b) => a.size.x * a.size.y != b.size.x * b.size.y
                    ? (b.size.x * b.size.y).CompareTo(a.size.x * a.size.y)
                    : a.order.CompareTo(b.order));

                bool placed = false;
                foreach ((StreetBlock prefab, int turns, Vector2Int size, int _) in candidates)
                {
                    if (!FillerFits(corner, size, min, max, occupied))
                        continue;
                    for (int fx = 0; fx < size.x; fx++)
                    {
                        for (int fy = 0; fy < size.y; fy++)
                            occupied.Add(new Vector2Int(corner.x + fx, corner.y + fy));
                    }
                    Vector2 center = origin + new Vector2((corner.x + size.x * 0.5f) * cell.x, (corner.y + size.y * 0.5f) * cell.y);
                    GameObject block = Instantiate(prefab.gameObject, fillersRoot);
                    block.name = $"Filler {placedFillers.Count:00} {prefab.name}";
                    block.transform.localPosition = new Vector3(center.x, height, center.y);
                    block.transform.localRotation = Quaternion.Euler(0f, 90f * turns, 0f);
                    placedFillers.Add(block.GetComponent<StreetBlock>());
                    placed = true;
                    break;
                }
                if (!placed)
                    holes.Add(corner);
            }
        }
        if (holes.Count > 0)
        {
            StreetBlock smallest = valid.OrderBy(f => f.Size.x * f.Size.y).First();
            if (plugGaps)
                PlugGaps(holes, smallest, origin, cell, height, fillersRoot, random);
            else
                Debug.LogWarning($"{holes.Count} filler cell(s) stayed empty because no filler fits them. Enable Plug Gaps or add a one-cell ({cell.x}x{cell.y} m) filler block.", this);
        }
    }

    /// <summary>
    /// Covers cells no filler block fits with the smallest filler's floor tiles plus one gap prefab per cell
    /// (centred, random quarter turn, skipped if larger than the cell).
    /// </summary>
    private void PlugGaps(List<Vector2Int> holes, StreetBlock source, Vector2 origin, Vector2 cell, float height,
        Transform fillersRoot, System.Random random)
    {
        var root = new GameObject("Gap Plugs").transform;
        root.SetParent(fillersRoot, false);

        // The generator lays tile (x, z) with a constant offset inside its square, so the lowest tile is the template.
        Transform template = null;
        Vector3 templateOffset = default;
        Quaternion templateRotation = Quaternion.identity;
        Transform floor = source.FloorRoot;
        Vector2 sourceHalf = source.Size * 0.5f;
        if (floor != null)
        {
            foreach (Transform tile in floor)
            {
                if (tile.GetComponentInChildren<Renderer>(true) == null)
                    continue;
                Vector3 local = source.transform.InverseTransformPoint(tile.position);
                if (template == null || local.x + local.z < templateOffset.x + templateOffset.z - 0.001f)
                {
                    template = tile;
                    templateOffset = local;
                    templateRotation = Quaternion.Inverse(source.transform.rotation) * tile.rotation;
                }
            }
        }
        if (template == null)
            Debug.LogWarning($"Filler '{source.name}' has no floor tiles to plug {holes.Count} gap cell(s) with.", this);
        templateOffset += new Vector3(sourceHalf.x, 0f, sourceHalf.y);

        var pool = new List<GameObject>();
        pool.AddRange(gapFillPrefabs.Where(p => p != null));
        if (pool.Count == 0 && source.TryGetComponent(out StreetBlockGenerator generator))
        {
            foreach (GameObject[] list in new[] { generator.fillerBuildingPrefabs, generator.middleBuildingPrefabs, generator.buildingPrefabs })
            {
                if (list != null && list.Any(p => p != null))
                {
                    pool.AddRange(list.Where(p => p != null));
                    break;
                }
            }
        }

        Vector2 tileSize = source.TileSize;
        int tilesPerCell = source.TilesPerCell;
        int skipped = 0;
        foreach (Vector2Int hole in holes)
        {
            Vector2 cellMin = origin + new Vector2(hole.x * cell.x, hole.y * cell.y);
            pluggedCells.Add(new[]
            {
                root.TransformPoint(new Vector3(cellMin.x, height, cellMin.y)),
                root.TransformPoint(new Vector3(cellMin.x, height, cellMin.y + cell.y)),
                root.TransformPoint(new Vector3(cellMin.x + cell.x, height, cellMin.y + cell.y)),
                root.TransformPoint(new Vector3(cellMin.x + cell.x, height, cellMin.y)),
            });
            if (template != null)
            {
                for (int x = 0; x < tilesPerCell; x++)
                {
                    for (int z = 0; z < tilesPerCell; z++)
                    {
                        GameObject tile = Instantiate(template.gameObject, root);
                        tile.name = $"Gap Floor ({hole.x},{hole.y})";
                        tile.transform.localPosition = new Vector3(cellMin.x + x * tileSize.x + templateOffset.x,
                            height + templateOffset.y, cellMin.y + z * tileSize.y + templateOffset.z);
                        tile.transform.localRotation = templateRotation;
                    }
                }
            }
            if (pool.Count == 0)
                continue;

            Vector2 centre = cellMin + cell * 0.5f;
            GameObject prefab = pool[random.Next(pool.Count)];
            GameObject building = Instantiate(prefab, root);
            building.name = $"Gap {prefab.name} ({hole.x},{hole.y})";
            building.transform.localPosition = new Vector3(centre.x, height, centre.y);
            building.transform.localRotation = Quaternion.Euler(0f, 90f * random.Next(4), 0f);
            if (!TryGetHorizontalBounds(building, root, out Rect bounds) ||
                bounds.width > cell.x + 0.01f || bounds.height > cell.y + 0.01f)
            {
                DestroyImmediate(building);
                skipped++;
                continue;
            }
            // Re-centre the measured footprint on the cell (prefab pivots are not always centred).
            Vector2 shift = centre - bounds.center;
            building.transform.localPosition += new Vector3(shift.x, 0f, shift.y);
        }
        if (skipped > 0 && skipped == holes.Count)
            Debug.LogWarning($"No gap fill prefab fits one {cell.x}x{cell.y} m cell; {holes.Count} gap cell(s) only got floor tiles. Assign smaller Gap Fill Prefabs.", this);
        PluggedCellCount += holes.Count;
    }

    private static bool TryGetHorizontalBounds(GameObject instance, Transform space, out Rect rect)
    {
        rect = default;
        bool any = false;
        float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
        void Encapsulate(Bounds world)
        {
            Vector3 c = world.center, e = world.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = space.InverseTransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                if (!any)
                {
                    minX = maxX = corner.x;
                    minZ = maxZ = corner.z;
                    any = true;
                }
                minX = Mathf.Min(minX, corner.x);
                maxX = Mathf.Max(maxX, corner.x);
                minZ = Mathf.Min(minZ, corner.z);
                maxZ = Mathf.Max(maxZ, corner.z);
            }
        }
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            Encapsulate(renderer.bounds);
        Physics.SyncTransforms();
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
        {
            if (!collider.isTrigger)
                Encapsulate(collider.bounds);
        }
        if (any)
            rect = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        return any;
    }

    private static bool FillerFits(Vector2Int corner, Vector2Int size, Vector2Int min, Vector2Int max, HashSet<Vector2Int> occupied)
    {
        // Fillers may overhang past the outer margin (those cells are always empty), so large fillers still reach the edge.
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                if (occupied.Contains(new Vector2Int(corner.x + x, corner.y + y)))
                    return false;
            }
        }
        return true;
    }

    private static Vector2Int ToCell(Vector2 metres, Vector2 cell) =>
        new Vector2Int(Mathf.RoundToInt(metres.x / cell.x), Mathf.RoundToInt(metres.y / cell.y));

    private static Rect Footprint(MapBlockPlacement placement)
    {
        Vector2 size = placement.Prefab.Size;
        if (placement.QuarterTurns % 2 != 0)
            size = new Vector2(size.y, size.x);
        return new Rect(new Vector2(placement.Position.x, placement.Position.z) - size * 0.5f, size);
    }

    /// <summary>
    /// Parks the <see cref="jeepneyPrefab"/> in each placed spawn block and gives the block up to
    /// <see cref="spawnPointCount"/> spawn points where a player-sized capsule touches no solid collider and the floor
    /// below is reachable from the block's entrance.
    /// </summary>
    private void GenerateSpawnPoints(IReadOnlyList<MapBlockPlacement> layout)
    {
        Physics.SyncTransforms();
        for (int i = 0; i < layout.Count; i++)
        {
            StreetBlock block = placedBlocks[i];
            if (layout[i].Role != MapBlockRole.Spawn || block == null)
                continue;
            PlayerSpawnPoints existing = block.GetComponentInChildren<PlayerSpawnPoints>(true);
            bool keepExisting = existing != null && existing.transform.childCount > 0;

            if (jeepneyPrefab != null && PlacedJeepney == null)
            {
                var keep = new List<Vector3>();
                if (keepExisting)
                {
                    foreach (Transform point in existing.transform)
                        keep.Add(block.transform.InverseTransformPoint(point.position));
                }
                PlacedJeepney = PlaceJeepney(block, keep);
                if (PlacedJeepney == null)
                    Debug.LogWarning($"No free spot for the jeepney was found in spawn block '{block.name}'.", block);
            }

            if (!generateSpawnPoints || keepExisting)
                continue;
            if (existing != null)
                DestroyImmediate(existing.gameObject);

            List<(Vector3 position, Quaternion rotation)> points = FindSpawnPoints(block, BuildSpawnGrid(block));
            if (points.Count == 0)
            {
                Debug.LogError($"No unobstructed spot for player spawn points was found in spawn block '{block.name}'.", block);
                continue;
            }
            if (points.Count < spawnPointCount)
                Debug.LogWarning($"Only {points.Count} of {spawnPointCount} spawn points fit in spawn block '{block.name}'.", block);

            var root = new GameObject("PlayerSpawnPoints (Generated)");
            root.SetActive(false);
            root.transform.SetParent(block.transform, false);
            for (int p = 0; p < points.Count; p++)
            {
                Transform point = new GameObject($"Spawn {p + 1}").transform;
                point.SetParent(root.transform, false);
                point.localPosition = points[p].position;
                point.localRotation = points[p].rotation;
            }
            root.AddComponent<PlayerSpawnPoints>();
            root.SetActive(true);
        }
    }

    /// <summary>Free floor cells of a block (block space, 0.5 m grid) and which of them are reachable from its entrance.</summary>
    private sealed class SpawnGrid
    {
        public const float Step = 0.5f;
        public Vector2 Half;
        public int CountX;
        public int CountZ;
        public bool[,] Free;
        public bool[,] Reachable;
        public float[,] Heights;
        public Vector3 Entrance;
        public bool SeededFromEntrance;

        public Vector3 LocalAt(int x, int z) => new Vector3(-Half.x + x * Step, 0f, -Half.y + z * Step);

        public bool TryCell(Vector3 local, out Vector2Int cell)
        {
            cell = new Vector2Int(Mathf.RoundToInt((local.x + Half.x) / Step), Mathf.RoundToInt((local.z + Half.y) / Step));
            return CountX > 0 && cell.x >= 0 && cell.y >= 0 && cell.x < CountX && cell.y < CountZ;
        }
    }

    private SpawnGrid BuildSpawnGrid(StreetBlock block)
    {
        const float lift = 0.05f;
        var grid = new SpawnGrid
        {
            Half = block.Size * 0.5f - Vector2.one * spawnClearanceRadius,
            Entrance = block.Entrances.Count > 0 ? block.Entrances[0].LocalCenter : Vector3.zero,
        };
        if (grid.Half.x < 0f || grid.Half.y < 0f)
        {
            grid.Free = grid.Reachable = new bool[0, 0];
            grid.Heights = new float[0, 0];
            return grid;
        }

        int countX = grid.CountX = Mathf.FloorToInt(grid.Half.x * 2f / SpawnGrid.Step) + 1;
        int countZ = grid.CountZ = Mathf.FloorToInt(grid.Half.y * 2f / SpawnGrid.Step) + 1;
        var free = grid.Free = new bool[countX, countZ];
        var heights = grid.Heights = new float[countX, countZ];
        Transform blockFloor = block.FloorRoot;
        bool hasFloorColliders = blockFloor != null && blockFloor.GetComponentInChildren<Collider>() != null;
        List<Transform> floors = PlacedFloors();
        var overlaps = new Collider[32];

        for (int x = 0; x < countX; x++)
        {
            for (int z = 0; z < countZ; z++)
            {
                Vector3 world = block.transform.TransformPoint(grid.LocalAt(x, z));
                float groundY = world.y;
                if (hasFloorColliders)
                {
                    RaycastHit[] hits = Physics.RaycastAll(world + Vector3.up * 1.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
                    bool grounded = false;
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.collider.transform.IsChildOf(blockFloor) && (!grounded || hit.point.y > groundY))
                        {
                            groundY = hit.point.y;
                            grounded = true;
                        }
                    }
                    if (!grounded)
                        continue;
                }

                Vector3 bottom = new Vector3(world.x, groundY + spawnClearanceRadius + lift, world.z);
                Vector3 top = new Vector3(world.x, groundY + Mathf.Max(spawnClearanceHeight - spawnClearanceRadius, spawnClearanceRadius + lift), world.z);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, spawnClearanceRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int c = 0; c < count && !blocked; c++)
                    blocked = IsGeneratedSolid(overlaps[c], floors);
                if (blocked)
                    continue;
                free[x, z] = true;
                heights[x, z] = block.transform.InverseTransformPoint(new Vector3(world.x, groundY, world.z)).y;
            }
        }

        // Keep only floor reachable from the entrance (or the centre if the block has none), so no point is walled in.
        Vector3 entrance = grid.Entrance;
        var reachable = grid.Reachable = new bool[countX, countZ];
        var queue = new Queue<Vector2Int>();
        float bestStart = float.MaxValue;
        Vector2Int nearest = new Vector2Int(-1, -1);
        for (int x = 0; x < countX; x++)
        {
            for (int z = 0; z < countZ; z++)
            {
                if (!free[x, z])
                    continue;
                float distance = Vector3.Distance(grid.LocalAt(x, z), new Vector3(entrance.x, 0f, entrance.z));
                if (distance <= 1.5f)
                {
                    reachable[x, z] = true;
                    queue.Enqueue(new Vector2Int(x, z));
                }
                if (distance < bestStart)
                {
                    bestStart = distance;
                    nearest = new Vector2Int(x, z);
                }
            }
        }
        grid.SeededFromEntrance = queue.Count > 0;
        if (queue.Count == 0 && nearest.x >= 0)
        {
            reachable[nearest.x, nearest.y] = true;
            queue.Enqueue(nearest);
        }
        var directions = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;
                if (next.x < 0 || next.y < 0 || next.x >= countX || next.y >= countZ || !free[next.x, next.y] || reachable[next.x, next.y])
                    continue;
                reachable[next.x, next.y] = true;
                queue.Enqueue(next);
            }
        }
        return grid;
    }

    private List<Transform> PlacedFloors()
    {
        var floors = new List<Transform>();
        foreach (StreetBlock placed in placedBlocks)
        {
            if (placed != null && placed.FloorRoot != null)
                floors.Add(placed.FloorRoot);
        }
        return floors;
    }

    private bool IsGeneratedSolid(Collider collider, List<Transform> floors)
    {
        if (collider.isTrigger || !collider.transform.IsChildOf(generatedRoot))
            return false;
        foreach (Transform floor in floors)
        {
            if (collider.transform.IsChildOf(floor))
                return false;
        }
        return true;
    }

    /// <summary>Spawn points in block space, nearest the block centre first.</summary>
    private List<(Vector3 position, Quaternion rotation)> FindSpawnPoints(StreetBlock block, SpawnGrid grid)
    {
        const float lift = 0.05f;
        var result = new List<(Vector3, Quaternion)>();
        Vector3 entrance = grid.Entrance;
        var cells = new List<Vector2Int>();
        for (int x = 0; x < grid.CountX; x++)
        {
            for (int z = 0; z < grid.CountZ; z++)
            {
                if (grid.Reachable[x, z])
                    cells.Add(new Vector2Int(x, z));
            }
        }
        cells.Sort((a, b) => grid.LocalAt(a.x, a.y).sqrMagnitude.CompareTo(grid.LocalAt(b.x, b.y).sqrMagnitude));

        foreach (Vector2Int cell in cells)
        {
            Vector3 local = grid.LocalAt(cell.x, cell.y);
            bool spaced = true;
            foreach ((Vector3 position, Quaternion _) in result)
                spaced &= new Vector2(position.x - local.x, position.z - local.z).magnitude >= spawnPointSpacing - 0.001f;
            if (!spaced)
                continue;
            Vector3 facing = new Vector3(entrance.x - local.x, 0f, entrance.z - local.z);
            Quaternion rotation = facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing) : Quaternion.identity;
            result.Add((new Vector3(local.x, grid.Heights[cell.x, cell.y] + lift, local.z), rotation));
            if (result.Count >= spawnPointCount)
                break;
        }
        return result;
    }

    /// <summary>
    /// Parks the jeepney on free, reachable floor of the spawn block: as far from the entrance as fits, axis-aligned with
    /// its back (local -Z) towards the block centre, keeping <see cref="jeepneyRearClearance"/> of reachable floor behind
    /// it. Each pose is verified by re-checking reachability with the jeepney in place: the entrance, the floor behind its
    /// back and the spawn points must stay reachable and it must not cover <paramref name="keepClear"/> (block space).
    /// Deterministic (local physics only), so every peer parks it in the same spot.
    /// </summary>
    private GameObject PlaceJeepney(StreetBlock block, List<Vector3> keepClear)
    {
        var holder = new GameObject("Jeepney Measure");
        holder.SetActive(false);
        GameObject jeepney = Instantiate(jeepneyPrefab, holder.transform);
        jeepney.name = jeepneyPrefab.name;
        jeepney.transform.localPosition = Vector3.zero;
        jeepney.transform.localRotation = Quaternion.identity;
        jeepney.transform.localScale = Vector3.one;
        if (!TryMeasure(jeepney, jeepney.transform, out Bounds footprint))
        {
            DestroyImmediate(holder);
            Debug.LogWarning($"Jeepney prefab '{jeepneyPrefab.name}' has no renderers or colliders to measure.", this);
            return null;
        }

        SpawnGrid baseline = BuildSpawnGrid(block);
        int baselinePoints = generateSpawnPoints ? FindSpawnPoints(block, baseline).Count : 0;
        List<Transform> floors = PlacedFloors();
        const float margin = 0.1f;
        Vector3 extents = footprint.extents + new Vector3(margin, 0f, margin);
        Vector3 entrance = new Vector3(baseline.Entrance.x, 0f, baseline.Entrance.z);

        bool InFootprint(Vector3 local, Vector3 position, Quaternion yaw, float grow)
        {
            Vector3 p = Quaternion.Inverse(yaw) * (local - position) - footprint.center;
            return Mathf.Abs(p.x) <= extents.x + grow && Mathf.Abs(p.z) <= extents.z + grow;
        }
        bool InRear(Vector3 local, Vector3 position, Quaternion yaw)
        {
            Vector3 p = Quaternion.Inverse(yaw) * (local - position);
            return Mathf.Abs(p.x - footprint.center.x) <= footprint.extents.x &&
                p.z < footprint.min.z - margin && p.z >= footprint.min.z - margin - jeepneyRearClearance;
        }
        Vector3 RearSpot(Vector3 position, Quaternion yaw) =>
            position + yaw * new Vector3(footprint.center.x, 0f, footprint.min.z - margin - Mathf.Min(0.75f, jeepneyRearClearance));

        var candidates = new List<(Vector3 position, Quaternion yaw, float score)>();
        for (int x = 0; x < baseline.CountX; x += 2)
        {
            for (int z = 0; z < baseline.CountZ; z += 2)
            {
                if (!baseline.Reachable[x, z])
                    continue;
                Vector3 position = baseline.LocalAt(x, z);
                Vector3 away = new Vector3(position.x, 0f, position.z);
                if (away.sqrMagnitude < 0.01f)
                    away = -entrance;
                if (away.sqrMagnitude < 0.01f)
                    away = Vector3.forward;
                // Facing away from the centre (back towards it) along the nearest axes.
                Vector3 major = Mathf.Abs(away.x) >= Mathf.Abs(away.z) ? new Vector3(Mathf.Sign(away.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(away.z));
                Vector3 minor = Mathf.Abs(away.x) >= Mathf.Abs(away.z) ? new Vector3(0f, 0f, away.z >= 0f ? 1f : -1f) : new Vector3(away.x >= 0f ? 1f : -1f, 0f, 0f);
                foreach (Vector3 forward in new[] { major, minor })
                {
                    Quaternion yaw = Quaternion.LookRotation(forward);
                    if (!FitsJeepney(block, baseline, floors, footprint, position, yaw, InFootprint, InRear, keepClear, entrance))
                        continue;
                    float score = Vector3.Distance(position, entrance) + (forward == major ? 0.25f : 0f);
                    candidates.Add((position, yaw, score));
                }
            }
        }
        candidates.Sort((a, b) => b.score.CompareTo(a.score));

        const int maxVerified = 12;
        jeepney.transform.SetParent(block.transform, false);
        jeepney.SetActive(false);
        DestroyImmediate(holder);
        for (int c = 0; c < candidates.Count && c < maxVerified; c++)
        {
            (Vector3 position, Quaternion yaw, float _) = candidates[c];
            baseline.TryCell(position, out Vector2Int cell);
            float height = baseline.Heights[cell.x, cell.y];
            jeepney.transform.localPosition = new Vector3(position.x, height - footprint.min.y, position.z);
            jeepney.transform.localRotation = yaw;
            jeepney.SetActive(true);
            Physics.SyncTransforms();

            SpawnGrid after = BuildSpawnGrid(block);
            bool rearReachable = after.TryCell(RearSpot(position, yaw), out Vector2Int rear) && after.Reachable[rear.x, rear.y];
            bool pointsFit = !generateSpawnPoints || FindSpawnPoints(block, after).Count >= Mathf.Min(spawnPointCount, baselinePoints);
            if (after.SeededFromEntrance && rearReachable && pointsFit)
            {
                jeepney.name = $"{jeepneyPrefab.name} (Generated)";
                return jeepney;
            }
            jeepney.SetActive(false);
        }
        DestroyImmediate(jeepney);
        Physics.SyncTransforms();
        return null;
    }

    private bool FitsJeepney(StreetBlock block, SpawnGrid grid, List<Transform> floors, Bounds footprint, Vector3 position, Quaternion yaw,
        Func<Vector3, Vector3, Quaternion, float, bool> inFootprint, Func<Vector3, Vector3, Quaternion, bool> inRear,
        List<Vector3> keepClear, Vector3 entrance)
    {
        // Inside the block, clear of the entrance opening and of points that must stay free.
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 local = new Vector3(corner % 2 == 0 ? footprint.min.x : footprint.max.x, 0f, corner < 2 ? footprint.min.z : footprint.max.z);
            Vector3 point = position + yaw * local;
            if (Mathf.Abs(point.x) > grid.Half.x || Mathf.Abs(point.z) > grid.Half.y)
                return false;
        }
        if (inFootprint(entrance, position, yaw, 2f))
            return false;
        foreach (Vector3 point in keepClear)
        {
            if (inFootprint(new Vector3(point.x, 0f, point.z), position, yaw, spawnClearanceRadius))
                return false;
        }

        // Its footprint lies on reachable floor and the floor behind its back stays reachable.
        int covered = 0;
        int rearCells = 0;
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        for (int x = 0; x < grid.CountX; x++)
        {
            for (int z = 0; z < grid.CountZ; z++)
            {
                Vector3 local = grid.LocalAt(x, z);
                if (inFootprint(local, position, yaw, 0f))
                {
                    if (!grid.Reachable[x, z])
                        return false;
                    covered++;
                    minHeight = Mathf.Min(minHeight, grid.Heights[x, z]);
                    maxHeight = Mathf.Max(maxHeight, grid.Heights[x, z]);
                }
                else if (inRear(local, position, yaw))
                {
                    if (!grid.Reachable[x, z])
                        return false;
                    rearCells++;
                }
            }
        }
        if (covered == 0 || rearCells == 0 || maxHeight - minHeight > 0.3f)
            return false;

        // Nothing solid (e.g. thin props the player grid slips past) inside the body.
        grid.TryCell(position, out Vector2Int cell);
        Vector3 center = position + yaw * footprint.center;
        center.y = grid.Heights[cell.x, cell.y] + footprint.extents.y + 0.1f;
        Vector3 half = new Vector3(footprint.extents.x, Mathf.Max(0.05f, footprint.extents.y - 0.05f), footprint.extents.z);
        foreach (Collider hit in Physics.OverlapBox(block.transform.TransformPoint(center), half, block.transform.rotation * yaw, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsGeneratedSolid(hit, floors))
                return false;
        }
        return true;
    }

    private void FindNeighbours(IReadOnlyList<MapBlockPlacement> layout)
    {
        var footprints = new Rect[layout.Count];
        for (int i = 0; i < layout.Count; i++)
        {
            Vector2 size = layout[i].Prefab.Size;
            if (layout[i].QuarterTurns % 2 != 0)
                size = new Vector2(size.y, size.x);
            footprints[i] = new Rect(new Vector2(layout[i].Position.x, layout[i].Position.z) - size * 0.5f, size);
        }

        for (int i = 0; i < layout.Count; i++)
        {
            var list = new List<int>();
            for (int j = 0; j < layout.Count; j++)
            {
                if (i != j && Touch(footprints[i], footprints[j]))
                    list.Add(j);
            }
            neighbours.Add(list);
        }
    }

    private static bool Touch(Rect a, Rect b)
    {
        const float epsilon = 0.01f;
        float overlapX = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float overlapY = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        // Edge contact: touching on one axis while sharing a real length along the other.
        return (Mathf.Abs(overlapX) <= epsilon && overlapY > epsilon) || (Mathf.Abs(overlapY) <= epsilon && overlapX > epsilon);
    }

    private Rect WorldFootprint(int index)
    {
        StreetBlock block = placedBlocks[index];
        Vector2 size = block.Size;
        if (Mathf.Abs(Vector3.Dot(block.transform.right, Vector3.right)) < 0.5f)
            size = new Vector2(size.y, size.x);
        Vector3 center = block.transform.position;
        return new Rect(new Vector2(center.x, center.z) - size * 0.5f, size);
    }

    /// <summary>
    /// Removes the tagged edge walls (<see cref="StreetEdgeWall"/>) of every pair of touching town blocks along the stretch
    /// they share, so the town reads as one open area. Walls facing non-town blocks or the map's outside stay.
    /// </summary>
    private void OpenTownSeams(IReadOnlyList<MapBlockPlacement> layout)
    {
        const float epsilon = 0.05f;
        openSeams.Clear();
        for (int a = 0; a < placedBlocks.Count; a++)
        {
            if (!IsTown(a))
                continue;
            foreach (int b in neighbours[a])
            {
                if (b <= a || !IsTown(b))
                    continue;
                Rect ra = WorldFootprint(a);
                Rect rb = WorldFootprint(b);
                Vector3 outwardA;
                float line;
                float min;
                float max;
                bool alongZ;
                if (Mathf.Abs(ra.xMax - rb.xMin) <= epsilon || Mathf.Abs(rb.xMax - ra.xMin) <= epsilon)
                {
                    bool right = Mathf.Abs(ra.xMax - rb.xMin) <= epsilon;
                    outwardA = right ? Vector3.right : Vector3.left;
                    line = right ? ra.xMax : ra.xMin;
                    min = Mathf.Max(ra.yMin, rb.yMin);
                    max = Mathf.Min(ra.yMax, rb.yMax);
                    alongZ = true;
                }
                else
                {
                    bool up = Mathf.Abs(ra.yMax - rb.yMin) <= epsilon;
                    outwardA = up ? Vector3.forward : Vector3.back;
                    line = up ? ra.yMax : ra.yMin;
                    min = Mathf.Max(ra.xMin, rb.xMin);
                    max = Mathf.Min(ra.xMax, rb.xMax);
                    alongZ = false;
                }
                if (max - min <= epsilon)
                    continue;

                RemoveEdgeWalls(placedBlocks[a], outwardA, alongZ, min, max);
                RemoveEdgeWalls(placedBlocks[b], -outwardA, alongZ, min, max);
                float y = Mathf.Max(placedBlocks[a].transform.position.y, placedBlocks[b].transform.position.y);
                openSeams.Add(alongZ
                    ? new[] { new Vector3(line, y, min), new Vector3(line, y, max) }
                    : new[] { new Vector3(min, y, line), new Vector3(max, y, line) });
            }
        }
    }

    private static void RemoveEdgeWalls(StreetBlock block, Vector3 worldOutward, bool alongZ, float min, float max)
    {
        foreach (StreetEdgeWall wall in block.GetComponentsInChildren<StreetEdgeWall>(true))
        {
            Vector3 outward = block.transform.TransformDirection(wall.Side.Outward());
            if (Vector3.Dot(outward, worldOutward) < 0.9f)
                continue;
            Vector3 center = WorldCenter(wall.gameObject);
            float along = alongZ ? center.z : center.x;
            if (along >= min - 0.01f && along <= max + 0.01f)
                DestroyImmediate(wall.gameObject);
        }
    }

    private static Vector3 WorldCenter(GameObject target)
    {
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) bounds = renderer.bounds; else bounds.Encapsulate(renderer.bounds);
            any = true;
        }
        if (!any)
        {
            foreach (BoxCollider box in target.GetComponentsInChildren<BoxCollider>(true))
            {
                Vector3 center = box.transform.TransformPoint(box.center);
                if (!any) bounds = new Bounds(center, Vector3.zero); else bounds.Encapsulate(center);
                any = true;
            }
        }
        return any ? bounds.center : target.transform.position;
    }

    /// <summary>
    /// Floor points (0.5 m grid, at floor height) inside block <paramref name="blockIndex"/> where a player-sized capsule
    /// stands free and that a player can walk to from the spawn block. Entrance obstacles are ignored (they open later),
    /// so blocks sealed behind gates still count, but gaps behind or between buildings that nobody fits through do not.
    /// Computed once per build; empty if there is no spawn block or floor.
    /// </summary>
    public IReadOnlyList<Vector3> GetReachableFloor(int blockIndex)
    {
        if (blockIndex < 0 || blockIndex >= placedBlocks.Count)
            return Array.Empty<Vector3>();
        if (reachableFloor == null || reachableFloor.Length != placedBlocks.Count)
            reachableFloor = BuildReachableFloor();
        return reachableFloor[blockIndex];
    }

    private List<Vector3>[] BuildReachableFloor()
    {
        const float step = 0.5f;
        const float lift = 0.05f;
        var result = new List<Vector3>[placedBlocks.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = new List<Vector3>();
        if (placedBlocks.Count == 0 || generatedRoot == null)
            return result;

        Physics.SyncTransforms();
        Rect area = WorldFootprint(0);
        for (int i = 1; i < placedBlocks.Count; i++)
        {
            Rect r = WorldFootprint(i);
            area = Rect.MinMaxRect(Mathf.Min(area.xMin, r.xMin), Mathf.Min(area.yMin, r.yMin), Mathf.Max(area.xMax, r.xMax), Mathf.Max(area.yMax, r.yMax));
        }
        int countX = Mathf.FloorToInt(area.width / step);
        int countZ = Mathf.FloorToInt(area.height / step);
        var blockOf = new int[countX, countZ];
        var heights = new float[countX, countZ];
        List<Transform> floors = PlacedFloors();
        var overlaps = new Collider[32];
        Vector3 At(int x, int z) => new Vector3(area.xMin + (x + 0.5f) * step, 0f, area.yMin + (z + 0.5f) * step);

        for (int x = 0; x < countX; x++)
        {
            for (int z = 0; z < countZ; z++)
            {
                blockOf[x, z] = -1;
                Vector3 world = At(x, z);
                if (!TryGetBlockIndex(world, out int block))
                    continue;
                world.y = placedBlocks[block].transform.position.y;
                bool grounded = false;
                float groundY = world.y;
                foreach (RaycastHit hit in Physics.RaycastAll(world + Vector3.up * 1.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    bool onFloor = false;
                    foreach (Transform floor in floors)
                        onFloor |= hit.collider.transform.IsChildOf(floor);
                    if (onFloor && (!grounded || hit.point.y > groundY))
                    {
                        groundY = hit.point.y;
                        grounded = true;
                    }
                }
                if (!grounded)
                    continue;
                Vector3 bottom = new Vector3(world.x, groundY + spawnClearanceRadius + lift, world.z);
                Vector3 top = new Vector3(world.x, groundY + Mathf.Max(spawnClearanceHeight - spawnClearanceRadius, spawnClearanceRadius + lift), world.z);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, spawnClearanceRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int c = 0; c < count && !blocked; c++)
                    blocked = IsGeneratedSolid(overlaps[c], floors) && overlaps[c].GetComponentInParent<EntranceObstacle>(true) == null;
                if (blocked)
                    continue;
                blockOf[x, z] = block;
                heights[x, z] = groundY;
            }
        }

        // Flood from the spawn block's entrances (else its nearest free cell to the centre).
        var reached = new bool[countX, countZ];
        var queue = new Queue<Vector2Int>();
        for (int i = 0; i < placedBlocks.Count; i++)
        {
            if (GetRole(i) != MapBlockRole.Spawn || placedBlocks[i] == null)
                continue;
            var seeds = new List<Vector3>();
            foreach (StreetBlockEntrance entrance in placedBlocks[i].Entrances)
                seeds.Add(placedBlocks[i].transform.TransformPoint(entrance.LocalCenter));
            float best = float.MaxValue;
            Vector2Int nearest = new Vector2Int(-1, -1);
            for (int x = 0; x < countX; x++)
            {
                for (int z = 0; z < countZ; z++)
                {
                    if (blockOf[x, z] < 0)
                        continue;
                    Vector3 cell = At(x, z);
                    foreach (Vector3 seed in seeds)
                    {
                        if (new Vector2(cell.x - seed.x, cell.z - seed.z).magnitude <= 1.5f && !reached[x, z])
                        {
                            reached[x, z] = true;
                            queue.Enqueue(new Vector2Int(x, z));
                        }
                    }
                    if (blockOf[x, z] == i)
                    {
                        Vector3 offset = cell - placedBlocks[i].transform.position;
                        float distance = new Vector2(offset.x, offset.z).magnitude;
                        if (distance < best)
                        {
                            best = distance;
                            nearest = new Vector2Int(x, z);
                        }
                    }
                }
            }
            if (nearest.x >= 0 && !reached[nearest.x, nearest.y])
            {
                reached[nearest.x, nearest.y] = true;
                queue.Enqueue(nearest);
            }
        }
        var directions = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;
                if (next.x < 0 || next.y < 0 || next.x >= countX || next.y >= countZ || blockOf[next.x, next.y] < 0 || reached[next.x, next.y] ||
                    Mathf.Abs(heights[next.x, next.y] - heights[current.x, current.y]) > 0.6f)
                    continue;
                reached[next.x, next.y] = true;
                queue.Enqueue(next);
            }
        }
        for (int x = 0; x < countX; x++)
        {
            for (int z = 0; z < countZ; z++)
            {
                if (reached[x, z])
                {
                    Vector3 cell = At(x, z);
                    result[blockOf[x, z]].Add(new Vector3(cell.x, heights[x, z], cell.z));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Bakes one NavMesh over every block's floor (so neighbouring blocks are joined), then cuts buildings, walls,
    /// decorations and entrance obstacles out of it with carving NavMeshObstacles.
    /// </summary>
    private void BuildNavigation()
    {
        var sources = new List<NavMeshBuildSource>();
        var blockSources = new List<NavMeshBuildSource>();
        var markups = new List<NavMeshBuildMarkup>();
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (StreetBlock block in placedBlocks)
        {
            Transform floor = block.FloorRoot;
            if (floor == null)
            {
                Debug.LogWarning($"Street block '{block.name}' has no floor (Generated/Floor or Floor Root); it gets no NavMesh.", block);
                continue;
            }

            blockSources.Clear();
            NavMeshBuilder.CollectSources(floor, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, blockSources);
            if (blockSources.Count == 0)
                NavMeshBuilder.CollectSources(floor, ~0, NavMeshCollectGeometry.RenderMeshes, 0, markups, blockSources);
            sources.AddRange(blockSources);

            Vector2 size = block.Size;
            var blockBounds = new Bounds(block.transform.position,
                block.transform.rotation * new Vector3(size.x, 0f, size.y));
            blockBounds.size = new Vector3(Mathf.Abs(blockBounds.size.x), 0f, Mathf.Abs(blockBounds.size.z));
            if (hasBounds)
                bounds.Encapsulate(blockBounds);
            else
                bounds = blockBounds;
            hasBounds = true;

            if (carveObstacles)
                AddCarvingObstacles(block, floor);
        }

        if (sources.Count == 0)
        {
            Debug.LogWarning("No floor geometry was found for the NavMesh; enemies cannot move on this map.", this);
            return;
        }

        bounds.Expand(new Vector3(2f, 20f, 2f));
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(navMeshAgentTypeId);
        if (bakeAgentRadius > 0f)
        {
            // Keep about three voxels across the radius so the smaller clearance is still resolved accurately.
            float voxel = Mathf.Min(settings.agentRadius / 3f, bakeAgentRadius / 3f);
            settings.agentRadius = bakeAgentRadius;
            settings.overrideVoxelSize = true;
            settings.voxelSize = voxel;
        }
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data == null)
        {
            Debug.LogError($"Baking the street NavMesh failed. Check that agent type {navMeshAgentTypeId} exists.", this);
            return;
        }
        data.name = "Street NavMesh";
        navMeshInstance = NavMesh.AddNavMeshData(data);
        navMeshInstance.owner = this;
    }

    /// <summary>Radius the street NavMesh is baked (and carved) with.</summary>
    public float BakeAgentRadius => bakeAgentRadius > 0f ? bakeAgentRadius : NavMesh.GetSettingsByID(navMeshAgentTypeId).agentRadius;
    public float DecorationCarveScale => decorationCarveScale;

    private void AddCarvingObstacles(StreetBlock block, Transform floor)
    {
        AddCarvingObstacles(block.transform, floor, block.DecorationsRoot);
    }

    internal void AddClearedObstacleCarvers(Transform root)
    {
        if (carveObstacles)
            AddCarvingObstacles(root, null, null);
    }

    private void AddCarvingObstacles(Transform root, Transform floor, Transform decorations)
    {
        var customCarved = new HashSet<NavMeshCarveOverride>();
        foreach (Collider solid in root.GetComponentsInChildren<Collider>(true))
        {
            if (solid.isTrigger || (floor != null && solid.transform.IsChildOf(floor)) || solid.GetComponent<NavMeshObstacle>() != null)
                continue;

            NavMeshCarveOverride custom = solid.GetComponentInParent<NavMeshCarveOverride>(true);
            if (custom != null && custom.Mode == NavMeshCarveMode.None)
                continue;
            if (custom != null && custom.Mode == NavMeshCarveMode.Custom)
            {
                if (customCarved.Add(custom))
                    AddCarver(custom.transform, custom.Center, custom.BoxSize, solid.enabled);
                continue;
            }

            if (!TryGetLocalBox(solid, out Vector3 center, out Vector3 size))
                continue;
            if (decorations != null && solid.transform.IsChildOf(decorations))
                size = ScaleHorizontally(solid.transform, size, decorationCarveScale);
            AddCarver(solid.transform, center, size, solid.enabled);
        }
    }

    private static void AddCarver(Transform parent, Vector3 center, Vector3 size, bool enabled)
    {
        var carver = new GameObject("NavMesh Obstacle").AddComponent<NavMeshObstacle>();
        carver.gameObject.layer = parent.gameObject.layer;
        carver.transform.SetParent(parent, false);
        carver.shape = NavMeshObstacleShape.Box;
        carver.center = center;
        carver.size = size;
        carver.carving = true;
        carver.carveOnlyStationary = true;
        carver.carvingTimeToStationary = 0f;
        carver.enabled = enabled;
    }

    /// <summary>Scales the local box axes that lie (mostly) horizontal in the world, leaving the vertical one.</summary>
    private static Vector3 ScaleHorizontally(Transform space, Vector3 size, float scale)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 direction = space.rotation * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
            if (Mathf.Abs(direction.y) < 0.7f)
                size[axis] = Mathf.Max(0.01f, size[axis] * scale);
        }
        return size;
    }

    private static bool TryGetLocalBox(Collider solid, out Vector3 center, out Vector3 size)
    {
        switch (solid)
        {
            case BoxCollider box:
                center = box.center;
                size = box.size;
                return true;
            case SphereCollider sphere:
                center = sphere.center;
                size = Vector3.one * sphere.radius * 2f;
                return true;
            case CapsuleCollider capsule:
                center = capsule.center;
                size = Vector3.one * capsule.radius * 2f;
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                return true;
            case MeshCollider mesh when mesh.sharedMesh != null:
                center = mesh.sharedMesh.bounds.center;
                size = mesh.sharedMesh.bounds.size;
                return true;
            default:
                center = size = default;
                return false;
        }
    }

    public void Clear()
    {
        Transform existing = generatedRoot != null ? generatedRoot : transform.Find(GeneratedRootName);
        if (existing != null)
        {
            if (Application.isPlaying)
            {
                // Deactivate first so spawn points unregister immediately rather than at end of frame.
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }

        generatedRoot = null;
        PlacedJeepney = null;
        placedBlocks.Clear();
        placedFillers.Clear();
        pluggedCells.Clear();
        PluggedCellCount = 0;
        jointBarricades.Clear();
        openSeams.Clear();
        reachableFloor = null;
        IsTownLayout = false;
        neighbours.Clear();
        joints.Clear();
        if (navMeshInstance.valid)
            NavMesh.RemoveNavMeshData(navMeshInstance);
        navMeshInstance = default;
        Layout = Array.Empty<MapBlockPlacement>();
        IsBuilt = false;
        BuildFailed = false;
    }

    [ContextMenu("Preview With Random Seed")]
    private void PreviewRandom() => Build(Environment.TickCount);

    [ContextMenu("Clear Preview")]
    private void ClearPreview() => Clear();
}
