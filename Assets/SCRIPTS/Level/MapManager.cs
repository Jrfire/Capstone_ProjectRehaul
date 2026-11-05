using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
    [SerializeField, Min(1), Tooltip("Layouts retried (deterministically from the seed) before generation fails.")]
    private int maxAttempts = 100;
    [SerializeField, Tooltip("Build automatically when the scene starts.")]
    private bool buildOnStart = true;
    [SerializeField, Tooltip("Offline only: use Fixed Seed instead of a random one. Networked sessions always use the host's seed.")]
    private bool useFixedSeed;
    [SerializeField] private int fixedSeed;

    [SerializeField, Range(0f, 1f), Tooltip("Chance, each time a connector is added, to place one that joins two or more open entrances and so closes a loop. Loops give players routes around barricades.")]
    private float loopChance = 0.75f;

    [Header("Barricades")]
    [SerializeField, Tooltip("Keep entrance obstacles that seal off end cap blocks (the only places that may be fully blocked, e.g. for loot behind them).")]
    private bool barricadeEndCaps = true;
    [SerializeField, Tooltip("Keep other entrance obstacles only where the street still has a route around them; turn off to remove every non-end-cap obstacle.")]
    private bool barricadeLoops = true;

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
    private Transform generatedRoot;
    private readonly List<StreetBlock> placedBlocks = new List<StreetBlock>();
    private readonly List<List<int>> neighbours = new List<List<int>>();
    private readonly List<MapJoint> joints = new List<MapJoint>();
    private NavMeshDataInstance navMeshInstance;

    public static MapManager Active { get; private set; }

    /// <summary>When true (set by the networking layer), managers wait for <see cref="SetNetworkSeed"/> instead of rolling a seed.</summary>
    public static bool WaitForNetworkSeed { get; set; }

    public event Action<MapManager> Built;

    public IReadOnlyList<StreetBlock> ConnectorBlocks => connectorBlocks;
    public IReadOnlyList<StreetBlock> EndCapBlocks => endCapBlocks;
    public IReadOnlyList<StreetBlock> SpawnBlocks => spawnBlocks;
    public int ConnectorBlockCount => connectorBlockCount;
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

    public MapBlockRole GetRole(int index) => Layout[index].Role;

    /// <summary>Blocks whose footprints share an edge with block <paramref name="index"/>.</summary>
    public IReadOnlyList<int> GetNeighbours(int index) => neighbours[index];

    /// <summary>Every pair of joined entrances, and whether a barricade stands in it.</summary>
    public IReadOnlyList<MapJoint> Joints => joints;

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

    public static void SetNetworkSeed(string sceneName, int seed)
    {
        networkSeeds[sceneName] = seed;
        if (Active != null && !Active.IsDone && Active.buildOnStart && Active.gameObject.scene.name == sceneName)
            Active.Build(seed);
    }

    public static bool TryGetNetworkSeed(string sceneName, out int seed) => networkSeeds.TryGetValue(sceneName, out seed);

    public static void ClearNetworkSeeds() => networkSeeds.Clear();

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
            Build(seed);
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
        if (!MapLayoutPlanner.TryPlan(connectorBlocks, endCapBlocks, spawnBlocks, connectorBlockCount, seed, maxAttempts,
                out List<MapBlockPlacement> layout, out string error, loopChance))
        {
            Debug.LogError(error, this);
            BuildFailed = true;
            Built?.Invoke(this);
            return false;
        }

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
        ClearSpawnEntrances(layout);
        ResolveBarricades(layout);
        if (buildNavMesh && Application.isPlaying)
            BuildNavigation();
        IsBuilt = true;
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
        for (int j = 0; j < joints.Count; j++)
        {
            List<EntranceObstacle> obstacles = jointObstacles[j];
            if (obstacles.Count == 0)
                continue;
            bool endCap = layout[joints[j].BlockA].Role == MapBlockRole.EndCap || layout[joints[j].BlockB].Role == MapBlockRole.EndCap;
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
            }
            for (int k = 0; k < obstacles.Count; k++)
            {
                if (k != keepIndex)
                    RemoveObstacle(obstacles[k]);
            }
            joints[j] = new MapJoint(joints[j].BlockA, joints[j].EntranceA, joints[j].BlockB, joints[j].EntranceB, blocked[j]);
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
        Transform decorations = block.DecorationsRoot;
        var customCarved = new HashSet<NavMeshCarveOverride>();
        foreach (Collider solid in block.GetComponentsInChildren<Collider>(true))
        {
            if (solid.isTrigger || solid.transform.IsChildOf(floor) || solid.GetComponent<NavMeshObstacle>() != null)
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
        placedBlocks.Clear();
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
