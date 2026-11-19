using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

[Serializable]
public sealed class EnemySpawnEntry
{
    [Tooltip("Networked enemy prefab (must be in the NetworkManager's Registered Spawnable Prefabs).")]
    public NetworkIdentity prefab;
    [Min(0)] public int count = 1;
}

/// <summary>
/// Server-only: after the scene's <see cref="MapManager"/> builds the street and its NavMesh, spawns enemies in random
/// blocks that are neither a player spawn block nor next to one, and makes that block each enemy's home. Enemies that
/// roam the whole map (<see cref="EnemyBrain.RoamsWholeMap"/>, e.g. the Manananggal) start there and move themselves.
/// </summary>
public sealed class EnemySpawner : MonoBehaviour
{
    [SerializeField, Tooltip("Defaults to the scene's active MapManager.")]
    private MapManager map;
    [SerializeField, Tooltip("Fallback enemies, used when this scene was not reached through a travel roll (e.g. opened directly in the Editor).")]
    private List<EnemySpawnEntry> enemies = new List<EnemySpawnEntry>();
    [SerializeField, Min(0), Tooltip("Number of each rolled enemy type, regardless of the map's size.")]
    private int rolledEnemyCount = 2;
    [Header("More enemies per quota")]
    [SerializeField, Min(1), Tooltip("Quotas completed per step of extra enemies (2 = every 2 quotas).")]
    private int quotasPerExtraEnemy = 2;
    [SerializeField, Min(0), Tooltip("Extra enemies added per step, each given to a random rolled type (same or different).")]
    private int extraEnemiesPerStep = 2;
    [SerializeField, Min(0), Tooltip("Optional safety limit on enemies of one rolled type. 0 = no limit (extras keep growing every step).")]
    private int maxEnemiesPerType;
    [SerializeField, Min(0), Tooltip("Most rolled enemies on a map in total. 0 = no limit.")]
    private int maxTotalEnemies = 14;
    [SerializeField, Min(0), Tooltip("Frames to wait after the map is built so NavMesh carving is applied.")]
    private int settleFrames = 3;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private bool hasSpawned;

    public IReadOnlyList<GameObject> Spawned => spawned;
    /// <summary>True once the server has run <see cref="SpawnAll"/>.</summary>
    public bool HasSpawned => hasSpawned;
    public List<EnemySpawnEntry> Enemies => enemies;
    public int RolledEnemyCount => rolledEnemyCount;

    private static int CurrentRound => RoundClock.Instance != null && RoundClock.Instance.enabled ? RoundClock.Instance.Round : 1;

    /// <summary>Extra enemies in <paramref name="round"/>: Extra Enemies Per Step for every Quotas Per Extra Enemy quotas completed.</summary>
    public int ExtrasForRound(int round) => extraEnemiesPerStep * (Mathf.Max(0, round - 1) / Mathf.Max(1, quotasPerExtraEnemy));

    /// <summary>
    /// How many of each rolled type spawn: Rolled Enemy Count each, then <see cref="ExtrasForRound"/> extras handed to
    /// random types. Max Enemies Per Type / Max Total Enemies only apply when set above 0 (default: no limit).
    /// </summary>
    public int[] DistributeCounts(int typeCount, int round, Func<float> random01 = null)
    {
        var counts = new int[Mathf.Max(0, typeCount)];
        if (counts.Length == 0)
            return counts;
        Func<float> random = random01 ?? (() => UnityEngine.Random.value);
        int perType = maxEnemiesPerType > 0 ? maxEnemiesPerType : int.MaxValue;
        int maxTotal = maxTotalEnemies > 0 ? maxTotalEnemies : int.MaxValue;
        int total = 0;
        for (int i = 0; i < counts.Length && total < maxTotal; i++)
        {
            counts[i] = Mathf.Min(rolledEnemyCount, perType, maxTotal - total);
            total += counts[i];
        }
        int extras = ExtrasForRound(round);
        var open = new List<int>();
        for (int extra = 0; extra < extras && total < maxTotal; extra++)
        {
            open.Clear();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] < perType)
                    open.Add(i);
            }
            if (open.Count == 0)
                break;
            int pick = open[Mathf.Clamp((int)(random() * open.Count), 0, open.Count - 1)];
            counts[pick]++;
            total++;
        }
        return counts;
    }

    /// <summary>The rolled enemy types from the trip into this scene (see <see cref="DistributeCounts"/>), otherwise the fallback list.</summary>
    public List<EnemySpawnEntry> ResolveEntries()
    {
        TravelDirector travel = TravelDirector.Instance;
        if (travel == null || !travel.HasArrivalRoll)
            return enemies;
        var valid = new List<EnemyDefinition>();
        foreach (EnemyDefinition enemy in travel.ArrivalEnemies)
        {
            if (enemy != null && enemy.prefab != null)
                valid.Add(enemy);
            else if (enemy != null)
                Debug.LogError($"Enemy definition '{enemy.name}' has no prefab.", enemy);
        }
        int[] counts = DistributeCounts(valid.Count, CurrentRound);
        var rolled = new List<EnemySpawnEntry>();
        for (int i = 0; i < valid.Count; i++)
            rolled.Add(new EnemySpawnEntry { prefab = valid[i].prefab, count = counts[i] });
        return rolled;
    }

    private void OnEnable()
    {
        StartCoroutine(SpawnWhenReady());
    }

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
        for (int i = 0; i < settleFrames; i++)
            yield return null;
        if (NetworkServer.active && !hasSpawned)
            SpawnAll();
    }

    /// <summary>
    /// Blocks enemies may call home: not a spawn block, not adjacent to one, and reachable from the spawn block without
    /// passing an uncleared barricade (a gated end cap would trap the enemy).
    /// </summary>
    public static List<int> FindCandidateBlocks(MapManager map, out bool relaxed)
    {
        var spawnBlocks = new List<int>();
        for (int i = 0; i < map.BlockCount; i++)
        {
            if (map.GetRole(i) == MapBlockRole.Spawn)
                spawnBlocks.Add(i);
        }
        bool[] open = spawnBlocks.Count > 0 ? map.FindOpenlyReachableFromSpawn() : null;
        bool Reachable(int block) => open == null || open[block];

        var candidates = new List<int>();
        for (int i = 0; i < map.BlockCount; i++)
        {
            if (spawnBlocks.Contains(i) || !Reachable(i))
                continue;
            bool nearSpawn = false;
            foreach (int spawn in spawnBlocks)
                nearSpawn |= map.GetNeighbours(spawn).Contains(i);
            if (!nearSpawn)
                candidates.Add(i);
        }

        relaxed = candidates.Count == 0;
        if (relaxed)
        {
            for (int i = 0; i < map.BlockCount; i++)
            {
                if (!spawnBlocks.Contains(i) && Reachable(i))
                    candidates.Add(i);
            }
        }
        return candidates;
    }

    [Server]
    public void SpawnAll()
    {
        hasSpawned = true;
        List<int> candidates = FindCandidateBlocks(map, out bool relaxed);
        if (candidates.Count == 0)
        {
            Debug.LogError("No street block can host enemies (only spawn blocks or blocks sealed behind barricades).", this);
            return;
        }
        if (relaxed)
            Debug.LogWarning("Every block touches a player spawn block; enemies spawn next to the spawn block. Add more connector blocks.", this);

        var available = new List<int>();
        foreach (EnemySpawnEntry entry in ResolveEntries())
        {
            if (entry == null || entry.prefab == null)
                continue;
            for (int n = 0; n < entry.count; n++)
            {
                // Prefer a different block for every enemy until they run out.
                if (available.Count == 0)
                    available.AddRange(candidates);
                int pick = UnityEngine.Random.Range(0, available.Count);
                int block = available[pick];
                available.RemoveAt(pick);
                SpawnIn(entry.prefab, block);
            }
        }
    }

    private void SpawnIn(NetworkIdentity prefab, int blockIndex)
    {
        EnemyHomeArea area = EnemyHomeArea.FromBlock(map.GetBlock(blockIndex));
        Vector3 position = area.Center;
        bool roams = prefab.TryGetComponent(out EnemyBrain prefabBrain) && prefabBrain.RoamsWholeMap;
        // Roaming enemies (flyers) place themselves; only NavMesh walkers need a NavMesh point.
        for (int i = 0; !roams && i < 40; i++)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(area.RandomPoint(), out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas) &&
                area.Contains(hit.position))
            {
                position = hit.position;
                break;
            }
        }

        GameObject enemy = Instantiate(prefab.gameObject, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0, 4) * 90f, 0f));
        enemy.name = $"{prefab.name} (Block {blockIndex})";
        if (enemy.TryGetComponent(out EnemyBrain brain))
            brain.AssignHomeBlock(map, blockIndex);
        NetworkServer.Spawn(enemy);
        spawned.Add(enemy);
    }
}
