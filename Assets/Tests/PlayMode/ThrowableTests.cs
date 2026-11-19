#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Spawns the project's molotov fire on the active server for enemy reaction tests.</summary>
internal static class TestFires
{
    public static FireZone Spawn(Vector3 position, List<GameObject> created, float seconds = 8f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Items/MolotovFire.prefab").GetComponent<FireZone>();
        FireZone fire = FireZone.ServerSpawn(prefab, position);
        fire.Configure(3f, 1.25f, seconds, damage: 0);
        created.Add(fire.gameObject);
        return fire;
    }
}

public sealed class ThrowableTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string MolotovPath = "Assets/SPRITES/Items/Molotov.asset";
    private const string BawangPath = "Assets/SPRITES/Items/Bawang.asset";
    private const string FirePath = "Assets/PREFABS/Items/MolotovFire.prefab";
    private const string PickupPath = "Assets/PREFABS/ItemPickup.prefab";

    private readonly List<Object> created = new List<Object>();
    private GameObject managerObject;
    private Transport previousTransport;
    private bool previousListen;

    [SetUp]
    public void Setup()
    {
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
    }

    [TearDown]
    public void TearDown()
    {
        if (managerObject != null)
        {
            managerObject.GetComponent<NetworkManager>().StopHost();
            Object.DestroyImmediate(managerObject);
        }
        NetworkClient.Disconnect();
        NetworkClient.Shutdown();
        NetworkServer.Shutdown();
        NetworkManager.ResetStatics();
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
                Object.DestroyImmediate(created[i]);
        }
        created.Clear();
        foreach (FireZone fire in new List<FireZone>(FireZone.Active))
            if (fire != null)
                Object.DestroyImmediate(fire.gameObject);
        PlayerControlLock.ReleaseAll();
        HudVisibility.ShowAll();
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
    }

    [UnityTest]
    public IEnumerator WeaponDurabilityDropsOnlyOnHitsSurvivesDropsAndBreaksTheWeapon()
    {
        Track(Ground());
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        PlayerItemUse itemUse = player.GetComponent<PlayerItemUse>();
        ItemCatalog catalog = UseCatalog(inventory);
        ItemDefinition club = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        club.Configure("club", "Club", null, ItemSlotKind.Active);
        club.ConfigureWeapon(2.5f, 1f, breaks: true);
        club.ConfigureDurability(true, 3);
        catalog.Register(club);
        Assert.That(inventory.ServerTryAdd(club), Is.True);
        Assert.That(inventory.GetDurability(0), Is.EqualTo(3), "A new weapon starts with full durability.");

        itemUse.GetAim(out Vector3 origin, out Vector3 forward);
        Assert.That(itemUse.ServerUse(0, "club", origin, forward), Is.True, "Swinging at nothing still swings.");
        Assert.That(inventory.GetDurability(0), Is.EqualTo(3), "Misses do not wear the weapon.");

        GameObject target = Track(Box("Target", origin + forward * 1.8f, Vector3.one * 0.8f));
        target.AddComponent<TestTarget>();
        Physics.SyncTransforms();
        Assert.That(itemUse.ServerUse(0, "club", origin, forward), Is.True);
        Assert.That(inventory.GetDurability(0), Is.EqualTo(2), "An enemy hit uses one durability.");

        var settings = new SerializedObject(inventory);
        settings.FindProperty("itemPickupPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath).GetComponent<ItemPickup>();
        settings.ApplyModifiedPropertiesWithoutUndo();
        Object.DestroyImmediate(target);
        ItemPickup dropped = inventory.ServerDrop(inventory.GetItem(0), inventory.GetDurability(0));
        inventory.ServerRemove(0);
        Assert.That(dropped, Is.Not.Null);
        SetPickupCatalog(dropped, catalog);
        Assert.That(dropped.Uses, Is.EqualTo(2), "The dropped weapon keeps its wear.");
        Assert.That(inventory.ServerPickUp(dropped), Is.True);
        Assert.That(inventory.GetDurability(0), Is.EqualTo(2), "Picking it up again restores its wear.");

        GameObject barricade = Track(Box("Barricade", origin + forward * 1.8f, new Vector3(3f, 3f, 0.3f)));
        var obstacle = barricade.AddComponent<EntranceObstacle>();
        var obstacleSettings = new SerializedObject(obstacle);
        obstacleSettings.FindProperty("requirement").enumValueIndex = (int)ObstacleRequirement.Destruction;
        obstacleSettings.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();
        Assert.That(obstacle.Label, Is.EqualTo("Blocked"), "Barricades say Blocked.");
        Assert.That(itemUse.ServerUse(0, "club", origin, forward), Is.True);
        Assert.That(obstacle.Destroyed, Is.True, "A destruction weapon breaks the barricade.");
        Assert.That(inventory.GetDurability(0), Is.EqualTo(1), "Breaking an obstacle uses one durability.");

        Object.DestroyImmediate(barricade);
        target = Track(Box("Target2", origin + forward * 1.8f, Vector3.one * 0.8f));
        target.AddComponent<TestTarget>();
        Physics.SyncTransforms();
        Assert.That(itemUse.ServerUse(0, "club", origin, forward), Is.True);
        Assert.That(inventory.GetItem(0), Is.Null, "The weapon disappears when its durability runs out.");
    }

    [UnityTest]
    public IEnumerator DurabilityShowsSmallHeartsBelowTheWeaponIcon()
    {
        Track(Ground());
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        InventoryHud hud = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab")))
            .GetComponentInChildren<InventoryHud>(true);
        Assert.That(hud.DurabilitySprite, Is.Not.Null, "The prefab reuses the full heart sprite.");
        Assert.That(hud.DurabilityEmptySprite, Is.Not.Null, "Spent uses show the empty heart sprite.");

        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        PlayerItemUse itemUse = player.GetComponent<PlayerItemUse>();
        ItemCatalog catalog = UseCatalog(inventory);
        ItemDefinition club = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        club.Configure("club", "Club", null, ItemSlotKind.Active);
        club.ConfigureWeapon(2.5f, 1f, breaks: true);
        club.ConfigureDurability(true, 3);
        catalog.Register(club);
        ItemDefinition spear = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        spear.Configure("spear", "Spear", null, ItemSlotKind.Active);
        spear.ConfigureWeapon(2.5f, 1f, breaks: false);
        spear.ConfigureDurability(true, hud.MaxDurabilityHearts + 3);
        catalog.Register(spear);
        Assert.That(inventory.ServerTryAdd(club) && inventory.ServerTryAdd(spear), Is.True);
        yield return null;
        yield return null;

        IReadOnlyList<UnityEngine.UI.Image> hearts = hud.GetDurabilityHearts(0);
        Assert.That(hearts.Count, Is.EqualTo(3), "One small heart per use.");
        Assert.That(hearts, Has.All.Matches<UnityEngine.UI.Image>(heart => heart.sprite == hud.DurabilitySprite));
        Assert.That(hearts[0].rectTransform.anchoredPosition.x, Is.LessThan(hearts[2].rectTransform.anchoredPosition.x));
        Assert.That(hearts[0].rectTransform.sizeDelta.x, Is.LessThan(hud.SlotSize * 0.5f), "The hearts are small.");
        Assert.That(hud.DurabilityText[0].gameObject.activeSelf, Is.False, "Few uses need no number.");
        Assert.That(hud.GetDurabilityHearts(1).Count, Is.EqualTo(1), "Many uses show one heart ...");
        Assert.That(hud.DurabilityText[1].text, Is.EqualTo((hud.MaxDurabilityHearts + 3).ToString()), "... plus the count.");

        itemUse.GetAim(out Vector3 origin, out Vector3 forward);
        GameObject target = Track(Box("Target", origin + forward * 1.8f, Vector3.one * 0.8f));
        target.AddComponent<TestTarget>();
        Physics.SyncTransforms();
        Assert.That(itemUse.ServerUse(0, "club", origin, forward), Is.True);
        yield return null;
        hearts = hud.GetDurabilityHearts(0);
        Assert.That(hearts.Count, Is.EqualTo(3));
        Assert.That(hearts[2].sprite, Is.SameAs(hud.DurabilityEmptySprite), "A used-up heart turns empty.");
        Assert.That(hearts[1].sprite, Is.SameAs(hud.DurabilitySprite));
    }

    [UnityTest]
    public IEnumerator KeysOpenOnlyTheirOwnGateAndAreConsumed()
    {
        Track(Ground());
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        PlayerItemUse itemUse = player.GetComponent<PlayerItemUse>();
        ItemCatalog catalog = UseCatalog(inventory);
        ItemDefinition template = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        template.Configure("key", "Key", null, ItemSlotKind.Active);
        template.ConfigureUse(ItemUseKind.Key);
        catalog.SetKeyTemplate(template);
        ItemDefinition keyG2 = catalog.GetKey("G2");
        Assert.That(keyG2, Is.Not.Null);
        Assert.That(keyG2.DisplayName, Is.EqualTo("Key G2"));
        Assert.That(catalog.Find(keyG2.Id), Is.SameAs(keyG2), "Runtime keys resolve by id on every peer.");

        itemUse.GetAim(out Vector3 origin, out Vector3 forward);
        GameObject gate = Track(Box("Gate", origin + forward * 1.8f, new Vector3(3f, 3f, 0.3f)));
        var obstacle = gate.AddComponent<EntranceObstacle>();
        var obstacleSettings = new SerializedObject(obstacle);
        obstacleSettings.FindProperty("requirement").enumValueIndex = (int)ObstacleRequirement.Key;
        obstacleSettings.ApplyModifiedPropertiesWithoutUndo();
        obstacle.SetGateIdentity("G1");
        Physics.SyncTransforms();
        Assert.That(obstacle.Label, Is.EqualTo("Locked. Requires Key G1"));

        Assert.That(inventory.ServerTryAdd(keyG2), Is.True);
        Assert.That(itemUse.ServerUse(0, keyG2.Id, origin, forward), Is.False, "Key G2 does not open Gate G1.");
        Assert.That(obstacle.IsCleared, Is.False);
        Assert.That(inventory.GetItem(0), Is.SameAs(keyG2), "A wrong key is kept.");

        inventory.ServerRemove(0);
        ItemDefinition keyG1 = catalog.GetKey("G1");
        Assert.That(inventory.ServerTryAdd(keyG1), Is.True);
        Assert.That(itemUse.ServerUse(0, keyG1.Id, origin, forward), Is.True, "Key G1 opens Gate G1.");
        Assert.That(obstacle.IsCleared, Is.True);
        Assert.That(inventory.GetItem(0), Is.Null, "The key is used up.");
    }

    [UnityTest]
    public IEnumerator AThrownMolotovLeavesTheInventoryAndStartsAFireThatBurnsPlayersThenGoesOut()
    {
        Track(Ground());
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        PlayerItemUse itemUse = player.GetComponent<PlayerItemUse>();
        ItemCatalog catalog = UseCatalog(inventory);
        ItemDefinition molotov = AssetDatabase.LoadAssetAtPath<ItemDefinition>(MolotovPath);
        catalog.Register(molotov);
        Assert.That(molotov.UseKind, Is.EqualTo(ItemUseKind.Throwable));
        Assert.That(inventory.ServerTryAdd(molotov), Is.True);

        player.transform.rotation = Quaternion.identity;
        itemUse.GetAim(out Vector3 origin, out Vector3 forward);
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        Assert.That(itemUse.ServerUse(0, molotov.Id, origin, forward), Is.True, "Left mouse throws the molotov.");
        Assert.That(inventory.GetItem(0), Is.Null, "The molotov leaves the inventory.");
        yield return WaitUntil(() => FireZone.Active.Count == 1, 4f, "The molotov did not start a fire where it landed.");
        FireZone fire = FireZone.Active[0];
        Assert.That(fire.transform.position.y, Is.EqualTo(0f).Within(0.3f), "The fire sits on the ground.");
        Assert.That(Vector3.Dot(fire.transform.position - player.transform.position, forward), Is.GreaterThan(2f), "It flew forward.");
        Assert.That(fire.Lifetime, Is.EqualTo(8f).Within(0.01f));
        Assert.That(fire.VicinityRadius, Is.EqualTo(3f).Within(0.01f));
        Object.DestroyImmediate(fire.gameObject);

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        FireZone burning = FireZone.ServerSpawn(AssetDatabase.LoadAssetAtPath<GameObject>(FirePath).GetComponent<FireZone>(), player.transform.position);
        burning.Configure(3f, 1.25f, 1.6f);
        yield return WaitUntil(() => health.CurrentHealth == PlayerHealth.MaxHealth - 1, 1f, "Standing in the fire did not burn at once.");
        yield return new WaitForSeconds(0.5f);
        Assert.That(health.CurrentHealth, Is.EqualTo(PlayerHealth.MaxHealth - 1), "The fire burns once per tick.");
        yield return WaitUntil(() => health.CurrentHealth == PlayerHealth.MaxHealth - 2, 1f, "The next tick did not burn half a heart.");
        yield return WaitUntil(() => burning == null, 2f, "The fire did not go out after its lifetime.");
        Assert.That(FireZone.Active, Is.Empty);
    }

    [UnityTest]
    public IEnumerator AThrownBawangHitsAGarlicTargetAndOtherwiseJustLands()
    {
        Track(Ground());
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        PlayerItemUse itemUse = player.GetComponent<PlayerItemUse>();
        ItemCatalog catalog = UseCatalog(inventory);
        ItemDefinition bawang = AssetDatabase.LoadAssetAtPath<ItemDefinition>(BawangPath);
        catalog.Register(bawang);
        Assert.That(bawang.ThrowableKind, Is.EqualTo(ThrowableKind.Garlic));

        player.transform.rotation = Quaternion.identity;
        itemUse.GetAim(out Vector3 origin, out Vector3 forward);
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        GameObject body = Track(Box("Enemy", origin + forward * 4f + Vector3.up * 0.3f, new Vector3(1.5f, 2.5f, 1f)));
        TestTarget target = body.AddComponent<TestTarget>();
        Physics.SyncTransforms();

        var impacts = new List<Collider>();
        System.Action<ThrowableProjectile, Collider, Vector3> onImpact = (_, hit, __) => impacts.Add(hit);
        ThrowableProjectile.ServerImpact += onImpact;
        try
        {
            Assert.That(inventory.ServerTryAdd(bawang), Is.True);
            Assert.That(itemUse.ServerUse(0, bawang.Id, origin, forward), Is.True);
            yield return WaitUntil(() => impacts.Count == 1, 3f, "The bawang never landed.");
            Assert.That(target.GarlicHits, Is.EqualTo(1), "The bawang hit the enemy's collider.");
            Assert.That(target.GarlicSeconds, Is.EqualTo(30f).Within(0.01f), "Garlic repels for 30 seconds.");
            Assert.That(target.Thrower, Is.SameAs(player.GetComponent<NetworkIdentity>()));
            Assert.That(FireZone.Active, Is.Empty, "Garlic starts no fire.");

            Object.DestroyImmediate(body);
            Physics.SyncTransforms();
            Assert.That(inventory.ServerTryAdd(bawang), Is.True);
            Assert.That(itemUse.ServerUse(0, bawang.Id, origin, forward), Is.True);
            yield return WaitUntil(() => impacts.Count == 2, 3f, "A missed bawang never landed.");
            Assert.That(target == null || target.GarlicHits == 1, Is.True);
        }
        finally
        {
            ThrowableProjectile.ServerImpact -= onImpact;
        }
    }

    [UnityTest]
    public IEnumerator FallingOffTheMapReturnsTheOwnerToASpawnPoint()
    {
        Track(Ground());
        var spawnRoot = Track(new GameObject("SpawnPoints"));
        spawnRoot.SetActive(false);
        var point = new GameObject("Point");
        point.transform.SetParent(spawnRoot.transform, false);
        point.transform.position = new Vector3(5f, 0.2f, 5f);
        spawnRoot.AddComponent<PlayerSpawnPoints>();
        spawnRoot.SetActive(true);
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerMovement movement = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
        movement.TeleportTo(new Vector3(0f, -150f, 0f), Quaternion.identity);
        yield return WaitUntil(() => movement.transform.position.y > -10f, 1f, "The player stayed below the map.");
        Assert.That(Vector3.Distance(movement.transform.position, point.transform.position), Is.LessThan(1f), "The player respawned on the spawn point.");
    }

    private sealed class TestTarget : MonoBehaviour, IWeaponTarget, IGarlicTarget
    {
        public int GarlicHits;
        public float GarlicSeconds;
        public NetworkIdentity Thrower;

        public bool ServerTakeWeaponHit(ItemDefinition weapon, NetworkIdentity attacker) => true;

        public bool ServerTakeGarlic(NetworkIdentity thrower, float seconds)
        {
            GarlicHits++;
            GarlicSeconds = seconds;
            Thrower = thrower;
            return true;
        }
    }

    private ItemCatalog UseCatalog(PlayerInventory inventory)
    {
        ItemCatalog catalog = Track(ScriptableObject.CreateInstance<ItemCatalog>());
        var settings = new SerializedObject(inventory);
        settings.FindProperty("catalog").objectReferenceValue = catalog;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return catalog;
    }

    private static void SetPickupCatalog(ItemPickup pickup, ItemCatalog catalog)
    {
        // Pickups resolve their item id through their own catalog; point this one at the test catalog.
        var settings = new SerializedObject(pickup);
        settings.FindProperty("catalog").objectReferenceValue = catalog;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private void StartHost()
    {
        managerObject = new GameObject("Throwable Test NetworkManager");
        managerObject.SetActive(false);
        var transport = managerObject.AddComponent<TelepathyTransport>();
        var manager = managerObject.AddComponent<SteamNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        manager.autoCreatePlayer = true;
        managerObject.SetActive(true);
        NetworkServer.listen = false;
        manager.StartHost();
    }

    private static GameObject Ground()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(60f, 1f, 60f);
        return ground;
    }

    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = size;
        return box;
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string message)
    {
        float timeout = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(condition(), Is.True, message);
    }

    private T Track<T>(T obj) where T : Object
    {
        created.Add(obj);
        return obj;
    }
}
#endif
