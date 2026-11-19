using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server: counts how often living players enter each street block of the active map and when each block last had a
/// player in it. Enemies use it to relocate to where players actually go (see TikbalangBrain). Updated at most once
/// per frame by whichever enemy asks first; resets when the active map changes.
/// </summary>
public static class BlockVisitTracker
{
    private static MapManager trackedMap;
    private static int lastFrame = -1;
    private static readonly Dictionary<int, int> visits = new Dictionary<int, int>();
    private static readonly Dictionary<int, float> lastOccupied = new Dictionary<int, float>();
    private static readonly Dictionary<uint, int> playerBlocks = new Dictionary<uint, int>();
    private static readonly HashSet<int> occupied = new HashSet<int>();

    public static MapManager TrackedMap => trackedMap;

    /// <summary>Refreshes the counts for <paramref name="map"/> (once per frame).</summary>
    public static void Tick(MapManager map)
    {
        if (map == null || !map.IsBuilt)
            return;
        if (map != trackedMap)
            Reset(map);
        if (lastFrame == Time.frameCount)
            return;
        lastFrame = Time.frameCount;
        occupied.Clear();
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (player == null || !player.isActiveAndEnabled || player.IsInTrance ||
                (player.TryGetComponent(out PlayerHealth health) && health.IsDead))
                continue;
            if (!map.TryGetBlockIndex(player.transform.position, out int block))
                continue;
            occupied.Add(block);
            lastOccupied[block] = Time.time;
            uint id = player.netId;
            if (!playerBlocks.TryGetValue(id, out int previous) || previous != block)
            {
                playerBlocks[id] = block;
                visits[block] = VisitCount(block) + 1;
            }
        }
    }

    public static void Reset(MapManager map = null)
    {
        trackedMap = map;
        lastFrame = -1;
        visits.Clear();
        lastOccupied.Clear();
        playerBlocks.Clear();
        occupied.Clear();
    }

    public static int VisitCount(int block) => visits.TryGetValue(block, out int count) ? count : 0;

    /// <summary>True when a living player stood in the block at the last <see cref="Tick"/>.</summary>
    public static bool IsOccupied(int block) => occupied.Contains(block);

    /// <summary>Time.time a player was last in the block, or negative infinity if never.</summary>
    public static float LastOccupiedTime(int block) =>
        lastOccupied.TryGetValue(block, out float time) ? time : float.NegativeInfinity;

    /// <summary>Test hook: adds visits without players.</summary>
    public static void AddVisits(MapManager map, int block, int count)
    {
        if (map != trackedMap)
            Reset(map);
        visits[block] = VisitCount(block) + count;
    }

    /// <summary>Blocks with at least one visit, most visited first (ties: lower index first).</summary>
    public static List<int> RankedBlocks()
    {
        var ranked = new List<int>();
        foreach (KeyValuePair<int, int> pair in visits)
        {
            if (pair.Value > 0)
                ranked.Add(pair.Key);
        }
        ranked.Sort((a, b) =>
        {
            int byVisits = VisitCount(b).CompareTo(VisitCount(a));
            return byVisits != 0 ? byVisits : a.CompareTo(b);
        });
        return ranked;
    }
}
