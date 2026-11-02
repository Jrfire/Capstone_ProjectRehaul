using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

public sealed class StreetBlockGenerationException : Exception
{
    public StreetBlockGenerationException(string message) : base(message) { }
}

/// <summary>
/// Builds a street block from a <see cref="StreetBlockGenerator"/>. Everything is measured in block space
/// (origin = floor centre, Y = floor top) from real renderer/collider bounds, so pivots do not matter.
/// </summary>
public static class StreetBlockBuilder
{
    public const string GeneratedName = "Generated";
    private const float Epsilon = 0.001f;
    private const int DecorationAttempts = 40;

    public static bool Generate(StreetBlockGenerator generator)
    {
        try
        {
            Build(generator);
            return true;
        }
        catch (StreetBlockGenerationException exception)
        {
            Debug.LogError($"Street block '{generator.name}' was not generated: {exception.Message}", generator);
            return false;
        }
    }

    public static void Clear(StreetBlockGenerator generator)
    {
        Transform existing = generator.transform.Find(GeneratedName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);
        Undo.RecordObject(generator.GetComponent<StreetBlock>(), "Clear Street Block");
        generator.GetComponent<StreetBlock>().ApplyLayout(Vector2Int.zero, Vector2.zero, 1, generator.seed,
            Array.Empty<StreetBlockEntrance>());
    }

    /// <summary>Builds the block, replacing previous content only on success. Throws on invalid settings.</summary>
    public static void Build(StreetBlockGenerator generator)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(generator.gameObject))
            throw new StreetBlockGenerationException(
                "this object is a prefab instance. Open the prefab (double-click it) to regenerate it, or unpack the instance first.");

        var context = new Context(generator);
        GameObject root = new GameObject(GeneratedName + " (building)");
        root.transform.SetParent(generator.transform, false);
        try
        {
            context.Run(root.transform);
        }
        catch
        {
            Object.DestroyImmediate(root);
            throw;
        }

        Transform previous = generator.transform.Find(GeneratedName);
        if (previous != null)
            Undo.DestroyObjectImmediate(previous.gameObject);
        root.name = GeneratedName;
        root.transform.SetSiblingIndex(0);
        Undo.RegisterCreatedObjectUndo(root, "Generate Street Block");
        StreetBlock block = generator.GetComponent<StreetBlock>();
        Undo.RecordObject(block, "Generate Street Block");
        block.ApplyLayout(generator.sizeInTiles, context.TileSize, generator.tilesPerCell, generator.seed, context.Entrances);
        EditorUtility.SetDirty(block);
    }

    /// <summary>Bounds of a placed object's renderers and solid colliders, expressed in <paramref name="space"/>.</summary>
    public static Bounds MeasureIn(GameObject instance, Transform space)
    {
        bool any = false;
        Bounds total = default;
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
        if (!any)
            throw new StreetBlockGenerationException($"'{instance.name}' has no renderer or collider to measure.");
        return total;
    }

    private static void Encapsulate(ref Bounds total, ref bool any, Bounds local, Transform from, Transform space)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
            Vector3 point = local.center + Vector3.Scale(local.extents, sign);
            Vector3 world = from != null ? from.TransformPoint(point) : point;
            Vector3 inSpace = space.InverseTransformPoint(world);
            if (!any)
            {
                total = new Bounds(inSpace, Vector3.zero);
                any = true;
            }
            else
            {
                total.Encapsulate(inSpace);
            }
        }
    }

    private sealed class Context
    {
        private readonly StreetBlockGenerator settings;
        private readonly Random random;
        private readonly Dictionary<GameObject, Vector3> prefabSizes = new Dictionary<GameObject, Vector3>();
        private readonly List<Rect> occupied = new List<Rect>();
        private readonly List<Rect> paths = new List<Rect>();
        private readonly List<GameObject> placed = new List<GameObject>();
        private Transform blockSpace;
        private float halfX;
        private float halfZ;

        public Context(StreetBlockGenerator settings)
        {
            this.settings = settings;
            random = new Random(settings.seed);
        }

        public Vector2 TileSize { get; private set; }
        public List<StreetBlockEntrance> Entrances { get; } = new List<StreetBlockEntrance>();
        private Vector2 Size => new Vector2(settings.sizeInTiles.x * TileSize.x, settings.sizeInTiles.y * TileSize.y);

        public void Run(Transform root)
        {
            blockSpace = settings.transform;
            ValidateSettings();
            Transform floor = Group(root, "Floor");
            Transform obstacles = Group(root, "Obstacles");
            Transform buildings = Group(root, "Buildings");
            Transform walls = Group(root, "Walls");
            Transform boundaries = Group(root, "Boundaries");
            Transform decorations = Group(root, "Decorations");

            BuildFloor(floor);
            ValidatePrefabSizes();
            List<(BlockSide side, int cell, float center, float width)> openings = ChooseEntrances();
            foreach (var opening in openings)
                AddPath(opening.side, opening.center, opening.width);
            Dictionary<BlockSide, EntranceObstacle> placedObstacles = PlaceObstacles(obstacles, openings);
            foreach (var opening in openings)
            {
                placedObstacles.TryGetValue(opening.side, out EntranceObstacle obstacle);
                Entrances.Add(new StreetBlockEntrance(opening.side, opening.cell, opening.width,
                    SidePoint(opening.side, opening.center, 0f), obstacle));
            }

            var insets = new Dictionary<BlockSide, float>();
            void Seal()
            {
                foreach (BlockSide side in BlockSideUtility.All)
                    insets[side] = SealEdge(walls, boundaries, side, openings.Where(opening => opening.side == side)
                        .Select(opening => (opening.center - opening.width * 0.5f, opening.center + opening.width * 0.5f)).ToList());
            }

            bool wrap = settings.wallMode == WallMode.WrapEdges;
            if (wrap)
                Seal();
            foreach (BlockSide side in new[] { BlockSide.South, BlockSide.North, BlockSide.West, BlockSide.East })
                PlaceBuildings(buildings, side, wrap ? insets[side] : 0f);
            if (!wrap)
                Seal();
            PlaceDecorations(decorations);
            VerifyInsideFloor();
        }

        private void ValidateSettings()
        {
            int perCell = settings.tilesPerCell;
            if (settings.floorTile == null)
                throw new StreetBlockGenerationException("assign a Floor Tile prefab.");
            if (perCell < 1)
                throw new StreetBlockGenerationException("Tiles Per Cell must be at least 1.");
            if (settings.sizeInTiles.x < perCell || settings.sizeInTiles.y < perCell ||
                settings.sizeInTiles.x % perCell != 0 || settings.sizeInTiles.y % perCell != 0)
                throw new StreetBlockGenerationException(
                    $"Size In Tiles ({settings.sizeInTiles.x}x{settings.sizeInTiles.y}) must be a positive multiple of Tiles Per Cell ({perCell}) on both axes so blocks share one grid.");
            if (settings.entranceWidthTiles < 2)
                throw new StreetBlockGenerationException("Entrance Width Tiles must be at least 2.");
            if (settings.entranceWidthTiles > perCell)
                throw new StreetBlockGenerationException(
                    $"Entrance Width Tiles ({settings.entranceWidthTiles}) cannot exceed Tiles Per Cell ({perCell}); an entrance must fit on one grid cell so neighbouring blocks line up.");
            if (settings.sideMode == EntranceSideMode.Random && (settings.entranceCount < 0 || settings.entranceCount > 4))
                throw new StreetBlockGenerationException("Entrance Count must be between 0 and 4.");
            int entrances = EntranceTotal();
            if (settings.obstacleCount > entrances)
                throw new StreetBlockGenerationException(
                    $"Obstacle Count ({settings.obstacleCount}) cannot exceed the number of entrances ({entrances}).");
            if (settings.obstacleCount > 0 && (settings.obstaclePrefabs == null || settings.obstaclePrefabs.Length == 0))
                throw new StreetBlockGenerationException("Obstacle Count is above zero but no Obstacle Prefabs are assigned.");
            CheckList(settings.obstaclePrefabs, "Obstacle Prefabs");
            CheckList(settings.buildingPrefabs, "Building Prefabs");
            CheckList(settings.wallPrefabs, "Wall Prefabs");
            CheckList(settings.decorationPrefabs, "Decoration Prefabs");
            if (settings.obstaclePrefabs != null)
            {
                foreach (GameObject prefab in settings.obstaclePrefabs)
                {
                    if (prefab.GetComponentInChildren<EntranceObstacle>(true) == null)
                        throw new StreetBlockGenerationException($"obstacle prefab '{prefab.name}' needs an EntranceObstacle component.");
                }
            }
        }

        private static void CheckList(GameObject[] prefabs, string label)
        {
            if (prefabs == null)
                return;
            for (int index = 0; index < prefabs.Length; index++)
            {
                if (prefabs[index] == null)
                    throw new StreetBlockGenerationException($"{label} element {index} is empty.");
            }
        }

        private int EntranceTotal()
        {
            if (settings.sideMode == EntranceSideMode.Random)
                return settings.entranceCount;
            return BlockSideUtility.All.Count(side => (settings.manualSides & side.ToFlag()) != 0);
        }

        private void BuildFloor(Transform floor)
        {
            Vector3 tile = SizeOf(settings.floorTile);
            if (tile.x < 0.01f || tile.z < 0.01f)
                throw new StreetBlockGenerationException($"floor tile '{settings.floorTile.name}' has no horizontal size.");
            TileSize = new Vector2(tile.x, tile.z);
            Vector2 size = Size;
            halfX = size.x * 0.5f;
            halfZ = size.y * 0.5f;
            for (int x = 0; x < settings.sizeInTiles.x; x++)
            {
                for (int z = 0; z < settings.sizeInTiles.y; z++)
                {
                    GameObject instance = Spawn(settings.floorTile, floor, 0f);
                    Bounds bounds = MeasureIn(instance, blockSpace);
                    instance.transform.localPosition += new Vector3(-halfX + x * TileSize.x - bounds.min.x, -bounds.max.y,
                        -halfZ + z * TileSize.y - bounds.min.z);
                    instance.name = $"{settings.floorTile.name} ({x},{z})";
                }
            }

            if (settings.addFloorCollider && settings.floorTile.GetComponentInChildren<Collider>(true) == null)
            {
                float thickness = Mathf.Max(tile.y, 0.1f);
                BoxCollider collider = floor.gameObject.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, -thickness * 0.5f, 0f);
                collider.size = new Vector3(size.x, thickness, size.y);
            }
        }

        private void ValidatePrefabSizes()
        {
            Vector2 size = Size;
            foreach (GameObject prefab in settings.buildingPrefabs ?? Array.Empty<GameObject>())
            {
                Vector3 footprint = SizeOf(prefab);
                bool fitsSouthNorth = footprint.x <= size.x + Epsilon && footprint.z <= size.y + Epsilon;
                bool fitsWestEast = footprint.x <= size.y + Epsilon && footprint.z <= size.x + Epsilon;
                if (!fitsSouthNorth && !fitsWestEast)
                    throw new StreetBlockGenerationException(
                        $"building '{prefab.name}' is {footprint.x:0.##} m wide x {footprint.z:0.##} m deep (including roofs/overhangs) but the floor set is only {size.x:0.##} x {size.y:0.##} m. Enlarge Size In Tiles or remove the building.");
            }
            CheckFits(settings.wallPrefabs, "wall", size);
            CheckFits(settings.obstaclePrefabs, "obstacle", size);
            CheckFits(settings.decorationPrefabs, "decoration", size);
        }

        private void CheckFits(GameObject[] prefabs, string label, Vector2 size)
        {
            foreach (GameObject prefab in prefabs ?? Array.Empty<GameObject>())
            {
                Vector3 footprint = SizeOf(prefab);
                bool fits = (footprint.x <= size.x + Epsilon && footprint.z <= size.y + Epsilon) ||
                    (footprint.x <= size.y + Epsilon && footprint.z <= size.x + Epsilon);
                if (!fits)
                    throw new StreetBlockGenerationException(
                        $"{label} '{prefab.name}' ({footprint.x:0.##} x {footprint.z:0.##} m) does not fit inside the {size.x:0.##} x {size.y:0.##} m floor set.");
            }
        }

        private List<(BlockSide side, int cell, float center, float width)> ChooseEntrances()
        {
            List<BlockSide> sides;
            if (settings.sideMode == EntranceSideMode.Manual)
            {
                sides = BlockSideUtility.All.Where(side => (settings.manualSides & side.ToFlag()) != 0).ToList();
            }
            else
            {
                sides = BlockSideUtility.All.OrderBy(_ => random.Next()).Take(settings.entranceCount)
                    .OrderBy(side => (int)side).ToList();
            }

            var openings = new List<(BlockSide, int, float, float)>();
            foreach (BlockSide side in sides)
            {
                bool alongX = side == BlockSide.South || side == BlockSide.North;
                int cells = (alongX ? settings.sizeInTiles.x : settings.sizeInTiles.y) / settings.tilesPerCell;
                float tile = alongX ? TileSize.x : TileSize.y;
                int cell;
                if (settings.entrancePlacement == EntrancePlacement.Random)
                    cell = random.Next(cells);
                else
                    cell = cells % 2 == 1 ? cells / 2 : cells / 2 - random.Next(2);
                float cellLength = tile * settings.tilesPerCell;
                openings.Add((side, cell, (cell + 0.5f) * cellLength, settings.entranceWidthTiles * tile));
            }
            return openings;
        }

        private float SideLength(BlockSide side) =>
            side == BlockSide.South || side == BlockSide.North ? Size.x : Size.y;

        private float DepthToCenter(BlockSide side) =>
            side == BlockSide.South || side == BlockSide.North ? halfZ : halfX;

        /// <summary>Rect in block XZ for an interval along a side (from its West/South corner) and a depth band inward.</summary>
        private Rect SideRect(BlockSide side, float along0, float along1, float depth0, float depth1)
        {
            switch (side)
            {
                case BlockSide.South:
                    return Rect.MinMaxRect(-halfX + along0, -halfZ + depth0, -halfX + along1, -halfZ + depth1);
                case BlockSide.North:
                    return Rect.MinMaxRect(-halfX + along0, halfZ - depth1, -halfX + along1, halfZ - depth0);
                case BlockSide.West:
                    return Rect.MinMaxRect(-halfX + depth0, -halfZ + along0, -halfX + depth1, -halfZ + along1);
                default:
                    return Rect.MinMaxRect(halfX - depth1, -halfZ + along0, halfX - depth0, -halfZ + along1);
            }
        }

        private Vector3 SidePoint(BlockSide side, float along, float depth)
        {
            Rect point = SideRect(side, along, along, depth, depth);
            return new Vector3(point.xMin, 0f, point.yMin);
        }

        private void AddPath(BlockSide side, float center, float width)
        {
            float half = width * 0.5f;
            float depth = DepthToCenter(side);
            float middle = SideLength(side) * 0.5f;
            paths.Add(SideRect(side, center - half, center + half, 0f, depth + half));
            paths.Add(SideRect(side, Mathf.Min(center, middle) - half, Mathf.Max(center, middle) + half, depth - half, depth + half));
        }

        private Dictionary<BlockSide, EntranceObstacle> PlaceObstacles(Transform parent,
            List<(BlockSide side, int cell, float center, float width)> openings)
        {
            var result = new Dictionary<BlockSide, EntranceObstacle>();
            foreach (var opening in openings.OrderBy(_ => random.Next()).Take(settings.obstacleCount).OrderBy(o => (int)o.side))
            {
                GameObject prefab = settings.obstaclePrefabs[random.Next(settings.obstaclePrefabs.Length)];
                Vector3 size = SizeOf(prefab);
                if (size.z > DepthToCenter(opening.side) * 2f + Epsilon)
                    throw new StreetBlockGenerationException($"obstacle '{prefab.name}' is deeper than the block.");
                float along0 = opening.center - size.x * 0.5f;
                Rect rect = SideRect(opening.side, along0, along0 + size.x, 0f, size.z);
                if (!InsideFloor(rect))
                    throw new StreetBlockGenerationException(
                        $"obstacle '{prefab.name}' ({size.x:0.##} m wide) would extend outside the floor at the {opening.side} entrance.");
                if (size.x + Epsilon < opening.width)
                    Debug.LogWarning($"Obstacle '{prefab.name}' is {size.x:0.##} m wide but the {opening.side} entrance is {opening.width:0.##} m; players may walk around it.", settings);
                GameObject instance = PlaceAt(prefab, parent, opening.side.InwardYaw(), rect);
                instance.name = $"{prefab.name} ({opening.side} entrance)";
                result[opening.side] = instance.GetComponentInChildren<EntranceObstacle>(true);
            }
            return result;
        }

        /// <param name="inset">Depth from the edge already used by wrapping walls; buildings start behind it.</param>
        private void PlaceBuildings(Transform parent, BlockSide side, float inset)
        {
            GameObject[] prefabs = settings.buildingPrefabs ?? Array.Empty<GameObject>();
            if (prefabs.Length == 0)
                return;
            float length = SideLength(side);
            float maxDepth = DepthToCenter(side) * 2f - inset;
            float step = Mathf.Max(0.05f, Mathf.Min(0.5f, Mathf.Min(TileSize.x, TileSize.y) * 0.25f));
            float cursor = 0f;
            while (cursor < length - Epsilon)
            {
                bool placedOne = false;
                foreach (GameObject prefab in prefabs.OrderBy(_ => random.Next()))
                {
                    Vector3 size = SizeOf(prefab);
                    if (cursor + size.x > length + Epsilon || size.z > maxDepth + Epsilon)
                        continue;
                    Rect rect = SideRect(side, cursor, cursor + size.x, inset, inset + size.z);
                    if (Blocked(rect, 0f))
                        continue;
                    PlaceAt(prefab, parent, side.InwardYaw(), rect).name = $"{prefab.name} ({side})";
                    cursor += size.x + settings.buildingSpacing;
                    placedOne = true;
                    break;
                }
                if (!placedOne)
                    cursor += step;
            }
        }

        /// <returns>The deepest wall or boundary placed along this edge.</returns>
        private float SealEdge(Transform walls, Transform boundaries, BlockSide side, List<(float start, float end)> gaps)
        {
            float length = SideLength(side);
            float inset = 0f;
            var covered = new List<(float start, float end)>(gaps);
            foreach (Rect rect in occupied)
            {
                if (TouchesEdge(rect, side, out float start, out float end))
                    covered.Add((start, end));
            }

            foreach ((float start, float end) in Uncovered(covered, length))
            {
                float cursor = start;
                GameObject[] prefabs = settings.wallPrefabs ?? Array.Empty<GameObject>();
                while (prefabs.Length > 0)
                {
                    GameObject fitting = prefabs.Where(prefab =>
                    {
                        Vector3 size = SizeOf(prefab);
                        return cursor + size.x <= end + Epsilon && !Blocked(SideRect(side, cursor, cursor + size.x, 0f, size.z), 0f);
                    }).OrderBy(_ => random.Next()).FirstOrDefault();
                    if (fitting == null)
                        break;
                    Vector3 wallSize = SizeOf(fitting);
                    PlaceAt(fitting, walls, side.InwardYaw(), SideRect(side, cursor, cursor + wallSize.x, 0f, wallSize.z))
                        .name = $"{fitting.name} ({side})";
                    cursor += wallSize.x;
                    inset = Mathf.Max(inset, wallSize.z);
                }

                if (end - cursor > Epsilon)
                {
                    const float thickness = 0.1f;
                    Rect rect = SideRect(side, cursor, end, 0f, thickness);
                    var boundary = new GameObject($"Boundary ({side} {cursor:0.##}-{end:0.##})");
                    boundary.transform.SetParent(boundaries, false);
                    boundary.transform.localPosition = new Vector3(rect.center.x, settings.boundaryHeight * 0.5f, rect.center.y);
                    boundary.AddComponent<BoxCollider>().size = new Vector3(rect.width, settings.boundaryHeight, rect.height);
                    occupied.Add(rect);
                    inset = Mathf.Max(inset, thickness);
                }
            }
            return inset;
        }

        private bool TouchesEdge(Rect rect, BlockSide side, out float start, out float end)
        {
            switch (side)
            {
                case BlockSide.South:
                case BlockSide.North:
                    start = rect.xMin + halfX;
                    end = rect.xMax + halfX;
                    return side == BlockSide.South ? rect.yMin <= -halfZ + Epsilon : rect.yMax >= halfZ - Epsilon;
                default:
                    start = rect.yMin + halfZ;
                    end = rect.yMax + halfZ;
                    return side == BlockSide.West ? rect.xMin <= -halfX + Epsilon : rect.xMax >= halfX - Epsilon;
            }
        }

        private static List<(float start, float end)> Uncovered(List<(float start, float end)> covered, float length)
        {
            var result = new List<(float, float)>();
            float cursor = 0f;
            foreach ((float start, float end) in covered.OrderBy(interval => interval.start))
            {
                if (start > cursor + Epsilon)
                    result.Add((cursor, Mathf.Min(start, length)));
                cursor = Mathf.Max(cursor, end);
                if (cursor >= length)
                    break;
            }
            if (cursor < length - Epsilon)
                result.Add((cursor, length));
            return result;
        }

        private void PlaceDecorations(Transform parent)
        {
            GameObject[] prefabs = settings.decorationPrefabs ?? Array.Empty<GameObject>();
            if (prefabs.Length == 0 || settings.decorationCount == 0)
                return;
            int failed = 0;
            for (int index = 0; index < settings.decorationCount; index++)
            {
                GameObject prefab = prefabs[random.Next(prefabs.Length)];
                float yaw = settings.randomDecorationYaw ? (float)(random.NextDouble() * 360.0) : 0f;
                GameObject instance = Spawn(prefab, parent, yaw);
                Bounds bounds = MeasureIn(instance, blockSpace);
                bool success = false;
                if (bounds.size.x <= Size.x + Epsilon && bounds.size.z <= Size.y + Epsilon)
                {
                    for (int attempt = 0; attempt < DecorationAttempts && !success; attempt++)
                    {
                        float x = -halfX + (float)random.NextDouble() * (Size.x - bounds.size.x);
                        float z = -halfZ + (float)random.NextDouble() * (Size.y - bounds.size.z);
                        Rect rect = new Rect(x, z, bounds.size.x, bounds.size.z);
                        if (Blocked(rect, settings.decorationSpacing))
                            continue;
                        instance.transform.localPosition += new Vector3(x - bounds.min.x, -bounds.min.y, z - bounds.min.z);
                        Finish(instance, rect);
                        success = true;
                    }
                }
                if (!success)
                {
                    Object.DestroyImmediate(instance);
                    failed++;
                }
            }
            if (failed > 0)
                Debug.LogWarning($"Street block '{settings.name}': {failed} of {settings.decorationCount} decorations did not fit without blocking buildings, walls or the entrance walkway.", settings);
        }

        private bool Blocked(Rect rect, float clearance)
        {
            Rect expanded = Rect.MinMaxRect(rect.xMin - clearance, rect.yMin - clearance, rect.xMax + clearance, rect.yMax + clearance);
            return !InsideFloor(rect) || occupied.Any(other => Overlaps(expanded, other)) || paths.Any(path => Overlaps(rect, path));
        }

        private bool InsideFloor(Rect rect) =>
            rect.xMin >= -halfX - Epsilon && rect.xMax <= halfX + Epsilon &&
            rect.yMin >= -halfZ - Epsilon && rect.yMax <= halfZ + Epsilon;

        private static bool Overlaps(Rect a, Rect b) =>
            a.xMin < b.xMax - Epsilon && b.xMin < a.xMax - Epsilon &&
            a.yMin < b.yMax - Epsilon && b.yMin < a.yMax - Epsilon;

        private GameObject PlaceAt(GameObject prefab, Transform parent, float yaw, Rect rect)
        {
            GameObject instance = Spawn(prefab, parent, yaw);
            Bounds bounds = MeasureIn(instance, blockSpace);
            instance.transform.localPosition += new Vector3(rect.xMin - bounds.min.x, -bounds.min.y, rect.yMin - bounds.min.z);
            Finish(instance, rect);
            return instance;
        }

        private void Finish(GameObject instance, Rect rect)
        {
            occupied.Add(rect);
            placed.Add(instance);
            if (settings.addMissingColliders && instance.GetComponentInChildren<Collider>(true) == null)
            {
                Bounds local = MeasureIn(instance, instance.transform);
                BoxCollider collider = instance.AddComponent<BoxCollider>();
                collider.center = local.center;
                collider.size = local.size;
            }
        }

        private void VerifyInsideFloor()
        {
            foreach (GameObject instance in placed)
            {
                Bounds bounds = MeasureIn(instance, blockSpace);
                if (!InsideFloor(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z)))
                    throw new StreetBlockGenerationException($"'{instance.name}' extends outside the floor set.");
            }
        }

        /// <summary>Unrotated size of a prefab (X = width, Z = depth with its front facing +Z).</summary>
        private Vector3 SizeOf(GameObject prefab)
        {
            if (prefabSizes.TryGetValue(prefab, out Vector3 size))
                return size;
            var temporary = new GameObject("Street block measurement");
            temporary.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                GameObject instance = Spawn(prefab, temporary.transform, 0f);
                size = MeasureIn(instance, temporary.transform).size;
            }
            finally
            {
                Object.DestroyImmediate(temporary);
            }
            prefabSizes[prefab] = size;
            return size;
        }

        private static GameObject Spawn(GameObject prefab, Transform parent, float yaw)
        {
            GameObject instance = PrefabUtility.IsPartOfPrefabAsset(prefab)
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)
                : Object.Instantiate(prefab, parent);
            instance.SetActive(true);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return instance;
        }

        private static Transform Group(Transform root, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(root, false);
            return group.transform;
        }
    }
}
