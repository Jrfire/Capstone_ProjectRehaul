#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class PauseMenuTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string PausePath = "Assets/PREFABS/PauseMenu.prefab";
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

    private GameObject managerObject;
    private GameObject pauseObject;
    private GameObject ground;
    private Transport previousTransport;
    private bool previousListen;

    [SetUp]
    public void Setup()
    {
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        SteamBootstrap.SkipSteamInitialization = true;
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
        if (pauseObject != null)
            Object.DestroyImmediate(pauseObject);
        if (ground != null)
            Object.DestroyImmediate(ground);
        foreach (SteamBootstrap bootstrap in Object.FindObjectsByType<SteamBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(bootstrap.gameObject);

        Scene menu = SceneManager.GetSceneByName(MenuLaunch.MenuSceneName);
        if (menu.IsValid() && menu.isLoaded)
        {
            Scene empty = SceneManager.CreateScene("PauseMenuTestsEmpty");
            SceneManager.SetActiveScene(empty);
            AsyncOperation unload = SceneManager.UnloadSceneAsync(menu);
            while (unload != null && !unload.isDone)
                yield return null;
        }

        MenuLaunch.TakeNotice();
        SteamBootstrap.SkipSteamInitialization = false;
        PlayerControlLock.ReleaseAll();
        HudVisibility.ShowAll();
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
    }

    [UnityTest]
    public IEnumerator PauseMenuLocksControlsShowsOptionsAndListsPlayers()
    {
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PauseMenu pause = SpawnPauseMenu();
        Assert.That(pause.IsOpen, Is.False, "The pause menu starts closed.");

        pause.Open();
        Assert.That(pause.IsOpen, Is.True);
        Assert.That(PlayerControlLock.IsLocked, Is.True, "Movement, look and interaction pause while it is open.");
        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        Assert.That(pause.PlayerRowCount, Is.EqualTo(1));
        string hostName = NetworkClient.localPlayer.GetComponent<PlayerName>().DisplayName;
        Assert.That(pause.PlayerNames(), Is.EqualTo(new[] { hostName }));
        Assert.That(pause.KickVisible(0), Is.False, "The host cannot kick themselves.");
        Assert.That(PauseMenu.CanKick(NetworkClient.localPlayer), Is.False);
        var manager = (SteamNetworkManager)NetworkManager.singleton;
        Assert.That(manager.ServerKick(NetworkServer.localConnection), Is.False, "The host's own connection is never kicked.");

        pause.ShowOptions();
        Assert.That(pause.OptionsOpen, Is.True, "Options opens the shared settings panel.");
        Assert.That(pause.Settings.BindingRowCount, Is.GreaterThanOrEqualTo(8), "Controls can be rebound in game.");
        Assert.That(pause.Settings.SoundsPage.activeSelf, Is.True);
        pause.ShowMain();
        Assert.That(pause.OptionsOpen, Is.False);
        Assert.That(pause.IsOpen, Is.True);

        pause.Close();
        Assert.That(pause.IsOpen, Is.False);
        Assert.That(PlayerControlLock.IsLocked, Is.False, "Resume gives the controls back.");
    }

    [UnityTest]
    public IEnumerator PauseMenuNeedsASession()
    {
        PauseMenu pause = SpawnPauseMenu();
        pause.Open();
        Assert.That(pause.IsOpen, Is.False, "Without a game session there is nothing to pause.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator QuittingAsHostReturnsToTheMainMenuWithoutANotice()
    {
        StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PauseMenu pause = SpawnPauseMenu();
        pause.Open();
        pause.Quit();
        Assert.That(NetworkServer.active, Is.False, "Quit shuts the server down.");
        Assert.That(NetworkClient.active, Is.False);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == MenuLaunch.MenuSceneName, 5f, "The main menu did not load.");
        yield return null;
        Assert.That(MenuLaunch.ReturningToMenu, Is.False);
        Assert.That(PlayerControlLock.IsLocked, Is.False);
        MainMenuScreen screen = Object.FindAnyObjectByType<MainMenuScreen>();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen.NoticeShown, Is.False, "Leaving on purpose shows no disconnect notice.");
        Assert.That(PauseMenu.Instance, Is.Null, "The game's persistent pause menu is torn down.");
    }

    [UnityTest]
    public IEnumerator TheMainMenuShowsTheKickNotice()
    {
        MenuLaunch.SetNotice(MenuLaunch.KickedNotice);
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(MenuScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        while (!load.isDone)
            yield return null;
        Scene menu = SceneManager.GetSceneByPath(MenuScenePath);
        try
        {
            yield return null;
            MainMenuScreen screen = menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuScreen>(true)).Single();
            Assert.That(screen.NoticeShown, Is.True);
            Assert.That(screen.NoticeMessage, Is.EqualTo("You have been kicked from the game."));
            Assert.That(MenuLaunch.HasNotice, Is.False, "The notice is shown once.");
            screen.HideNotice();
            Assert.That(screen.NoticeShown, Is.False);
            Assert.That(screen.ActivePanel.name, Does.Contain("Main"));
        }
        finally
        {
            SceneManager.UnloadSceneAsync(menu);
        }
        yield return null;
    }

    private PauseMenu SpawnPauseMenu()
    {
        pauseObject = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PausePath));
        return pauseObject.GetComponent<PauseMenu>();
    }

    private void StartHost()
    {
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(50f, 1f, 50f);
        managerObject = new GameObject("Pause Test NetworkManager");
        managerObject.SetActive(false);
        var transport = managerObject.AddComponent<TelepathyTransport>();
        var manager = managerObject.AddComponent<SteamNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        manager.autoCreatePlayer = true;
        manager.dontDestroyOnLoad = true;
        managerObject.SetActive(true);
        NetworkServer.listen = false;
        manager.StartHost();
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string message)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end)
                Assert.Fail(message);
            yield return null;
        }
    }
}
#endif
