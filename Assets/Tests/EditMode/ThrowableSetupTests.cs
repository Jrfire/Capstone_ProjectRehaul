using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class ThrowableSetupTests
{
    private const string CatalogPath = "Assets/Settings/ItemCatalog.asset";
    private const string HudPath = "Assets/PREFABS/PlayerHud.prefab";
    private const string TravelMapPath = "Assets/PREFABS/TravelMapScreen.prefab";
    private const string BasePath = "Assets/Scenes/Base.unity";
    private const string StreetsScenePath = "Assets/Scenes/Streets.unity";

    [Test]
    public void GateLabelsNameTheirKeyAndBarricadesSayBlocked()
    {
        var gate = new GameObject("Gate").AddComponent<EntranceObstacle>();
        var barricade = new GameObject("Barricade").AddComponent<EntranceObstacle>();
        try
        {
            SetRequirement(gate, ObstacleRequirement.Key);
            SetRequirement(barricade, ObstacleRequirement.Destruction);
            gate.SetGateIdentity("G1");
            Assert.That(gate.Label, Is.EqualTo("Locked. Requires Key G1"));
            Assert.That(barricade.Label, Is.EqualTo("Blocked"));
            Assert.That(gate.TryUseKey("G2"), Is.False, "Another gate's key does not fit.");
            Assert.That(gate.IsCleared, Is.False);
            Assert.That(gate.TryUseKey("G1"), Is.True);
            Assert.That(gate.IsCleared, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(gate.gameObject);
            Object.DestroyImmediate(barricade.gameObject);
        }
    }

    [Test]
    public void PopulationMatchesTheSpawnerAndIsCappedAtFourteen()
    {
        var spawner = new GameObject("Spawner").AddComponent<EnemySpawner>();
        try
        {
            for (int types = 1; types <= 4; types++)
            {
                for (int round = 1; round <= 12; round++)
                {
                    int expected = Mathf.Min(14, spawner.DistributeCounts(types, round, () => 0.3f).Sum());
                    Assert.That(TravelDirector.PopulationFor(types, round, 2, 2, 2, 0, 14), Is.EqualTo(expected),
                        $"{types} types, round {round}");
                }
            }
            Assert.That(TravelDirector.PopulationFor(1, 1, 2, 2, 2, 0, 14), Is.EqualTo(2));
            Assert.That(TravelDirector.PopulationFor(1, 3, 2, 2, 2, 0, 14), Is.EqualTo(4));
            Assert.That(TravelDirector.PopulationFor(4, 30, 2, 2, 2, 0, 14), Is.EqualTo(14));
            Assert.That(TravelDirector.PopulationFor(0, 5, 2, 2, 2, 0, 14), Is.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(spawner.gameObject);
        }
    }

    [Test]
    public void AgimatAndItemCountsGrowWithQuotasAndMapSize()
    {
        var spawner = new GameObject("Agimats").AddComponent<AgimatSpawner>();
        try
        {
            int min = spawner.MinCount, max = spawner.MaxCount;
            Assert.That(spawner.MinCountForRound(1), Is.EqualTo(min));
            Assert.That(spawner.MaxCountForRound(1), Is.EqualTo(max));
            Assert.That(spawner.MinCountForRound(3), Is.EqualTo(min + 2), "+1 minimum per quota.");
            Assert.That(spawner.MaxCountForRound(3), Is.EqualTo(max + 4), "+2 maximum per quota.");

            int small = spawner.ItemCountFor(5, false);
            int large = spawner.ItemCountFor(15, false);
            Assert.That(large, Is.GreaterThan(small), "Bigger maps get more items.");
            Assert.That(spawner.ItemCountFor(15, true), Is.EqualTo(Mathf.CeilToInt(large * 1.5f)), "Blood moons multiply items by 1.5.");
        }
        finally
        {
            Object.DestroyImmediate(spawner.gameObject);
        }
    }

    [Test]
    public void LootRollsFollowItemWeightsAndSkipZeroWeights()
    {
        var never = ScriptableObject.CreateInstance<ItemDefinition>();
        var light = ScriptableObject.CreateInstance<ItemDefinition>();
        var heavy = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            never.ConfigureLoot(0f);
            light.ConfigureLoot(1f);
            heavy.ConfigureLoot(3f);
            var pool = new[] { never, light, heavy };
            int lightCount = 0, heavyCount = 0;
            for (int i = 0; i < 400; i++)
            {
                ItemDefinition rolled = AgimatSpawner.RollWeighted(pool, (i + 0.5f) / 400f);
                Assert.That(rolled, Is.Not.SameAs(never), "Weight 0 items never roll.");
                if (rolled == light) lightCount++;
                if (rolled == heavy) heavyCount++;
            }
            Assert.That(lightCount, Is.EqualTo(100).Within(2));
            Assert.That(heavyCount, Is.EqualTo(300).Within(2));
            Assert.That(AgimatSpawner.RollWeighted(new[] { never }, 0.5f), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(never);
            Object.DestroyImmediate(light);
            Object.DestroyImmediate(heavy);
        }
    }

    [Test]
    public void CatalogHoldsTheThrowablesAndAKeyTemplate()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        Assert.That(catalog, Is.Not.Null);
        ItemDefinition molotov = catalog.Items.FirstOrDefault(item => item != null && item.ThrowableKind == ThrowableKind.Molotov);
        ItemDefinition bawang = catalog.Items.FirstOrDefault(item => item != null && item.ThrowableKind == ThrowableKind.Garlic);
        Assert.That(molotov, Is.Not.Null, "Molotov is in the catalog.");
        Assert.That(bawang, Is.Not.Null, "Bawang is in the catalog.");
        foreach (ItemDefinition throwable in new[] { molotov, bawang })
        {
            Assert.That(throwable.UseKind, Is.EqualTo(ItemUseKind.Throwable));
            Assert.That(throwable.ThrowablePrefab, Is.Not.Null);
            Assert.That(throwable.ThrowablePrefab.GetComponent<ThrowableProjectile>(), Is.Not.Null);
            Assert.That(throwable.ThrowablePrefab.GetComponent<NetworkIdentity>(), Is.Not.Null);
        }
        Assert.That(catalog.KeyTemplate, Is.Not.Null);
        Assert.That(catalog.KeyTemplate.UseKind, Is.EqualTo(ItemUseKind.Key));
        Assert.That(catalog.Items.Select(item => item.Id).Distinct().Count(), Is.EqualTo(catalog.Items.Count), "Item ids are unique.");
    }

    [Test]
    public void HudShowsDurabilityAndTheTravelMapShowsPopulation()
    {
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath).GetComponentInChildren<InventoryHud>(true);
        Assert.That(hud.DurabilityIcons, Has.Length.EqualTo(PlayerInventory.MaxSlots));
        Assert.That(hud.DurabilityText, Has.Length.EqualTo(PlayerInventory.MaxSlots));
        Assert.That(hud.DurabilityIcons, Has.All.Not.Null);
        Assert.That(hud.DurabilityText, Has.All.Not.Null);

        var map = AssetDatabase.LoadAssetAtPath<GameObject>(TravelMapPath).GetComponentInChildren<TravelMapScreen>(true);
        Assert.That(map.PopulationLabel, Is.Not.Null);
        Assert.That(map.PopulationLabel.rectTransform.anchoredPosition.y,
            Is.LessThan(map.EnemiesLabel.rectTransform.anchoredPosition.y), "Population sits below Enemies.");
    }

    [Test]
    public void ProjectileAndFirePrefabsAreRegisteredAndStreetsScatterItems()
    {
        EditorSceneManager.OpenScene(BasePath, OpenSceneMode.Single);
        NetworkManager manager = Object.FindAnyObjectByType<NetworkManager>();
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        foreach (ItemDefinition throwable in catalog.Items.Where(item => item.UseKind == ItemUseKind.Throwable))
        {
            Assert.That(manager.spawnPrefabs, Does.Contain(throwable.ThrowablePrefab), throwable.DisplayName);
            FireZone fire = throwable.ThrowablePrefab.GetComponent<ThrowableProjectile>().FirePrefab;
            if (throwable.ThrowableKind == ThrowableKind.Molotov)
            {
                Assert.That(fire, Is.Not.Null);
                Assert.That(manager.spawnPrefabs, Does.Contain(fire.gameObject));
                Assert.That(fire.Lifetime, Is.EqualTo(8f).Within(0.01f));
                Assert.That(fire.DamagePerTick, Is.EqualTo(1), "Half a heart per tick.");
            }
        }

        EditorSceneManager.OpenScene(StreetsScenePath, OpenSceneMode.Single);
        AgimatSpawner spawner = Object.FindAnyObjectByType<AgimatSpawner>();
        Assert.That(spawner.ItemPickupPrefab, Is.Not.Null);
        Assert.That(spawner.Items, Is.Not.Empty);
    }

    [Test]
    public void DarkWoodTreesHaveBakedLeaves()
    {
        string[] trees = AssetDatabase.FindAssets("t:Prefab Dark", new[] { "Assets/Prefab" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.Contains("Dark Wood") || path.Contains("DarkWood") || path.Contains("Dark_Wood"))
            .ToArray();
        Assert.That(trees, Is.Not.Empty);
        foreach (string path in trees)
        {
            var tree = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var leaves = tree.GetComponent<TreeLeafAttachment>();
            Assert.That(leaves, Is.Not.Null, path);
            Assert.That(leaves.LeafSprite, Is.Not.Null, path);
            Assert.That(leaves.Canopy, Is.Not.Null, path);
            Assert.That(leaves.Canopy.GetComponentsInChildren<SpriteRenderer>(true), Is.Not.Empty, path);
        }
    }

    private static void SetRequirement(EntranceObstacle obstacle, ObstacleRequirement requirement)
    {
        var settings = new SerializedObject(obstacle);
        settings.FindProperty("requirement").enumValueIndex = (int)requirement;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
