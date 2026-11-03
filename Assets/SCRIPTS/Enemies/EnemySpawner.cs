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
/// blocks that are neither a player spawn block nor next to one, and makes that block each enemy's home.
/// </summary>
public sealed class EnemySpawner : MonoBehaviour
{
    [SerializeField, Tooltip("Defaults to the scene's active MapManager.")]
    private MapManager map;
    [SerializeField, Tooltip("Fallback enemies, used when this scene was not reached through a travel roll (e.g. opened directly in the Editor).")]
    private List<EnemySpawnEntry> enemies = new List<EnemySpawnEntry>();
    [SerializeField, Min(0), Tooltip("How many of the travel terminal's rolled enemy type to spawn.")]
    private int rolledEnemyCount = 1;
    [SerializeField, Min(0), Tooltip("Frames to wait after the map is built so NavMesh carving is applied.")]
    private int settleFrames = 3;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private bool hasSpawned;

    public IReadOnlyList<GameObject> Spawned => spawned;
    public List<EnemySpawnEntry> Enemies => enemies;
    public int RolledEnemyCount => rolledEnemyCount;

    /// <summary>The rolled enemy from the trip into this scene, otherwise the fallback list.</summary>
    public List<EnemySpawnEntry> ResolveEntries()
    {
        TravelDirector travel = TravelDirector.Instance;
        if (travel == null || !travel.HasArrivalRoll)
            return enemies;
        var rolled = new List<EnemySpawnEntry>();
        if (travel.ArrivalEnemy != null && travel.ArrivalEnemy.prefab != null)
            rolled.Add(new EnemySpawnEntry { prefab = travel.ArrivalEnemy.prefab, count = rolledEnemyCount });
        else if (travel.ArrivalEnemy != null)
            Debug.LogError($"Enemy definition '{travel.ArrivalEnemy.name}' has no prefab.", travel.ArrivalEnemy);
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

    /// <summary>Blocks enemies may call home: not a spawn block and not adjacent to one.</summary>
    public static List<int> FindCandidateBlocks(MapManager map, out bool relaxed)
    {
        var spawnBlocks = new List<int>();
        for (int i = 0; i < map.BlockCount; i++)
        {
            if (map.GetRole(i) == MapBlockRole.Spawn)
                spawnBlocks.Add(i);
        }

        var candidates = new List<int>();
        for (int i = 0; i < map.BlockCount; i++)
        {
            if (spawnBlocks.Contains(i))
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
                if (!spawnBlocks.Contains(i))
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
            Debug.LogError("No street block can host enemies (the map only has spawn blocks).", this);
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
        for (int i = 0; i < 40; i++)
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
