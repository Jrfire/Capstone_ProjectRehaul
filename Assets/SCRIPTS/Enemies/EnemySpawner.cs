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
    [SerializeField, Min(0), Tooltip("Base number of each rolled enemy type (before the per-quota extras below).")]
    private int rolledEnemyCount = 1;
    [Header("More enemies per quota")]
    [SerializeField, Min(1), Tooltip("Quotas met per extra possible enemy of each rolled type (2 = +1 every 2 quotas).")]
    private int quotasPerExtraEnemy = 2;
    [SerializeField, Min(1), Tooltip("Most enemies of one rolled type.")]
    private int maxEnemiesPerType = 4;
    [SerializeField, Range(0f, 1f), Tooltip("Chance of each possible extra enemy on a small map (Small Map Blocks or fewer).")]
    private float smallMapExtraChance = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Chance of each possible extra enemy on a large map (Large Map Blocks or more).")]
    private float largeMapExtraChance = 0.8f;
    [SerializeField, Min(1)] private int smallMapBlocks = 5;
    [SerializeField, Min(1)] private int largeMapBlocks = 15;
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

    /// <summary>Most enemies of one rolled type in <paramref name="round"/>: base, +1 every Quotas Per Extra Enemy quotas met, capped.</summary>
    public int MaxCountForRound(int round) =>
        Mathf.Clamp(rolledEnemyCount + Mathf.Max(0, round - 1) / Mathf.Max(1, quotasPerExtraEnemy), Mathf.Min(rolledEnemyCount, maxEnemiesPerType), maxEnemiesPerType);

    /// <summary>Chance of each possible extra enemy; grows from the small-map to the large-map chance with the block count.</summary>
    public float ExtraChanceForBlocks(int blockCount) =>
        Mathf.Lerp(smallMapExtraChance, largeMapExtraChance, Mathf.InverseLerp(smallMapBlocks, largeMapBlocks, blockCount));

    /// <summary>
    /// Rolls how many of one rolled type spawn: the base count, plus each possible extra (up to
    /// <see cref="MaxCountForRound"/>) with <see cref="ExtraChanceForBlocks"/>.
    /// </summary>
    public int RollCount(int round, int blockCount, Func<float> random01 = null)
    {
        Func<float> random = random01 ?? (() => UnityEngine.Random.value);
        int max = MaxCountForRound(round);
        int count = Mathf.Min(rolledEnemyCount, max);
        float chance = ExtraChanceForBlocks(blockCount);
        for (int extra = count; extra < max; extra++)
        {
            if (random() < chance)
                count++;
        }
        return count;
    }

    /// <summary>The rolled enemy types from the trip into this scene (Rolled Enemy Count of each), otherwise the fallback list.</summary>
    public List<EnemySpawnEntry> ResolveEntries()
    {
        TravelDirector travel = TravelDirector.Instance;
        if (travel == null || !travel.HasArrivalRoll)
            return enemies;
        var rolled = new List<EnemySpawnEntry>();
        int blockCount = map != null ? map.BlockCount : 0;
        foreach (EnemyDefinition enemy in travel.ArrivalEnemies)
        {
            if (enemy != null && enemy.prefab != null)
                rolled.Add(new EnemySpawnEntry { prefab = enemy.prefab, count = RollCount(CurrentRound, blockCount) });
            else if (enemy != null)
                Debug.LogError($"Enemy definition '{enemy.name}' has no prefab.", enemy);
        }
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
