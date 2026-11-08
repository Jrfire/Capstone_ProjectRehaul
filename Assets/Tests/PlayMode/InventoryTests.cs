#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class InventoryTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string LoadingPath = "Assets/PREFABS/TravelLoadingScreen.prefab";
    private const string HudPath = "Assets/PREFABS/PlayerHud.prefab";

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
        PlayerControlLock.ReleaseAll();
        HudVisibility.ShowAll();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
    }

    [UnityTest]
    public IEnumerator SlotsStartAtTwoAndTheSelectedSlotGrowsWhileTheBarPulses()
    {
        Track(GroundAt(Vector3.zero));
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerInventory inventory = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
        Assert.That(inventory, Is.Not.Null, "NetworkPlayer carries the inventory.");
        InventoryHud hud = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath))).GetComponentInChildren<InventoryHud>(true);
        yield return null;
        yield return null;

        Assert.That(inventory.Capacity, Is.EqualTo(PlayerInventory.BaseSlots));
        Assert.That(hud.Bar.gameObject.activeSelf, Is.True);
        Assert.That(hud.PassiveSlot.gameObject.activeSelf, Is.True, "The passive slot is always shown.");
        Assert.That(ActiveSlots(hud), Is.EqualTo(2), "Two active slots by default.");
        Assert.That(inventory.SelectedSlot, Is.Zero);
        Assert.That(hud.Slots[0].preferredWidth, Is.EqualTo(hud.SelectedSlotSize), "The selected slot is larger.");
        Assert.That(hud.Slots[1].preferredWidth, Is.EqualTo(hud.SlotSize));
        Assert.That(hud.Bar.localScale.x, Is.EqualTo(1f), "No pulse on spawn.");

        inventory.SelectSlot(3);
        Assert.That(inventory.SelectedSlot, Is.EqualTo(1), "Slots 3 and 4 need the upgrade.");
        yield return null;
        Assert.That(hud.Pulsing, Is.True, "Changing slots enlarges the bar.");
        yield return WaitUntil(() => hud.Bar.localScale.x > 1.05f, 2f, "The bar did not pulse larger.");
        yield return WaitUntil(() => Mathf.Abs(hud.Slots[1].preferredWidth - hud.SelectedSlotSize) <= 0.5f, 2f,
            "The new slot did not ease to the larger size.");
        Assert.That(hud.Slots[1].preferredWidth, Is.EqualTo(hud.SelectedSlotSize).Within(0.5f), "The new slot eases to the larger size.");
        Assert.That(hud.Slots[0].preferredWidth, Is.EqualTo(hud.SlotSize).Within(0.5f));
        yield return WaitUntil(() => SyncedSelection(inventory) == 1, 2f, "The selection did not reach the server.");

        inventory.ScrollSlots(1);
        Assert.That(inventory.SelectedSlot, Is.Zero, "Scrolling wraps around the available slots.");
        inventory.ScrollSlots(-1);
        Assert.That(inventory.SelectedSlot, Is.EqualTo(1));
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(hud.Pulsing, Is.True, "Still enlarged right after the last change.");
        yield return WaitUntil(() => hud.Bar.localScale.x == 1f, 3f, "The bar did not settle back after the player stopped changing slots.");

        HudVisibility.Hide(this);
        yield return null;
        Assert.That(hud.Bar.gameObject.activeSelf, Is.False, "Menus hide the inventory bar.");
        HudVisibility.Show(this);
    }

    [UnityTest]
    public IEnumerator ItemsFillSlotsAndABackpackAddsTwoMore()
    {
        Track(GroundAt(Vector3.zero));
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerInventory inventory = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
        var catalog = Track(ScriptableObject.CreateInstance<ItemCatalog>());
        ItemDefinition bandage = Item(catalog, "bandage", ItemSlotKind.Active);
        ItemDefinition key = Item(catalog, "key", ItemSlotKind.Active);
        ItemDefinition axe = Item(catalog, "axe", ItemSlotKind.Active);
        ItemDefinition backpack = Item(catalog, "backpack", ItemSlotKind.Passive, 2);
        var settings = new SerializedObject(inventory);
        settings.FindProperty("catalog").objectReferenceValue = catalog;
        settings.ApplyModifiedPropertiesWithoutUndo();
        InventoryHud hud = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath))).GetComponentInChildren<InventoryHud>(true);

        Assert.That(inventory.ServerTryAdd(bandage), Is.True);
        Assert.That(inventory.ServerTryAdd(key), Is.True);
        Assert.That(inventory.ServerTryAdd(axe), Is.False, "Two slots are full.");
        Assert.That(inventory.ServerTryAdd(Track(ScriptableObject.CreateInstance<ItemDefinition>())), Is.False, "Items must be in the catalog.");
        yield return null;
        yield return null;
        Assert.That(hud.SlotIcons[0].enabled && hud.SlotIcons[0].sprite == bandage.Icon, Is.True, "The item icon sits over the slot.");
        Assert.That(hud.SlotIcons[1].sprite, Is.SameAs(key.Icon));
        Assert.That(hud.SlotIcons[0].transform.parent, Is.SameAs(hud.Slots[0].transform));
        Assert.That(hud.PassiveIcon.enabled, Is.False);

        Assert.That(inventory.ServerTryAdd(backpack), Is.True, "Passive items go to the passive slot.");
        Assert.That(inventory.ServerTryAdd(backpack), Is.False, "Only one passive slot.");
        Assert.That(inventory.Capacity, Is.EqualTo(4), "A backpack adds two slots.");
        Assert.That(inventory.ServerTryAdd(axe), Is.True);
        yield return null;
        yield return null;
        Assert.That(ActiveSlots(hud), Is.EqualTo(4));
        Assert.That(hud.PassiveIcon.enabled && hud.PassiveIcon.sprite == backpack.Icon, Is.True);
        Assert.That(hud.SlotIcons[2].sprite, Is.SameAs(axe.Icon));
        inventory.SelectSlot(3);
        Assert.That(inventory.SelectedSlot, Is.EqualTo(3), "Slot 4 is usable with the upgrade.");
        Assert.That(inventory.SelectedItem, Is.Null);
        inventory.SelectSlot(2);
        Assert.That(inventory.SelectedItem, Is.SameAs(axe));

        var overflow = new List<ItemDefinition>();
        Assert.That(inventory.ServerRemovePassive(overflow), Is.SameAs(backpack));
        Assert.That(overflow, Is.EquivalentTo(new[] { axe }), "Items in the lost slots come out.");
        Assert.That(inventory.Capacity, Is.EqualTo(2));
        Assert.That(inventory.SelectedSlot, Is.EqualTo(1), "The selection clamps to the remaining slots.");
        yield return null;
        yield return null;
        Assert.That(ActiveSlots(hud), Is.EqualTo(2));

        Assert.That(inventory.ServerRemove(0), Is.SameAs(bandage));
        inventory.ServerClear();
        Assert.That(inventory.GetItem(1), Is.Null);
    }

    [UnityTest]
    public IEnumerator LeftMouseUsesTheSelectedItemHealingOrAttacking()
    {
        Track(GroundAt(Vector3.zero));
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        PlayerItemUse itemUse = player.GetComponent<PlayerItemUse>();
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        PlayerAudio playerAudio = player.GetComponent<PlayerAudio>();
        Assert.That(itemUse, Is.Not.Null, "NetworkPlayer handles left mouse item use.");
        var catalog = Track(ScriptableObject.CreateInstance<ItemCatalog>());
        ItemDefinition medkit = Item(catalog, "medkit", ItemSlotKind.Active);
        medkit.ConfigureUse(ItemUseKind.Consumable, 2);
        ItemDefinition itak = Item(catalog, "itak", ItemSlotKind.Active);
        itak.ConfigureUse(ItemUseKind.Weapon);
        var settings = new SerializedObject(inventory);
        settings.FindProperty("catalog").objectReferenceValue = catalog;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var used = new List<ItemDefinition>();
        System.Action<PlayerItemUse, ItemDefinition, int> onUsed = (_, item, __) => used.Add(item);
        PlayerItemUse.ServerItemUsed += onUsed;
        try
        {
            Assert.That(itemUse.TryUseSelected(), Is.False, "An empty slot does nothing.");
            Assert.That(inventory.ServerTryAdd(medkit) && inventory.ServerTryAdd(itak), Is.True);
            inventory.SelectSlot(0);
            Assert.That(itemUse.ServerUse(0), Is.False, "Healing is refused at full health.");
            Assert.That(inventory.GetItem(0), Is.SameAs(medkit), "A refused heal keeps the item.");

            health.TakeDamage(2);
            Assert.That(itemUse.TryUseSelected(), Is.True);
            yield return WaitUntil(() => health.CurrentHealth == PlayerHealth.MaxHealth, 2f, "The medkit did not heal.");
            Assert.That(inventory.GetItem(0), Is.Null, "Consumables are used up.");
            Assert.That(used, Is.EqualTo(new[] { medkit }));

            inventory.SelectSlot(1);
            yield return new WaitForSeconds(0.4f);
            int attacks = playerAudio.AttacksPlayed;
            Assert.That(itemUse.TryUseSelected(), Is.True, "Left mouse attacks with a weapon.");
            Assert.That(playerAudio.AttacksPlayed, Is.EqualTo(attacks + 1), "The attack sound plays.");
            Assert.That(itemUse.TryUseSelected(), Is.False, "The use cooldown applies.");
            yield return WaitUntil(() => used.Count == 2, 2f, "The weapon use did not reach the server.");
            Assert.That(used[1], Is.SameAs(itak));
            Assert.That(inventory.GetItem(1), Is.SameAs(itak), "Weapons are not consumed.");

            PlayerControlLock.Acquire(this);
            yield return new WaitForSeconds(0.4f);
            Assert.That(itemUse.CanUse, Is.False, "Menus block item use.");
            PlayerControlLock.Release(this);
        }
        finally
        {
            PlayerItemUse.ServerItemUsed -= onUsed;
        }
    }

    [UnityTest]
    public IEnumerator WeaponSwingsHitTheTargetInFrontBreakBarricadesAndAreBlockedByWalls()
    {
        Track(GroundAt(Vector3.zero));
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerItemUse itemUse = player.GetComponent<PlayerItemUse>();
        ItemDefinition stick = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        stick.Configure("stick", "Stick", null, ItemSlotKind.Active);
        stick.ConfigureWeapon(2.5f, 1.5f, breaks: false);
        ItemDefinition itak = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        itak.Configure("itak", "Itak", null, ItemSlotKind.Active);
        itak.ConfigureWeapon(2.5f, 1.5f, breaks: true);
        Vector3 origin = player.transform.position + Vector3.up * 1.6f;
        Vector3 forward = Vector3.forward;
        player.transform.rotation = Quaternion.identity;
        yield return null;
        itemUse.GetAim(out Vector3 cameraOrigin, out Vector3 cameraForward);
        Assert.That(Vector3.Distance(cameraOrigin, player.transform.position), Is.LessThan(2.5f), "The aim starts at the player's camera.");
        origin = cameraOrigin;
        forward = cameraForward;

        TestWeaponTarget target = Track(Box("Target", origin + forward * 2f, Vector3.one * 0.8f)).AddComponent<TestWeaponTarget>();
        Physics.SyncTransforms();
        Assert.That(itemUse.ServerAttack(stick, origin, forward), Is.SameAs(target.gameObject), "The target in reach is hit.");
        Assert.That(target.Hits, Is.EqualTo(1));
        Assert.That(target.LastWeapon, Is.SameAs(stick));
        Assert.That(itemUse.ServerAttack(stick, origin, -forward), Is.Null, "Swinging the other way misses.");
        Assert.That(itemUse.ServerAttack(stick, origin + Vector3.right * 50f, forward), Is.SameAs(target.gameObject),
            "An implausible aim origin falls back to the player's own camera.");

        target.transform.position = origin + forward * 4f;
        Physics.SyncTransforms();
        Assert.That(itemUse.ServerAttack(stick, origin, forward), Is.Null, "Targets beyond the weapon's range are not hit.");

        target.transform.position = origin + forward * 2f;
        GameObject wall = Track(Box("Wall", origin + forward * 1.2f, new Vector3(3f, 3f, 0.2f)));
        Physics.SyncTransforms();
        int hits = target.Hits;
        Assert.That(itemUse.ServerAttack(stick, origin, forward), Is.Null, "A wall in front blocks the swing.");
        Assert.That(target.Hits, Is.EqualTo(hits));
        Object.DestroyImmediate(wall);
        Object.DestroyImmediate(target.gameObject);

        GameObject barricade = Track(Box("Barricade", origin + forward * 1.8f, new Vector3(3f, 3f, 0.3f)));
        EntranceObstacle obstacle = barricade.AddComponent<EntranceObstacle>();
        var obstacleSettings = new SerializedObject(obstacle);
        obstacleSettings.FindProperty("requirement").enumValueIndex = (int)ObstacleRequirement.Destruction;
        obstacleSettings.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();
        Assert.That(itemUse.ServerAttack(stick, origin, forward), Is.Null, "Only weapons that break obstacles break barricades.");
        Assert.That(obstacle.Destroyed, Is.False);

        var usedHits = new List<GameObject>();
        System.Action<PlayerItemUse, ItemDefinition, GameObject> onHit = (_, __, hit) => usedHits.Add(hit);
        PlayerItemUse.ServerWeaponHit += onHit;
        try
        {
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            ItemCatalog catalog = Track(ScriptableObject.CreateInstance<ItemCatalog>());
            catalog.Register(itak);
            var settings = new SerializedObject(inventory);
            settings.FindProperty("catalog").objectReferenceValue = catalog;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(inventory.ServerTryAdd(itak), Is.True);
            Assert.That(itemUse.ServerUse(0, "itak", origin, forward), Is.True, "Left mouse with the itak swings it.");
            Assert.That(obstacle.Destroyed, Is.True, "The itak breaks the barricade.");
            Assert.That(itemUse.LastWeaponHit, Is.SameAs(barricade));
            Assert.That(usedHits, Is.EqualTo(new[] { barricade }));
            Assert.That(inventory.GetItem(0), Is.SameAs(itak), "Weapons are kept.");
        }
        finally
        {
            PlayerItemUse.ServerWeaponHit -= onHit;
        }
    }

    private sealed class TestWeaponTarget : MonoBehaviour, IWeaponTarget
    {
        public int Hits;
        public ItemDefinition LastWeapon;

        public bool ServerTakeWeaponHit(ItemDefinition weapon, NetworkIdentity attacker)
        {
            Hits++;
            LastWeapon = weapon;
            return true;
        }
    }

    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = size;
        return box;
    }

    [UnityTest]
    public IEnumerator ATeamWipeAwayFromBaseHalvesTheAgimatsAndTheMoney()
    {
        Track(GroundAt(Vector3.zero));
        (RoundClock clock, AgimatBank bank) = StartHost("NotTheBase");
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject hudObject = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath)));
        AgimatHud hud = hudObject.GetComponentInChildren<AgimatHud>(true);
        bank.ServerAdd(AgimatType.Gold, 5);
        bank.ServerAdd(AgimatType.Silver, 3);
        bank.ServerAdd(AgimatType.Bronze, 1);
        bank.ServerAddMoney(45);
        yield return WaitUntil(() => hud.MoneyText.text == "\u20B145", 2f, "The money did not show next to the hearts.");
        Assert.That(bank.ServerTrySpend(50), Is.False, "Cannot spend more than the team has.");
        Assert.That(bank.ServerTrySpend(0), Is.True);

        // The base scene cannot be loaded here, so the return trip after the wipe logs an error.
        LogAssert.ignoreFailingMessages = true;
        NetworkClient.localPlayer.GetComponent<PlayerHealth>().SetHealth(0);
        yield return WaitUntil(() => bank.Money == 22, 3f, "Everyone dying away from base did not halve the money.");
        LogAssert.ignoreFailingMessages = false;
        Assert.That(bank.Count(AgimatType.Gold), Is.EqualTo(2));
        Assert.That(bank.Count(AgimatType.Silver), Is.EqualTo(1));
        Assert.That(bank.Count(AgimatType.Bronze), Is.Zero, "Halves round down.");
        yield return WaitUntil(() => hud.MoneyText.text == "\u20B122", 2f, "The HUD did not show the halved money.");
        Assert.That(clock.Round, Is.EqualTo(1));
    }

    private static int ActiveSlots(InventoryHud hud)
    {
        int count = 0;
        foreach (var slot in hud.Slots)
        {
            if (slot.gameObject.activeSelf)
                count++;
        }
        return count;
    }

    private static int SyncedSelection(PlayerInventory inventory) =>
        (int)typeof(PlayerInventory).GetField("syncedSelectedSlot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(inventory);

    private ItemDefinition Item(ItemCatalog catalog, string id, ItemSlotKind kind, int extraSlots = 0)
    {
        Texture2D texture = Track(new Texture2D(4, 4));
        ItemDefinition item = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        item.Configure(id, id, Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f)), kind, extraSlots);
        catalog.Register(item);
        return item;
    }

    private (RoundClock, AgimatBank) StartHost(string baseScene = null)
    {
        managerObject = new GameObject("Inventory Test NetworkManager");
        managerObject.SetActive(false);
        var transport = managerObject.AddComponent<TelepathyTransport>();
        var manager = managerObject.AddComponent<SteamNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        manager.autoCreatePlayer = true;
        manager.dontDestroyOnLoad = true;
        var director = managerObject.AddComponent<TravelDirector>();
        var settings = new SerializedObject(director);
        settings.FindProperty("loadingScreenPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(LoadingPath).GetComponent<TravelLoadingScreen>();
        settings.FindProperty("fadeDuration").floatValue = 0.1f;
        settings.FindProperty("minimumLoadingSeconds").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var clock = managerObject.AddComponent<RoundClock>();
        var clockSettings = new SerializedObject(clock);
        clockSettings.FindProperty("roundMinutes").floatValue = 1f;
        clockSettings.FindProperty("baseSceneName").stringValue = baseScene ?? SceneManager.GetActiveScene().name;
        clockSettings.FindProperty("allDeadReturnSeconds").floatValue = 0.2f;
        clockSettings.ApplyModifiedPropertiesWithoutUndo();
        var bank = managerObject.AddComponent<AgimatBank>();
        managerObject.SetActive(true);

        NetworkServer.listen = false;
        manager.StartHost();
        return (clock, bank);
    }

    private static GameObject GroundAt(Vector3 position)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = position + new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(50f, 1f, 50f);
        return ground;
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
