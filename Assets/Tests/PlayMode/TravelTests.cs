#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class TravelTests : InputTestFixture
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string LoadingPath = "Assets/PREFABS/TravelLoadingScreen.prefab";
    private const string MapPath = "Assets/PREFABS/TravelMapScreen.prefab";
    private const string Destination = "StreetsTest";
    private const string TikbalangDefinitionPath = "Assets/PREFABS/Enemies/Definitions/Tikbalang.asset";

    private GameObject transportObject;
    private GameObject managerObject;
    private GameObject player;
    private GameObject ground;
    private GameObject terminalObject;
    private GameObject travelObject;
    private TravelLoadingScreen loadingScreen;
    private Transport previousTransport;
    private bool previousListen;

    public override void Setup()
    {
        base.Setup();
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
    }

    public override void TearDown()
    {
        if (managerObject != null)
        {
            managerObject.GetComponent<NetworkManager>().StopHost();
            Object.DestroyImmediate(managerObject);
        }
        if (transportObject != null)
        {
            NetworkClient.Disconnect();
            NetworkClient.Shutdown();
            NetworkServer.Shutdown();
            Object.DestroyImmediate(transportObject);
        }
        NetworkManager.ResetStatics();

        foreach (GameObject created in new[] { player, ground, terminalObject, travelObject })
        {
            if (created != null)
                Object.DestroyImmediate(created);
        }
        if (loadingScreen != null)
            Object.DestroyImmediate(loadingScreen.gameObject);
        if (TravelMapScreen.Current != null)
            Object.DestroyImmediate(TravelMapScreen.Current.gameObject);

        PlayerControlLock.ReleaseAll();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        MapManager.ClearNetworkSeeds();
        MapManager.WaitForNetworkSeed = false;
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        base.TearDown();
    }

    [UnityTearDown]
    public IEnumerator LeaveDestinationScene()
    {
        Scene destination = SceneManager.GetSceneByName(Destination);
        if (!destination.IsValid() || !destination.isLoaded)
            yield break;
        Scene cleanup = SceneManager.CreateScene("Travel Test Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync(destination);
    }

    [UnityTest]
    public IEnumerator ControlLockBlocksMovementLookAndInteraction()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        SpawnInMemoryHost();
        yield return null;
        yield return null;

        PlayerInteraction interaction = player.GetComponent<PlayerInteraction>();
        Camera camera = player.GetComponentInChildren<Camera>(true);
        terminalObject = Terminal(camera.transform.position + camera.transform.forward * 2f);
        MapTerminal terminal = terminalObject.GetComponent<MapTerminal>();
        Physics.SyncTransforms();
        Step(interaction);
        Assert.That(interaction.CurrentTarget, Is.SameAs(terminal));

        var owner = new object();
        PlayerControlLock.Acquire(owner);
        Press(keyboard.wKey);
        InputSystem.Update();
        Vector3 before = player.transform.position;
        Step(player.GetComponent<PlayerMovement>());
        Assert.That(player.transform.position, Is.EqualTo(before), "Movement (and gravity) pause while controls are locked.");
        Step(interaction);
        Assert.That(interaction.CurrentTarget, Is.Null);
        PlayerLook look = player.GetComponent<PlayerLook>();
        Step(look);
        Assert.That(typeof(PlayerLook).GetField("cursorCaptured", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(look), Is.False);

        PlayerControlLock.Release(owner);
        Assert.That(PlayerControlLock.IsLocked, Is.False);
        Step(player.GetComponent<PlayerMovement>());
        Assert.That(player.transform.position.z, Is.GreaterThan(before.z));
        Release(keyboard.wKey);
        InputSystem.Update();
    }

    [UnityTest]
    public IEnumerator PersistentPlayerTeleportsAndOnlyTravelTargetsWithADestination()
    {
        SpawnInMemoryHost();
        yield return null;
        Assert.That(player.scene.name, Is.EqualTo("DontDestroyOnLoad"));
        Assert.That(PlayerPersistence.LocalPlayerSpawned, Is.True);

        player.GetComponent<PlayerMovement>().TeleportTo(new Vector3(3f, 0.5f, -2f), Quaternion.Euler(0f, 90f, 0f));
        Assert.That(Vector3.Distance(player.transform.position, new Vector3(3f, 0.5f, -2f)), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(player.transform.rotation, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(0.01f));

        travelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var travel = travelObject.AddComponent<TravelInteractable>();
        Assert.That(travel.CanInteract(null), Is.False, "Without a TravelDirector or destination the jeepney is not usable.");
    }

    [UnityTest]
    public IEnumerator MapScreenGraysOutCurrentSceneAndConfirms()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        terminalObject = Terminal(Vector3.zero, SceneManager.GetActiveScene().name, Destination, "MissingScene");
        MapTerminal terminal = terminalObject.GetComponent<MapTerminal>();
        TravelMapScreen screen = TravelMapScreen.Open(terminal.MapScreenPrefab, terminal);
        yield return null;

        Assert.That(screen.IsOpen, Is.True);
        Assert.That(screen.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
        Assert.That(screen.DestinationButtons.Select(b => b.name), Is.EqualTo(new[] { SceneManager.GetActiveScene().name, Destination, "MissingScene" }));
        Button current = screen.DestinationButtons[0], destination = screen.DestinationButtons[1], missing = screen.DestinationButtons[2];
        Assert.That(current.interactable, Is.False, "The current scene is grayed out.");
        Assert.That(missing.interactable, Is.False, "Scenes outside the build list cannot be chosen.");
        Assert.That(destination.interactable, Is.True);
        Assert.That(current.GetComponentInChildren<Text>().color.r, Is.LessThan(0.6f));
        Assert.That(PlayerControlLock.IsLocked, Is.True);
        Assert.That(Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(), Is.Not.Null);

        current.onClick.Invoke();
        Assert.That(screen.ConfirmationOpen, Is.False);
        destination.onClick.Invoke();
        Assert.That(screen.ConfirmationOpen, Is.True);
        Assert.That(screen.PendingDestination, Is.EqualTo(Destination));
        StringAssert.Contains($"Travel to {Destination}?", screen.GetComponentsInChildren<Text>().Select(t => t.text).Aggregate((a, b) => a + "|" + b));
        Assert.That(screen.EnemiesLabel, Is.Not.Null);
        Assert.That(screen.EnemiesLabel.isActiveAndEnabled, Is.True);
        Assert.That(screen.EnemiesLabel.text, Is.EqualTo("Enemies: Unknown"), "Without a host roll the roster is unknown.");

        Press(keyboard.escapeKey);
        InputSystem.Update();
        Step(screen);
        Release(keyboard.escapeKey);
        InputSystem.Update();
        Assert.That(screen.ConfirmationOpen, Is.False, "Escape first closes the confirmation.");
        Assert.That(screen.IsOpen, Is.True);

        destination.onClick.Invoke();
        LogAssert.Expect(LogType.Error, "Travel needs a TravelDirector on the NetworkManager object.");
        screen.ConfirmTravel();
        Assert.That(screen.IsOpen, Is.False, "Yes closes the panels.");
        Assert.That(PlayerControlLock.IsLocked, Is.False);

        TravelMapScreen.Open(terminal.MapScreenPrefab, terminal);
        Assert.That(screen.IsOpen, Is.True);
        Press(keyboard.escapeKey);
        InputSystem.Update();
        Step(screen);
        Release(keyboard.escapeKey);
        InputSystem.Update();
        Assert.That(screen.IsOpen, Is.False, "Escape closes the map.");
        Assert.That(TravelMapScreen.Current, Is.SameAs(screen));
    }

    [UnityTest]
    public IEnumerator LoadingScreenFadesAndShowsBlackLoadingText()
    {
        loadingScreen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LoadingPath)).GetComponent<TravelLoadingScreen>();
        Assert.That(loadingScreen.LoadingVisible, Is.False);
        Assert.That(loadingScreen.FadeAlpha, Is.Zero);

        loadingScreen.ShowLoading();
        Assert.That(loadingScreen.LoadingVisible, Is.True);
        Assert.That(loadingScreen.LoadingText.isActiveAndEnabled, Is.True);
        Assert.That(loadingScreen.VisibleLoadingText, Is.EqualTo("Loading"));
        StringAssert.StartsWith("Loading", loadingScreen.LoadingText.text);
        Assert.That(loadingScreen.transform.Find("LoadingRoot/Background").GetComponent<Image>().color, Is.EqualTo(Color.black));
        loadingScreen.dotInterval = 0.1f;
        var seenDots = new HashSet<int>();
        for (float end = Time.realtimeSinceStartup + 0.6f; Time.realtimeSinceStartup < end;)
        {
            seenDots.Add(loadingScreen.DotCount);
            yield return null;
        }
        Assert.That(seenDots, Is.EquivalentTo(new[] { 0, 1, 2, 3 }), "The dots animate from Loading to Loading...");
        Assert.That(loadingScreen.EnemyRow.gameObject.activeSelf, Is.False, "No enemies, no miniature row.");

        var tikbalang = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Enemies/Tikbalang.prefab").GetComponent<EnemyLoadingMiniature>();
        Assert.That(tikbalang, Is.Not.Null);
        Assert.That(tikbalang.Frames.Count, Is.EqualTo(8));
        var other = new GameObject("Other Enemy").AddComponent<EnemyLoadingMiniature>();
        var otherSettings = new SerializedObject(other);
        SerializedProperty frames = otherSettings.FindProperty("frames");
        frames.arraySize = 1;
        frames.GetArrayElementAtIndex(0).objectReferenceValue = tikbalang.Frames[3];
        otherSettings.ApplyModifiedPropertiesWithoutUndo();
        loadingScreen.SetEnemies(new[] { tikbalang, other, tikbalang });
        Assert.That(loadingScreen.EnemyFigures.Count, Is.EqualTo(2), "One miniature per distinct enemy.");
        Assert.That(loadingScreen.EnemyRow.gameObject.activeSelf, Is.True);
        Assert.That(loadingScreen.EnemyRow.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(loadingScreen.EnemyRow);
        RectTransform first = loadingScreen.EnemyFigures[0].rectTransform, second = loadingScreen.EnemyFigures[1].rectTransform;
        Assert.That(second.position.x, Is.GreaterThan(first.position.x + first.rect.width * 0.5f), "The layout group lines the miniatures up side by side.");
        Assert.That(Mathf.Abs(second.position.y - first.position.y), Is.LessThan(1f));
        Vector3[] corners = new Vector3[4];
        first.GetWorldCorners(corners);
        Assert.That(corners[0].y, Is.LessThan(Screen.height * 0.35f), "The miniatures sit at the bottom of the screen.");
        var shownFrames = new HashSet<Sprite>();
        for (float end = Time.realtimeSinceStartup + 0.5f; Time.realtimeSinceStartup < end;)
        {
            shownFrames.Add(loadingScreen.EnemyFigures[0].sprite);
            yield return null;
        }
        Assert.That(shownFrames.Count, Is.GreaterThan(1), "The miniature animates.");
        Assert.That(shownFrames.All(frame => tikbalang.Frames.Contains(frame)), Is.True);
        loadingScreen.SetEnemies(null);
        yield return null;
        Assert.That(loadingScreen.EnemyFigures.Count, Is.Zero);
        Assert.That(loadingScreen.EnemyRow.gameObject.activeSelf, Is.False);
        Object.Destroy(other.gameObject);

        yield return loadingScreen.FadeTo(1f, 0.05f);
        Assert.That(loadingScreen.FadeAlpha, Is.EqualTo(1f));
        Assert.That(loadingScreen.transform.Find("Fade").GetSiblingIndex(), Is.EqualTo(loadingScreen.transform.childCount - 1), "The fade draws above the loading screen.");
        loadingScreen.HideLoading();
        yield return loadingScreen.FadeTo(0f, 0.05f);
        Assert.That(loadingScreen.LoadingVisible, Is.False);
        Assert.That(loadingScreen.FadeAlpha, Is.Zero);
    }

    [UnityTest]
    public IEnumerator HostTravelKeepsThePlayerBuildsTheStreetAndReleasesTheLoadingScreen()
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(50f, 1f, 50f);
        terminalObject = Terminal(new Vector3(10f, 1f, 10f), Destination);
        travelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        travelObject.transform.position = new Vector3(-10f, 1f, 10f);
        TravelInteractable travel = travelObject.AddComponent<TravelInteractable>();

        managerObject = new GameObject("Travel Test NetworkManager");
        managerObject.SetActive(false);
        var transport = managerObject.AddComponent<TelepathyTransport>();
        var manager = managerObject.AddComponent<SteamNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        manager.spawnPrefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Enemies/Tikbalang.prefab"));
        manager.autoCreatePlayer = true;
        manager.dontDestroyOnLoad = true;
        var director = managerObject.AddComponent<TravelDirector>();
        var settings = new SerializedObject(director);
        settings.FindProperty("loadingScreenPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(LoadingPath).GetComponent<TravelLoadingScreen>();
        settings.FindProperty("fadeDuration").floatValue = 0.1f;
        settings.FindProperty("loadingRevealDuration").floatValue = 0.05f;
        settings.FindProperty("readyTimeout").floatValue = 10f;
        settings.FindProperty("minimumLoadingSeconds").floatValue = 2.5f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        managerObject.SetActive(true);

        NetworkServer.listen = false;
        manager.StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        player = NetworkClient.localPlayer.gameObject;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        health.TakeDamage(1);
        Assert.That(travel.CanInteract(null), Is.False);

        string hub = SceneManager.GetActiveScene().name;
        director.RequestDestination(hub);
        director.RequestDestination("MissingScene");
        yield return null;
        yield return null;
        Assert.That(director.SelectedDestination, Is.Null, "The server rejects the current scene and unknown scenes.");

        director.RequestDestination(Destination);
        yield return WaitUntil(() => director.SelectedDestination == Destination, 2f, "The destination was not shared.");
        Assert.That(travel.CanInteract(null), Is.True);
        StringAssert.Contains(Destination, travel.Prompt);
        yield return WaitUntil(() => director.RolledEnemyName(Destination) != null, 2f, "The host's enemy roll did not reach the client.");
        Assert.That(director.RolledEnemyName(Destination), Is.EqualTo("Tikbalang"));
        Assert.That(TravelMapScreen.EnemiesText(Destination), Is.EqualTo("Enemies: Tikbalang"));
        TravelMapScreen screen = TravelMapScreen.Open(terminalObject.GetComponent<MapTerminal>().MapScreenPrefab, terminalObject.GetComponent<MapTerminal>());
        screen.ShowConfirmation(Destination);
        Assert.That(screen.EnemiesLabel.text, Is.EqualTo("Enemies: Tikbalang"));
        screen.Close();

        PlayerStamina stamina = player.GetComponent<PlayerStamina>();
        Assert.That(stamina.TryUseStamina(40f), Is.True);
        travel.Interact(null);
        yield return WaitUntil(() => director.IsTraveling, 2f, "Travel did not begin.");
        Assert.That(PlayerControlLock.IsLocked, Is.True);
        Assert.That(PlayerStamina.TravelPaused, Is.True);
        Assert.That(director.SelectedDestination, Is.Null);

        bool sawLoading = false;
        float loadingShown = -1f, loadingHidden = -1f;
        var loadingEnemyFrames = new HashSet<Sprite>();
        var frozenPositions = new Dictionary<EnemyBrain, Vector3>();
        bool sawFrozen = false;
        float timeout = Time.realtimeSinceStartup + 30f;
        while ((SceneManager.GetActiveScene().name != Destination || director.IsTraveling || director.ServerLoadingActive) &&
               Time.realtimeSinceStartup < timeout)
        {
            if (director.ServerLoadingActive)
            {
                sawFrozen |= EnemyBrain.IsFrozenForLoading;
                foreach (EnemyBrain brain in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (!frozenPositions.TryGetValue(brain, out Vector3 start))
                        frozenPositions[brain] = brain.transform.position;
                    else
                        Assert.That(Vector3.Distance(start, brain.transform.position), Is.LessThan(0.01f),
                            "Enemies stay still while the loading screen is up.");
                }
            }
            bool visible = director.LoadingScreen.LoadingVisible;
            sawLoading |= visible;
            if (visible && loadingShown < 0f)
                loadingShown = Time.realtimeSinceStartup;
            if (!visible && loadingShown >= 0f && loadingHidden < 0f)
                loadingHidden = Time.realtimeSinceStartup;
            if (visible)
            {
                foreach (Image figure in director.LoadingScreen.EnemyFigures)
                    loadingEnemyFrames.Add(figure.sprite);
            }
            yield return null;
        }
        if (loadingHidden < 0f)
            loadingHidden = Time.realtimeSinceStartup;

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(Destination));
        Assert.That(director.IsTraveling, Is.False, "Travel did not complete.");
        Assert.That(sawLoading, Is.True, "The loading screen was never shown.");
        Assert.That(sawFrozen, Is.True, "Enemy brains report the loading freeze during travel.");
        Assert.That(EnemyBrain.IsFrozenForLoading, Is.False, "Enemies resume once the game has faded back in.");
        Assert.That(loadingHidden - loadingShown, Is.GreaterThanOrEqualTo(2.5f - 0.1f),
            "The loading screen stays up for the minimum time even though the street builds sooner.");
        var tikbalangFrames = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Enemies/Tikbalang.prefab")
            .GetComponent<EnemyLoadingMiniature>().Frames;
        Assert.That(loadingEnemyFrames.Count, Is.GreaterThan(1), "The rolled Tikbalang's miniature animated on the loading screen.");
        Assert.That(loadingEnemyFrames.All(frame => tikbalangFrames.Contains(frame)), Is.True);
        Assert.That(player != null && NetworkClient.localPlayer != null && NetworkClient.localPlayer.gameObject == player, Is.True,
            "The same persistent player object survives the scene change.");
        Assert.That(health.CurrentHealth, Is.EqualTo(5));
        Assert.That(stamina.CurrentStamina, Is.EqualTo(60f), "Stamina and its regen delay are frozen during travel.");
        Assert.That(PlayerStamina.TravelPaused, Is.False);
        Assert.That(NetworkManager.networkSceneName, Is.EqualTo(Destination));

        MapManager map = MapManager.Active;
        Assert.That(map != null && map.IsBuilt, Is.True);
        Assert.That(MapManager.TryGetNetworkSeed(Destination, out int seed), Is.True);
        Assert.That(map.Seed, Is.EqualTo(seed));
        Transform spawnPoints = map.GeneratedRoot.GetComponentInChildren<PlayerSpawnPoints>().transform;
        float nearest = spawnPoints.Cast<Transform>().Min(point => Vector3.Distance(point.position, player.transform.position));
        Assert.That(nearest, Is.LessThan(0.75f), "The player arrives at a spawn point in the spawn block.");

        Assert.That(PlayerControlLock.IsLocked, Is.False);
        Assert.That(director.LoadingScreen.LoadingVisible, Is.False);
        yield return WaitUntil(() => director.LoadingScreen.FadeAlpha == 0f, 2f, "The screen did not fade back in.");

        Assert.That(map.HasNavMesh, Is.True, "The street's floors are baked into a NavMesh.");
        for (int i = 0; i < map.BlockCount; i++)
        {
            Vector3 center = map.GetBlock(i).transform.position;
            Assert.That(NavMesh.SamplePosition(center, out _, 1f, NavMesh.AllAreas), Is.True, $"Block {i} has no NavMesh at its centre.");
        }
        NavMeshObstacle[] carvers = map.GeneratedRoot.GetComponentsInChildren<NavMeshObstacle>();
        Assert.That(carvers.Length, Is.GreaterThan(0), "Buildings/walls carve the NavMesh.");
        Assert.That(carvers.All(carver => carver.carving), Is.True);

        EnemySpawner spawner = Object.FindAnyObjectByType<EnemySpawner>();
        Assert.That(spawner, Is.Not.Null);
        yield return WaitUntil(() => spawner.Spawned.Count == 1, 2f, "The Tikbalang was not spawned.");
        var tikbalang = spawner.Spawned[0].GetComponent<TikbalangBrain>();
        Assert.That(director.HasArrivalRoll, Is.True);
        Assert.That(director.ArrivalEnemy.DisplayName, Is.EqualTo("Tikbalang"), "The arrival keeps the roll made before travel.");
        Assert.That(director.RolledEnemyName(Destination), Is.Null, "Rolls are cleared when travel starts.");
        Assert.That(Object.FindObjectsByType<MapTerminal>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty,
            "Only the base has a travel map; away from base the jeepney heads home.");
        StreetBlock spawnBlock = Enumerable.Range(0, map.BlockCount).Where(i => map.GetRole(i) == MapBlockRole.Spawn).Select(map.GetBlock).Single();
        Assert.That(spawnBlock.Entrances.All(entrance => entrance.Obstacle == null), Is.True, "The spawn block's entrance is never obstructed.");
        Assert.That(tikbalang.netIdentity.netId, Is.Not.EqualTo(0u));
        var candidates = EnemySpawner.FindCandidateBlocks(map, out bool relaxed);
        Assert.That(relaxed, Is.False);
        Assert.That(candidates, Does.Contain(tikbalang.HomeBlockIndex), "It guards a block away from the player spawn.");
        Assert.That(map.GetRole(tikbalang.HomeBlockIndex), Is.Not.EqualTo(MapBlockRole.Spawn));
        yield return WaitUntil(() => tikbalang.Motor.IsActive, 1f, "The Tikbalang's agent did not activate on the NavMesh.");
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string message)
    {
        float timeout = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(condition(), Is.True, message);
    }

    private void SpawnInMemoryHost()
    {
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "Run these tests without an active NetworkManager.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        transportObject = new GameObject("Travel Test Transport");
        Transport.active = transportObject.AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(prefab);
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);

        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();
        player = Object.Instantiate(prefab);
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, player), Is.True);
    }

    private static GameObject Terminal(Vector3 position, params string[] destinations)
    {
        GameObject terminal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        terminal.name = "Test Map Terminal";
        terminal.transform.position = position;
        var component = terminal.AddComponent<MapTerminal>();
        var so = new SerializedObject(component);
        SerializedProperty list = so.FindProperty("destinations");
        list.arraySize = destinations.Length;
        var tikbalang = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(TikbalangDefinitionPath);
        for (int i = 0; i < destinations.Length; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("sceneName").stringValue = destinations[i];
            SerializedProperty pool = element.FindPropertyRelative("enemyPool");
            pool.arraySize = 1;
            pool.GetArrayElementAtIndex(0).FindPropertyRelative("enemy").objectReferenceValue = tikbalang;
            pool.GetArrayElementAtIndex(0).FindPropertyRelative("weight").floatValue = 1f;
        }
        so.FindProperty("mapScreenPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MapPath).GetComponent<TravelMapScreen>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return terminal;
    }

    private static void Step(Behaviour behaviour) =>
        behaviour.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behaviour, null);
}
#endif
