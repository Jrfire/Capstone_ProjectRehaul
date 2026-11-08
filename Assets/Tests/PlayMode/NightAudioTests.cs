#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Capstone.Audio;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class NightAudioTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string LoadingPath = "Assets/PREFABS/TravelLoadingScreen.prefab";
    private const string HudPath = "Assets/PREFABS/PlayerHud.prefab";
    private const string CatalogPath = "Assets/Settings/ItemCatalog.asset";

    private readonly List<Object> created = new List<Object>();
    private GameObject managerObject;
    private Transport previousTransport;
    private bool previousListen;
    private int catalogSize = -1;

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
        foreach (ItemPickup pickup in ItemPickup.Active.ToArray())
        {
            if (pickup != null)
                Object.DestroyImmediate(pickup.gameObject);
        }
        foreach (GameObject corpse in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(go => go.name.StartsWith("Corpse (")))
            Object.DestroyImmediate(corpse);
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
                Object.DestroyImmediate(created[i]);
        }
        created.Clear();
        if (catalogSize >= 0)
        {
            // Test items were registered in the project catalog so dropped pickups resolve them; remove them again.
            var items = new SerializedObject(AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath));
            items.FindProperty("items").arraySize = catalogSize;
            items.ApplyModifiedPropertiesWithoutUndo();
            catalogSize = -1;
        }
        PlayerControlLock.ReleaseAll();
        HudVisibility.ShowAll();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
    }

    [UnityTest]
    public IEnumerator HalfAHeartKillsDropsEverythingAndLeavesACorpse()
    {
        Track(GroundAt(Vector3.zero));
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        PlayerCorpse corpse = player.GetComponent<PlayerCorpse>();

        health.TakeDamage(1);
        Assert.That(health.CurrentHealth, Is.EqualTo(5), "Larger hits leave the player alive.");
        health.TakeDamage(3);
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
        Assert.That(corpse.Corpse, Is.Null);

        ItemDefinition bandage = TestItem("test-bandage", ItemSlotKind.Active);
        ItemDefinition backpack = TestItem("test-backpack", ItemSlotKind.Passive, 2);
        Assert.That(inventory.ServerTryAdd(bandage), Is.True);
        Assert.That(inventory.ServerTryAdd(backpack), Is.True);

        Vector3 deathPosition = player.transform.position;
        health.TakeDamage(1);
        Assert.That(health.CurrentHealth, Is.Zero, "Half a heart is never left: the player dies instead.");
        Assert.That(health.IsDead, Is.True);
        Assert.That(inventory.GetItem(0), Is.Null, "Items drop on death.");
        Assert.That(inventory.PassiveItem, Is.Null);
        Assert.That(ItemPickup.Active.Select(pickup => pickup.ItemId), Is.EquivalentTo(new[] { "test-bandage", "test-backpack" }));
        yield return WaitUntil(() => corpse.Corpse != null, 2f, "No body was left where the player died.");
        Vector3 offset = corpse.Corpse.transform.position - deathPosition;
        Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.LessThan(0.25f), "The body lies where the player died.");

        health.SetHealth(PlayerHealth.MaxHealth);
        yield return WaitUntil(() => corpse.Corpse == null, 2f, "Reviving removes the body.");
    }

    [UnityTest]
    public IEnumerator GDropsTheActiveItemAndQUnequipsThePassive()
    {
        Track(GroundAt(Vector3.zero));
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerInventory inventory = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
        ItemDefinition key = TestItem("test-key", ItemSlotKind.Active);
        ItemDefinition axe = TestItem("test-axe", ItemSlotKind.Active);
        ItemDefinition backpack = TestItem("test-backpack", ItemSlotKind.Passive, 2);

        Assert.That(inventory.ServerTryAdd(key), Is.True);
        inventory.DropSelected();
        yield return WaitUntil(() => ItemPickup.Active.Count == 1, 2f, "G did not drop the selected item.");
        Assert.That(inventory.GetItem(0), Is.Null);
        ItemPickup dropped = ItemPickup.Active[0];
        Assert.That(dropped.Item, Is.SameAs(key));
        inventory.RequestPickUp(dropped);
        yield return WaitUntil(() => inventory.GetItem(0) == key, 2f, "The dropped item could not be picked up again.");
        yield return WaitUntil(() => ItemPickup.Active.Count == 0, 2f, "The picked-up item stayed on the ground.");

        Assert.That(inventory.ServerTryAdd(backpack), Is.True);
        inventory.UnequipPassive();
        yield return WaitUntil(() => inventory.PassiveItem == null, 2f, "Q did not unequip the passive item.");
        Assert.That(inventory.GetItem(1), Is.SameAs(backpack), "With room, the passive item moves into the slots.");
        Assert.That(ItemPickup.Active, Is.Empty);

        Assert.That(inventory.ServerRemove(1), Is.SameAs(backpack));
        Assert.That(inventory.ServerTryAdd(axe), Is.True);
        Assert.That(inventory.ServerTryAdd(backpack), Is.True);
        Assert.That(inventory.Capacity, Is.EqualTo(4));
        Assert.That(inventory.ServerTryAdd(TestItem("test-rope", ItemSlotKind.Active)), Is.True);
        Assert.That(inventory.ServerTryAdd(TestItem("test-lamp", ItemSlotKind.Active)), Is.True);
        inventory.UnequipPassive();
        yield return WaitUntil(() => inventory.PassiveItem == null, 2f, "Q did not unequip the passive item.");
        Assert.That(inventory.Capacity, Is.EqualTo(2));
        Assert.That(ItemPickup.Active.Select(pickup => pickup.ItemId), Is.EquivalentTo(new[] { "test-rope", "test-lamp", "test-backpack" }),
            "With full slots the passive item (and the items its lost slots held) are dropped.");
    }

    [UnityTest]
    public IEnumerator OnlyOnePlayerTalksToTheAlbularyoAndTheNightStartsWhenTheyLeave()
    {
        Track(GroundAt(Vector3.zero));
        (RoundClock clock, AgimatBank bank) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        uint local = NetworkClient.localPlayer.netId;
        NightTransitionScreen night = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath)))
            .GetComponentInChildren<NightTransitionScreen>(true);
        var albularyoObject = Track(new GameObject("Test Albularyo"));
        albularyoObject.SetActive(false);
        Nametag nametag = albularyoObject.AddComponent<Nametag>();
        nametag.DisplayName = "Talk to the Albularyo.";
        AlbularyoInteractable albularyo = albularyoObject.AddComponent<AlbularyoInteractable>();
        albularyoObject.SetActive(true);

        // Another living player is talking: this player is refused and sees the busy nametag.
        GameObject other = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), new Vector3(3f, 0f, 0f), Quaternion.identity));
        NetworkServer.Spawn(other);
        typeof(AgimatBank).GetField("serverTalker", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bank, other.GetComponent<NetworkIdentity>().netId);
        Assert.That(bank.SomeoneElseTalking, Is.True);
        Assert.That(nametag.DisplayName, Is.EqualTo(albularyo.BusyNametag));
        Assert.That(albularyo.BusyNametag, Is.EqualTo("Someone else is talking to the albularyo."));
        Assert.That(albularyo.CanInteract(null), Is.False);
        bool refused = false;
        System.Action onRefused = () => refused = true;
        AgimatBank.TalkRefused += onRefused;
        try
        {
            bank.RequestTalk(true);
            yield return WaitUntil(() => refused, 2f, "The second talker was not refused.");
        }
        finally
        {
            AgimatBank.TalkRefused -= onRefused;
        }
        Assert.That(bank.Talker, Is.EqualTo(other.GetComponent<NetworkIdentity>().netId));
        NetworkServer.Destroy(other);
        yield return WaitUntil(() => bank.Talker == 0, 2f, "A despawned talker kept the Albularyo busy.");
        Assert.That(nametag.DisplayName, Is.EqualTo("Talk to the Albularyo."));

        bank.RequestTalk(true);
        yield return WaitUntil(() => bank.Talker == local, 2f, "The free Albularyo did not accept the talker.");
        Assert.That(bank.SomeoneElseTalking, Is.False, "The talker's own nametag is not replaced.");
        bank.ServerAdd(AgimatType.Bronze, AgimatBank.QuotaFor(1));
        for (int i = 0; i < AgimatBank.QuotaFor(1); i++)
            Assert.That(bank.ServerSacrifice(AgimatType.Bronze), Is.True);
        Assert.That(clock.Round, Is.EqualTo(2));
        Assert.That(bank.PendingNight, Is.EqualTo(2), "The night waits until the talker closes the screen.");
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(night.IsPlaying, Is.False);

        int nightStarts = 0;
        System.Action<int> onNight = _ => nightStarts++;
        AgimatBank.NightTransitionStarted += onNight;
        bank.RequestTalk(false);
        yield return WaitUntil(() => night.IsPlaying, 2f, "Closing the Albularyo did not start the night transition.");
        Assert.That(night.Label.text, Is.EqualTo("NIGHT: 2"));
        Assert.That(bank.Talker, Is.Zero);
        Assert.That(PlayerControlLock.IsLocked, Is.True, "Everyone is held while the screen is black.");
        yield return WaitUntil(() => night.Blackout.enabled && night.Blackout.color.a > 0.99f, 3f, "The screen did not fade to black.");
        float waitEnd = Time.realtimeSinceStartup + 20f;
        while (night.IsPlaying && Time.realtimeSinceStartup < waitEnd)
            yield return null;
        AgimatBank.NightTransitionStarted -= onNight;
        Assert.That(nightStarts, Is.EqualTo(1), "The night transition starts once.");
        Assert.That(night.IsPlaying, Is.False, "The night transition did not finish.");
        Assert.That(night.Blackout.enabled, Is.False);
        Assert.That(PlayerControlLock.IsLocked, Is.False);
    }

    [UnityTest]
    public IEnumerator ReadiedPlayersAreListedAndLockedOutOfTerminals()
    {
        Track(GroundAt(Vector3.zero));
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerPersistence player = NetworkClient.localPlayer.GetComponent<PlayerPersistence>();
        ReadyHud hud = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath))).GetComponentInChildren<ReadyHud>(true);
        yield return null;
        Assert.That(TravelDirector.ServerEveryoneReady(), Is.False);
        Assert.That(hud.Panel.activeSelf, Is.False, "Hidden until a trip is selected or someone readies.");

        player.ServerSetReady(true);
        yield return null;
        yield return null;
        Assert.That(TravelDirector.ServerEveryoneReady(), Is.True);
        Assert.That(PlayerPersistence.LocalReadied, Is.True);
        Assert.That(hud.Panel.activeSelf, Is.True);
        Assert.That(hud.ListText.text, Does.Contain("Ready"));

        var albularyoObject = Track(new GameObject("Test Albularyo"));
        AlbularyoInteractable albularyo = albularyoObject.AddComponent<AlbularyoInteractable>();
        Assert.That(albularyo.CanInteract(null), Is.False, "Readied players cannot talk to the Albularyo.");
        var terminalObject = Track(new GameObject("Test Invite"));
        SteamInviteInteractable invite = terminalObject.AddComponent<SteamInviteInteractable>();
        Assert.That(invite.CanInteract(null), Is.False, "Readied players cannot use the invite terminal.");

        TravelDirector.ServerClearReady();
        yield return null;
        yield return null;
        Assert.That(PlayerPersistence.LocalReadied, Is.False);
        Assert.That(hud.Panel.activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator NewPlayersMustPickACharacterBeforeMoving()
    {
        Track(GroundAt(Vector3.zero));
        CharacterSelectScreen screen = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/CharacterSelectScreen.prefab")))
            .GetComponent<CharacterSelectScreen>();
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerCharacterSelection selection = NetworkClient.localPlayer.GetComponent<PlayerCharacterSelection>();
        PlayerVision vision = NetworkClient.localPlayer.GetComponent<PlayerVision>();
        yield return WaitUntil(() => screen.IsOpen, 2f, "The character select screen did not open for a new player.");
        Assert.That(selection.HasSelected, Is.False);
        Assert.That(vision.ActiveDefinition, Is.Null, "Players start with no character definition.");
        Assert.That(PlayerControlLock.IsLocked, Is.True, "Players cannot move before choosing.");
        Assert.That(screen.TitleText.text, Is.EqualTo("Select your character"));

        screen.Choose(1);
        yield return WaitUntil(() => selection.HasSelected, 2f, "Choosing did not select a character.");
        Assert.That(selection.SelectedIndex, Is.EqualTo(1));
        Assert.That(vision.ActiveDefinition, Is.SameAs(selection.Options[1].definition));
        yield return null;
        Assert.That(screen.IsOpen, Is.False);
        Assert.That(PlayerControlLock.IsLocked, Is.False);
    }

    [UnityTest]
    public IEnumerator HighestMusicRequestPlaysAndOneShotsExpire()
    {
        GameObject host = Track(new GameObject("Test AudioManager"));
        host.AddComponent<AudioManager>();
        AudioManager audio = AudioManager.Instance;
        Assert.That(audio, Is.Not.Null);
        AudioClip ambience = Track(AudioClip.Create("ambience", 44100, 1, 44100, false));
        AudioClip sting = Track(AudioClip.Create("sting", 8820, 1, 44100, false));
        object arrival = new object();
        object chase = new object();

        AudioManager.RequestMusic(arrival, ambience, 0, 1f, true);
        yield return null;
        Assert.That(audio.CurrentTrack, Is.SameAs(ambience));
        AudioManager.RequestMusic(chase, sting, 10, 1f, false);
        yield return null;
        Assert.That(audio.CurrentTrack, Is.SameAs(sting), "The higher priority request plays.");
        yield return new WaitForSecondsRealtime(sting.length + 0.15f);
        yield return null;
        Assert.That(audio.CurrentTrack, Is.SameAs(ambience), "A non-looping track ends and the next request resumes.");
        Assert.That(AudioManager.IsMusicRequested(chase), Is.False);
        AudioManager.ReleaseMusic(arrival);
        yield return null;
        Assert.That(audio.CurrentTrack, Is.Null);
    }

    [Test]
    public void GroundSurfacesAreFoundOnTheColliderOrItsTile()
    {
        GameObject road = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        Assert.That(GroundSurface.Of(road.GetComponent<Collider>(), GroundSurfaceType.Dirt), Is.EqualTo(GroundSurfaceType.Dirt), "Unmarked floors use the fallback.");
        road.AddComponent<GroundSurface>().Type = GroundSurfaceType.Solid;
        Assert.That(GroundSurface.Of(road.GetComponent<Collider>(), GroundSurfaceType.Dirt), Is.EqualTo(GroundSurfaceType.Solid));

        GameObject floor = Track(new GameObject("Floor"));
        BoxCollider collider = floor.AddComponent<BoxCollider>();
        var tile = new GameObject("Dirt Tile");
        tile.transform.SetParent(floor.transform, false);
        tile.AddComponent<GroundSurface>().Type = GroundSurfaceType.Dirt;
        Assert.That(GroundSurface.Of(collider), Is.EqualTo(GroundSurfaceType.Dirt), "A floor collider covering marked tiles uses their surface.");
    }

    private ItemDefinition TestItem(string id, ItemSlotKind kind, int extraSlots = 0)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        if (catalogSize < 0)
            catalogSize = catalog.Items.Count;
        Texture2D texture = Track(new Texture2D(4, 4));
        ItemDefinition item = Track(ScriptableObject.CreateInstance<ItemDefinition>());
        item.Configure(id, id, Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f)), kind, extraSlots);
        catalog.Register(item);
        return item;
    }

    private (RoundClock, AgimatBank) StartHost()
    {
        managerObject = new GameObject("Night Audio Test NetworkManager");
        managerObject.SetActive(false);
        var transport = managerObject.AddComponent<TelepathyTransport>();
        var manager = managerObject.AddComponent<SteamNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        manager.autoCreatePlayer = true;
        manager.dontDestroyOnLoad = true;
        manager.spawnPrefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/ItemPickup.prefab"));
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
        clockSettings.FindProperty("baseSceneName").stringValue = SceneManager.GetActiveScene().name;
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
