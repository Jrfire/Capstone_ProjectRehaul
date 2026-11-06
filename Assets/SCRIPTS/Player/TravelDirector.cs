using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct TravelSelectRequest : NetworkMessage
{
    public string sceneName;
}

public struct TravelStartRequest : NetworkMessage
{
}

public struct TravelDestinationMessage : NetworkMessage
{
    public string sceneName;
}

public struct TravelSeedMessage : NetworkMessage
{
    public string sceneName;
    public int seed;
}

public struct TravelBeginMessage : NetworkMessage
{
    public string sceneName;
    public float fadeDuration;
    /// <summary>Asset ids of the destination's enemy prefabs, shown as miniatures on the loading screen.</summary>
    public uint[] enemyAssetIds;
}

public struct TravelSceneReadyMessage : NetworkMessage
{
    public string sceneName;
}

public struct TravelArrivalMessage : NetworkMessage
{
    public Vector3 position;
    public Quaternion rotation;
}

public struct TravelCompleteMessage : NetworkMessage
{
}

/// <summary>Every destination's rolled enemy for the current scene's terminals ("" = no enemies).</summary>
public struct TravelEnemyRollMessage : NetworkMessage
{
    public string[] scenes;
    public string[] enemies;
}

/// <summary>
/// Coordinates group travel between scenes. Lives on the persistent NetworkManager object; any player may pick a
/// destination on a <see cref="MapTerminal"/> and start the trip on a <see cref="TravelInteractable"/>. The host
/// fades everyone out, changes the Mirror scene, waits for every client to build the map, moves players onto the
/// new spawn points and then fades everyone back in.
/// </summary>
[DisallowMultipleComponent]
public sealed class TravelDirector : MonoBehaviour
{
    [SerializeField, Tooltip("Fade/loading overlay prefab, instantiated once under this object.")]
    private TravelLoadingScreen loadingScreenPrefab;
    [SerializeField, Min(0f), Tooltip("Seconds for each fade to or from black.")]
    private float fadeDuration = 1f;
    [SerializeField, Min(0f), Tooltip("Seconds to fade from black into the loading screen.")]
    private float loadingRevealDuration = 0.5f;
    [SerializeField, Min(1f), Tooltip("The host stops waiting for clients that are not ready after this many seconds.")]
    private float readyTimeout = 60f;
    [SerializeField, Min(0f), Tooltip("The loading screen stays up at least this many seconds, even when the scene builds sooner.")]
    private float minimumLoadingSeconds = 8f;
    private float serverLoadingShownAt;

    private readonly HashSet<int> pendingConnections = new HashSet<int>();
    private readonly HashSet<int> readyConnections = new HashSet<int>();
    private TravelLoadingScreen loadingScreen;
    private Coroutine clientFadeRoutine;
    private Coroutine clientReadyRoutine;
    private Coroutine serverRoutine;
    private string serverDestination;
    private string serverTravelScene;
    private int serverTravelSeed;
    private readonly Dictionary<string, EnemyDefinition> serverRolls = new Dictionary<string, EnemyDefinition>();
    private readonly Dictionary<string, string> clientRolls = new Dictionary<string, string>();

    public static TravelDirector Instance { get; private set; }

    /// <summary>
    /// Server only: true once a travel started, so the arrival scene's EnemySpawner spawns <see cref="ArrivalEnemy"/>
    /// (null = the destination rolled no enemies) instead of its own fallback list.
    /// </summary>
    public bool HasArrivalRoll { get; private set; }
    public EnemyDefinition ArrivalEnemy { get; private set; }
    /// <summary>Raised on clients whenever the rolled enemy roster changes.</summary>
    public event System.Action RollsChanged;
    /// <summary>Server: a trip is starting from the current scene (first argument) to the destination (second argument).</summary>
    public event System.Action<string, string> ServerTravelStarting;
    /// <summary>Server: every player has arrived in this scene and is fading back in.</summary>
    public event System.Action<string> ServerTravelCompleted;

    /// <summary>Destination confirmed on a map terminal and shared with every client, or null.</summary>
    public string SelectedDestination { get; private set; }
    /// <summary>True on this client from the start of the fade until the arrival fade-in starts.</summary>
    public bool IsTraveling { get; private set; }
    public bool ServerTraveling { get; private set; }
    /// <summary>
    /// Server: true while players are fading out, loading or fading back into the new scene. Enemies stay frozen
    /// (no movement or attacks) during this time.
    /// </summary>
    public bool ServerLoadingActive => ServerTraveling || Time.realtimeSinceStartup < serverFadeInEndsAt;
    private float serverFadeInEndsAt;
    public string ServerDestination => serverDestination;
    public float FadeDuration => fadeDuration;
    public TravelLoadingScreen LoadingScreen => EnsureLoadingScreen();
    public bool CanTravel => !string.IsNullOrEmpty(SelectedDestination) && !IsTraveling && NetworkClient.isConnected;

    public static bool CanLoadScene(string sceneName) =>
        !string.IsNullOrWhiteSpace(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A reloaded scene's duplicate NetworkManager is destroyed by Mirror; keep the original director.
            enabled = false;
            return;
        }
        Instance = this;
        EnsureLoadingScreen();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;
        PlayerControlLock.Release(this);
        PlayerStamina.TravelPaused = false;
        Instance = null;
    }

    private TravelLoadingScreen EnsureLoadingScreen()
    {
        if (loadingScreen == null && loadingScreenPrefab != null && Instance == this)
        {
            loadingScreen = Instantiate(loadingScreenPrefab, transform);
            loadingScreen.name = loadingScreenPrefab.name;
        }
        return loadingScreen;
    }

    // ---------------------------------------------------------------- client requests

    public void RequestDestination(string sceneName)
    {
        if (!NetworkClient.isConnected)
        {
            Debug.LogWarning("Host or join a session before choosing a travel destination.", this);
            return;
        }
        NetworkClient.Send(new TravelSelectRequest { sceneName = sceneName });
    }

    public void RequestTravel()
    {
        if (CanTravel)
            NetworkClient.Send(new TravelStartRequest());
    }

    // ---------------------------------------------------------------- server

    public void RegisterServerHandlers()
    {
        MapManager.WaitForNetworkSeed = true;
        NetworkServer.ReplaceHandler<TravelSelectRequest>(OnServerSelect);
        NetworkServer.ReplaceHandler<TravelStartRequest>(OnServerStart);
        NetworkServer.ReplaceHandler<TravelSceneReadyMessage>(OnServerSceneReady);
        foreach (MapTerminal terminal in FindObjectsByType<MapTerminal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            ServerRollDestinations(terminal);
    }

    /// <summary>
    /// Server: rolls an enemy for every destination of <paramref name="terminal"/> that has no roll yet. Rolls last
    /// until the next trip starts; the chosen destination's roll is what spawns there.
    /// </summary>
    public void ServerRollDestinations(MapTerminal terminal)
    {
        if (!NetworkServer.active || Instance != this || terminal == null ||
            terminal.gameObject.scene != SceneManager.GetActiveScene())
            return;
        bool changed = false;
        foreach (TravelDestination destination in terminal.DestinationEntries)
        {
            if (destination == null || string.IsNullOrEmpty(destination.Name) || serverRolls.ContainsKey(destination.Name))
                continue;
            serverRolls[destination.Name] = destination.Roll();
            changed = true;
        }
        if (changed)
            NetworkServer.SendToAll(BuildRollMessage());
    }

    public EnemyDefinition ServerRollFor(string sceneName) =>
        sceneName != null && serverRolls.TryGetValue(sceneName, out EnemyDefinition enemy) ? enemy : null;

    /// <summary>Client: rolled enemy name for a destination, "" when it rolled none, or null when not rolled yet.</summary>
    public string RolledEnemyName(string sceneName) =>
        sceneName != null && clientRolls.TryGetValue(sceneName, out string enemy) ? enemy : null;

    private TravelEnemyRollMessage BuildRollMessage()
    {
        var scenes = new string[serverRolls.Count];
        var enemies = new string[serverRolls.Count];
        int index = 0;
        foreach (KeyValuePair<string, EnemyDefinition> roll in serverRolls)
        {
            scenes[index] = roll.Key;
            enemies[index] = roll.Value != null ? roll.Value.DisplayName : string.Empty;
            index++;
        }
        return new TravelEnemyRollMessage { scenes = scenes, enemies = enemies };
    }

    public void ResetServer()
    {
        if (serverRoutine != null)
            StopCoroutine(serverRoutine);
        serverRoutine = null;
        serverDestination = null;
        serverTravelScene = null;
        ServerTraveling = false;
        serverFadeInEndsAt = 0f;
        serverRolls.Clear();
        HasArrivalRoll = false;
        ArrivalEnemy = null;
        pendingConnections.Clear();
        readyConnections.Clear();
        if (!NetworkClient.active)
            MapManager.WaitForNetworkSeed = false;
    }

    public void OnServerConnect(NetworkConnectionToClient connection)
    {
        if (!string.IsNullOrEmpty(serverDestination))
            connection.Send(new TravelDestinationMessage { sceneName = serverDestination });
        connection.Send(BuildRollMessage());

        if (ServerTraveling && !string.IsNullOrEmpty(serverTravelScene))
            connection.Send(new TravelSeedMessage { sceneName = serverTravelScene, seed = serverTravelSeed });
        else if (MapManager.Active != null && MapManager.Active.IsBuilt)
            connection.Send(new TravelSeedMessage { sceneName = MapManager.Active.gameObject.scene.name, seed = MapManager.Active.Seed });
    }

    public void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        pendingConnections.Remove(connection.connectionId);
        readyConnections.Remove(connection.connectionId);
    }

    public void OnServerSceneChanged(string sceneName)
    {
        foreach (MapTerminal terminal in FindObjectsByType<MapTerminal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            ServerRollDestinations(terminal);
        if (ServerTraveling && sceneName == serverTravelScene)
            serverRoutine = StartCoroutine(ServerWaitForEveryone());
    }

    /// <summary>Server validation for a destination chosen on a terminal; returns null when it is allowed.</summary>
    public static string ValidateDestination(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return "No destination was chosen.";
        if (sceneName == SceneManager.GetActiveScene().name)
            return $"Players are already in '{sceneName}'.";
        if (!MapTerminal.TravelMapAvailable)
            return "Destinations can only be chosen at base.";
        if (!CanLoadScene(sceneName))
            return $"Scene '{sceneName}' is not in the Build Profile's scene list.";
        if (RoundClock.Instance != null && RoundClock.Instance.enabled && !RoundClock.Instance.CanAfford(sceneName))
            return $"Not enough time left before midnight to travel to '{sceneName}'.";

        foreach (MapTerminal terminal in FindObjectsByType<MapTerminal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (terminal.gameObject.scene == SceneManager.GetActiveScene() && terminal.Lists(sceneName))
                return null;
        }
        return $"No map terminal in this scene lists '{sceneName}'.";
    }

    private void OnServerSelect(NetworkConnectionToClient connection, TravelSelectRequest request)
    {
        if (ServerTraveling)
            return;
        string sceneName = request.sceneName?.Trim();
        string error = ValidateDestination(sceneName);
        if (error != null)
        {
            Debug.LogWarning($"Rejected travel destination from connection {connection.connectionId}: {error}", this);
            return;
        }

        serverDestination = sceneName;
        NetworkServer.SendToAll(new TravelDestinationMessage { sceneName = sceneName });
    }

    private void OnServerStart(NetworkConnectionToClient connection, TravelStartRequest request)
    {
        if (ServerTraveling)
            return;
        // Away from base the jeepney always heads home: each player boards individually.
        if (RoundClock.Instance != null && RoundClock.Instance.enabled && !RoundClock.Instance.InBase)
        {
            RoundClock.Instance.ServerBoard(connection);
            return;
        }
        if (string.IsNullOrEmpty(serverDestination))
            return;
        string error = ValidateDestination(serverDestination);
        if (error != null)
        {
            Debug.LogWarning($"Cannot travel to '{serverDestination}': {error}", this);
            return;
        }
        serverRoutine = StartCoroutine(ServerTravel(serverDestination));
    }

    /// <summary>
    /// Server: starts a group trip without a terminal selection (e.g. the jeepney home at midnight). Returns false
    /// when travel is already in progress or the scene cannot be loaded.
    /// </summary>
    public bool ServerBeginTravel(string sceneName)
    {
        if (!NetworkServer.active || ServerTraveling || Instance != this)
            return false;
        if (!CanLoadScene(sceneName))
        {
            Debug.LogError($"Cannot travel to '{sceneName}': it is not in the Build Profile's scene list.", this);
            return false;
        }
        serverRoutine = StartCoroutine(ServerTravel(sceneName));
        return true;
    }

    private IEnumerator ServerTravel(string sceneName)
    {
        ServerTraveling = true;
        ServerTravelStarting?.Invoke(SceneManager.GetActiveScene().name, sceneName);
        serverTravelScene = sceneName;
        serverTravelSeed = Random.Range(int.MinValue, int.MaxValue);
        serverDestination = null;
        NetworkServer.SendToAll(new TravelDestinationMessage { sceneName = string.Empty });

        // The destination keeps the enemy rolled while choosing it; the new scene's terminals roll afresh.
        HasArrivalRoll = true;
        ArrivalEnemy = ServerRollFor(sceneName);
        serverRolls.Clear();
        NetworkServer.SendToAll(BuildRollMessage());

        // The seed goes out before the scene message so every peer builds the same street.
        MapManager.SetNetworkSeed(sceneName, serverTravelSeed);
        NetworkServer.SendToAll(new TravelSeedMessage { sceneName = sceneName, seed = serverTravelSeed });
        NetworkServer.SendToAll(new TravelBeginMessage
        {
            sceneName = sceneName,
            fadeDuration = fadeDuration,
            enemyAssetIds = ArrivalEnemyAssetIds()
        });
        // Clients reveal the loading screen once their fade to black finishes.
        serverLoadingShownAt = Time.realtimeSinceStartup + fadeDuration;

        yield return new WaitForSecondsRealtime(fadeDuration + 0.2f);
        if (!NetworkServer.active || NetworkManager.singleton == null)
        {
            ServerTraveling = false;
            yield break;
        }

        pendingConnections.Clear();
        readyConnections.Clear();
        foreach (int id in NetworkServer.connections.Keys)
            pendingConnections.Add(id);
        serverRoutine = null;
        NetworkManager.singleton.ServerChangeScene(sceneName);
    }

    /// <summary>Server: the enemies rolled for the trip's destination (one today; ready for several).</summary>
    private uint[] ArrivalEnemyAssetIds()
    {
        var ids = new List<uint>();
        if (ArrivalEnemy != null && ArrivalEnemy.prefab != null && ArrivalEnemy.prefab.assetId != 0)
            ids.Add(ArrivalEnemy.prefab.assetId);
        return ids.ToArray();
    }

    private IEnumerator ServerWaitForEveryone()
    {
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < readyTimeout)
        {
            pendingConnections.RemoveWhere(id => !NetworkServer.connections.ContainsKey(id));
            bool mapDone = MapManager.Active == null || MapManager.Active.IsDone;
            if (mapDone && pendingConnections.IsSubsetOf(readyConnections))
                break;
            yield return null;
        }

        if (!pendingConnections.IsSubsetOf(readyConnections))
            Debug.LogWarning($"Travel continued after {readyTimeout} s without every client reporting ready.", this);

        while (Time.realtimeSinceStartup < serverLoadingShownAt + minimumLoadingSeconds)
            yield return null;
        pendingConnections.RemoveWhere(id => !NetworkServer.connections.ContainsKey(id));

        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (!readyConnections.Contains(connection.connectionId))
                continue;
            SendArrival(connection);
            connection.Send(new TravelCompleteMessage());
        }

        pendingConnections.Clear();
        readyConnections.Clear();
        // Clients fade the loading screen to black, hide it and fade into the game before anyone can act.
        serverFadeInEndsAt = Time.realtimeSinceStartup + fadeDuration * 2f;
        ServerTraveling = false;
        serverRoutine = null;
        ServerTravelCompleted?.Invoke(SceneManager.GetActiveScene().name);
    }

    private void OnServerSceneReady(NetworkConnectionToClient connection, TravelSceneReadyMessage message)
    {
        if (message.sceneName != SceneManager.GetActiveScene().name)
            return;

        if (ServerTraveling && pendingConnections.Contains(connection.connectionId))
        {
            readyConnections.Add(connection.connectionId);
            return;
        }

        // Late joiners and stragglers past the timeout are released individually.
        SendArrival(connection);
        connection.Send(new TravelCompleteMessage());
    }

    /// <summary>Server: moves a player onto one of the current scene's spawn points.</summary>
    public static void SendArrival(NetworkConnectionToClient connection)
    {
        Transform start = connection.identity != null && NetworkManager.singleton != null
            ? NetworkManager.singleton.GetStartPosition()
            : null;
        if (start != null)
            connection.Send(new TravelArrivalMessage { position = start.position, rotation = start.rotation });
    }

    // ---------------------------------------------------------------- client

    public void RegisterClientHandlers()
    {
        MapManager.WaitForNetworkSeed = true;
        NetworkClient.ReplaceHandler<TravelDestinationMessage>(OnClientDestination);
        NetworkClient.ReplaceHandler<TravelSeedMessage>(message => MapManager.SetNetworkSeed(message.sceneName, message.seed));
        NetworkClient.ReplaceHandler<TravelBeginMessage>(OnClientBegin);
        NetworkClient.ReplaceHandler<TravelArrivalMessage>(OnClientArrival);
        NetworkClient.ReplaceHandler<TravelCompleteMessage>(_ => OnClientComplete());
        NetworkClient.ReplaceHandler<TravelEnemyRollMessage>(OnClientRolls);
    }

    private void OnClientRolls(TravelEnemyRollMessage message)
    {
        clientRolls.Clear();
        int count = Mathf.Min(message.scenes?.Length ?? 0, message.enemies?.Length ?? 0);
        for (int index = 0; index < count; index++)
        {
            if (!string.IsNullOrEmpty(message.scenes[index]))
                clientRolls[message.scenes[index]] = message.enemies[index] ?? string.Empty;
        }
        RollsChanged?.Invoke();
    }

    public void ResetClient()
    {
        StopClientRoutine(ref clientFadeRoutine);
        StopClientRoutine(ref clientReadyRoutine);
        SelectedDestination = null;
        IsTraveling = false;
        clientRolls.Clear();
        PlayerControlLock.Release(this);
        PlayerStamina.TravelPaused = false;
        TravelMapScreen.CloseIfOpen();
        if (loadingScreen != null)
        {
            loadingScreen.SetEnemies(null);
            loadingScreen.HideLoading();
            loadingScreen.SetFade(0f);
        }
        PlayerPersistence.ResetLocalPlayerState();
        MapManager.ClearNetworkSeeds();
        if (!NetworkServer.active)
            MapManager.WaitForNetworkSeed = false;
    }

    public void OnClientChangeScene(string sceneName)
    {
        TravelMapScreen.CloseIfOpen();
        IsTraveling = true;
        PlayerControlLock.Acquire(this);
        PlayerStamina.TravelPaused = true;
        // Travellers already faded out; late joiners (or other scene changes) go straight to the loading screen.
        if (clientFadeRoutine == null && EnsureLoadingScreen() != null && !loadingScreen.LoadingVisible)
        {
            loadingScreen.SetEnemies(null);
            loadingScreen.ShowLoading();
            loadingScreen.SetFade(0f);
        }
    }

    public void OnClientSceneChanged()
    {
        StopClientRoutine(ref clientReadyRoutine);
        clientReadyRoutine = StartCoroutine(ReportSceneReady());
    }

    private IEnumerator ReportSceneReady()
    {
        Scene scene = SceneManager.GetActiveScene();
        while (MapManager.Active != null && MapManager.Active.gameObject.scene == scene && !MapManager.Active.IsDone)
            yield return null;

        clientReadyRoutine = null;
        if (NetworkClient.isConnected)
            NetworkClient.Send(new TravelSceneReadyMessage { sceneName = scene.name });
    }

    private void OnClientDestination(TravelDestinationMessage message) =>
        SelectedDestination = string.IsNullOrEmpty(message.sceneName) ? null : message.sceneName;

    private void OnClientBegin(TravelBeginMessage message)
    {
        IsTraveling = true;
        PlayerControlLock.Acquire(this);
        PlayerStamina.TravelPaused = true;
        if (NetworkClient.localPlayer != null && NetworkClient.localPlayer.TryGetComponent(out PlayerStamina stamina))
            stamina.CommitForTravel();
        TravelMapScreen.CloseIfOpen();
        StopClientRoutine(ref clientFadeRoutine);
        if (EnsureLoadingScreen() != null)
            loadingScreen.SetEnemies(FindMiniatures(message.enemyAssetIds));
        clientFadeRoutine = StartCoroutine(FadeIntoLoading(message.fadeDuration));
    }

    private static List<EnemyLoadingMiniature> FindMiniatures(uint[] assetIds)
    {
        var miniatures = new List<EnemyLoadingMiniature>();
        if (assetIds == null)
            return miniatures;
        foreach (uint assetId in assetIds)
        {
            EnemyLoadingMiniature miniature = EnemyLoadingMiniature.Find(assetId);
            if (miniature != null)
                miniatures.Add(miniature);
            else
                Debug.LogWarning($"No EnemyLoadingMiniature on the enemy prefab with asset id {assetId}; it is not shown on the loading screen.");
        }
        return miniatures;
    }

    private IEnumerator FadeIntoLoading(float duration)
    {
        if (EnsureLoadingScreen() != null)
        {
            yield return loadingScreen.FadeTo(1f, duration);
            loadingScreen.ShowLoading();
            yield return loadingScreen.FadeTo(0f, loadingRevealDuration);
        }
        clientFadeRoutine = null;
    }

    private static void OnClientArrival(TravelArrivalMessage message)
    {
        if (NetworkClient.localPlayer != null && NetworkClient.localPlayer.TryGetComponent(out PlayerMovement movement))
            movement.TeleportTo(message.position, message.rotation);
    }

    private void OnClientComplete()
    {
        StopClientRoutine(ref clientFadeRoutine);
        clientFadeRoutine = StartCoroutine(FadeIntoGame());
    }

    private IEnumerator FadeIntoGame()
    {
        if (EnsureLoadingScreen() != null)
        {
            if (loadingScreen.LoadingVisible)
                yield return loadingScreen.FadeTo(1f, fadeDuration);
            loadingScreen.HideLoading();
        }

        PlayerControlLock.Release(this);
        PlayerStamina.TravelPaused = false;
        IsTraveling = false;
        if (loadingScreen != null)
            yield return loadingScreen.FadeTo(0f, fadeDuration);
        clientFadeRoutine = null;
    }

    private void StopClientRoutine(ref Coroutine routine)
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
    }
}
