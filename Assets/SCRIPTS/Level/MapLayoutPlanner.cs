using System;
using System.Collections.Generic;
using UnityEngine;

public enum MapBlockRole
{
    Connector,
    Spawn,
    EndCap
}

public readonly struct MapBlockPlacement
{
    public MapBlockPlacement(StreetBlock prefab, MapBlockRole role, Vector3 position, int quarterTurns)
    {
        Prefab = prefab;
        Role = role;
        Position = position;
        QuarterTurns = quarterTurns;
    }

    public StreetBlock Prefab { get; }
    public MapBlockRole Role { get; }
    /// <summary>Block origin relative to the map root (floor centre at floor height).</summary>
    public Vector3 Position { get; }
    /// <summary>Clockwise 90 degree yaw turns, as seen from above.</summary>
    public int QuarterTurns { get; }
    public Quaternion Rotation => Quaternion.Euler(0f, 90f * QuarterTurns, 0f);
}

/// <summary>
/// Deterministically lays street blocks out on a flat grid so that every entrance meets a matching entrance.
/// Connectors grow the street, the spawn block is attached to the outermost open entrance, and end caps close the rest.
/// </summary>
public static class MapLayoutPlanner
{
    private const float Epsilon = 0.05f;
    private const float ProbeDistance = 0.1f;

    private sealed class PlacedBlock
    {
        public Rect Footprint;
        public readonly List<PlacedEntrance> Entrances = new List<PlacedEntrance>();
    }

    private sealed class PlacedEntrance
    {
        public PlacedBlock Owner;
        public Vector2 Center;
        public BlockSide Side;
        public float Width;
        public bool Connected;
        public Vector2 Probe => Center + Outward(Side) * ProbeDistance;
    }

    private readonly struct Candidate
    {
        public Candidate(StreetBlock prefab, int turns, StreetBlockEntrance entrance)
        {
            Prefab = prefab;
            Turns = turns;
            Entrance = entrance;
        }

        public StreetBlock Prefab { get; }
        public int Turns { get; }
        public StreetBlockEntrance Entrance { get; }
    }

    public static BlockSide Rotate(BlockSide side, int quarterTurns) => (BlockSide)(((int)side + 3 * Mod4(quarterTurns)) % 4);

    /// <summary>Rotates a block-space XZ offset by clockwise quarter turns (Unity's positive yaw).</summary>
    public static Vector2 Rotate(Vector2 v, int quarterTurns) => Mod4(quarterTurns) switch
    {
        1 => new Vector2(v.y, -v.x),
        2 => new Vector2(-v.x, -v.y),
        3 => new Vector2(-v.y, v.x),
        _ => v
    };

    public static bool TryPlan(IEnumerable<StreetBlock> connectors, IEnumerable<StreetBlock> endCaps,
        IEnumerable<StreetBlock> spawnBlocks, int connectorCount, int seed, int maxAttempts,
        out List<MapBlockPlacement> layout, out string error, float loopChance = 0f)
    {
        layout = null;
        List<StreetBlock> connectorPool = Collect(connectors);
        List<StreetBlock> capPool = Collect(endCaps);
        List<StreetBlock> spawnPool = Collect(spawnBlocks);
        if (!Validate(connectorPool, capPool, spawnPool, connectorCount, out error))
            return false;

        var random = new System.Random(seed);
        for (int attempt = 0; attempt < Mathf.Max(1, maxAttempts); attempt++)
        {
            if (TryAttempt(connectorPool, capPool, spawnPool, connectorCount, Mathf.Clamp01(loopChance), random, out layout))
            {
                error = null;
                return true;
            }
        }

        layout = null;
        error = $"MapManager could not connect {connectorCount} connector blocks with the given pools after {Mathf.Max(1, maxAttempts)} attempts (seed {seed}). " +
            "Add connector blocks with more entrances, end caps/spawn blocks that fit the connectors' entrance widths, or lower the block count.";
        return false;
    }

    private static List<StreetBlock> Collect(IEnumerable<StreetBlock> pool)
    {
        var result = new List<StreetBlock>();
        if (pool == null)
            return result;
        foreach (StreetBlock block in pool)
        {
            if (block != null && !result.Contains(block))
                result.Add(block);
        }
        return result;
    }

    private static bool Validate(List<StreetBlock> connectors, List<StreetBlock> caps, List<StreetBlock> spawns,
        int connectorCount, out string error)
    {
        error = null;
        if (connectorCount < 0)
            error = "MapManager's connector block count cannot be negative.";
        else if (connectorCount > 0 && connectors.Count == 0)
            error = "MapManager needs at least one connector block prefab in its Connector Blocks pool.";
        else if (caps.Count == 0)
            error = "MapManager needs at least one single-entrance block in its End Cap Blocks pool to close the street.";
        else if (spawns.Count == 0)
            error = "MapManager needs at least one single-entrance block in its Spawn Blocks pool.";
        if (error != null)
            return false;

        foreach (StreetBlock block in connectors)
        {
            if (block.Entrances.Count < 2)
            {
                error = $"Connector block '{block.name}' has {block.Entrances.Count} entrance(s); connectors need at least 2 so the street can continue.";
                return false;
            }
        }

        foreach (StreetBlock block in caps)
        {
            if (block.Entrances.Count != 1)
            {
                error = $"End cap block '{block.name}' has {block.Entrances.Count} entrances; end caps need exactly 1.";
                return false;
            }
        }

        foreach (StreetBlock block in spawns)
        {
            if (block.Entrances.Count != 1)
            {
                error = $"Spawn block '{block.name}' has {block.Entrances.Count} entrances; spawn blocks need exactly 1.";
                return false;
            }
        }

        StreetBlock reference = connectors.Count > 0 && connectorCount > 0 ? connectors[0] : spawns[0];
        foreach (StreetBlock block in Concat(connectors, caps, spawns))
        {
            if (block.Size.x <= 0f || block.Size.y <= 0f)
            {
                error = $"Street block '{block.name}' has no generated layout. Generate it with the Street Block Generator before using it in MapManager.";
                return false;
            }
            if ((block.CellSize - reference.CellSize).sqrMagnitude > Epsilon * Epsilon)
            {
                error = $"Street block '{block.name}' uses a {block.CellSize.x} x {block.CellSize.y} m grid cell but '{reference.name}' uses " +
                    $"{reference.CellSize.x} x {reference.CellSize.y} m. Every pooled block must share the same cell size.";
                return false;
            }
        }
        return true;
    }

    private static IEnumerable<StreetBlock> Concat(params List<StreetBlock>[] pools)
    {
        foreach (List<StreetBlock> pool in pools)
        {
            foreach (StreetBlock block in pool)
                yield return block;
        }
    }

    private static bool TryAttempt(List<StreetBlock> connectors, List<StreetBlock> caps, List<StreetBlock> spawns,
        int connectorCount, float loopChance, System.Random random, out List<MapBlockPlacement> layout)
    {
        layout = new List<MapBlockPlacement>();
        var placed = new List<PlacedBlock>();

        if (connectorCount > 0)
        {
            StreetBlock first = connectors[random.Next(connectors.Count)];
            Place(first, MapBlockRole.Connector, Vector2.zero, random.Next(4), placed, layout);
            while (placed.Count < connectorCount)
            {
                if (!TryCloseLoop(connectors, loopChance, OpenEntrances(placed), random, placed, layout) &&
                    !TryGrow(connectors, MapBlockRole.Connector, OpenEntrances(placed), random, placed, layout))
                    return false;
            }

            Vector2 centroid = Vector2.zero;
            foreach (PlacedBlock block in placed)
                centroid += block.Footprint.center;
            centroid /= placed.Count;

            // The spawn block goes on the open entrance farthest from the street's centre, so players start at an edge.
            List<PlacedEntrance> open = OpenEntrances(placed);
            open.Sort((a, b) => (b.Center - centroid).sqrMagnitude.CompareTo((a.Center - centroid).sqrMagnitude));
            bool spawnPlaced = false;
            foreach (PlacedEntrance entrance in open)
            {
                if (TryAttach(spawns, MapBlockRole.Spawn, entrance, random, placed, layout))
                {
                    spawnPlaced = true;
                    break;
                }
            }
            if (!spawnPlaced)
                return false;
        }
        else
        {
            Place(spawns[random.Next(spawns.Count)], MapBlockRole.Spawn, Vector2.zero, random.Next(4), placed, layout);
        }

        List<PlacedEntrance> remaining = OpenEntrances(placed);
        foreach (PlacedEntrance entrance in remaining)
        {
            if (!entrance.Connected && !TryAttach(caps, MapBlockRole.EndCap, entrance, random, placed, layout))
                return false;
        }
        return OpenEntrances(placed).Count == 0;
    }

    private static List<PlacedEntrance> OpenEntrances(List<PlacedBlock> placed)
    {
        var open = new List<PlacedEntrance>();
        foreach (PlacedBlock block in placed)
        {
            foreach (PlacedEntrance entrance in block.Entrances)
            {
                if (!entrance.Connected)
                    open.Add(entrance);
            }
        }
        return open;
    }

    private static bool TryGrow(List<StreetBlock> pool, MapBlockRole role, List<PlacedEntrance> frontier,
        System.Random random, List<PlacedBlock> placed, List<MapBlockPlacement> layout)
    {
        Shuffle(frontier, random);
        foreach (PlacedEntrance entrance in frontier)
        {
            if (TryAttach(pool, role, entrance, random, placed, layout))
                return true;
        }
        return false;
    }

    /// <summary>
    /// With probability <paramref name="loopChance"/>, places a connector that joins two or more open entrances at once,
    /// closing a loop so the street has more than one route between those blocks. Returns false if none was placed.
    /// </summary>
    private static bool TryCloseLoop(List<StreetBlock> pool, float loopChance, List<PlacedEntrance> frontier,
        System.Random random, List<PlacedBlock> placed, List<MapBlockPlacement> layout)
    {
        if (loopChance <= 0f || random.NextDouble() >= loopChance)
            return false;

        var closers = new List<(StreetBlock prefab, Vector2 origin, int turns)>();
        foreach (PlacedEntrance target in frontier)
        {
            foreach (Candidate candidate in Candidates(pool, target))
            {
                Vector2 origin = Origin(candidate, target);
                if (Fits(candidate.Prefab, origin, candidate.Turns, placed) &&
                    CountConnections(candidate.Prefab, origin, candidate.Turns, placed) >= 2 &&
                    !closers.Exists(c => c.prefab == candidate.Prefab && c.turns == candidate.Turns && (c.origin - origin).sqrMagnitude <= Epsilon * Epsilon))
                    closers.Add((candidate.Prefab, origin, candidate.Turns));
            }
        }
        if (closers.Count == 0)
            return false;

        (StreetBlock prefab, Vector2 origin, int turns) chosen = closers[random.Next(closers.Count)];
        Place(chosen.prefab, MapBlockRole.Connector, chosen.origin, chosen.turns, placed, layout);
        return true;
    }

    private static int CountConnections(StreetBlock prefab, Vector2 origin, int turns, List<PlacedBlock> placed)
    {
        int count = 0;
        foreach (PlacedEntrance entrance in Describe(prefab, origin, turns).Entrances)
        {
            PlacedBlock neighbour = BlockAt(entrance.Probe, placed);
            if (neighbour != null && FindPartner(entrance, neighbour) != null)
                count++;
        }
        return count;
    }

    private static List<Candidate> Candidates(List<StreetBlock> pool, PlacedEntrance target)
    {
        var candidates = new List<Candidate>();
        foreach (StreetBlock prefab in pool)
        {
            for (int turns = 0; turns < 4; turns++)
            {
                foreach (StreetBlockEntrance entrance in prefab.Entrances)
                {
                    if (Rotate(entrance.Side, turns) == target.Side.Opposite() && Mathf.Abs(entrance.Width - target.Width) <= Epsilon)
                        candidates.Add(new Candidate(prefab, turns, entrance));
                }
            }
        }
        return candidates;
    }

    private static Vector2 Origin(Candidate candidate, PlacedEntrance target) =>
        target.Center - Rotate(new Vector2(candidate.Entrance.LocalCenter.x, candidate.Entrance.LocalCenter.z), candidate.Turns);

    private static bool TryAttach(List<StreetBlock> pool, MapBlockRole role, PlacedEntrance target,
        System.Random random, List<PlacedBlock> placed, List<MapBlockPlacement> layout)
    {
        List<Candidate> candidates = Candidates(pool, target);
        Shuffle(candidates, random);
        foreach (Candidate candidate in candidates)
        {
            Vector2 local = new Vector2(candidate.Entrance.LocalCenter.x, candidate.Entrance.LocalCenter.z);
            Vector2 origin = target.Center - Rotate(local, candidate.Turns);
            if (Fits(candidate.Prefab, origin, candidate.Turns, placed))
            {
                Place(candidate.Prefab, role, origin, candidate.Turns, placed, layout);
                return true;
            }
        }
        return false;
    }

    private static PlacedBlock Describe(StreetBlock prefab, Vector2 origin, int turns)
    {
        Vector2 size = prefab.Size;
        if (Mod4(turns) % 2 == 1)
            size = new Vector2(size.y, size.x);

        var block = new PlacedBlock { Footprint = new Rect(origin - size * 0.5f, size) };
        foreach (StreetBlockEntrance entrance in prefab.Entrances)
        {
            block.Entrances.Add(new PlacedEntrance
            {
                Owner = block,
                Center = origin + Rotate(new Vector2(entrance.LocalCenter.x, entrance.LocalCenter.z), turns),
                Side = Rotate(entrance.Side, turns),
                Width = entrance.Width
            });
        }
        return block;
    }

    private static bool Fits(StreetBlock prefab, Vector2 origin, int turns, List<PlacedBlock> placed)
    {
        PlacedBlock candidate = Describe(prefab, origin, turns);
        foreach (PlacedBlock block in placed)
        {
            if (Overlaps(candidate.Footprint, block.Footprint))
                return false;
        }

        var matched = new HashSet<PlacedEntrance>();
        foreach (PlacedEntrance entrance in candidate.Entrances)
        {
            PlacedBlock neighbour = BlockAt(entrance.Probe, placed);
            if (neighbour == null)
                continue;
            PlacedEntrance partner = FindPartner(entrance, neighbour);
            // An entrance facing an existing block must open onto one of its free entrances, never into a wall.
            if (partner == null)
                return false;
            matched.Add(partner);
        }

        foreach (PlacedBlock block in placed)
        {
            foreach (PlacedEntrance entrance in block.Entrances)
            {
                if (!entrance.Connected && !matched.Contains(entrance) && Contains(candidate.Footprint, entrance.Probe))
                    return false;
            }
        }
        return true;
    }

    private static void Place(StreetBlock prefab, MapBlockRole role, Vector2 origin, int turns,
        List<PlacedBlock> placed, List<MapBlockPlacement> layout)
    {
        PlacedBlock block = Describe(prefab, origin, turns);
        foreach (PlacedEntrance entrance in block.Entrances)
        {
            PlacedBlock neighbour = BlockAt(entrance.Probe, placed);
            PlacedEntrance partner = neighbour != null ? FindPartner(entrance, neighbour) : null;
            if (partner != null)
            {
                partner.Connected = true;
                entrance.Connected = true;
            }
        }

        placed.Add(block);
        layout.Add(new MapBlockPlacement(prefab, role, new Vector3(origin.x, 0f, origin.y), Mod4(turns)));
    }

    private static PlacedEntrance FindPartner(PlacedEntrance entrance, PlacedBlock neighbour)
    {
        foreach (PlacedEntrance other in neighbour.Entrances)
        {
            if (!other.Connected && other.Side == entrance.Side.Opposite() &&
                (other.Center - entrance.Center).sqrMagnitude <= Epsilon * Epsilon &&
                Mathf.Abs(other.Width - entrance.Width) <= Epsilon)
                return other;
        }
        return null;
    }

    private static PlacedBlock BlockAt(Vector2 point, List<PlacedBlock> placed)
    {
        foreach (PlacedBlock block in placed)
        {
            if (Contains(block.Footprint, point))
                return block;
        }
        return null;
    }

    private static bool Overlaps(Rect a, Rect b) =>
        a.xMin < b.xMax - Epsilon && b.xMin < a.xMax - Epsilon &&
        a.yMin < b.yMax - Epsilon && b.yMin < a.yMax - Epsilon;

    private static bool Contains(Rect rect, Vector2 point) =>
        point.x > rect.xMin && point.x < rect.xMax && point.y > rect.yMin && point.y < rect.yMax;

    private static Vector2 Outward(BlockSide side)
    {
        Vector3 outward = side.Outward();
        return new Vector2(outward.x, outward.z);
    }

    private static int Mod4(int value) => ((value % 4) + 4) % 4;

    private static void Shuffle<T>(IList<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
