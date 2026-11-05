using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MapTravelTests
{
    private const string BlockFolder = "Assets/PREFABS/StreetBlocks";

    private static StreetBlock Block(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{BlockFolder}/{name}.prefab").GetComponent<StreetBlock>();

    private static StreetBlock[] Connectors => new[] { Block("StreetBlock_2Entrances_1"), Block("StreetBlock_3Entrances_1"), Block("IntersectStreetBlock_1") };
    private static StreetBlock[] EndCaps => new[] { Block("EndBlockStreetBlock_1") };
    private static StreetBlock[] Spawns => new[] { Block("SpawnBlockStreetBlock_1") };

    private struct WorldEntrance
    {
        public int Block;
        public Vector2 Center;
        public BlockSide Side;
        public float Width;
    }

    private static List<WorldEntrance> Entrances(IReadOnlyList<MapBlockPlacement> layout)
    {
        var result = new List<WorldEntrance>();
        for (int i = 0; i < layout.Count; i++)
        {
            MapBlockPlacement placement = layout[i];
            foreach (StreetBlockEntrance entrance in placement.Prefab.Entrances)
            {
                Vector3 world = placement.Position + placement.Rotation * entrance.LocalCenter;
                result.Add(new WorldEntrance
                {
                    Block = i,
                    Center = new Vector2(world.x, world.z),
                    Side = MapLayoutPlanner.Rotate(entrance.Side, placement.QuarterTurns),
                    Width = entrance.Width
                });
            }
        }
        return result;
    }

    private static Rect Footprint(MapBlockPlacement placement)
    {
        Vector2 size = placement.Prefab.Size;
        if (placement.QuarterTurns % 2 != 0)
            size = new Vector2(size.y, size.x);
        return new Rect(new Vector2(placement.Position.x, placement.Position.z) - size / 2f, size);
    }

    private static List<MapBlockPlacement> Plan(int seed, int count = 6)
    {
        Assert.That(MapLayoutPlanner.TryPlan(Connectors, EndCaps, Spawns, count, seed, 100, out List<MapBlockPlacement> layout, out string error),
            Is.True, error);
        return layout;
    }

    [Test]
    public void RotationMathMatchesUnityYaw()
    {
        for (int turns = 0; turns < 4; turns++)
        {
            Quaternion rotation = Quaternion.Euler(0f, 90f * turns, 0f);
            foreach (BlockSide side in BlockSideUtility.All)
            {
                Vector3 expected = rotation * side.Outward();
                Assert.That(Vector3.Distance(MapLayoutPlanner.Rotate(side, turns).Outward(), expected), Is.LessThan(0.001f), $"{side} x{turns}");
            }

            Vector3 offset = rotation * new Vector3(2f, 0f, 5f);
            Vector2 rotated = MapLayoutPlanner.Rotate(new Vector2(2f, 5f), turns);
            Assert.That(Vector2.Distance(rotated, new Vector2(offset.x, offset.z)), Is.LessThan(0.001f));
        }
    }

    [Test]
    public void ProjectBlocksHaveExpectedEntranceCounts()
    {
        Assert.That(Block("IntersectStreetBlock_1").Entrances.Count, Is.EqualTo(4));
        Assert.That(Block("StreetBlock_2Entrances_1").Entrances.Count, Is.EqualTo(2));
        Assert.That(Block("StreetBlock_3Entrances_1").Entrances.Count, Is.EqualTo(3));
        Assert.That(Block("EndBlockStreetBlock_1").Entrances.Count, Is.EqualTo(1));
        Assert.That(Block("SpawnBlockStreetBlock_1").Entrances.Count, Is.EqualTo(1));
    }

    [Test]
    public void LayoutsConnectEveryEntranceWithoutOverlaps([NUnit.Framework.Range(1, 25)] int seed)
    {
        List<MapBlockPlacement> layout = Plan(seed * 7919);
        Assert.That(layout.Count(p => p.Role == MapBlockRole.Connector), Is.EqualTo(6));
        Assert.That(layout.Count(p => p.Role == MapBlockRole.Spawn), Is.EqualTo(1));
        Assert.That(layout.Where(p => p.Role == MapBlockRole.EndCap).All(p => p.Prefab.Entrances.Count == 1), Is.True);

        for (int a = 0; a < layout.Count; a++)
        {
            for (int b = a + 1; b < layout.Count; b++)
            {
                Rect ra = Footprint(layout[a]), rb = Footprint(layout[b]);
                float overlapX = Mathf.Min(ra.xMax, rb.xMax) - Mathf.Max(ra.xMin, rb.xMin);
                float overlapY = Mathf.Min(ra.yMax, rb.yMax) - Mathf.Max(ra.yMin, rb.yMin);
                Assert.That(overlapX <= 0.01f || overlapY <= 0.01f, $"Blocks {a} and {b} overlap.");
            }
        }

        List<WorldEntrance> entrances = Entrances(layout);
        foreach (WorldEntrance entrance in entrances)
        {
            List<WorldEntrance> partners = entrances.Where(other => other.Block != entrance.Block &&
                Vector2.Distance(other.Center, entrance.Center) < 0.05f).ToList();
            Assert.That(partners.Count, Is.EqualTo(1), $"Entrance of block {entrance.Block} at {entrance.Center} is not connected exactly once.");
            Assert.That(partners[0].Side, Is.EqualTo(entrance.Side.Opposite()));
            Assert.That(partners[0].Width, Is.EqualTo(entrance.Width).Within(0.05f));
        }
    }

    [Test]
    public void SpawnBlockIsPlacedTowardsTheEdge([NUnit.Framework.Range(1, 10)] int seed)
    {
        List<MapBlockPlacement> layout = Plan(seed * 104729);
        Vector3[] connectors = layout.Where(p => p.Role == MapBlockRole.Connector).Select(p => p.Position).ToArray();
        Vector3 centroid = connectors.Aggregate(Vector3.zero, (sum, p) => sum + p) / connectors.Length;
        float spawnDistance = Vector3.Distance(layout.Single(p => p.Role == MapBlockRole.Spawn).Position, centroid);
        Assert.That(spawnDistance, Is.GreaterThan(connectors.Average(p => Vector3.Distance(p, centroid))));
        Assert.That(layout.Single(p => p.Role == MapBlockRole.Spawn).Prefab.Entrances.Count, Is.EqualTo(1));
    }

    [Test]
    public void SameSeedProducesSameLayout()
    {
        List<MapBlockPlacement> first = Plan(424242), second = Plan(424242);
        Assert.That(second.Count, Is.EqualTo(first.Count));
        for (int i = 0; i < first.Count; i++)
        {
            Assert.That(second[i].Prefab, Is.SameAs(first[i].Prefab));
            Assert.That(second[i].Position, Is.EqualTo(first[i].Position));
            Assert.That(second[i].QuarterTurns, Is.EqualTo(first[i].QuarterTurns));
        }
    }

    [Test]
    public void ZeroConnectorsJoinSpawnBlockToAnEndCap()
    {
        List<MapBlockPlacement> layout = Plan(5, 0);
        Assert.That(layout.Select(p => p.Role), Is.EqualTo(new[] { MapBlockRole.Spawn, MapBlockRole.EndCap }));
    }

    [Test]
    public void InvalidPoolsReportErrors()
    {
        Assert.That(MapLayoutPlanner.TryPlan(Connectors, new StreetBlock[0], Spawns, 3, 1, 10, out _, out string error), Is.False);
        StringAssert.Contains("End Cap", error);
        Assert.That(MapLayoutPlanner.TryPlan(Connectors, EndCaps, Connectors, 3, 1, 10, out _, out error), Is.False);
        StringAssert.Contains("spawn blocks need exactly 1", error);
        Assert.That(MapLayoutPlanner.TryPlan(EndCaps, EndCaps, Spawns, 3, 1, 10, out _, out error), Is.False);
        StringAssert.Contains("connectors need at least 2", error);
        Assert.That(MapLayoutPlanner.TryPlan(new StreetBlock[0], EndCaps, Spawns, 3, 1, 10, out _, out error), Is.False);
        StringAssert.Contains("Connector Blocks", error);
    }

    [Test]
    public void MapManagerBuildsTheLayoutInEditMode()
    {
        var root = new GameObject("MapManager Test");
        try
        {
            var manager = root.AddComponent<MapManager>();
            var so = new SerializedObject(manager);
            void Fill(string property, StreetBlock[] blocks)
            {
                SerializedProperty list = so.FindProperty(property);
                list.arraySize = blocks.Length;
                for (int i = 0; i < blocks.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = blocks[i];
            }
            Fill("connectorBlocks", Connectors);
            Fill("endCapBlocks", EndCaps);
            Fill("spawnBlocks", Spawns);
            so.FindProperty("connectorBlockCount").intValue = 4;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(manager.Build(77), Is.True);
            Assert.That(manager.IsDone && manager.IsBuilt, Is.True);
            Assert.That(manager.GeneratedRoot.childCount, Is.EqualTo(manager.Layout.Count));
            Assert.That(manager.GeneratedRoot.GetComponentsInChildren<PlayerSpawnPoints>(true).Length, Is.EqualTo(1));
            for (int i = 0; i < manager.Layout.Count; i++)
                Assert.That(manager.GeneratedRoot.GetChild(i).localPosition, Is.EqualTo(manager.Layout[i].Position));

            manager.Clear();
            Assert.That(root.transform.childCount, Is.Zero);
            Assert.That(manager.IsDone, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SpawnBlocksGetUnobstructedSpawnPointsButNoTravelMap([NUnit.Framework.Range(1, 3)] int spawnIndex)
    {
        StreetBlock prefab = Block($"SpawnBlockStreetBlock_{spawnIndex}");
        Assert.That(prefab.GetComponentInChildren<MapTerminal>(true), Is.Null, "Only the base has a travel map.");
        Assert.That(prefab.Entrances.All(entrance => entrance.Obstacle == null), Is.True);

        WithManager(new[] { prefab }, null, manager =>
        {
            Assert.That(manager.Build(spawnIndex * 31), Is.True);
            int index = Enumerable.Range(0, manager.Layout.Count).Single(i => manager.Layout[i].Role == MapBlockRole.Spawn);
            StreetBlock spawn = manager.GetBlock(index);
            PlayerSpawnPoints[] all = manager.GeneratedRoot.GetComponentsInChildren<PlayerSpawnPoints>(true);
            Assert.That(all.Length, Is.EqualTo(1));
            Assert.That(all[0].transform.IsChildOf(spawn.transform), Is.True);
            Transform[] points = all[0].transform.Cast<Transform>().ToArray();
            Assert.That(points.Length, Is.EqualTo(manager.SpawnPointCount), "Six spawn points (host + five guests) fit.");

            Physics.SyncTransforms();
            var floors = Enumerable.Range(0, manager.BlockCount).Select(i => manager.GetBlock(i).FloorRoot).Where(f => f != null).ToArray();
            float radius = manager.SpawnClearanceRadius;
            for (int a = 0; a < points.Length; a++)
            {
                Vector3 p = points[a].position;
                Assert.That(spawn.ContainsXZ(p), Is.True, $"{points[a].name} is outside the spawn block.");
                Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * radius, p + Vector3.up * (manager.SpawnClearanceHeight - radius),
                    radius, ~0, QueryTriggerInteraction.Ignore);
                Collider blocker = hits.FirstOrDefault(hit => hit.transform.IsChildOf(manager.GeneratedRoot) && !floors.Any(f => hit.transform.IsChildOf(f)));
                Assert.That(blocker, Is.Null, $"{points[a].name} is obstructed by {(blocker != null ? blocker.name : "")}.");
                Assert.That(Physics.Raycast(p + Vector3.up, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore), Is.True, $"{points[a].name} has no floor.");
                for (int b = a + 1; b < points.Length; b++)
                    Assert.That(Vector3.Distance(p, points[b].position), Is.GreaterThanOrEqualTo(manager.SpawnPointSpacing - 0.01f));
            }
        });
    }

    [Test]
    public void FillerBlocksSurroundTheStreetWithoutChangingIt([NUnit.Framework.Range(1, 4)] int seed)
    {
        var created = new List<GameObject>();
        StreetBlock Filler(int cells)
        {
            var filler = new GameObject($"Test Filler {cells}x{cells}");
            filler.transform.position = new Vector3(0f, -500f, 0f);
            created.Add(filler);
            var block = filler.AddComponent<StreetBlock>();
            block.ApplyLayout(new Vector2Int(cells * 2, cells * 2), new Vector2(3f, 3f), 2, 0, new StreetBlockEntrance[0]);
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(filler.transform, false);
            body.transform.localScale = new Vector3(cells * 6f - 1f, 4f, cells * 6f - 1f);
            body.transform.localPosition = Vector3.up * 2f;
            return block;
        }

        try
        {
            StreetBlock[] fillers = { Filler(1), Filler(2) };
            List<MapBlockPlacement> plain = null;
            WithManager(Spawns, null, manager =>
            {
                Assert.That(manager.Build(seed), Is.True);
                Assert.That(manager.FillerCount, Is.Zero, "No filler pool, no fillers.");
                plain = manager.Layout.ToList();
            });

            WithManager(Spawns, fillers, manager =>
            {
                Assert.That(manager.Build(seed), Is.True);
                Assert.That(manager.Layout.Select(p => (p.Prefab, p.Position, p.QuarterTurns)),
                    Is.EqualTo(plain.Select(p => (p.Prefab, p.Position, p.QuarterTurns))), "Fillers never change the street layout.");
                Assert.That(manager.BlockCount, Is.EqualTo(manager.Layout.Count), "Fillers are not street blocks.");
                Assert.That(manager.FillerCount, Is.GreaterThan(0));

                var rects = manager.Layout.Select(Footprint).ToList();
                Rect street = rects.Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                    Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
                for (int i = 0; i < manager.FillerCount; i++)
                {
                    StreetBlock filler = manager.GetFiller(i);
                    Assert.That(filler.Entrances.Count, Is.Zero);
                    Vector2 size = filler.Size;
                    if (Mathf.RoundToInt(filler.transform.eulerAngles.y / 90f) % 2 != 0)
                        size = new Vector2(size.y, size.x);
                    Vector3 local = manager.GeneratedRoot.InverseTransformPoint(filler.transform.position);
                    rects.Add(new Rect(new Vector2(local.x, local.z) - size / 2f, size));
                }
                for (int a = 0; a < rects.Count; a++)
                {
                    for (int b = a + 1; b < rects.Count; b++)
                    {
                        float overlapX = Mathf.Min(rects[a].xMax, rects[b].xMax) - Mathf.Max(rects[a].xMin, rects[b].xMin);
                        float overlapY = Mathf.Min(rects[a].yMax, rects[b].yMax) - Mathf.Max(rects[a].yMin, rects[b].yMin);
                        Assert.That(overlapX <= 0.01f || overlapY <= 0.01f, $"Blocks/fillers {a} and {b} overlap.");
                    }
                }

                // With a one-cell filler every cell of the bounding box plus the 2-cell margin is filled (fillers may overhang it).
                AssertCellsCovered(manager, rects, street, 12f);
                Assert.That(manager.PluggedCellCount, Is.Zero, "A one-cell filler leaves nothing to plug.");
                Assert.That(manager.GetFiller(0).transform.parent.name, Is.EqualTo("Fillers"));
            });
        }
        finally
        {
            foreach (GameObject obj in created)
                Object.DestroyImmediate(obj);
        }
    }

    [Test]
    public void GapsNoFillerFitsArePluggedWithFloorAndASmallBuilding([NUnit.Framework.Range(1, 4)] int seed)
    {
        var created = new List<GameObject>();
        try
        {
            // Only a 3x3-cell filler (with real floor tiles), so 1-2 cell gaps between street blocks need plugs.
            var filler = new GameObject("Test Filler 3x3");
            filler.transform.position = new Vector3(0f, -500f, 0f);
            created.Add(filler);
            var block = filler.AddComponent<StreetBlock>();
            block.ApplyLayout(new Vector2Int(6, 6), new Vector2(3f, 3f), 2, 0, new StreetBlockEntrance[0]);
            var floor = new GameObject("Floor").transform;
            var generated = new GameObject("Generated").transform;
            generated.SetParent(filler.transform, false);
            floor.SetParent(generated, false);
            for (int x = 0; x < 6; x++)
            {
                for (int z = 0; z < 6; z++)
                {
                    GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Tile ({x},{z})";
                    tile.transform.SetParent(floor, false);
                    tile.transform.localScale = new Vector3(3f, 0.2f, 3f);
                    tile.transform.localPosition = new Vector3(-9f + 1.5f + x * 3f, -0.1f, -9f + 1.5f + z * 3f);
                }
            }
            GameObject small = GameObject.CreatePrimitive(PrimitiveType.Cube);
            small.name = "Small Gap House";
            small.transform.position = new Vector3(0f, -600f, 0f);
            small.transform.localScale = new Vector3(4f, 3f, 5f);
            created.Add(small);
            GameObject huge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            huge.name = "Huge House";
            huge.transform.position = new Vector3(0f, -700f, 0f);
            huge.transform.localScale = new Vector3(9f, 3f, 9f);
            created.Add(huge);

            WithManager(Spawns, new[] { block }, manager =>
            {
                var so = new SerializedObject(manager);
                SerializedProperty gapPrefabs = so.FindProperty("gapFillPrefabs");
                gapPrefabs.arraySize = 2;
                gapPrefabs.GetArrayElementAtIndex(0).objectReferenceValue = small;
                gapPrefabs.GetArrayElementAtIndex(1).objectReferenceValue = huge;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(manager.Build(seed), Is.True);
                Assert.That(manager.PluggedCellCount, Is.GreaterThan(0), "Gaps smaller than the only filler are plugged.");

                var rects = manager.Layout.Select(Footprint).ToList();
                Rect street = rects.Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                    Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
                for (int i = 0; i < manager.FillerCount; i++)
                {
                    Vector3 local = manager.GeneratedRoot.InverseTransformPoint(manager.GetFiller(i).transform.position);
                    rects.Add(new Rect(new Vector2(local.x, local.z) - new Vector2(9f, 9f), new Vector2(18f, 18f)));
                }
                Transform plugs = manager.GeneratedRoot.Find("Fillers/Gap Plugs");
                Assert.That(plugs, Is.Not.Null);
                var floors = plugs.Cast<Transform>().Where(t => t.name.StartsWith("Gap Floor")).ToList();
                Assert.That(floors.Count, Is.EqualTo(manager.PluggedCellCount * 4), "Each plugged cell gets 2x2 floor tiles.");
                foreach (Transform tile in floors)
                {
                    Vector3 local = manager.GeneratedRoot.InverseTransformPoint(tile.position);
                    Assert.That(local.y, Is.EqualTo(-0.1f).Within(0.01f), "Plug tiles sit at the street floor height.");
                    rects.Add(new Rect(local.x - 1.5f, local.z - 1.5f, 3f, 3f));
                }
                AssertCellsCovered(manager, rects, street, 12f);

                var buildings = plugs.Cast<Transform>().Where(t => t.name.StartsWith("Gap ") && !t.name.StartsWith("Gap Floor")).ToList();
                Assert.That(buildings.Count, Is.GreaterThan(0));
                foreach (Transform building in buildings)
                {
                    Assert.That(building.name, Does.StartWith("Gap Small Gap House"), "Prefabs larger than a cell are skipped.");
                    Vector3 local = manager.GeneratedRoot.InverseTransformPoint(building.position);
                    Vector2 cellMin = new Vector2(Mathf.Floor((local.x - street.xMin) / 6f) * 6f + street.xMin,
                        Mathf.Floor((local.z - street.yMin) / 6f) * 6f + street.yMin);
                    Vector2 offset = new Vector2(local.x, local.z) - cellMin;
                    Assert.That(offset.x, Is.EqualTo(3f).Within(0.01f), "Gap buildings are centred in their cell.");
                    Assert.That(offset.y, Is.EqualTo(3f).Within(0.01f));
                    for (int i = 0; i < manager.Layout.Count; i++)
                    {
                        Rect footprint = Footprint(manager.Layout[i]);
                        Assert.That(footprint.Contains(new Vector2(local.x, local.z)), Is.False, "Plugs never sit inside street blocks.");
                    }
                }
            });
        }
        finally
        {
            foreach (GameObject obj in created)
                Object.DestroyImmediate(obj);
        }
    }

    private static void AssertCellsCovered(MapManager manager, List<Rect> rects, Rect street, float margin)
    {
        for (float x = street.xMin - margin + 3f; x < street.xMax + margin; x += 6f)
        {
            for (float z = street.yMin - margin + 3f; z < street.yMax + margin; z += 6f)
            {
                var centre = new Vector2(x, z);
                Assert.That(rects.Any(r => r.Contains(centre)), Is.True, $"Cell at {centre} is empty (void under the map).");
            }
        }
    }

    private static void WithManager(StreetBlock[] spawns, StreetBlock[] fillers, System.Action<MapManager> check)
    {
        var root = new GameObject("MapManager Spawn Test");
        try
        {
            var manager = root.AddComponent<MapManager>();
            var so = new SerializedObject(manager);
            void Fill(string property, StreetBlock[] blocks)
            {
                SerializedProperty list = so.FindProperty(property);
                list.arraySize = blocks.Length;
                for (int i = 0; i < blocks.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = blocks[i];
            }
            Fill("connectorBlocks", Connectors);
            Fill("endCapBlocks", EndCaps);
            Fill("spawnBlocks", spawns);
            Fill("fillerBlocks", fillers ?? new StreetBlock[0]);
            so.FindProperty("connectorBlockCount").intValue = 4;
            so.ApplyModifiedPropertiesWithoutUndo();
            check(manager);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MapPlacesObstaclesFromBlockGeneratorsWhenBlocksHaveNone()
    {
        int loopBarricades = 0;
        for (int seed = 1; seed <= 8; seed++)
        {
            WithManager(Spawns, null, manager =>
            {
                Assert.That(manager.Build(seed), Is.True);
                foreach (MapJoint joint in manager.Joints)
                {
                    bool endCap = manager.GetRole(joint.BlockA) == MapBlockRole.EndCap || manager.GetRole(joint.BlockB) == MapBlockRole.EndCap;
                    bool spawn = manager.GetRole(joint.BlockA) == MapBlockRole.Spawn || manager.GetRole(joint.BlockB) == MapBlockRole.Spawn;
                    StreetBlock a = manager.GetBlock(joint.BlockA), b = manager.GetBlock(joint.BlockB);
                    EntranceObstacle obstacle = a.Entrances[joint.EntranceA].Obstacle ?? b.Entrances[joint.EntranceB].Obstacle;
                    if (spawn)
                    {
                        Assert.That(joint.Barricaded || obstacle != null, Is.False, "The spawn entrance is never blocked.");
                        continue;
                    }
                    if (endCap)
                        Assert.That(joint.Barricaded, Is.True, $"Seed {seed}: end cap joints get a gate.");
                    else if (joint.Barricaded)
                        loopBarricades++;
                    Assert.That(obstacle != null, Is.EqualTo(joint.Barricaded));
                    if (obstacle == null)
                        continue;

                    StreetBlock host = obstacle.GetComponentInParent<StreetBlock>();
                    int hostIndex = Enumerable.Range(0, manager.BlockCount).Single(i => manager.GetBlock(i) == host);
                    if (endCap)
                        Assert.That(manager.GetRole(hostIndex), Is.EqualTo(MapBlockRole.EndCap), "The gate sits in the end cap.");
                    StreetBlockEntrance entrance = host == a ? a.Entrances[joint.EntranceA] : b.Entrances[joint.EntranceB];
                    Transform placed = obstacle.transform;
                    while (placed.parent.name != "Obstacles")
                        placed = placed.parent;
                    Bounds bounds = StreetBlockBuilder.MeasureIn(placed.gameObject, host.transform);
                    Vector3 outward = entrance.Side.Outward();
                    float outer = Mathf.Max(Vector3.Dot(bounds.min, outward), Vector3.Dot(bounds.max, outward));
                    Assert.That(outer, Is.EqualTo(Vector3.Dot(entrance.LocalCenter, outward)).Within(0.05f), "The gate's outer face is on the block edge.");
                    Vector3 along = Vector3.Cross(Vector3.up, outward);
                    Assert.That(Vector3.Dot(bounds.center, along), Is.EqualTo(Vector3.Dot(entrance.LocalCenter, along)).Within(0.05f),
                        "The gate is centred on the entrance.");
                    Assert.That(bounds.min.y, Is.EqualTo(entrance.LocalCenter.y).Within(0.05f), "The gate stands on the floor.");
                    Assert.That(obstacle.GetComponentsInChildren<Collider>().Any(c => !c.isTrigger), Is.True, "The gate is solid.");
                }
            });
        }
        Assert.That(loopBarricades, Is.GreaterThan(0), "Some connector joints get a gate where a route around exists.");
    }

    [Test]
    public void EnemiesNeverCallABarricadedBlockHomeUntilItOpens()
    {
        int sealedSeen = 0;
        for (int seed = 1; seed <= 8; seed++)
        {
            WithManager(Spawns, null, manager =>
            {
                Assert.That(manager.Build(seed), Is.True);
                bool[] open = manager.FindOpenlyReachableFromSpawn();
                List<int> candidates = EnemySpawner.FindCandidateBlocks(manager, out _);
                Assert.That(candidates, Is.Not.Empty);
                for (int i = 0; i < manager.BlockCount; i++)
                {
                    if (manager.GetRole(i) != MapBlockRole.EndCap)
                        continue;
                    // Map-placed gates seal every end cap by default.
                    Assert.That(open[i], Is.False, $"Seed {seed}: gated end cap {i} is reachable.");
                    Assert.That(candidates, Has.No.Member(i), $"Seed {seed}: an enemy could spawn sealed in end cap {i}.");
                    sealedSeen++;
                }
                foreach (int candidate in candidates)
                    Assert.That(open[candidate], Is.True);

                // Clearing a gate opens its end cap for enemies again.
                int joint = Enumerable.Range(0, manager.Joints.Count).First(j => manager.Joints[j].Barricaded &&
                    (manager.GetRole(manager.Joints[j].BlockA) == MapBlockRole.EndCap || manager.GetRole(manager.Joints[j].BlockB) == MapBlockRole.EndCap));
                MapJoint gated = manager.Joints[joint];
                int endCap = manager.GetRole(gated.BlockA) == MapBlockRole.EndCap ? gated.BlockA : gated.BlockB;
                EntranceObstacle gate = manager.GetBlock(gated.BlockA).Entrances[gated.EntranceA].Obstacle ??
                                        manager.GetBlock(gated.BlockB).Entrances[gated.EntranceB].Obstacle;
                if (gate.RequiresKey)
                    gate.TryUseKey();
                if (gate.RequiresDestruction)
                    gate.TryDestroy();
                Assert.That(gate.IsCleared, Is.True);
                Assert.That(manager.IsJointOpen(joint), Is.True);
                Assert.That(manager.FindOpenlyReachableFromSpawn()[endCap], Is.True, "An opened gate lets enemies in.");
            });
        }
        Assert.That(sealedSeen, Is.GreaterThan(0));
    }

    [Test]
    public void ObstaclesNeverBlockTheSpawnBlocksEntrance([NUnit.Framework.Range(1, 6)] int seed)
    {
        BuildObstructed(seed, 3, 0.75f, (manager, remaining) =>
        {
            Assert.That(remaining.Length, Is.GreaterThan(0), "Obstacles sealing end caps are kept.");
            int spawnIndex = Enumerable.Range(0, manager.Layout.Count).Single(i => manager.Layout[i].Role == MapBlockRole.Spawn);
            StreetBlock spawn = manager.GetBlock(spawnIndex);
            foreach (StreetBlockEntrance entrance in spawn.Entrances)
            {
                Assert.That(entrance.Obstacle == null, Is.True);
                Vector3 opening = spawn.transform.TransformPoint(entrance.LocalCenter);
                foreach (EntranceObstacle obstacle in remaining)
                {
                    Vector3 offset = obstacle.transform.position - opening;
                    Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.GreaterThan(entrance.Width * 0.5f + 0.5f),
                        $"'{obstacle.name}' blocks the spawn block's entrance.");
                }
            }
        });
    }

    [Test]
    public void LoopedStreetsOnlyKeepBarricadesPlayersCanWalkAround([NUnit.Framework.Range(1, 12)] int seed)
    {
        BuildObstructed(seed, 8, 1f, (manager, remaining) =>
        {
            Assert.That(manager.Joints.Count, Is.EqualTo(manager.BlockCount - 1 + manager.LoopCount));
            foreach (MapJoint joint in manager.Joints)
            {
                StreetBlock a = manager.GetBlock(joint.BlockA);
                Vector3 centre = a.transform.TransformPoint(a.Entrances[joint.EntranceA].LocalCenter);
                int here = remaining.Count(o => Vector2.Distance(new Vector2(o.transform.position.x, o.transform.position.z),
                    new Vector2(centre.x, centre.z)) <= 1f);
                Assert.That(here, Is.EqualTo(joint.Barricaded ? 1 : 0), "Each joint keeps at most one obstacle.");
                bool endCap = manager.GetRole(joint.BlockA) == MapBlockRole.EndCap || manager.GetRole(joint.BlockB) == MapBlockRole.EndCap;
                if (endCap)
                    Assert.That(joint.Barricaded, Is.True, "End caps are the places that may be sealed off completely.");
            }

            // Walking from the spawn block without crossing a barricade reaches every block except sealed end caps.
            var reached = new HashSet<int> { Enumerable.Range(0, manager.BlockCount).Single(i => manager.GetRole(i) == MapBlockRole.Spawn) };
            var queue = new Queue<int>(reached);
            while (queue.Count > 0)
            {
                int block = queue.Dequeue();
                foreach (MapJoint joint in manager.Joints.Where(j => !j.Barricaded && (j.BlockA == block || j.BlockB == block)))
                {
                    int other = joint.BlockA == block ? joint.BlockB : joint.BlockA;
                    if (reached.Add(other))
                        queue.Enqueue(other);
                }
            }
            for (int i = 0; i < manager.BlockCount; i++)
            {
                if (manager.GetRole(i) != MapBlockRole.EndCap)
                    Assert.That(reached, Does.Contain(i), $"Block '{manager.GetBlock(i).name}' is cut off by barricades.");
            }
        });
    }

    [Test]
    public void LoopChanceBuildsLoopsWithBarricadesOnThem()
    {
        int loops = 0, loopBarricades = 0, treeLoops = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            BuildObstructed(seed, 8, 1f, (manager, _) =>
            {
                loops += manager.LoopCount;
                loopBarricades += manager.Joints.Count(j => j.Barricaded &&
                    manager.GetRole(j.BlockA) != MapBlockRole.EndCap && manager.GetRole(j.BlockB) != MapBlockRole.EndCap);
            });
            BuildObstructed(seed, 8, 0f, (manager, _) => treeLoops += manager.LoopCount);
        }
        Assert.That(loops, Is.GreaterThan(treeLoops), "Loop Chance closes extra loops.");
        Assert.That(loops, Is.GreaterThanOrEqualTo(12), "Most looped streets have at least one loop.");
        Assert.That(loopBarricades, Is.GreaterThan(0), "Some barricades stand on loops, with a way around them.");
    }

    private static void BuildObstructed(int seed, int connectorCount, float loopChance, System.Action<MapManager, EntranceObstacle[]> check)
    {
        var created = new List<GameObject>();
        StreetBlock Obstructed(StreetBlock prefab)
        {
            // Scene copies with an obstacle on every entrance stand in for blocks generated with obstacles.
            StreetBlock copy = Object.Instantiate(prefab, new Vector3(0f, -500f, 0f), Quaternion.identity);
            created.Add(copy.gameObject);
            var so = new SerializedObject(copy);
            SerializedProperty entrances = so.FindProperty("entrances");
            for (int i = 0; i < entrances.arraySize; i++)
            {
                var barricade = new GameObject($"Test Barricade {i}");
                barricade.transform.SetParent(copy.transform, false);
                barricade.transform.localPosition = copy.Entrances[i].LocalCenter;
                entrances.GetArrayElementAtIndex(i).FindPropertyRelative("obstacle").objectReferenceValue =
                    barricade.AddComponent<EntranceObstacle>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return copy;
        }

        var root = new GameObject("MapManager Obstacle Test");
        created.Add(root);
        try
        {
            var manager = root.AddComponent<MapManager>();
            var so = new SerializedObject(manager);
            void Fill(string property, StreetBlock[] blocks)
            {
                SerializedProperty list = so.FindProperty(property);
                list.arraySize = blocks.Length;
                for (int i = 0; i < blocks.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = blocks[i];
            }
            Fill("connectorBlocks", Connectors.Select(Obstructed).ToArray());
            Fill("endCapBlocks", EndCaps.Select(Obstructed).ToArray());
            Fill("spawnBlocks", Spawns.Select(Obstructed).ToArray());
            so.FindProperty("connectorBlockCount").intValue = connectorCount;
            so.FindProperty("loopChance").floatValue = loopChance;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(manager.Build(seed), Is.True);
            check(manager, manager.GeneratedRoot.GetComponentsInChildren<EntranceObstacle>(true));
        }
        finally
        {
            foreach (GameObject obj in created)
                Object.DestroyImmediate(obj);
        }
    }
    [Test]
    public void TravelPrefabsAreWired()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        Assert.That(player.GetComponent<PlayerPersistence>(), Is.Not.Null);
        Assert.That(player.GetComponent<NetworkIdentity>().assetId, Is.Not.Zero);

        var loading = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/TravelLoadingScreen.prefab").GetComponent<TravelLoadingScreen>();
        var loadingSo = new SerializedObject(loading);
        foreach (string field in new[] { "fadeImage", "loadingRoot", "loadingText", "enemyRow", "miniatureTemplate" })
            Assert.That(loadingSo.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
        Assert.That(loading.LoadingText.text, Is.EqualTo("Loading..."));
        Assert.That(loading.loadingMessage, Is.EqualTo("Loading"));
        Assert.That(loading.EnemyRow.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>(), Is.Not.Null);
        Assert.That(loading.EnemyRow.IsChildOf(loading.transform.Find("LoadingRoot")), Is.True);
        Assert.That(loading.EnemyRow.anchorMax.y, Is.Zero, "The enemy row is anchored to the bottom of the screen.");
        var miniature = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Enemies/Tikbalang.prefab").GetComponent<EnemyLoadingMiniature>();
        Assert.That(miniature, Is.Not.Null);
        Assert.That(miniature.Frames.Count, Is.EqualTo(8));
        Assert.That(miniature.Frames.All(frame => frame != null && frame.texture.name == "TikbalangLoading-Sheet"), Is.True);
        Assert.That(loading.GetComponentInChildren<UnityEngine.Video.VideoPlayer>(true), Is.Null, "The placeholder loading screen has no video.");
        Assert.That(loading.transform.Find("LoadingRoot/Background").GetComponent<UnityEngine.UI.Image>().color, Is.EqualTo(Color.black));
        Assert.That(loading.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));

        var map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/TravelMapScreen.prefab").GetComponent<TravelMapScreen>();
        var mapSo = new SerializedObject(map);
        foreach (string field in new[] { "screenRoot", "mapBackground", "destinationList", "destinationButtonTemplate", "closeButton", "confirmPanel", "confirmLabel", "enemiesLabel", "yesButton", "noButton" })
            Assert.That(mapSo.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
        Assert.That(map.EnemiesLabel.transform.IsChildOf(((GameObject)mapSo.FindProperty("confirmPanel").objectReferenceValue).transform), Is.True,
            "Enemies: [Enemy] is shown on the confirmation panel.");
        Assert.That(map.EnemiesLabel.raycastTarget, Is.False);

        var tikbalang = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/PREFABS/Enemies/Definitions/Tikbalang.asset");
        Assert.That(tikbalang, Is.Not.Null);
        Assert.That(tikbalang.DisplayName, Is.EqualTo("Tikbalang"));
        Assert.That(tikbalang.prefab, Is.Not.Null);
        Assert.That(tikbalang.prefab.GetComponent<TikbalangBrain>(), Is.Not.Null);
        Assert.That(tikbalang.prefab.assetId, Is.Not.Zero);

        var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        PlayerDamageFlash flash = hud.GetComponentInChildren<PlayerDamageFlash>(true);
        Assert.That(flash, Is.Not.Null, "The persistent HUD owns the red damage flash.");
        Assert.That(flash.FlashImage, Is.Not.Null);
        Assert.That(flash.GetComponent<Canvas>().overrideSorting, Is.True, "The flash draws above the other HUD panels.");
    }

    [Test]
    public void DestinationRollIsWeightedAndEmptyPoolsRollNothing()
    {
        var first = ScriptableObject.CreateInstance<EnemyDefinition>();
        var second = ScriptableObject.CreateInstance<EnemyDefinition>();
        try
        {
            var destination = new TravelDestination { sceneName = "StreetsTest" };
            Assert.That(destination.Roll(() => 0.5f), Is.Null);
            destination.enemyPool.Add(new EnemyRollEntry { enemy = first, weight = 1f });
            destination.enemyPool.Add(new EnemyRollEntry { enemy = second, weight = 3f });
            destination.enemyPool.Add(new EnemyRollEntry { enemy = null, weight = 50f });
            Assert.That(destination.Roll(() => 0f), Is.SameAs(first));
            Assert.That(destination.Roll(() => 0.2f), Is.SameAs(first));
            Assert.That(destination.Roll(() => 0.3f), Is.SameAs(second));
            Assert.That(destination.Roll(() => 0.9999f), Is.SameAs(second), "Empty entries are never rolled.");
            destination.enemyPool[1].weight = 0f;
            Assert.That(destination.Roll(() => 0.9f), Is.SameAs(first), "Zero-weight entries are never rolled.");
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void ScenesAreWiredForTravel()
    {
        string[] buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        Assert.That(buildScenes, Does.Contain("Assets/Scenes/SampleScene.unity"));
        Assert.That(buildScenes, Does.Contain("Assets/Scenes/StreetsTest.unity"));

        Scene hub = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = InScene<NetworkManager>(hub).Single();
            TravelDirector director = manager.GetComponent<TravelDirector>();
            Assert.That(director, Is.Not.Null);
            Assert.That(new SerializedObject(director).FindProperty("loadingScreenPrefab").objectReferenceValue, Is.Not.Null);
            MapTerminal terminal = InScene<MapTerminal>(hub).Single();
            Assert.That(terminal.Lists("StreetsTest"), Is.True);
            TravelDestination streetsEntry = terminal.Find("StreetsTest");
            Assert.That(streetsEntry.enemyPool.Select(entry => entry.enemy != null ? entry.enemy.DisplayName : null), Does.Contain("Tikbalang"),
                "The StreetsTest destination rolls from an enemy pool.");
            Assert.That(terminal.MapScreenPrefab, Is.Not.Null);
            TravelInteractable travel = InScene<TravelInteractable>(hub).Single();
            Assert.That(travel.GetComponent<Collider>(), Is.Not.Null);
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }

        Scene streets = EditorSceneManager.OpenScene("Assets/Scenes/StreetsTest.unity", OpenSceneMode.Additive);
        try
        {
            MapManager mapManager = InScene<MapManager>(streets).Single();
            Assert.That(mapManager.ConnectorBlocks.Count, Is.GreaterThan(0));
            Assert.That(mapManager.ConnectorBlocks.Concat(mapManager.EndCapBlocks).Concat(mapManager.SpawnBlocks), Has.None.Null);
            Assert.That(mapManager.EndCapBlocks.Count, Is.GreaterThan(0));
            Assert.That(mapManager.SpawnBlocks.Count, Is.GreaterThan(0));
            Assert.That(mapManager.transform.childCount, Is.Zero, "Preview blocks should not be saved in the scene.");
            Assert.That(InScene<NetworkManager>(streets), Is.Empty, "The destination scene must reuse the persistent NetworkManager.");
        }
        finally
        {
            EditorSceneManager.CloseScene(streets, true);
        }
    }

    private static T[] InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
