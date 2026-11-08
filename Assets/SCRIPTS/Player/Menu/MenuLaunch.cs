using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hands the main menu's choice (host a save slot, or join a friend) to the game scene, whose
/// <see cref="SteamSession"/> carries it out once Steam and the NetworkManager exist. When a session that was started
/// from the menu ends, <see cref="ReturnToMenu"/> clears the persistent game objects and loads the menu again.
/// </summary>
public static class MenuLaunch
{
    public const string MenuSceneName = "MainMenu";
    public const string GameSceneName = "SampleScene";

    public enum Mode
    {
        None,
        Host,
        Join,
    }

    public static Mode Pending { get; private set; }
    /// <summary>Save slot to host (0-based); -1 when the session does not save.</summary>
    public static int PendingSaveSlot { get; private set; } = -1;
    public static ulong PendingLobby { get; private set; }
    public static string PendingHostId { get; private set; }
    /// <summary>True while the current game was entered from the main menu (leaving returns there).</summary>
    public static bool FromMenu { get; private set; }
    /// <summary>Save slot of the session being hosted (0-based), kept until returning to the menu; -1 = no saving.</summary>
    public static int ActiveSaveSlot { get; private set; } = -1;

    /// <summary>Sets the slot the hosted session saves to (tests, or hosting without the menu).</summary>
    public static void SetActiveSaveSlot(int slot) => ActiveSaveSlot = slot;

    public const string HostDisconnectedNotice = "Host disconnected the server";
    public const string KickedNotice = "You have been kicked from the game.";
    public const string ConnectFailedNotice = "Could not connect to the host.";

    /// <summary>Message the main menu shows on arrival (why the game ended); taken once.</summary>
    public static string PendingNotice { get; private set; }
    public static bool HasNotice => !string.IsNullOrEmpty(PendingNotice);
    /// <summary>True after the local player chose to leave (pause menu Quit); their own disconnect is expected.</summary>
    public static bool LeavingDeliberately { get; private set; }

    public static void SetNotice(string message) => PendingNotice = message;

    public static string TakeNotice()
    {
        string notice = PendingNotice;
        PendingNotice = null;
        return notice;
    }

    /// <summary>
    /// Leaves the current game for the main menu (pause menu Quit). The host stops the server, saving first and
    /// disconnecting every guest (who return with <see cref="HostDisconnectedNotice"/>); a guest just disconnects.
    /// </summary>
    public static void LeaveGame()
    {
        LeavingDeliberately = true;
        NetworkManager manager = NetworkManager.singleton;
        if (manager != null && NetworkServer.active && NetworkClient.active)
            manager.StopHost();
        else if (manager != null && NetworkClient.active)
            manager.StopClient();
        else if (manager != null && NetworkServer.active)
            manager.StopServer();
        // The manager's OnStopClient normally returns to the menu; this covers sessions without a client.
        if (LeavingDeliberately)
            ReturnToMenu();
    }

    public static void Host(int saveSlot)
    {
        Clear();
        Pending = Mode.Host;
        PendingSaveSlot = saveSlot;
        ActiveSaveSlot = saveSlot;
        LoadGame();
    }

    public static void JoinLobby(ulong lobbyId)
    {
        Clear();
        Pending = Mode.Join;
        PendingLobby = lobbyId;
        ActiveSaveSlot = -1;
        LoadGame();
    }

    public static void JoinHost(string hostId)
    {
        Clear();
        Pending = Mode.Join;
        PendingHostId = hostId;
        ActiveSaveSlot = -1;
        LoadGame();
    }

    /// <summary>Takes the pending request (once).</summary>
    public static Mode Consume(out int saveSlot, out ulong lobby, out string hostId)
    {
        Mode mode = Pending;
        saveSlot = PendingSaveSlot;
        lobby = PendingLobby;
        hostId = PendingHostId;
        Clear();
        return mode;
    }

    public static void Clear()
    {
        Pending = Mode.None;
        PendingSaveSlot = -1;
        PendingLobby = 0;
        PendingHostId = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Clear();
        FromMenu = false;
        ActiveSaveSlot = -1;
        Quitting = false;
        PendingNotice = null;
        LeavingDeliberately = false;
        ReturningToMenu = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.wantsToQuit -= OnWantsToQuit;
        Application.wantsToQuit += OnWantsToQuit;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
    }

    /// <summary>True once the application (or Play Mode) is shutting down; ending a session then must not load the menu.</summary>
    public static bool Quitting { get; private set; }

    private static bool OnWantsToQuit()
    {
        Quitting = true;
        return true;
    }

#if UNITY_EDITOR
    private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            Quitting = true;
    }
#endif

    private static void LoadGame()
    {
        FromMenu = true;
        SceneManager.LoadScene(GameSceneName);
    }

    /// <summary>
    /// Destroys the persistent game objects (NetworkManager, HUDs, players) and loads the main menu. Steam stays
    /// initialized by <see cref="SteamBootstrap"/>.
    /// </summary>
    public static void ReturnToMenu()
    {
        if (ReturningToMenu)
            return;
        FromMenu = false;
        LeavingDeliberately = false;
        ActiveSaveSlot = -1;
        Clear();
        if (Quitting || !Application.isPlaying || !Application.CanStreamedLevelBeLoaded(MenuSceneName))
            return;
        ReturningToMenu = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        PlayerControlLock.ReleaseAll();
        HudVisibility.ShowAll();
        foreach (GameObject root in PersistentRoots())
        {
            if (root.GetComponentInChildren<SteamBootstrap>(true) != null || root.GetComponent<BrightnessVolume>() != null)
                continue;
            if (root.GetComponentInChildren<NetworkManager>(true) != null || HasGameComponent(root))
                Object.Destroy(root);
        }
        SceneManager.LoadScene(MenuSceneName);
    }

    /// <summary>True from <see cref="ReturnToMenu"/> until the menu scene has loaded (game objects are being torn down).</summary>
    public static bool ReturningToMenu { get; private set; }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != MenuSceneName)
            return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ReturningToMenu = false;
    }

    private static bool HasGameComponent(GameObject root)
    {
        System.Reflection.Assembly game = typeof(PlayerHud).Assembly;
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Assembly == game)
                return true;
        }
        return false;
    }

    private static List<GameObject> PersistentRoots()
    {
        var probe = new GameObject("PersistentProbe");
        Object.DontDestroyOnLoad(probe);
        var roots = new List<GameObject>(probe.scene.GetRootGameObjects());
        roots.Remove(probe);
        Object.DestroyImmediate(probe);
        return roots;
    }
}
