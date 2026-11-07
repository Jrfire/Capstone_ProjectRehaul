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
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.That(hud.Bar.localScale.x, Is.GreaterThan(1.05f));
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
