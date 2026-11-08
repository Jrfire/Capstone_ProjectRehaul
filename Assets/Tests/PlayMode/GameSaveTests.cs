#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class GameSaveTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string LoadingPath = "Assets/PREFABS/TravelLoadingScreen.prefab";
    private const string PickupPath = "Assets/PREFABS/ItemPickup.prefab";
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    private static readonly string[] PrefsKeys = { "Capstone.Bindings" };

    private readonly List<Object> created = new List<Object>();
    private readonly List<(ItemCatalog, ItemDefinition)> registered = new List<(ItemCatalog, ItemDefinition)>();
    private readonly Dictionary<string, string> savedPrefs = new Dictionary<string, string>();
    private GameObject managerObject;
    private Transport previousTransport;
    private bool previousListen;
    private string saveDirectory;

    [SetUp]
    public void Setup()
    {
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        saveDirectory = Path.Combine(Path.GetTempPath(), "CapstoneSavePlayTests_" + System.Guid.NewGuid().ToString("N"));
        SaveSystem.DirectoryOverride = saveDirectory;
        savedPrefs.Clear();
        foreach (string key in PrefsKeys)
        {
            if (PlayerPrefs.HasKey(key))
                savedPrefs[key] = PlayerPrefs.GetString(key);
        }
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
        foreach ((ItemCatalog catalog, ItemDefinition item) in registered)
        {
            var items = (List<ItemDefinition>)typeof(ItemCatalog)
                .GetField("items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(catalog);
            items.Remove(item);
        }
        registered.Clear();
        foreach (string key in PrefsKeys)
        {
            if (savedPrefs.TryGetValue(key, out string value))
                PlayerPrefs.SetString(key, value);
            else
                PlayerPrefs.DeleteKey(key);
        }
        GameSettings.ReloadFromPrefs();
        foreach (SteamBootstrap bootstrap in Object.FindObjectsByType<SteamBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(bootstrap.gameObject);
        SteamBootstrap.SkipSteamInitialization = false;
        MenuLaunch.SetActiveSaveSlot(-1);
        SaveSystem.DirectoryOverride = null;
        if (Directory.Exists(saveDirectory))
            Directory.Delete(saveDirectory, true);
        PlayerControlLock.ReleaseAll();
        HudVisibility.ShowAll();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
    }

    [UnityTest]
    public IEnumerator HostingASaveRestoresTheGameAndSkipsCharacterSelect()
    {
        Track(GroundAt(Vector3.zero));
        ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponent<PlayerInventory>().Catalog;
        Assert.That(catalog, Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath).GetComponent<ItemPickup>().Catalog, Is.SameAs(catalog));
        ItemDefinition key = Item(catalog, "save_test_key");

        var data = new GameSaveData { round = 3, elapsed = 100f, gold = 2, silver = 1, bronze = 4, turnedIn = 1, money = 55, roundReward = 10 };
        data.players.Add(new PlayerSaveData { key = "host", name = "Host", characterIndex = 1, health = 4,
            slots = new List<string> { "", key.Id } });
        data.baseItems.Add(new FloorItemSaveData { itemId = key.Id, position = new Vector3(3f, 0.5f, 0f) });
        Assert.That(SaveSystem.Write(0, data), Is.True);
        MenuLaunch.SetActiveSaveSlot(0);

        (RoundClock clock, AgimatBank bank, GameSaveManager saves) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;

        Assert.That(saves.ActiveSlot, Is.Zero);
        Assert.That(saves.LoadedFromSave, Is.True);
        Assert.That(new[] { bank.Count(AgimatType.Gold), bank.Count(AgimatType.Silver), bank.Count(AgimatType.Bronze) },
            Is.EqualTo(new[] { 2, 1, 4 }), "Agimats come back.");
        Assert.That(bank.TurnedIn, Is.EqualTo(1));
        Assert.That(bank.Money, Is.EqualTo(55), "Money comes back.");
        Assert.That(bank.RoundReward, Is.EqualTo(10));
        Assert.That(clock.Round, Is.EqualTo(3), "The night comes back.");
        Assert.That(clock.Elapsed, Is.EqualTo(100f).Within(0.5f), "The time left comes back.");

        var selection = player.GetComponent<PlayerCharacterSelection>();
        Assert.That(selection.HasSelected, Is.True, "Loaded players skip character select.");
        Assert.That(selection.SelectedIndex, Is.EqualTo(1));
        Assert.That(player.GetComponent<PlayerHealth>().CurrentHealth, Is.EqualTo(4));
        var inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.SlotIds().Take(2), Is.EqualTo(new[] { "", key.Id }), "The inventory comes back slot for slot.");

        ItemPickup pickup = ItemPickup.Active.Single();
        Assert.That(pickup.ItemId, Is.EqualTo(key.Id), "Items left on the base floor come back.");
        Assert.That(pickup.transform.position, Is.EqualTo(new Vector3(3f, 0.5f, 0f)));

        bank.ServerAddMoney(5);
        inventory.ServerRemove(1);
        Assert.That(saves.SaveNow(), Is.True, "At base the game saves.");
        GameSaveData written = SaveSystem.Load(0);
        Assert.That(written.money, Is.EqualTo(60));
        Assert.That(written.round, Is.EqualTo(3));
        PlayerSaveData profile = written.FindPlayer("host");
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.characterIndex, Is.EqualTo(1));
        Assert.That(profile.health, Is.EqualTo(4));
        Assert.That(profile.slots.All(string.IsNullOrEmpty), Is.True);
        Assert.That(written.baseItems.Single().itemId, Is.EqualTo(key.Id));

        bank.ServerAddMoney(7);
        managerObject.GetComponent<NetworkManager>().StopHost();
        yield return null;
        Assert.That(SaveSystem.Load(0).money, Is.EqualTo(67), "Leaving saves the game.");
    }

    [UnityTest]
    public IEnumerator SessionsWithoutASlotNeverSave()
    {
        Track(GroundAt(Vector3.zero));
        MenuLaunch.SetActiveSaveSlot(-1);
        (_, _, GameSaveManager saves) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        Assert.That(saves.ActiveSlot, Is.EqualTo(-1));
        Assert.That(saves.LoadedFromSave, Is.False);
        Assert.That(NetworkClient.localPlayer.GetComponent<PlayerCharacterSelection>().HasSelected, Is.False,
            "New games still pick a character.");
        Assert.That(saves.SaveNow(), Is.False);
        managerObject.GetComponent<NetworkManager>().StopHost();
        yield return null;
        Assert.That(Enumerable.Range(0, SaveSystem.SlotCount).Any(SaveSystem.Exists), Is.False);
    }

    [UnityTest]
    public IEnumerator AwayFromBaseTheGameDoesNotSave()
    {
        Track(GroundAt(Vector3.zero));
        MenuLaunch.SetActiveSaveSlot(2);
        (_, _, GameSaveManager saves) = StartHost("SomeOtherBase");
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        Assert.That(saves.ActiveSlot, Is.EqualTo(2));
        Assert.That(saves.SaveNow(), Is.False, "Only the base scene is saved.");
        managerObject.GetComponent<NetworkManager>().StopHost();
        yield return null;
        Assert.That(SaveSystem.Exists(2), Is.False, "Leaving away from base keeps the last base save.");
    }

    [UnityTest]
    public IEnumerator MainMenuNavigatesRebindsAndDeletesSaves()
    {
        SteamBootstrap.SkipSteamInitialization = true;
        Assert.That(SaveSystem.Write(1, new GameSaveData { round = 2, money = 30 }), Is.True);
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(MenuScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        while (!load.isDone)
            yield return null;
        Scene menu = SceneManager.GetSceneByPath(MenuScenePath);
        try
        {
            yield return null;
            MainMenuScreen screen = menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuScreen>(true)).Single();
            Assert.That(screen.ActivePanel.name, Does.Contain("Main"));

            screen.ShowPlay();
            Assert.That(screen.ActivePanel.name, Does.Contain("Play"));
            screen.ShowSaves();
            Assert.That(screen.ActivePanel.name, Does.Contain("Save"));
            Text[] slotTexts = SlotTexts(screen);
            Assert.That(slotTexts[0].text, Does.Contain("New Game"));
            Assert.That(slotTexts[1].text, Does.Contain("Night 2"));

            screen.SelectSlot(1);
            Assert.That(screen.SelectedSlot, Is.EqualTo(1));
            screen.DeleteSelectedSlot();
            Assert.That(SaveSystem.Exists(1), Is.True, "The first press only asks for confirmation.");
            screen.DeleteSelectedSlot();
            Assert.That(SaveSystem.Exists(1), Is.False, "The second press deletes the save.");
            Assert.That(SlotTexts(screen)[1].text, Does.Contain("New Game"));

            screen.ShowSettings();
            Assert.That(screen.ActivePanel.name, Does.Contain("Settings"));
            Assert.That(screen.BindingRowCount, Is.GreaterThanOrEqualTo(8), "Movement keys, sprint, use and more can be rebound.");
            int useRow = Enumerable.Range(0, screen.BindingRowCount).First(row => screen.BindingText(row) == "E");
            screen.ApplyRebind(useRow, "<Keyboard>/r");
            Assert.That(screen.BindingText(useRow), Is.EqualTo("R"));
            Assert.That(GameSettings.BindingOverrideCount, Is.EqualTo(1), "Rebinds are saved for the game's players.");
            screen.ResetBindings();
            Assert.That(screen.BindingText(useRow), Is.EqualTo("E"));
            Assert.That(GameSettings.BindingOverrideCount, Is.Zero);

            SettingsPanel settingsPanel = screen.Settings;
            Assert.That(settingsPanel.FindBindingSlot("Interact", false), Is.EqualTo(useRow), "E is the Interact row's primary key.");
            int interactAlternate = settingsPanel.FindBindingSlot("Interact", true);
            Assert.That(screen.BindingText(interactAlternate), Is.EqualTo("None"), "The alternate key starts unbound.");
            screen.ApplyRebind(interactAlternate, "<Keyboard>/f2");
            Assert.That(screen.BindingText(interactAlternate), Is.EqualTo("F2"));
            Assert.That(screen.BindingText(useRow), Is.EqualTo("E"), "The alternate sits beside the primary key.");
            screen.ResetBindings();
            Assert.That(screen.BindingText(interactAlternate), Is.EqualTo("None"));
            int useItem = settingsPanel.FindBindingSlot("UseItem", false);
            Assert.That(screen.BindingText(useItem), Is.EqualTo("LMB"), "Left mouse uses / attacks with the held item.");
            Assert.That(settingsPanel.FindBindingSlot("Jump", false) < 0 &&
                        settingsPanel.FindBindingSlot("Crouch", false) < 0 && settingsPanel.FindBindingSlot("InventoryScroll", false) < 0,
                Is.True, "Jump and crouch are gone; scrolling is not rebindable here.");
            Assert.That(screen.BindingText(settingsPanel.FindBindingSlot("Movement", true, "up")), Is.EqualTo("Up Arrow"),
                "Arrow keys are the movement alternates.");
            Text[] labels = settingsPanel.GetComponentsInChildren<Text>(true);
            Assert.That(labels.Any(text => text.text == "Use / Attack"), Is.True);
            Assert.That(labels.Count(text => text.text == "Interact"), Is.EqualTo(1), "One row per action, not one per key.");
        }
        finally
        {
            SceneManager.UnloadSceneAsync(menu);
        }
        yield return null;
    }

    private static Text[] SlotTexts(MainMenuScreen screen)
    {
        var serialized = new SerializedObject(screen);
        SerializedProperty texts = serialized.FindProperty("slotTexts");
        return Enumerable.Range(0, texts.arraySize).Select(i => (Text)texts.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
    }

    private ItemDefinition Item(ItemCatalog catalog, string id)
    {
        Texture2D texture = Track(new Texture2D(4, 4));
        ItemDefinition item = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        item.Configure(id, id, Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f)), ItemSlotKind.Active, 0);
        catalog.Register(item);
        registered.Add((catalog, item));
        return item;
    }

    private (RoundClock, AgimatBank, GameSaveManager) StartHost(string baseScene = null)
    {
        managerObject = new GameObject("Save Test NetworkManager");
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
        clockSettings.FindProperty("roundMinutes").floatValue = 10f;
        clockSettings.FindProperty("baseSceneName").stringValue = baseScene ?? SceneManager.GetActiveScene().name;
        clockSettings.ApplyModifiedPropertiesWithoutUndo();
        var bank = managerObject.AddComponent<AgimatBank>();
        var saves = managerObject.AddComponent<GameSaveManager>();
        var saveSettings = new SerializedObject(saves);
        saveSettings.FindProperty("itemPickupPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath).GetComponent<ItemPickup>();
        saveSettings.ApplyModifiedPropertiesWithoutUndo();
        manager.spawnPrefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath));
        managerObject.SetActive(true);

        NetworkServer.listen = false;
        manager.StartHost();
        return (clock, bank, saves);
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
