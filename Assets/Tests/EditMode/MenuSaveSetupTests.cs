using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MenuSaveSetupTests
{
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string SettingsPath = "Assets/PREFABS/SettingsPanel.prefab";
    private const string PausePath = "Assets/PREFABS/PauseMenu.prefab";
    private const string BindingsKey = "Capstone.Bindings";

    private string saveDirectory;

    [SetUp]
    public void SetUp()
    {
        saveDirectory = Path.Combine(Path.GetTempPath(), "CapstoneSaveTests_" + System.Guid.NewGuid().ToString("N"));
        SaveSystem.DirectoryOverride = saveDirectory;
    }

    [TearDown]
    public void TearDown()
    {
        SaveSystem.DirectoryOverride = null;
        if (Directory.Exists(saveDirectory))
            Directory.Delete(saveDirectory, true);
    }

    [Test]
    public void MainMenuIsTheFirstSceneAndIsWired()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        Assert.That(scenes.Length, Is.GreaterThan(1));
        Assert.That(scenes[0].path, Is.EqualTo(MenuScenePath), "The game starts in the main menu.");
        Assert.That(scenes[0].enabled, Is.True);
        Assert.That(scenes.Any(scene => scene.path == SampleScenePath && scene.enabled), Is.True);

        Scene menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Additive);
        try
        {
            MainMenuScreen screen = InScene<MainMenuScreen>(menu).Single();
            var serialized = new SerializedObject(screen);
            SerializedProperty property = serialized.GetIterator();
            var missing = new List<string>();
            while (property.NextVisible(true))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null &&
                    property.name != "m_Script")
                    missing.Add(property.propertyPath);
            }
            Assert.That(missing, Is.Empty, "Every MainMenuScreen reference is assigned.");
            Assert.That(serialized.FindProperty("slotButtons").arraySize, Is.EqualTo(SaveSystem.SlotCount));
            SettingsPanel settings = (SettingsPanel)serialized.FindProperty("settings").objectReferenceValue;
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(settings.gameObject),
                Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPath)), "The menu uses the shared settings prefab.");
            Assert.That(((GameObject)serialized.FindProperty("noticePanel").objectReferenceValue).activeSelf, Is.False,
                "The kick/disconnect notice starts hidden.");
            Assert.That(InScene<SteamBootstrap>(menu).Count(), Is.EqualTo(1), "The menu initializes Steam for invites.");
            Assert.That(InScene<EventSystem>(menu).Count(), Is.EqualTo(1));
            Assert.That(InScene<Camera>(menu).Count(), Is.EqualTo(1));
            Assert.That(InScene<NetworkManager>(menu), Is.Empty, "The menu has no NetworkManager.");
        }
        finally
        {
            EditorSceneManager.CloseScene(menu, true);
        }
    }

    [Test]
    public void SettingsAndPauseMenuPrefabsAreWired()
    {
        GameObject settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPath);
        SettingsPanel settings = settingsPrefab.GetComponent<SettingsPanel>();
        Assert.That(settings, Is.Not.Null);
        Assert.That(MissingReferences(settings), Is.Empty, "Every SettingsPanel reference is assigned.");
        var settingsSerialized = new SerializedObject(settings);
        Assert.That(((GameObject)settingsSerialized.FindProperty("bindingRowTemplate").objectReferenceValue).activeSelf, Is.False,
            "The binding row template stays inactive.");
        Transform rowTemplate = ((GameObject)settingsSerialized.FindProperty("bindingRowTemplate").objectReferenceValue).transform;
        Assert.That(rowTemplate.GetComponentsInChildren<Button>(true).Select(button => button.name),
            Is.EqualTo(new[] { "PrimaryButton", "AlternateButton" }), "Each control row has a primary and an alternate key button.");
        Assert.That(rowTemplate.parent.GetChild(0).name, Is.EqualTo("ColumnHeader"), "The key list starts with column captions.");
        SerializedProperty hidden = settingsSerialized.FindProperty("hiddenActions");
        Assert.That(Enumerable.Range(0, hidden.arraySize).Select(i => hidden.GetArrayElementAtIndex(i).stringValue),
            Is.EquivalentTo(new[] { "Look", "InventoryScroll" }));
        PlayerItemUse itemUse = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab").GetComponent<PlayerItemUse>();
        Assert.That(itemUse, Is.Not.Null, "LMB item use lives on the player.");
        Assert.That(new SerializedObject(itemUse).FindProperty("inputActions").objectReferenceValue, Is.Not.Null);
        Slider jumpscareSlider = (Slider)settingsSerialized.FindProperty("jumpscareSlider").objectReferenceValue;
        Assert.That(jumpscareSlider.transform.parent.GetComponentsInChildren<Text>(true).Any(text => text.text == "Jumpscare Volume"),
            Is.True, "The Sounds page offers a Jumpscare Volume slider.");

        GameObject pausePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PausePath);
        PauseMenu pause = pausePrefab.GetComponent<PauseMenu>();
        Assert.That(pause, Is.Not.Null);
        Assert.That(MissingReferences(pause), Is.Empty, "Every PauseMenu reference is assigned.");
        Canvas canvas = pausePrefab.GetComponent<Canvas>();
        Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(canvas.sortingOrder, Is.GreaterThan(600), "The pause menu draws above the HUD, death and game over screens.");
        var pauseSerialized = new SerializedObject(pause);
        Assert.That(((GameObject)pauseSerialized.FindProperty("screenRoot").objectReferenceValue).activeSelf, Is.False);
        Assert.That(((GameObject)pauseSerialized.FindProperty("playerRowTemplate").objectReferenceValue).activeSelf, Is.False);
        SettingsPanel pauseSettings = (SettingsPanel)pauseSerialized.FindProperty("settings").objectReferenceValue;
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(pauseSettings.gameObject), Is.SameAs(settingsPrefab),
            "Options uses the same settings as the main menu.");

        Scene hub = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Additive);
        try
        {
            PauseMenu placed = InScene<PauseMenu>(hub).Single();
            Assert.That(placed.transform.parent, Is.Null, "The pause menu is a persistent root.");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(placed.gameObject), Is.SameAs(pausePrefab));
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }
    }

    private static List<string> MissingReferences(Object target)
    {
        var missing = new List<string>();
        SerializedProperty property = new SerializedObject(target).GetIterator();
        while (property.NextVisible(true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null &&
                property.name != "m_Script")
                missing.Add(property.propertyPath);
        }
        return missing;
    }

    [Test]
    public void RemotePlayersAndTheAlbularyoUseReducedPixelation()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        Transform sprites = player.transform.Find("WorldView/WorldSprites");
        Assert.That(sprites, Is.Not.Null);
        Assert.That(sprites.GetComponent<ReducedPixelation>(), Is.Not.Null, "Other players' sprites are not pixelated.");

        Scene hub = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Additive);
        try
        {
            AlbularyoInteractable albularyo = InScene<AlbularyoInteractable>(hub).Single();
            SpriteRenderer sprite = albularyo.GetComponentsInChildren<SpriteRenderer>(true).Single();
            Assert.That(sprite.GetComponent<ReducedPixelation>(), Is.Not.Null, "The Albularyo's sprite is not pixelated.");
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }
    }

    [Test]
    public void TheNetworkManagerSavesTheGame()
    {
        Scene hub = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = InScene<NetworkManager>(hub).Single();
            GameSaveManager saves = manager.GetComponent<GameSaveManager>();
            Assert.That(saves, Is.Not.Null);
            Assert.That(saves.ItemPickupPrefab, Is.Not.Null, "Saved floor items need the pickup prefab.");
            Assert.That(manager.spawnPrefabs, Does.Contain(saves.ItemPickupPrefab.gameObject));
            Assert.That(saves.ItemPickupPrefab.Catalog, Is.Not.Null);
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }
    }

    [Test]
    public void SaveFilesRoundTripAndDelete()
    {
        Assert.That(SaveSystem.SlotCount, Is.EqualTo(3));
        Assert.That(SaveSystem.Exists(1), Is.False);
        Assert.That(SaveSystem.Load(1), Is.Null);
        Assert.That(SaveSystem.Describe(1), Does.Contain("New Game"));

        var data = new GameSaveData
        {
            round = 3,
            elapsed = 120f,
            gold = 2,
            silver = 1,
            bronze = 4,
            turnedIn = 1,
            money = 55,
            roundReward = 10,
        };
        data.players.Add(new PlayerSaveData { key = "76561198000000000", name = "Tester", characterIndex = 1, health = 4,
            passive = "backpack", slots = new List<string> { "key", "" } });
        data.baseItems.Add(new FloorItemSaveData { itemId = "key", position = new Vector3(1f, 2f, 3f) });

        Assert.That(SaveSystem.Write(1, data), Is.True);
        Assert.That(SaveSystem.Exists(1), Is.True);
        Assert.That(File.Exists(SaveSystem.PathFor(1)), Is.True);
        Assert.That(Path.GetDirectoryName(SaveSystem.PathFor(1)), Is.EqualTo(saveDirectory));

        GameSaveData loaded = SaveSystem.Load(1);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.round, Is.EqualTo(3));
        Assert.That(loaded.elapsed, Is.EqualTo(120f));
        Assert.That(new[] { loaded.gold, loaded.silver, loaded.bronze, loaded.turnedIn }, Is.EqualTo(new[] { 2, 1, 4, 1 }));
        Assert.That(loaded.money, Is.EqualTo(55));
        Assert.That(loaded.roundReward, Is.EqualTo(10));
        PlayerSaveData player = loaded.FindPlayer("76561198000000000");
        Assert.That(player, Is.Not.Null);
        Assert.That(player.characterIndex, Is.EqualTo(1));
        Assert.That(player.health, Is.EqualTo(4));
        Assert.That(player.passive, Is.EqualTo("backpack"));
        Assert.That(player.slots, Is.EqualTo(new[] { "key", "" }));
        Assert.That(loaded.baseItems.Single().position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        Assert.That(SaveSystem.Describe(1), Does.Contain("Night 3"));
        Assert.That(SaveSystem.Exists(0) || SaveSystem.Exists(2), Is.False, "Slots are separate files.");

        Assert.That(SaveSystem.Write(5, data), Is.False, "Only three slots exist.");
        SaveSystem.Delete(1);
        Assert.That(SaveSystem.Exists(1), Is.False);
        Assert.That(SaveSystem.Describe(1), Does.Contain("New Game"));
    }

    [Test]
    public void BindingOverridesPersistAcrossActionClones()
    {
        bool hadKey = PlayerPrefs.HasKey(BindingsKey);
        string previous = PlayerPrefs.GetString(BindingsKey, string.Empty);
        var source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        InputActionAsset first = Object.Instantiate(source);
        InputActionAsset second = Object.Instantiate(source);
        try
        {
            PlayerPrefs.DeleteKey(BindingsKey);
            GameSettings.ReloadFromPrefs();
            InputAction use = first.FindAction("Player/Interact", true);
            int index = use.bindings.IndexOf(binding => binding.effectivePath == "<Keyboard>/e");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            use.ApplyBindingOverride(index, "<Keyboard>/r");
            GameSettings.SaveBindingOverride(use, index);
            Assert.That(GameSettings.BindingOverrideCount, Is.EqualTo(1));

            GameSettings.ReloadFromPrefs();
            GameSettings.ApplyBindingOverrides(second);
            Assert.That(second.FindAction("Player/Interact", true).bindings[index].effectivePath, Is.EqualTo("<Keyboard>/r"),
                "Cloned actions (players, menu) pick up saved rebinds by binding id.");

            InputAction tracked = GameSettings.TrackBindings(source.FindAction("Player/Interact", true).Clone());
            Assert.That(tracked.bindings[index].effectivePath, Is.EqualTo("<Keyboard>/r"), "Tracked clones start with saved rebinds.");
            use.ApplyBindingOverride(index, "<Keyboard>/t");
            GameSettings.SaveBindingOverride(use, index);
            Assert.That(tracked.bindings[index].effectivePath, Is.EqualTo("<Keyboard>/t"),
                "Rebinding in the pause menu updates the live player's actions.");

            GameSettings.ResetBindings(second);
            Assert.That(tracked.bindings[index].effectivePath, Is.EqualTo("<Keyboard>/e"));
            tracked.Dispose();
            Assert.That(GameSettings.BindingOverrideCount, Is.Zero);
            Assert.That(second.FindAction("Player/Interact", true).bindings[index].effectivePath, Is.EqualTo("<Keyboard>/e"));
        }
        finally
        {
            if (hadKey)
                PlayerPrefs.SetString(BindingsKey, previous);
            else
                PlayerPrefs.DeleteKey(BindingsKey);
            GameSettings.ReloadFromPrefs();
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    private static IEnumerable<T> InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
}
