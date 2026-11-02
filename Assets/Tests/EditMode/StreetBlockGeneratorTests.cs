using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class StreetBlockGeneratorTests
{
    private const float Half = 6f;
    private readonly List<Object> objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int index = objects.Count - 1; index >= 0; index--)
        {
            if (objects[index] != null)
                Object.DestroyImmediate(objects[index]);
        }
        objects.Clear();
    }

    [Test]
    public void GeneratesASealedBlockInsideItsFootprintWithClearEntrances()
    {
        StreetBlockGenerator generator = NewGenerator();
        generator.transform.SetPositionAndRotation(new Vector3(10f, 2f, 5f), Quaternion.Euler(0f, 30f, 0f));
        generator.entranceCount = 3;
        generator.buildingPrefabs = new[] { Template("Small House", 3f, 2.5f, 2.5f), Template("Wide House", 4f, 3f, 3f) };
        generator.wallPrefabs = new[] { Template("Fence", 1f, 2f, 0.3f) };
        generator.decorationPrefabs = new[] { Template("Crate", 0.5f, 0.5f, 0.5f), Template("Bench", 0.8f, 1f, 0.4f) };
        generator.decorationCount = 10;

        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        StreetBlock block = generator.GetComponent<StreetBlock>();
        Assert.That(block.Size, Is.EqualTo(new Vector2(12f, 12f)));
        Assert.That(block.SizeInCells, Is.EqualTo(new Vector2Int(2, 2)));
        Assert.That(block.CellSize, Is.EqualTo(new Vector2(6f, 6f)));
        Assert.That(block.Entrances.Select(entrance => entrance.Side).Distinct().Count(), Is.EqualTo(3));
        foreach (StreetBlockEntrance entrance in block.Entrances)
        {
            Assert.That(entrance.Width, Is.EqualTo(4f).Within(0.001f));
            Vector3 center = entrance.LocalCenter;
            float along = entrance.Side == BlockSide.South || entrance.Side == BlockSide.North ? center.x : center.z;
            float across = entrance.Side == BlockSide.South || entrance.Side == BlockSide.North ? center.z : center.x;
            Assert.That(Mathf.Abs(along), Is.EqualTo(3f).Within(0.001f), "Entrances sit in the middle of a grid cell edge.");
            Assert.That(Mathf.Abs(across), Is.EqualTo(Half).Within(0.001f));
            Assert.That(Vector3.Dot(center.normalized, entrance.Side.Outward()), Is.GreaterThan(0.5f));
        }

        Transform generated = generator.transform.Find(StreetBlockBuilder.GeneratedName);
        Transform floor = generated.Find("Floor");
        Assert.That(floor.childCount, Is.EqualTo(36));
        Assert.That(floor.GetComponent<BoxCollider>().size.x, Is.EqualTo(12f).Within(0.001f));
        Assert.That(generated.Find("Buildings").childCount, Is.GreaterThan(0));
        Assert.That(generated.Find("Decorations").childCount, Is.GreaterThan(0));

        foreach (Transform piece in PlacedPieces(generated))
        {
            Rect rect = Footprint(piece.gameObject, generator.transform);
            Assert.That(rect.xMin >= -Half - 0.001f && rect.xMax <= Half + 0.001f &&
                rect.yMin >= -Half - 0.001f && rect.yMax <= Half + 0.001f, Is.True, $"{piece.name} leaves the floor: {rect}");
            Assert.That(piece.GetComponentInChildren<Collider>(), Is.Not.Null, $"{piece.name} needs a collider.");
            foreach (StreetBlockEntrance entrance in block.Entrances)
                Assert.That(Overlaps(rect, Corridor(entrance, 3f)), Is.False, $"{piece.name} blocks the {entrance.Side} entrance.");
        }

        AssertEdgesSealedExceptEntrances(generator.transform, block);
    }

    [Test]
    public void SameSeedRebuildsTheSameLayout()
    {
        StreetBlockGenerator generator = NewGenerator();
        generator.buildingPrefabs = new[] { Template("Small House", 3f, 2.5f, 2.5f), Template("Wide House", 4f, 3f, 3f) };
        generator.decorationPrefabs = new[] { Template("Crate", 0.5f, 0.5f, 0.5f) };
        generator.entrancePlacement = EntrancePlacement.Random;
        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        string first = Snapshot(generator);
        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        Assert.That(generator.transform.Find(StreetBlockBuilder.GeneratedName).parent.childCount, Is.EqualTo(1));
        Assert.That(Snapshot(generator), Is.EqualTo(first));
        generator.seed++;
        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        Assert.That(Snapshot(generator), Is.Not.EqualTo(first));
    }

    [Test]
    public void BuildingsWhoseRoofsDoNotFitFailWithoutReplacingTheLastBlock()
    {
        StreetBlockGenerator generator = NewGenerator();
        generator.buildingPrefabs = new[] { Template("Small House", 3f, 2.5f, 2.5f) };
        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        Transform previous = generator.transform.Find(StreetBlockBuilder.GeneratedName);
        int buildings = previous.Find("Buildings").childCount;

        GameObject wideRoof = Template("Wide Roof House", 11f, 3f, 3f);
        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.transform.SetParent(wideRoof.transform, false);
        roof.transform.localPosition = new Vector3(0f, 3.2f, -1.5f);
        roof.transform.localScale = new Vector3(13f, 0.4f, 3.4f);
        generator.buildingPrefabs = new[] { generator.buildingPrefabs[0], wideRoof };
        LogAssert.Expect(LogType.Error, new Regex("Wide Roof House.*13 m wide.*12 x 12 m"));
        Assert.That(StreetBlockBuilder.Generate(generator), Is.False);
        Assert.That(generator.transform.Find(StreetBlockBuilder.GeneratedName), Is.SameAs(previous));
        Assert.That(previous.Find("Buildings").childCount, Is.EqualTo(buildings));
        Assert.That(generator.transform.childCount, Is.EqualTo(1), "A failed build must not leave partial content.");
    }

    [Test]
    public void ManualSidesReceiveObstaclesAndInvalidSettingsAreReported()
    {
        StreetBlockGenerator generator = NewGenerator();
        generator.sideMode = EntranceSideMode.Manual;
        generator.manualSides = BlockSides.West | BlockSides.East;
        generator.entranceCount = 4;
        GameObject barricade = Template("Barricade", 4f, 2f, 0.3f);
        barricade.AddComponent<EntranceObstacle>();
        generator.obstaclePrefabs = new[] { barricade };
        generator.obstacleCount = 1;
        generator.buildingPrefabs = new[] { Template("Small House", 3f, 2.5f, 2.5f) };

        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        StreetBlock block = generator.GetComponent<StreetBlock>();
        Assert.That(block.Entrances.Select(entrance => entrance.Side), Is.EquivalentTo(new[] { BlockSide.West, BlockSide.East }));
        StreetBlockEntrance blocked = block.Entrances.Single(entrance => entrance.Obstacle != null);
        Rect obstacle = Footprint(blocked.Obstacle.gameObject, generator.transform);
        Assert.That(obstacle.center.y, Is.EqualTo(blocked.LocalCenter.z).Within(0.001f));
        Assert.That(blocked.Side == BlockSide.West ? obstacle.xMin : -obstacle.xMax, Is.EqualTo(-Half).Within(0.001f));
        Assert.That(obstacle.height, Is.EqualTo(4f).Within(0.001f), "The obstacle's width spans the opening.");

        AssertFails(generator, g => g.obstaclePrefabs = new[] { Template("Plain Crate", 1f, 1f, 1f) }, "needs an EntranceObstacle");
        AssertFails(generator, g => g.obstacleCount = 3, "cannot exceed the number of entrances");
        AssertFails(generator, g => g.entranceWidthTiles = 4, "cannot exceed Tiles Per Cell");
        AssertFails(generator, g => g.sizeInTiles = new Vector2Int(5, 6), "multiple of Tiles Per Cell");
        AssertFails(generator, g => g.floorTile = null, "Floor Tile");
    }

    [Test]
    public void ObstacleRequirementsNeedKeyDestructionOrBoth()
    {
        EntranceObstacle key = NewObstacle(ObstacleRequirement.Key);
        int cleared = 0;
        key.cleared.AddListener(() => cleared++);
        Assert.That(key.TryDestroy(), Is.False);
        Assert.That(key.TryUseKey(), Is.True);
        Assert.That(key.IsCleared, Is.True);
        Assert.That(key.gameObject.activeSelf, Is.False);
        Assert.That(key.TryUseKey(), Is.False);
        Assert.That(cleared, Is.EqualTo(1));

        EntranceObstacle destruction = NewObstacle(ObstacleRequirement.Destruction);
        Assert.That(destruction.TryUseKey(), Is.False);
        Assert.That(destruction.TryDestroy(), Is.True);
        Assert.That(destruction.IsCleared, Is.True);

        EntranceObstacle both = NewObstacle(ObstacleRequirement.KeyAndDestruction);
        Assert.That(both.TryDestroy(), Is.True);
        Assert.That(both.IsCleared, Is.False);
        Assert.That(both.gameObject.activeSelf, Is.True);
        Assert.That(both.TryUseKey(), Is.True);
        Assert.That(both.IsCleared, Is.True);
    }

    [Test]
    public void ProjectStreetAssetsBuildAValidBlockAndTheWorkshopIsReady()
    {
        StreetBlockGenerator generator = NewGenerator();
        generator.floorTile = Asset("Assets/Prefab/Streets Object/Path_Road.prefab");
        generator.sizeInTiles = new Vector2Int(4, 4);
        generator.tilesPerCell = 2;
        generator.buildingPrefabs = Enumerable.Range(1, 8).Select(index => Asset($"Assets/Prefab/Houses/House {index}.prefab")).ToArray();
        generator.decorationPrefabs = new[] { "Crate Box", "Street Light 1", "Vase_Pot" }
            .Select(name => Asset($"Assets/Prefab/Streets Object/{name}.prefab")).ToArray();
        generator.obstaclePrefabs = new[] { Asset("Assets/PREFABS/StreetBlocks/PlaceholderBarricade.prefab") };
        generator.obstacleCount = 1;
        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        StreetBlock block = generator.GetComponent<StreetBlock>();
        Assert.That(block.Size, Is.EqualTo(new Vector2(12f, 12f)));
        Assert.That(block.Entrances.Count(entrance => entrance.Obstacle != null), Is.EqualTo(1));
        foreach (Transform piece in PlacedPieces(generator.transform.Find(StreetBlockBuilder.GeneratedName)))
        {
            Rect rect = Footprint(piece.gameObject, generator.transform);
            Assert.That(rect.xMin >= -Half - 0.001f && rect.xMax <= Half + 0.001f &&
                rect.yMin >= -Half - 0.001f && rect.yMax <= Half + 0.001f, Is.True, $"{piece.name} leaves the floor.");
        }
        AssertEdgesSealedExceptEntrances(generator.transform, block);

        GameObject sample = Asset("Assets/PREFABS/StreetBlocks/StreetBlock_Sample.prefab");
        Assert.That(sample.GetComponent<StreetBlock>().Entrances.Count, Is.GreaterThan(0));
        Assert.That(sample.transform.Find(StreetBlockBuilder.GeneratedName), Is.Not.Null);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/StreetBlockWorkshop.unity", OpenSceneMode.Additive);
        try
        {
            StreetBlockGenerator workshop = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<StreetBlockGenerator>(true)).Single();
            Assert.That(workshop.floorTile, Is.EqualTo(generator.floorTile));
            Assert.That(workshop.buildingPrefabs.Length, Is.EqualTo(8));
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(workshop.gameObject), Is.False, "The workshop block must stay regenerable.");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void WrapEdgesRunsWallsAroundTheBlockAndSetsBuildingsBehindThem()
    {
        StreetBlockGenerator generator = NewGenerator();
        generator.entranceCount = 2;
        generator.buildingPrefabs = new[] { Template("Small House", 3f, 2.5f, 2.5f), Template("Wide House", 4f, 3f, 3f) };
        generator.wallPrefabs = new[] { Template("Fence", 1f, 2f, 0.3f) };
        generator.decorationPrefabs = new[] { Template("Crate", 0.5f, 0.5f, 0.5f) };
        generator.decorationCount = 4;

        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        Transform generated = generator.transform.Find(StreetBlockBuilder.GeneratedName);
        Assert.That(generated.Find("Buildings").Cast<Transform>().Any(building =>
            EdgeDistance(Footprint(building.gameObject, generator.transform)) < 0.001f), Is.True,
            "Fill Gaps keeps buildings on the block edge.");

        generator.wallMode = WallMode.WrapEdges;
        Assert.That(StreetBlockBuilder.Generate(generator), Is.True);
        generated = generator.transform.Find(StreetBlockBuilder.GeneratedName);
        StreetBlock block = generator.GetComponent<StreetBlock>();
        Transform walls = generated.Find("Walls");
        Assert.That(walls.childCount, Is.GreaterThanOrEqualTo(30), "Walls must run along all four edges.");
        foreach (BlockSide side in BlockSideUtility.All)
            Assert.That(walls.Cast<Transform>().Count(wall => wall.name.EndsWith($"({side})")), Is.GreaterThan(0), side.ToString());
        Assert.That(generated.Find("Buildings").childCount, Is.GreaterThan(0));
        foreach (Transform building in generated.Find("Buildings"))
        {
            Rect rect = Footprint(building.gameObject, generator.transform);
            Assert.That(EdgeDistance(rect), Is.GreaterThanOrEqualTo(0.3f - 0.001f), $"{building.name} is not behind the wall.");
            foreach (Transform wall in walls)
                Assert.That(Overlaps(rect, Footprint(wall.gameObject, generator.transform)), Is.False, $"{building.name} overlaps {wall.name}.");
        }
        foreach (Transform piece in PlacedPieces(generated))
        {
            Rect rect = Footprint(piece.gameObject, generator.transform);
            foreach (StreetBlockEntrance entrance in block.Entrances)
                Assert.That(Overlaps(rect, Corridor(entrance, 3f)), Is.False, $"{piece.name} blocks the {entrance.Side} entrance.");
        }
        AssertEdgesSealedExceptEntrances(generator.transform, block);
    }

    private static float EdgeDistance(Rect rect) =>
        Mathf.Min(Mathf.Min(rect.xMin + Half, Half - rect.xMax), Mathf.Min(rect.yMin + Half, Half - rect.yMax));

    private void AssertFails(StreetBlockGenerator generator, System.Action<StreetBlockGenerator> change, string message)
    {
        var copy = Track(Object.Instantiate(generator.gameObject)).GetComponent<StreetBlockGenerator>();
        Object.DestroyImmediate(copy.transform.Find(StreetBlockBuilder.GeneratedName).gameObject);
        change(copy);
        LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(message)));
        Assert.That(StreetBlockBuilder.Generate(copy), Is.False, message);
    }

    private static void AssertEdgesSealedExceptEntrances(Transform block, StreetBlock layout)
    {
        Physics.SyncTransforms();
        float half = layout.Size.x * 0.5f;
        foreach (BlockSide side in BlockSideUtility.All)
        {
            Vector3 outward = side.Outward();
            Vector3 along = Vector3.Cross(Vector3.up, outward);
            StreetBlockEntrance entrance = layout.GetEntrance(side);
            for (float offset = -half + 0.1f; offset <= half - 0.1f; offset += 0.25f)
            {
                Vector3 local = outward * (half + 1f) + along * offset + Vector3.up;
                Vector3 origin = block.TransformPoint(local);
                Vector3 direction = block.TransformDirection(-outward);
                bool hit = Physics.Raycast(origin, direction, out RaycastHit info, 1.05f);
                float fromGap = entrance == null ? float.MaxValue
                    : Mathf.Abs(Vector3.Dot(local - entrance.LocalCenter, along)) - entrance.Width * 0.5f;
                if (fromGap < -0.2f && entrance.Obstacle == null)
                    Assert.That(hit, Is.False, $"{side} entrance is blocked at {offset:0.##} by {(hit ? info.collider.name : "")}.");
                else if (fromGap > 0.2f)
                    Assert.That(hit, Is.True, $"{side} edge has a gap at {offset:0.##}.");
            }
        }
    }

    private static IEnumerable<Transform> PlacedPieces(Transform generated)
    {
        foreach (string group in new[] { "Obstacles", "Buildings", "Walls", "Decorations" })
        {
            foreach (Transform child in generated.Find(group))
                yield return child;
        }
    }

    private static Rect Corridor(StreetBlockEntrance entrance, float depth)
    {
        Vector3 outward = entrance.Side.Outward();
        Vector3 along = Vector3.Cross(Vector3.up, outward) * (entrance.Width * 0.5f - 0.05f);
        Vector3 a = entrance.LocalCenter - along;
        Vector3 b = entrance.LocalCenter + along - outward * depth;
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
    }

    private static Rect Footprint(GameObject piece, Transform space)
    {
        Bounds bounds = StreetBlockBuilder.MeasureIn(piece, space);
        return Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
    }

    private static bool Overlaps(Rect a, Rect b) =>
        a.xMin < b.xMax - 0.001f && b.xMin < a.xMax - 0.001f && a.yMin < b.yMax - 0.001f && b.yMin < a.yMax - 0.001f;

    private static string Snapshot(StreetBlockGenerator generator)
    {
        Transform generated = generator.transform.Find(StreetBlockBuilder.GeneratedName);
        return string.Join("\n", generated.GetComponentsInChildren<Transform>()
            .Select(t => $"{t.name} {t.localPosition.x:0.###} {t.localPosition.z:0.###} {t.localEulerAngles.y:0.#}"));
    }

    private StreetBlockGenerator NewGenerator()
    {
        GameObject block = Track(new GameObject("Test Street Block"));
        block.AddComponent<StreetBlock>();
        StreetBlockGenerator generator = block.AddComponent<StreetBlockGenerator>();
        generator.floorTile = Template("Tile", 2f, 0.1f, 2f);
        generator.sizeInTiles = new Vector2Int(6, 6);
        generator.tilesPerCell = 3;
        generator.entranceWidthTiles = 2;
        generator.decorationCount = 4;
        generator.seed = 42;
        return generator;
    }

    /// <summary>Inactive template whose +Z front sits at its pivot and whose body extends backwards, like the project houses.</summary>
    private GameObject Template(string name, float width, float height, float depth)
    {
        GameObject root = Track(new GameObject(name));
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0.3f, height * 0.5f + 0.2f, -depth * 0.5f);
        body.transform.localScale = new Vector3(width, height, depth);
        root.SetActive(false);
        return root;
    }

    private EntranceObstacle NewObstacle(ObstacleRequirement requirement)
    {
        GameObject gameObject = Track(new GameObject(requirement.ToString()));
        EntranceObstacle obstacle = gameObject.AddComponent<EntranceObstacle>();
        var serialized = new SerializedObject(obstacle);
        serialized.FindProperty("requirement").enumValueIndex = (int)requirement;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return obstacle;
    }

    private static GameObject Asset(string path)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    private T Track<T>(T value) where T : Object
    {
        objects.Add(value);
        return value;
    }
}
