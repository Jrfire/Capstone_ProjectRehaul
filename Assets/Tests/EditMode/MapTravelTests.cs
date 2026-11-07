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
    public void CompactPlansAreValidAndPackBlocksTighter()
    {
        Assert.That(MapLayoutPlanner.SharedEdge(new Rect(0f, 0f, 2f, 2f), new Rect(2f, 1f, 2f, 2f)), Is.EqualTo(1f).Within(0.001f));
        Assert.That(MapLayoutPlanner.SharedEdge(new Rect(0f, 0f, 2f, 2f), new Rect(2f, 2f, 2f, 2f)), Is.Zero, "Corners share no edge.");
        Assert.That(MapLayoutPlanner.SharedEdge(new Rect(0f, 0f, 2f, 2f), new Rect(3f, 0f, 2f, 2f)), Is.Zero);

        float normalShared = 0f, compactShared = 0f;
        for (int seed = 1; seed <= 12; seed++)
        {
            Assert.That(MapLayoutPlanner.TryPlan(Connectors, EndCaps, Spawns, 10, seed, 100, out List<MapBlockPlacement> compact, out string error,
                0.75f, compact: true), Is.True, error);
            Assert.That(MapLayoutPlanner.TryPlan(Connectors, EndCaps, Spawns, 10, seed, 100, out List<MapBlockPlacement> normal, out error,
                0.75f), Is.True, error);
            List<WorldEntrance> entrances = Entrances(compact);
            foreach (WorldEntrance entrance in entrances)
                Assert.That(entrances.Count(other => other.Block != entrance.Block && Vector2.Distance(other.Center, entrance.Center) < 0.05f),
                    Is.EqualTo(1), $"Seed {seed}: every compact entrance is joined.");
            for (int a = 0; a < compact.Count; a++)
                for (int b = a + 1; b < compact.Count; b++)
                {
                    Rect ra = Footprint(compact[a]), rb = Footprint(compact[b]);
                    Assert.That(Mathf.Min(ra.xMax, rb.xMax) - Mathf.Max(ra.xMin, rb.xMin) <= 0.01f ||
                        Mathf.Min(ra.yMax, rb.yMax) - Mathf.Max(ra.yMin, rb.yMin) <= 0.01f, Is.True, $"Seed {seed}: blocks {a} and {b} overlap.");
                    compactShared += MapLayoutPlanner.SharedEdge(ra, rb);
                }
            for (int a = 0; a < normal.Count; a++)
                for (int b = a + 1; b < normal.Count; b++)
                    normalShared += MapLayoutPlanner.SharedEdge(Footprint(normal[a]), Footprint(normal[b]));
        }
        Assert.That(compactShared, Is.GreaterThan(normalShared), "Town layouts hug their neighbours more than normal streets.");
    }

    [Test]
    public void TownRollIsDeterministicAndFollowsItsChance()
    {
        int hits = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            Assert.That(MapManager.RollTownLayout(seed, 0f), Is.False);
            Assert.That(MapManager.RollTownLayout(seed, 1f), Is.True);
            bool roll = MapManager.RollTownLayout(seed, 0.5f);
            Assert.That(MapManager.RollTownLayout(seed, 0.5f), Is.EqualTo(roll));
            if (roll)
                hits++;
        }
        Assert.That(hits, Is.InRange(140, 260));
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
    public void SpawnBlocksParkTheJeepneyHomeClearOfSpawnPoints([NUnit.Framework.Range(1, 3)] int spawnIndex)
    {
        GameObject jeepneyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Jeepney.prefab");
        Assert.That(jeepneyPrefab, Is.Not.Null);
        Assert.That(jeepneyPrefab.GetComponentInChildren<TravelInteractable>(true), Is.Not.Null, "Its back takes players home.");
        StreetBlock prefab = Block($"SpawnBlockStreetBlock_{spawnIndex}");

        WithManager(new[] { prefab }, null, manager =>
        {
            Assert.That(manager.Build(spawnIndex * 31), Is.True);
            int index = Enumerable.Range(0, manager.Layout.Count).Single(i => manager.Layout[i].Role == MapBlockRole.Spawn);
            StreetBlock spawn = manager.GetBlock(index);
            GameObject jeepney = manager.PlacedJeepney;
            Assert.That(jeepney, Is.Not.Null, "A jeepney is parked in the spawn block.");
            Assert.That(jeepney.activeInHierarchy && jeepney.transform.IsChildOf(spawn.transform), Is.True);
            Assert.That(manager.GeneratedRoot.GetComponentsInChildren<TravelInteractable>(true).Length, Is.EqualTo(1));
            Assert.That(MapScreen.TryGetJeepneyPosition(manager, out Vector3 labelled), Is.True);
            Assert.That(Vector3.Distance(labelled, jeepney.transform.position), Is.LessThan(0.01f), "The map labels the parked jeepney.");

            Physics.SyncTransforms();
            Bounds body = new Bounds(jeepney.transform.position, Vector3.zero);
            foreach (Collider collider in jeepney.GetComponentsInChildren<Collider>())
                body.Encapsulate(collider.bounds);
            foreach (Vector3 corner in new[] { body.min, body.max, new Vector3(body.min.x, 0f, body.max.z), new Vector3(body.max.x, 0f, body.min.z) })
                Assert.That(spawn.ContainsXZ(corner), Is.True, "The jeepney stays inside the spawn block.");
            Vector3 toCentre = spawn.transform.position - jeepney.transform.position;
            toCentre.y = 0f;
            if (toCentre.magnitude > 0.5f)
                Assert.That(Vector3.Dot(-jeepney.transform.forward, toCentre.normalized), Is.GreaterThan(0f), "Its back faces the spawn area.");

            var floors = Enumerable.Range(0, manager.BlockCount).Select(i => manager.GetBlock(i).FloorRoot).Where(f => f != null).ToArray();
            Collider[] solids = Physics.OverlapBox(body.center, body.extents * 0.9f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.transform.IsChildOf(manager.GeneratedRoot) && !hit.transform.IsChildOf(jeepney.transform) && !floors.Any(f => hit.transform.IsChildOf(f)))
                .ToArray();
            Assert.That(solids, Is.Empty, $"The jeepney overlaps {string.Join(", ", solids.Select(s => s.name))}.");

            Transform[] points = manager.GeneratedRoot.GetComponentsInChildren<PlayerSpawnPoints>(true).Single().transform.Cast<Transform>().ToArray();
            Assert.That(points.Length, Is.EqualTo(manager.SpawnPointCount), "All six spawn points still fit beside the jeepney.");
            float radius = manager.SpawnClearanceRadius;
            foreach (Transform point in points)
            {
                Vector3 p = point.position;
                Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * radius, p + Vector3.up * (manager.SpawnClearanceHeight - radius), radius, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(hits.Any(hit => hit.transform.IsChildOf(jeepney.transform)), Is.False, $"{point.name} is inside the jeepney.");
            }

            Vector3 back = jeepney.GetComponentInChildren<TravelInteractable>().transform.position;
            Vector3 boardingSpot = back - jeepney.transform.forward * 1f;
            boardingSpot.y = points[0].position.y + radius + 0.05f;
            Collider[] behind = Physics.OverlapCapsule(boardingSpot, boardingSpot + Vector3.up * (manager.SpawnClearanceHeight - 2f * radius), radius * 0.8f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => !floors.Any(f => hit.transform.IsChildOf(f))).ToArray();
            Assert.That(behind, Is.Empty, "There is room to stand behind the jeepney's back.");
        }, jeepneyPrefab);
    }

    [Test]
    public void SomeAgimatsAlwaysLieOutsideSealedBlocks([NUnit.Framework.Range(1, 6)] int seed)
    {
        BuildObstructed(seed, 3, 0.75f, (manager, remaining) =>
        {
            bool[] open = manager.FindOpenlyReachableFromSpawn();
            int spawn = Enumerable.Range(0, manager.BlockCount).Single(i => manager.GetRole(i) == MapBlockRole.Spawn);
            bool anyOpenEligible = Enumerable.Range(0, manager.BlockCount).Any(i => i != spawn && open[i] && !manager.GetNeighbours(spawn).Contains(i));
            int[] homes = Enumerable.Range(0, manager.BlockCount).Where(i => open[i] && i != spawn).Take(1).ToArray();
            for (int count = 3; count <= 5; count++)
            {
                List<int> blocks = AgimatSpawner.PlanBlocks(manager, homes, count, new System.Random(seed * 7 + count), 2);
                Assert.That(blocks.Count, Is.EqualTo(count));
                Assert.That(blocks, Has.No.Member(spawn));
                if (anyOpenEligible)
                    Assert.That(blocks.Count(block => open[block]), Is.GreaterThanOrEqualTo(2), "At least two agimats can be collected without opening a barricade.");
            }
        });
    }

    [Test]
    public void TheMapDrawsEveryBlockWithEntranceHolesRedObstaclesAndThePlayer([NUnit.Framework.Range(1, 4)] int seed)
    {
        BuildObstructed(seed, 3, 0.75f, (manager, remaining) =>
        {
            var view = new GameObject("Map View", typeof(RectTransform));
            try
            {
                ((RectTransform)view.transform).sizeDelta = new Vector2(800f, 600f);
                MapGraphic graphic = view.AddComponent<MapGraphic>();
                int spawn = Enumerable.Range(0, manager.BlockCount).Single(i => manager.GetRole(i) == MapBlockRole.Spawn);
                Vector3 player = manager.GetBlock(spawn).transform.position;
                graphic.Show(manager, true, player);
                var vh = new UnityEngine.UI.VertexHelper();
                graphic.DrawTo(vh);

                var blocks = Enumerable.Range(0, manager.BlockCount).Select(manager.GetBlock).ToList();
                Assert.That(graphic.BlocksDrawn, Is.EqualTo(manager.BlockCount), "Every street block is a square.");
                Assert.That(graphic.FillersDrawn, Is.EqualTo(manager.FillerCount + manager.PluggedCellCount));
                Assert.That(graphic.GapsDrawn, Is.EqualTo(blocks.Sum(b => b.Entrances.Count)), "Every entrance leaves a hole in its block's outline.");
                int blocked = blocks.SelectMany(b => b.Entrances).Count(e => e.Obstacle != null && !e.Obstacle.IsCleared);
                Assert.That(blocked, Is.GreaterThan(0));
                Assert.That(graphic.ObstaclesDrawn, Is.EqualTo(blocked), "Each obstacled entrance gets a red line.");
                Assert.That(vh.currentVertCount, Is.GreaterThan(4));

                Assert.That(graphic.PlayerDrawn, Is.True);
                Assert.That(Vector2.Distance(graphic.PlayerPoint, graphic.WorldToMap(player)), Is.LessThan(0.01f));
                Rect rect = ((RectTransform)view.transform).rect;
                foreach (StreetBlock block in blocks)
                    Assert.That(rect.Contains(graphic.WorldToMap(block.transform.position)), Is.True, "The whole street fits in the panel.");
                StreetBlock west = blocks.OrderBy(b => b.transform.position.x).First();
                StreetBlock east = blocks.OrderBy(b => b.transform.position.x).Last();
                StreetBlock south = blocks.OrderBy(b => b.transform.position.z).First();
                StreetBlock north = blocks.OrderBy(b => b.transform.position.z).Last();
                Assert.That(graphic.WorldToMap(west.transform.position).x, Is.LessThan(graphic.WorldToMap(east.transform.position).x + 0.01f), "East is right.");
                Assert.That(graphic.WorldToMap(south.transform.position).y, Is.LessThan(graphic.WorldToMap(north.transform.position).y + 0.01f), "North is up.");

                graphic.Show(null, false, Vector3.zero);
                graphic.DrawTo(vh);
                Assert.That(graphic.BlocksDrawn + graphic.GapsDrawn + graphic.ObstaclesDrawn, Is.Zero);
                Assert.That(graphic.PlayerDrawn, Is.False);
                Assert.That(vh.currentVertCount, Is.GreaterThan(4), "Without a map only the grid background is drawn.");
                vh.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(view);
            }
        });
    }

    [Test]
    public void TheMapScreenIsOnTheHudAndTabOpensIt()
    {
        var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        UnityEngine.InputSystem.InputAction map = actions.FindAction("Player/Map");
        Assert.That(map, Is.Not.Null);
        Assert.That(map.type, Is.EqualTo(UnityEngine.InputSystem.InputActionType.Button));
        Assert.That(map.bindings.Select(b => b.path), Does.Contain("<Keyboard>/tab"));

        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        MapScreen screen = hud.GetComponentInChildren<MapScreen>(true);
        Assert.That(screen, Is.Not.Null);
        Assert.That(new SerializedObject(screen).FindProperty("inputActions").objectReferenceValue, Is.SameAs(actions));
        Assert.That(screen.Panel != null && !screen.Panel.activeSelf, Is.True, "The map starts closed.");
        Assert.That(screen.Graphic, Is.Not.Null);
        Assert.That(screen.Graphic.raycastTarget, Is.False);
        Assert.That(screen.Graphic.transform.IsChildOf(screen.Panel.transform), Is.True);
        Assert.That(screen.TitleText != null && screen.EmptyText != null, Is.True);
        Assert.That(AssetDatabase.GetAssetPath(screen.Font), Is.EqualTo("Assets/Fonts/blood_crow/bloodcrow.ttf"));
        Canvas canvas = screen.GetComponent<Canvas>();
        Assert.That(canvas != null && canvas.overrideSorting && canvas.sortingOrder < 500, Is.True, "It draws below the damage flash.");
        Assert.That(hud.transform.GetChild(hud.transform.childCount - 1).name, Is.EqualTo("DamageFlash"));
        Assert.That(screen.JeepneyLabel, Does.Contain("Jeepney"));
    }

    [Test]
    public void TheCompassSitsTopCentreAndMatchesTheMapsDirections()
    {
        GameObject hudAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        CompassHud asset = hudAsset.GetComponentInChildren<CompassHud>(true);
        Assert.That(asset, Is.Not.Null);
        var rect = (RectTransform)asset.transform;
        Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(asset.Strip, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(asset.Strip.texture), Is.EqualTo("Assets/SPRITES/UI/compassPNG2.png"));
        Assert.That(asset.Strip.texture.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat));
        Assert.That(asset.Strip.raycastTarget, Is.False);
        Assert.That(asset.Content != null && asset.Content != asset.gameObject && asset.Strip.transform.IsChildOf(asset.Content.transform), Is.True);
        Assert.That(hudAsset.transform.GetChild(hudAsset.transform.childCount - 1).name, Is.EqualTo("DamageFlash"));

        GameObject hud = Object.Instantiate(hudAsset);
        try
        {
            CompassHud compass = hud.GetComponentInChildren<CompassHud>(true);
            float width = compass.VisibleFraction;
            Assert.That(width, Is.GreaterThan(0.1f).And.LessThan(1f));
            float northU = new SerializedObject(compass).FindProperty("northU").floatValue;
            float Centre() => Mathf.Repeat(compass.Strip.uvRect.x + compass.Strip.uvRect.width * 0.5f, 1f);
            float Diff(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a * 360f, b * 360f)) / 360f;

            // The strip reads N, E, S, W at quarter steps, so heading south (yaw 180, map down) centres the S mark.
            compass.SetHeading(0f);
            Assert.That(Diff(Centre(), northU), Is.LessThan(0.001f), "Facing +Z (map up) shows N.");
            compass.SetHeading(90f);
            Assert.That(Diff(Centre(), northU + 0.25f), Is.LessThan(0.001f), "Facing +X (map right) shows E.");
            compass.SetHeading(180f);
            Assert.That(Diff(Centre(), northU + 0.5f), Is.LessThan(0.001f), "Facing -Z (map down) shows S.");
            compass.SetHeading(-90f);
            Assert.That(compass.Heading, Is.EqualTo(270f).Within(0.001f));
            Assert.That(Diff(Centre(), northU + 0.75f), Is.LessThan(0.001f), "Facing -X shows W.");
            Assert.That(compass.Strip.uvRect.width, Is.EqualTo(width).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(hud);
        }
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
                AssertMapDrawsFillers(manager);
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
                Assert.That(manager.PluggedCells.Count, Is.EqualTo(manager.PluggedCellCount));
                foreach (Vector3[] cell in manager.PluggedCells)
                    Assert.That(Vector3.Distance(cell[0], cell[2]), Is.EqualTo(Mathf.Sqrt(72f)).Within(0.01f), "Plugged cells are recorded as one 6 m cell.");
                AssertMapDrawsFillers(manager);

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

    private static void AssertMapDrawsFillers(MapManager manager)
    {
        var view = new GameObject("Map View", typeof(RectTransform));
        try
        {
            ((RectTransform)view.transform).sizeDelta = new Vector2(800f, 600f);
            MapGraphic graphic = view.AddComponent<MapGraphic>();
            graphic.Show(manager, false, Vector3.zero);
            using var vh = new UnityEngine.UI.VertexHelper();
            graphic.DrawTo(vh);
            Assert.That(graphic.BlocksDrawn, Is.EqualTo(manager.BlockCount));
            Assert.That(graphic.FillersDrawn, Is.EqualTo(manager.FillerCount + manager.PluggedCellCount), "Filler blocks and plugged cells are drawn as squares.");
            Assert.That(graphic.FillersDrawn, Is.GreaterThan(0));
            Assert.That(graphic.GapsDrawn, Is.EqualTo(Enumerable.Range(0, manager.BlockCount).Sum(i => manager.GetBlock(i).Entrances.Count)), "Fillers have no entrance holes.");

            Rect rect = ((RectTransform)view.transform).rect;
            for (int i = 0; i < manager.FillerCount; i++)
            {
                StreetBlock filler = manager.GetFiller(i);
                Vector2 half = filler.Size * 0.5f;
                foreach (Vector3 corner in new[] { new Vector3(-half.x, 0f, -half.y), new Vector3(half.x, 0f, half.y) })
                {
                    Vector2 point = graphic.WorldToMap(filler.transform.TransformPoint(corner));
                    Assert.That(point.x >= rect.xMin - 0.01f && point.x <= rect.xMax + 0.01f && point.y >= rect.yMin - 0.01f && point.y <= rect.yMax + 0.01f,
                        Is.True, $"Filler {i} fits in the panel.");
                }
            }
            foreach (Vector3[] cell in manager.PluggedCells)
                Assert.That(rect.Contains(graphic.WorldToMap((cell[0] + cell[2]) * 0.5f)), Is.True, "Plugged cells fit in the panel.");
        }
        finally
        {
            Object.DestroyImmediate(view);
        }
    }

    private static void WithManager(StreetBlock[] spawns, StreetBlock[] fillers, System.Action<MapManager> check, GameObject jeepney = null)
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
            so.FindProperty("jeepneyPrefab").objectReferenceValue = jeepney;
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

    [Test]
    public void ReachableFloorOnlyHoldsFreeWalkableSpotsInsideEachBlock([NUnit.Framework.Range(1, 3)] int seed)
    {
        WithManager(Spawns, null, manager =>
        {
            Assert.That(manager.Build(seed * 13), Is.True);
            Physics.SyncTransforms();
            float radius = manager.SpawnClearanceRadius;
            int nonEmpty = 0;
            for (int i = 0; i < manager.BlockCount; i++)
            {
                IReadOnlyList<Vector3> points = manager.GetReachableFloor(i);
                if (points.Count > 0)
                    nonEmpty++;
                StreetBlock block = manager.GetBlock(i);
                Transform floor = block.FloorRoot;
                foreach (Vector3 point in points)
                {
                    Assert.That(manager.TryGetBlockIndex(point, out int owner) && owner == i, Is.True, "Points stay in their block.");
                    Collider[] hits = Physics.OverlapCapsule(point + Vector3.up * (radius + 0.1f), point + Vector3.up * (manager.SpawnClearanceHeight - radius),
                        radius * 0.95f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(hit => hit.transform.IsChildOf(manager.GeneratedRoot) && (floor == null || !hit.transform.IsChildOf(floor)) &&
                            hit.GetComponentInParent<EntranceObstacle>(true) == null)
                        .ToArray();
                    Assert.That(hits, Is.Empty, $"A player fits at {point} in block {i}: {string.Join(", ", hits.Select(h => h.name))}");
                }
            }
            Assert.That(nonEmpty, Is.EqualTo(manager.BlockCount), "Every block has somewhere reachable to hide agimats.");
            Assert.That(manager.GetReachableFloor(-1), Is.Empty);
        });
    }

    [Test]
    public void TownLayoutsOpenTheWallsBetweenTownBlocksAndKeepThemFreeOfObstacles([NUnit.Framework.Range(1, 3)] int seed)
    {
        var created = new List<GameObject>();
        int tagged = 0;
        StreetBlock Town(StreetBlock prefab)
        {
            // Scene copies flagged as town blocks with their edge walls tagged stand in for generated town blocks.
            StreetBlock copy = Object.Instantiate(prefab, new Vector3(0f, -500f, 0f), Quaternion.identity);
            created.Add(copy.gameObject);
            var so = new SerializedObject(copy);
            so.FindProperty("townBlock").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            Transform generated = copy.transform.Find(StreetBlockBuilder.GeneratedName);
            foreach (string group in new[] { "Walls", "Boundaries" })
            {
                Transform parent = generated.Find(group);
                if (parent == null)
                    continue;
                foreach (Transform piece in parent)
                {
                    Vector3 local = copy.transform.InverseTransformPoint(Center(piece.gameObject));
                    BlockSide side = Mathf.Abs(local.x) > Mathf.Abs(local.z)
                        ? (local.x > 0f ? BlockSide.East : BlockSide.West)
                        : (local.z > 0f ? BlockSide.North : BlockSide.South);
                    piece.gameObject.AddComponent<StreetEdgeWall>().SetSide(side);
                    tagged++;
                }
            }
            return copy;
        }

        var root = new GameObject("MapManager Town Test");
        created.Add(root);
        try
        {
            StreetBlock[] towns = Connectors.Select(Town).ToArray();
            Assert.That(tagged, Is.GreaterThan(0));
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
            Fill("townBlocks", towns);
            so.FindProperty("connectorBlockCount").intValue = 8;
            so.FindProperty("townLayoutChance").floatValue = 1f;
            so.FindProperty("townMinConnectorCount").intValue = 6;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(manager.Build(seed * 17), Is.True);
            Assert.That(manager.IsTownLayout, Is.True);
            int[] connectors = Enumerable.Range(0, manager.BlockCount).Where(i => manager.GetRole(i) == MapBlockRole.Connector).ToArray();
            Assert.That(connectors.All(i => manager.GetBlock(i).IsTownBlock), Is.True, "Town layouts use the town pool.");
            Assert.That(Enumerable.Range(0, manager.BlockCount).Where(i => manager.GetRole(i) != MapBlockRole.Connector)
                .All(i => !manager.GetBlock(i).IsTownBlock), Is.True, "End caps and the spawn block keep their walls.");
            Assert.That(manager.OpenSeams.Count, Is.GreaterThan(0), "Touching town blocks open up to each other.");

            StreetEdgeWall[] remaining = manager.GeneratedRoot.GetComponentsInChildren<StreetEdgeWall>(true);
            Assert.That(remaining.Length, Is.GreaterThan(0), "Walls facing the outside stay.");
            foreach (Vector3[] seam in manager.OpenSeams)
            {
                bool alongZ = Mathf.Abs(seam[0].x - seam[1].x) < 0.01f;
                float line = alongZ ? seam[0].x : seam[0].z;
                float min = alongZ ? Mathf.Min(seam[0].z, seam[1].z) : Mathf.Min(seam[0].x, seam[1].x);
                float max = alongZ ? Mathf.Max(seam[0].z, seam[1].z) : Mathf.Max(seam[0].x, seam[1].x);
                foreach (StreetEdgeWall wall in remaining)
                {
                    Vector3 c = Center(wall.gameObject);
                    float across = alongZ ? c.x : c.z;
                    float along = alongZ ? c.z : c.x;
                    Vector3 outward = wall.transform.GetComponentInParent<StreetBlock>().transform.TransformDirection(wall.Side.Outward());
                    bool facesSeam = Mathf.Abs(alongZ ? outward.x : outward.z) > 0.9f;
                    Assert.That(facesSeam && Mathf.Abs(across - line) < 1f && along > min + 1.5f && along < max - 1.5f, Is.False,
                        $"{wall.name} still stands on an open town seam.");
                }
            }

            foreach (MapJoint joint in manager.Joints)
            {
                StreetBlock a = manager.GetBlock(joint.BlockA), b = manager.GetBlock(joint.BlockB);
                if (!a.IsTownBlock || !b.IsTownBlock)
                    continue;
                Assert.That(joint.Barricaded, Is.False, "Town-to-town joints are never barricaded.");
                Assert.That(a.Entrances[joint.EntranceA].Obstacle == null && b.Entrances[joint.EntranceB].Obstacle == null, Is.True);
            }
            foreach (int town in connectors)
                Assert.That(manager.GetBlock(town).GetComponentsInChildren<EntranceObstacle>(true), Is.Empty, "Wall-less town blocks hold no obstacles.");
            foreach (MapJoint joint in manager.Joints)
            {
                bool endCap = manager.GetRole(joint.BlockA) == MapBlockRole.EndCap || manager.GetRole(joint.BlockB) == MapBlockRole.EndCap;
                if (endCap)
                    Assert.That(joint.Barricaded, Is.True, "Walled end caps at the map's edge still get gates.");
            }

            so.FindProperty("townMinConnectorCount").intValue = 50;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(manager.Build(seed * 17), Is.True);
            Assert.That(manager.IsTownLayout, Is.False, "Small maps keep the normal street layout.");
            Assert.That(manager.OpenSeams, Is.Empty);
        }
        finally
        {
            foreach (GameObject obj in created)
                Object.DestroyImmediate(obj);
        }
    }

    private static Vector3 Center(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds.center;
        }
        BoxCollider box = target.GetComponentInChildren<BoxCollider>(true);
        return box != null ? box.transform.TransformPoint(box.center) : target.transform.position;
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
    public void LaterRoundsMayRollSeveralDistinctEnemyTypes()
    {
        Assert.That(Enumerable.Range(1, 12).Select(TravelDestination.MaxEnemyTypesForRound),
            Is.EqualTo(new[] { 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4 }), "Round 3 = up to 2, round 6 = up to 3, round 9+ = up to 4.");
        var first = ScriptableObject.CreateInstance<EnemyDefinition>();
        var second = ScriptableObject.CreateInstance<EnemyDefinition>();
        var third = ScriptableObject.CreateInstance<EnemyDefinition>();
        try
        {
            var destination = new TravelDestination { sceneName = "StreetsTest" };
            Assert.That(destination.RollMany(4, 1f, () => 0f), Is.Empty);
            destination.enemyPool.Add(new EnemyRollEntry { enemy = first, weight = 1f });
            destination.enemyPool.Add(new EnemyRollEntry { enemy = second, weight = 1f });
            destination.enemyPool.Add(new EnemyRollEntry { enemy = first, weight = 1f });
            destination.enemyPool.Add(new EnemyRollEntry { enemy = third, weight = 1f });
            Assert.That(destination.RollMany(1, 1f, () => 0f), Is.EqualTo(new[] { first }));
            Assert.That(destination.RollMany(4, 1f, () => 0f), Is.EqualTo(new[] { first, second, third }), "Types are never repeated.");
            Assert.That(destination.RollMany(2, 1f, () => 0f), Has.Count.EqualTo(2), "Capped at the round's maximum.");
            Assert.That(destination.RollMany(4, 0.5f, () => 0.9f), Has.Count.EqualTo(1), "A failed chance stops further types.");
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(third);
        }
    }

    [Test]
    public void EachQuotaGrowsTheMapUpToTheBlockCap()
    {
        Assert.That(MapManager.EffectiveConnectorCount(6, 0, 15), Is.EqualTo(6));
        Assert.That(MapManager.EffectiveConnectorCount(6, 2, 15), Is.EqualTo(8));
        Assert.That(MapManager.EffectiveConnectorCount(6, 20, 15), Is.EqualTo(15), "Never more than the cap.");
        Assert.That(MapManager.EffectiveConnectorCount(18, 4, 15), Is.EqualTo(18), "A base above the cap is kept.");
        for (int seed = 1; seed <= 6; seed++)
        {
            Assert.That(MapManager.TryPlanCapped(Connectors, EndCaps, Spawns, 4, 20, 15, seed, 100, 0.75f,
                out List<MapBlockPlacement> grown, out int connectors, out string error), Is.True, error);
            Assert.That(grown.Count, Is.LessThanOrEqualTo(15), $"Seed {seed}: end caps and the spawn block count toward the 15-block cap.");
            Assert.That(connectors, Is.InRange(4, 15));
            Assert.That(grown.Count(block => block.Role == MapBlockRole.Connector), Is.EqualTo(connectors));
            Assert.That(MapManager.TryPlanCapped(Connectors, EndCaps, Spawns, 4, 0, 15, seed, 100, 0.75f,
                out List<MapBlockPlacement> baseLayout, out int baseConnectors, out _), Is.True);
            Assert.That(baseConnectors, Is.EqualTo(4), "Without extras the base layout is unchanged.");
            Assert.That(grown.Count, Is.GreaterThanOrEqualTo(baseLayout.Count), "Extras never shrink the map.");
        }
        Assert.That(MapManager.TryPlanCapped(Connectors, EndCaps, Spawns, 6, 4, 3, 1, 100, 0.75f,
            out List<MapBlockPlacement> overCap, out int kept, out _), Is.True);
        Assert.That(kept, Is.EqualTo(6), "A base layout already above the cap is kept.");
        Assert.That(overCap.Count, Is.GreaterThan(3));
        var director = new GameObject("Director").AddComponent<TravelDirector>();
        try
        {
            Assert.That(director.ExtraConnectorsForRound(1), Is.Zero);
            Assert.That(director.ExtraConnectorsForRound(2), Is.EqualTo(2));
            Assert.That(director.ExtraConnectorsForRound(4), Is.EqualTo(6));
        }
        finally
        {
            Object.DestroyImmediate(director.gameObject);
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
