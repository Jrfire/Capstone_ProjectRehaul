#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class RoundClockTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string LoadingPath = "Assets/PREFABS/TravelLoadingScreen.prefab";
    private const string HudPath = "Assets/PREFABS/PlayerHud.prefab";
    private const string MapPath = "Assets/PREFABS/TravelMapScreen.prefab";
    private const string Streets = "StreetsTest";

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

    [UnityTearDown]
    public IEnumerator TearDown()
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
        if (TravelMapScreen.Current != null)
            Object.DestroyImmediate(TravelMapScreen.Current.gameObject);
        PlayerControlLock.ReleaseAll();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        MapManager.ClearNetworkSeeds();
        MapManager.WaitForNetworkSeed = false;
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;

        Scene streets = SceneManager.GetSceneByName(Streets);
        if (streets.IsValid() && streets.isLoaded)
        {
            Scene cleanup = SceneManager.CreateScene("Round Clock Test Cleanup " + Time.frameCount);
            SceneManager.SetActiveScene(cleanup);
            yield return SceneManager.UnloadSceneAsync(streets);
        }
    }

    [UnityTest]
    public IEnumerator ClockPausesAtBaseChargesTheTripAndRunsAwayFromBase()
    {
        string hub = SceneManager.GetActiveScene().name;
        Track(GroundAt(Vector3.zero));
        GameObject terminal = Track(new GameObject("Clock Test Terminal"));
        terminal.SetActive(false);
        terminal.AddComponent<BoxCollider>();
        var mapTerminal = terminal.AddComponent<MapTerminal>();
        var terminalSettings = new SerializedObject(mapTerminal);
        terminalSettings.FindProperty("mapScreenPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(MapPath).GetComponent<TravelMapScreen>();
        SerializedProperty destinations = terminalSettings.FindProperty("destinations");
        destinations.arraySize = 1;
        destinations.GetArrayElementAtIndex(0).FindPropertyRelative("sceneName").stringValue = Streets;
        terminalSettings.ApplyModifiedPropertiesWithoutUndo();
        terminal.SetActive(true);

        GameObject costsObject = Track(new GameObject("Clock Test Travel Costs"));
        var costs = costsObject.AddComponent<TravelTimeManager>();
        var costSettings = new SerializedObject(costs);
        SerializedProperty list = costSettings.FindProperty("costs");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).FindPropertyRelative("sceneName").stringValue = Streets;
        list.GetArrayElementAtIndex(0).FindPropertyRelative("hours").floatValue = 2f;
        costSettings.ApplyModifiedPropertiesWithoutUndo();

        GameObject jeepney = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        var travel = jeepney.AddComponent<TravelInteractable>();

        (TravelDirector director, RoundClock clock) = StartHost(hub);
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        RoundClockHud hud = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath))).GetComponent<RoundClockHud>();

        Assert.That(clock.InBase, Is.True);
        Assert.That(clock.HourSeconds, Is.EqualTo(5f));
        for (int i = 0; i < 10; i++)
            yield return null;
        Assert.That(clock.Elapsed, Is.Zero, "The clock is paused at base.");
        Assert.That(clock.IsRunning, Is.False);
        Assert.That(hud.ClockPanel.gameObject.activeSelf, Is.True);
        Assert.That(hud.ClockImage.sprite.name, Is.EqualTo("ClockUI-Sheet_0"), "Rounds start at 12 o'clock (noon).");
        Assert.That(hud.ClockBlinking, Is.False);

        // A trip costing more time than remains is refused.
        clock.ServerSetElapsed(clock.HourSeconds * 10f);
        Assert.That(clock.CanAfford(Streets), Is.False);
        StringAssert.Contains("not enough time", TravelMapScreen.TravelTimeText(Streets));
        Assert.That(TravelDirector.ValidateDestination(Streets), Does.Contain("Not enough time"));
        yield return null;
        Assert.That(hud.ClockImage.sprite.name, Is.EqualTo("ClockUI-Sheet_10"));
        Assert.That(hud.ClockBlinking, Is.True, "Three hours or fewer left: the clock blinks and pulses.");
        clock.ServerSetElapsed(0f);
        Assert.That(TravelMapScreen.TravelTimeText(Streets), Is.EqualTo("Travel Time: 2 hours (round trip)"));

        director.RequestDestination(Streets);
        yield return WaitUntil(() => director.SelectedDestination == Streets, 2f, "The destination was not shared.");
        Assert.That(travel.CanInteract(null), Is.True);
        travel.Interact(null);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == Streets && !director.IsTraveling, 30f, "Travel did not complete.");

        Assert.That(clock.InBase, Is.False);
        Assert.That(clock.Elapsed, Is.InRange(10f, 10.5f), "Leaving base deducts the round-trip cost (2 hours) once.");
        yield return new WaitForSeconds(0.3f);
        Assert.That(clock.IsRunning, Is.True, "The clock runs away from base.");
        Assert.That(clock.Elapsed, Is.GreaterThan(10.1f));
        yield return null;
        Assert.That(hud.ClockImage.sprite.name, Is.EqualTo("ClockUI-Sheet_2"));
        Assert.That(travel.Prompt, Is.EqualTo("Board the jeepney back to base"), "Away from base the jeepney always heads home.");
        Assert.That(clock.CanBoard, Is.True);
        Assert.That(player.GetComponent<PlayerPersistence>().IsAboard, Is.False);
        Assert.That(clock.Phase, Is.EqualTo(RoundPhase.Running));
    }

    [UnityTest]
    public IEnumerator BoardingAtMidnightReturnsHomeAndStartsANewRoundWhenTheQuotaIsMet()
    {
        string hub = SceneManager.GetActiveScene().name;
        Track(GroundAt(Vector3.zero));
        (TravelDirector director, RoundClock clock) = StartHost(Streets);
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        RoundClockHud hud = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath))).GetComponent<RoundClockHud>();
        PlayerPersistence player = NetworkClient.localPlayer.GetComponent<PlayerPersistence>();
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        health.TakeDamage(1);

        Assert.That(clock.InBase, Is.False, $"'{hub}' stands in for a map away from base.");
        yield return new WaitForSeconds(0.2f);
        Assert.That(clock.IsRunning, Is.True);
        Assert.That(clock.Elapsed, Is.GreaterThan(0f));

        clock.ServerSetElapsed(clock.RoundSeconds);
        yield return WaitUntil(() => clock.Phase == RoundPhase.ReturnCountdown, 1f, "Midnight did not start the return countdown.");
        Assert.That(clock.IsMidnight, Is.True);
        Assert.That(clock.CountdownRemaining, Is.InRange(2f, 3f));
        yield return null;
        Assert.That(hud.CountdownPanel.activeSelf, Is.True);
        StringAssert.StartsWith("Midnight! Get back to the jeepney", hud.CountdownLabel.text);
        Assert.That(hud.ClockImage.sprite.name, Is.EqualTo("ClockUI-Sheet_0"), "Midnight shows 12.");

        clock.RequestBoard();
        yield return WaitUntil(() => director.IsTraveling, 2f, "Boarding as the only player did not leave immediately.");
        Assert.That(player.IsAboard, Is.True);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == Streets && !director.IsTraveling, 30f, "The jeepney did not reach base.");

        Assert.That(clock.InBase, Is.True);
        Assert.That(player.IsAboard, Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(5), "Players who boarded keep their health.");
        Assert.That(clock.Round, Is.EqualTo(2), "Meeting the quota starts a new round.");
        Assert.That(clock.Elapsed, Is.Zero, "The timer resets at base.");
        Assert.That(clock.Phase, Is.EqualTo(RoundPhase.Running));
        yield return null;
        Assert.That(hud.CountdownPanel.activeSelf, Is.False);
        Assert.That(PlayerControlLock.IsLocked, Is.False);
    }

    [UnityTest]
    public IEnumerator PlayersLeftBehindReturnWithOneHeartAndAMissedQuotaEndsTheGame()
    {
        Track(GroundAt(Vector3.zero));
        (TravelDirector director, RoundClock clock) = StartHost(Streets);
        clock.QuotaMet = () => false;
        int resets = 0;
        var leftBehind = new List<PlayerPersistence>();
        clock.ServerGameReset += () => resets++;
        clock.ServerPlayerLeftBehind += leftBehind.Add;
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        RoundClockHud hud = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath))).GetComponent<RoundClockHud>();
        PlayerPersistence player = NetworkClient.localPlayer.GetComponent<PlayerPersistence>();
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        PlayerStamina stamina = player.GetComponent<PlayerStamina>();
        Assert.That(stamina.TryUseStamina(60f), Is.True);

        clock.ServerSetElapsed(clock.RoundSeconds);
        yield return WaitUntil(() => clock.Phase == RoundPhase.ReturnCountdown, 1f, "Midnight did not start the return countdown.");
        yield return WaitUntil(() => director.IsTraveling, 5f, "The jeepney did not leave when the countdown ended.");
        Assert.That(leftBehind, Is.EquivalentTo(new[] { player }));
        Assert.That(health.CurrentHealth, Is.Zero, "Players who did not board die when the jeepney leaves.");
        Assert.That(clock.IsAwaitingRespawn(player), Is.True);

        yield return WaitUntil(() => SceneManager.GetActiveScene().name == Streets && !director.IsTraveling, 30f, "The jeepney did not reach base.");
        Assert.That(clock.IsAwaitingRespawn(player), Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(2), "Dead players respawn at base with one heart.");
        Assert.That(clock.Phase, Is.EqualTo(RoundPhase.GameOver), "Returning at midnight without the quota loses the game.");
        yield return null;
        Assert.That(hud.GameOverPanel.activeSelf, Is.True);
        Assert.That(PlayerControlLock.IsHeldBy(clock), Is.True);

        yield return WaitUntil(() => clock.Phase == RoundPhase.Running, 3f, "The game did not reset after the game over screen.");
        Assert.That(resets, Is.EqualTo(1));
        Assert.That(clock.Elapsed, Is.Zero);
        Assert.That(clock.Round, Is.EqualTo(1));
        Assert.That(health.CurrentHealth, Is.EqualTo(PlayerHealth.MaxHealth));
        yield return null;
        Assert.That(stamina.CurrentStamina, Is.EqualTo(PlayerStamina.MaxStamina));
        Assert.That(stamina.IsExhausted, Is.False);
        Assert.That(hud.GameOverPanel.activeSelf, Is.False);
        Assert.That(PlayerControlLock.IsHeldBy(clock), Is.False);
    }

    [UnityTest]
    public IEnumerator EnemiesIgnorePlayersWaitingAboard()
    {
        Track(GroundAt(Vector3.zero));
        (_, RoundClock clock) = StartHost(Streets);
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerPersistence player = NetworkClient.localPlayer.GetComponent<PlayerPersistence>();
        player.GetComponent<PlayerMovement>().TeleportTo(new Vector3(0f, 0f, 4f), Quaternion.identity);

        GameObject enemy = Track(new GameObject("Watcher"));
        enemy.transform.position = new Vector3(0f, 0f, 0f);
        var senses = enemy.AddComponent<EnemySenses>();
        yield return null;
        Assert.That(senses.FindVisiblePlayer(), Is.SameAs(player));

        // Set directly while the clock runs normally (boarding would otherwise depart at once with one player).
        Assert.That(clock.Phase, Is.EqualTo(RoundPhase.Running));
        player.ServerSetAboard(true);
        yield return null;
        Assert.That(senses.FindVisiblePlayer(), Is.Null);
        Assert.That(PlayerControlLock.IsHeldBy(player), Is.True, "Players waiting aboard cannot move.");
        player.ServerSetAboard(false);
        yield return null;
        Assert.That(PlayerControlLock.IsHeldBy(player), Is.False);

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        health.SetHealth(0);
        Assert.That(senses.FindVisiblePlayer(), Is.Null, "Enemies ignore dead players.");
        Assert.That(player.GetComponent<PlayerNoise>().IsMakingNoise, Is.False);
        health.SetHealth(PlayerHealth.MaxHealth);
        Assert.That(senses.FindVisiblePlayer(), Is.SameAs(player));
    }

    [UnityTest]
    public IEnumerator EveryoneDyingAwayFromBaseReturnsThemToBaseToRespawn()
    {
        string hub = SceneManager.GetActiveScene().name;
        Track(GroundAt(Vector3.zero));
        (TravelDirector director, RoundClock clock) = StartHost(Streets);
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerPersistence player = NetworkClient.localPlayer.GetComponent<PlayerPersistence>();
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        int diedEvents = 0;
        clock.ServerAllPlayersDied += () => diedEvents++;
        Assert.That(clock.InBase, Is.False, $"'{hub}' stands in for a map away from base.");

        health.TakeDamage(PlayerHealth.MaxHealth);
        Assert.That(health.IsDead, Is.True);
        Assert.That(clock.IsAwaitingRespawn(player), Is.True);
        clock.ServerBoard(NetworkServer.localConnection);
        Assert.That(player.IsAboard, Is.False, "Dead players cannot board.");
        yield return new WaitForSeconds(0.2f);
        Assert.That(director.IsTraveling, Is.False, "The return waits for the death screen.");

        yield return WaitUntil(() => director.IsTraveling, 3f, "Everyone being dead did not send the jeepney home.");
        Assert.That(diedEvents, Is.EqualTo(1));
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == Streets && !director.IsTraveling, 30f, "The jeepney did not reach base.");
        Assert.That(clock.InBase, Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(2), "Dead players respawn at base with one heart.");
        Assert.That(clock.IsAwaitingRespawn(player), Is.False);
    }

    [UnityTest]
    public IEnumerator TheTravelMapOnlyWorksAtBase()
    {
        Track(GroundAt(Vector3.zero));
        StartHost(Streets);
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject terminal = Track(new GameObject("Away Terminal"));
        terminal.SetActive(false);
        terminal.AddComponent<BoxCollider>();
        var mapTerminal = terminal.AddComponent<MapTerminal>();
        var settings = new SerializedObject(mapTerminal);
        settings.FindProperty("mapScreenPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(MapPath).GetComponent<TravelMapScreen>();
        SerializedProperty destinations = settings.FindProperty("destinations");
        destinations.arraySize = 1;
        destinations.GetArrayElementAtIndex(0).FindPropertyRelative("sceneName").stringValue = Streets;
        settings.ApplyModifiedPropertiesWithoutUndo();
        terminal.SetActive(true);

        Assert.That(MapTerminal.TravelMapAvailable, Is.False);
        Assert.That(mapTerminal.CanInteract(null), Is.False);
        mapTerminal.Interact(null);
        Assert.That(TravelMapScreen.Current == null || !TravelMapScreen.Current.gameObject.activeInHierarchy, Is.True);
        StringAssert.Contains("only be chosen at base", TravelDirector.ValidateDestination(Streets));
    }

    private (TravelDirector, RoundClock) StartHost(string baseScene)
    {
        managerObject = new GameObject("Round Clock Test NetworkManager");
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
        settings.FindProperty("loadingRevealDuration").floatValue = 0.05f;
        settings.FindProperty("readyTimeout").floatValue = 10f;
        settings.FindProperty("minimumLoadingSeconds").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var clock = managerObject.AddComponent<RoundClock>();
        var clockSettings = new SerializedObject(clock);
        clockSettings.FindProperty("roundMinutes").floatValue = 1f;
        clockSettings.FindProperty("baseSceneName").stringValue = baseScene;
        clockSettings.FindProperty("returnCountdownSeconds").floatValue = 3f;
        clockSettings.FindProperty("gameOverSeconds").floatValue = 1f;
        clockSettings.FindProperty("allDeadReturnSeconds").floatValue = 0.6f;
        clockSettings.ApplyModifiedPropertiesWithoutUndo();
        managerObject.SetActive(true);

        NetworkServer.listen = false;
        manager.StartHost();
        return (director, clock);
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
