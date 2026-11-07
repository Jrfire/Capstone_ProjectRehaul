using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-only: once the scene's <see cref="MapManager"/> has built the street (and the scene's EnemySpawner has placed
/// its enemies), scatters <see cref="minCount"/>-<see cref="maxCount"/> networked <see cref="AgimatPickup"/>s around the
/// map for players to hunt down. Each enemy home block (e.g. the Tikbalang's) gets one, sealed (barricaded) end caps are
/// filled next, and the rest go to blocks far from the spawn block. Inside a block the agimat is tucked away from the
/// block centre and its entrances. Put it next to the MapManager in every playable map scene.
/// </summary>
public sealed class AgimatSpawner : MonoBehaviour
{
    [SerializeField, Tooltip("Defaults to the scene's active MapManager.")]
    private MapManager map;
    [SerializeField, Tooltip("Networked agimat pickup prefab (must be in the NetworkManager's Registered Spawnable Prefabs).")]
    private AgimatPickup pickupPrefab;
    [SerializeField, Min(0)] private int minCount = 3;
    [SerializeField, Min(0)] private int maxCount = 5;
    [SerializeField, Min(0f), Tooltip("Relative chance of each type.")]
    private float goldWeight = 1f;
    [SerializeField, Min(0f)] private float silverWeight = 2f;
    [SerializeField, Min(0f)] private float bronzeWeight = 3f;
    [SerializeField, Tooltip("Give every enemy's home block (e.g. the Tikbalang's territory) one agimat.")]
    private bool placeInEnemyHomes = true;
    [SerializeField, Min(0), Tooltip("At least this many agimats (when that many spawn) go in blocks players can walk to from spawn without opening any barricade; only the rest may be locked in sealed end caps.")]
    private int minReachableCount = 2;
    [SerializeField, Min(0f), Tooltip("Height of the agimat's pivot above the floor.")]
    private float heightAboveFloor = 0.35f;
    [SerializeField, Min(0f), Tooltip("Agimats keep at least this far from each other.")]
    private float minSpacing = 3f;
    [SerializeField, Min(0f), Tooltip("Agimats keep at least this far from block entrances, so they are not seen from the street.")]
    private float entranceClearance = 3f;
    [SerializeField, Min(0), Tooltip("Frames to wait after the map and enemies are ready so NavMesh carving is applied.")]
    private int settleFrames = 3;

    private readonly List<AgimatPickup> spawned = new List<AgimatPickup>();
    private bool hasSpawned;

    public IReadOnlyList<AgimatPickup> Spawned => spawned;
    public bool HasSpawned => hasSpawned;
    public AgimatPickup PickupPrefab => pickupPrefab;
    public int MinCount => minCount;
    public int MaxCount => maxCount;
    public int MinReachableCount => minReachableCount;

    private void OnEnable() => StartCoroutine(SpawnWhenReady());

    private IEnumerator SpawnWhenReady()
    {
        while (map == null)
        {
            map = MapManager.Active;
            if (map == null)
                yield return null;
        }
        while (!map.IsDone)
            yield return null;
        if (!map.IsBuilt)
            yield break;
        // Enemy homes are known only after the scene's enemies spawned.
        EnemySpawner enemies = FindAnyObjectByType<EnemySpawner>();
        float waitUntil = Time.realtimeSinceStartup + 5f;
        while (NetworkServer.active && enemies != null && enemies.isActiveAndEnabled && !enemies.HasSpawned &&
               Time.realtimeSinceStartup < waitUntil)
            yield return null;
        for (int i = 0; i < settleFrames; i++)
            yield return null;
        if (NetworkServer.active && !hasSpawned)
            SpawnAll();
    }

    /// <summary>Server: places the agimats now (normally automatic once the map is ready).</summary>
    [Server]
    public void SpawnAll()
    {
        hasSpawned = true;
        if (pickupPrefab == null)
        {
            Debug.LogError("AgimatSpawner has no Pickup Prefab.", this);
            return;
        }
        if (map == null)
            map = MapManager.Active;
        if (map == null || !map.IsBuilt)
            return;

        var homes = new List<int>();
        if (placeInEnemyHomes)
        {
            foreach (EnemyBrain brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain != null && !brain.RoamsWholeMap && brain.HomeBlockIndex >= 0 && !homes.Contains(brain.HomeBlockIndex))
                    homes.Add(brain.HomeBlockIndex);
            }
        }

        int count = Random.Range(Mathf.Min(minCount, maxCount), Mathf.Max(minCount, maxCount) + 1);
        var used = new List<Vector3>();
        foreach (int block in PlanBlocks(map, homes, count, new System.Random(Random.Range(int.MinValue, int.MaxValue)), minReachableCount))
        {
            if (!TryFindSpot(block, used, out Vector3 position))
                continue;
            used.Add(position);
            AgimatPickup pickup = Instantiate(pickupPrefab, position, Quaternion.identity);
            pickup.name = $"{pickupPrefab.name} (Block {block})";
            pickup.ServerSetType(RollType());
            NetworkServer.Spawn(pickup.gameObject);
            spawned.Add(pickup);
        }
    }

    /// <summary>
    /// Blocks receiving one agimat each (a block may repeat once every block has one): one per enemy home block, then
    /// sealed end caps (not openly reachable from spawn), then the openly reachable blocks farthest from the spawn block.
    /// At least <paramref name="minReachable"/> (capped at <paramref name="count"/>) land in openly reachable blocks, so
    /// some can always be collected without opening barricades. The spawn block and its neighbours are skipped while
    /// other blocks exist.
    /// </summary>
    public static List<int> PlanBlocks(MapManager map, IReadOnlyList<int> homeBlocks, int count, System.Random random, int minReachable = 2)
    {
        var result = new List<int>();
        if (map == null || map.BlockCount == 0 || count <= 0)
            return result;

        int spawn = -1;
        for (int i = 0; i < map.BlockCount && spawn < 0; i++)
        {
            if (map.GetRole(i) == MapBlockRole.Spawn)
                spawn = i;
        }
        bool[] open = spawn >= 0 ? map.FindOpenlyReachableFromSpawn() : null;
        var eligible = new List<int>();
        for (int i = 0; i < map.BlockCount; i++)
        {
            if (i != spawn && (spawn < 0 || !map.GetNeighbours(spawn).Contains(i)))
                eligible.Add(i);
        }
        if (eligible.Count == 0)
        {
            for (int i = 0; i < map.BlockCount; i++)
            {
                if (i != spawn)
                    eligible.Add(i);
            }
        }
        if (eligible.Count == 0)
            eligible.Add(spawn);

        var homes = new List<int>();
        if (homeBlocks != null)
        {
            foreach (int home in homeBlocks)
            {
                if (eligible.Contains(home) && !homes.Contains(home))
                    homes.Add(home);
            }
        }
        Shuffle(homes, random);
        var sealedCaps = new List<int>();
        foreach (int block in eligible)
        {
            if (open != null && !open[block] && map.GetRole(block) == MapBlockRole.EndCap && !homes.Contains(block))
                sealedCaps.Add(block);
        }
        Shuffle(sealedCaps, random);

        bool IsOpen(int block) => open == null || open[block];
        var reachable = eligible.Where(IsOpen).ToList();
        // Only what is left after the guaranteed reachable agimats may be locked away.
        int sealedBudget = reachable.Count > 0 ? Mathf.Max(0, count - Mathf.Clamp(minReachable, 0, count)) : count;
        int sealedUsed = 0;

        // Homes first (leaving room for at least one sealed end cap when there is one), then sealed end caps.
        int homeBudget = sealedCaps.Count > 0 && sealedBudget > 0 ? Mathf.Max(1, count - 1) : count;
        for (int i = 0; i < homes.Count && result.Count < homeBudget; i++)
        {
            if (!IsOpen(homes[i]))
            {
                if (sealedUsed >= sealedBudget)
                    continue;
                sealedUsed++;
            }
            result.Add(homes[i]);
        }
        for (int i = 0; i < sealedCaps.Count && result.Count < count && sealedUsed < sealedBudget; i++)
        {
            result.Add(sealedCaps[i]);
            sealedUsed++;
        }

        Vector3 spawnPosition = spawn >= 0 && map.GetBlock(spawn) != null ? map.GetBlock(spawn).transform.position : Vector3.zero;
        List<int> fill = reachable.Count > 0 ? reachable : eligible;
        while (result.Count < count)
        {
            // The farthest reachable blocks without an agimat yet (a little random among the farthest three).
            var pool = new List<int>();
            foreach (int block in fill)
            {
                if (!result.Contains(block))
                    pool.Add(block);
            }
            if (pool.Count == 0)
                pool.AddRange(fill);
            pool.Sort((a, b) => Distance(map, b, spawnPosition).CompareTo(Distance(map, a, spawnPosition)));
            result.Add(pool[random.Next(Mathf.Min(3, pool.Count))]);
        }
        return result;
    }

    private static float Distance(MapManager map, int block, Vector3 from)
    {
        StreetBlock street = map.GetBlock(block);
        if (street == null)
            return 0f;
        Vector3 offset = street.transform.position - from;
        offset.y = 0f;
        return offset.magnitude;
    }

    private static void Shuffle(List<int> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>A NavMesh spot in the block, as far as possible from its centre and entrances and from other agimats.</summary>
    private bool TryFindSpot(int blockIndex, List<Vector3> used, out Vector3 position)
    {
        StreetBlock block = map.GetBlock(blockIndex);
        position = default;
        if (block == null)
            return false;
        EnemyHomeArea area = EnemyHomeArea.FromBlock(block);
        var entrances = new List<Vector3>();
        foreach (StreetBlockEntrance entrance in block.Entrances)
            entrances.Add(block.transform.TransformPoint(entrance.LocalCenter));

        float bestScore = float.NegativeInfinity;
        bool found = false;
        // Prefer floor a player can actually walk to (the NavMesh also covers gaps behind buildings nobody fits through).
        IReadOnlyList<Vector3> reachable = map.GetReachableFloor(blockIndex);
        for (int attempt = 0; attempt < (reachable.Count > 0 ? 60 : 0); attempt++)
        {
            Vector3 point = reachable[Random.Range(0, reachable.Count)];
            if (!ScoreSpot(point, used, area, entrances, ref bestScore, ref position))
                continue;
            found = true;
        }
        for (int attempt = 0; !found && attempt < 40; attempt++)
        {
            if (!NavMesh.SamplePosition(area.RandomPoint(), out NavMeshHit hit, 2f, NavMesh.AllAreas) || !area.Contains(hit.position))
                continue;
            if (ScoreSpot(hit.position, used, area, entrances, ref bestScore, ref position))
                found = true;
        }
        if (!found && !TooClose(area.Center, used, minSpacing))
        {
            // No NavMesh (e.g. navigation disabled): fall back to the floor under the block centre.
            Vector3 top = area.Center + Vector3.up * 5f;
            position = Physics.Raycast(top, Vector3.down, out RaycastHit floor, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? floor.point + Vector3.up * heightAboveFloor
                : area.Center + Vector3.up * heightAboveFloor;
            found = true;
        }
        return found;
    }

    /// <summary>Scores a candidate floor point (far from the block centre and entrances); true if it became the best so far.</summary>
    private bool ScoreSpot(Vector3 point, List<Vector3> used, EnemyHomeArea area, List<Vector3> entrances, ref float bestScore, ref Vector3 position)
    {
        if (TooClose(point, used, minSpacing))
            return false;
        Vector3 lifted = point + Vector3.up * heightAboveFloor;
        if (Physics.CheckSphere(lifted, 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        float score = HorizontalDistance(point, area.Center);
        float nearestEntrance = float.PositiveInfinity;
        foreach (Vector3 entrance in entrances)
            nearestEntrance = Mathf.Min(nearestEntrance, HorizontalDistance(point, entrance));
        if (nearestEntrance < entranceClearance)
            score -= 100f;
        if (score <= bestScore)
            return false;
        bestScore = score;
        position = lifted;
        return true;
    }

    private static bool TooClose(Vector3 point, List<Vector3> used, float spacing)
    {
        foreach (Vector3 other in used)
        {
            if (HorizontalDistance(point, other) < spacing)
                return true;
        }
        return false;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private AgimatType RollType()
    {
        float total = goldWeight + silverWeight + bronzeWeight;
        if (total <= 0f)
            return AgimatType.Bronze;
        float pick = Random.value * total;
        if (pick < goldWeight)
            return AgimatType.Gold;
        return pick < goldWeight + silverWeight ? AgimatType.Silver : AgimatType.Bronze;
    }
}
