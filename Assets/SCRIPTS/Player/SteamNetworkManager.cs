using System.Collections;
using Mirror;
using UnityEngine;

public struct SteamLobbyMessage : NetworkMessage
{
    public ulong lobbyId;
}

/// <summary>Server to one client: the host kicked you (the client leaves and shows a notice on the main menu).</summary>
public struct KickMessage : NetworkMessage
{
}

public sealed class SteamNetworkManager : NetworkManager
{
    [Header("Kicking")]
    [SerializeField, Min(0f), Tooltip("Real seconds a kicked guest gets to leave by itself before the server drops it.")]
    private float kickDisconnectDelay = 0.5f;

    private bool clientWasConnected;
    private SteamSession Session => TryGetComponent(out SteamSession session) ? session : null;
    private TravelDirector Travel => TryGetComponent(out TravelDirector director) && director.enabled ? director : null;
    private RoundClock Clock => TryGetComponent(out RoundClock clock) && clock.enabled ? clock : null;
    private AgimatBank Agimats => TryGetComponent(out AgimatBank bank) && bank.enabled ? bank : null;
    private WhiteLadyDirector WhiteLady => TryGetComponent(out WhiteLadyDirector director) && director.enabled ? director : null;
    private GameSaveManager Saves => TryGetComponent(out GameSaveManager saves) && saves.enabled ? saves : null;

    public override void OnStartHost()
    {
        base.OnStartHost();
        Session?.CreateSessionLobby();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        EntranceObstacleNetwork.RegisterServerHandlers();
        Travel?.RegisterServerHandlers();
        Clock?.RegisterServerHandlers();
        Agimats?.RegisterServerHandlers();
        WhiteLady?.RegisterServerHandlers();
        Saves?.OnServerStarted();
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient connection)
    {
        Transform start = GetStartPosition();
        GameObject player = start != null
            ? Instantiate(playerPrefab, start.position, start.rotation)
            : Instantiate(playerPrefab);
        player.name = $"{playerPrefab.name} [connId={connection.connectionId}]";
        Saves?.ApplyBeforeSpawn(connection, player);
        NetworkServer.AddPlayerForConnection(connection, player);
        Saves?.ApplyAfterSpawn(connection, player);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        clientWasConnected = false;
        EntranceObstacleNetwork.RegisterClientHandlers();
        NetworkClient.RegisterHandler<SteamLobbyMessage>(message =>
            Session?.JoinSessionLobby(message.lobbyId));
        NetworkClient.RegisterHandler<KickMessage>(OnClientKicked);
        Travel?.RegisterClientHandlers();
        Clock?.RegisterClientHandlers();
        Agimats?.RegisterClientHandlers();
        WhiteLady?.RegisterClientHandlers();
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        clientWasConnected = true;
        EntranceObstacleNetwork.RequestSnapshot();
    }

    // ---------------------------------------------------------------- kicking

    /// <summary>
    /// Server: removes a guest from the game. The guest is told first (so its menu shows
    /// <see cref="MenuLaunch.KickedNotice"/>) and dropped after <see cref="kickDisconnectDelay"/> if it is still here.
    /// The host's own connection cannot be kicked.
    /// </summary>
    public bool ServerKick(NetworkConnectionToClient connection)
    {
        if (!NetworkServer.active || connection == null || connection is LocalConnectionToClient ||
            connection == NetworkServer.localConnection)
            return false;
        connection.Send(new KickMessage());
        StartCoroutine(DisconnectAfterDelay(connection));
        return true;
    }

    private IEnumerator DisconnectAfterDelay(NetworkConnectionToClient connection)
    {
        yield return new WaitForSecondsRealtime(kickDisconnectDelay);
        if (NetworkServer.active && NetworkServer.connections.ContainsKey(connection.connectionId))
            connection.Disconnect();
    }

    private void OnClientKicked(KickMessage message)
    {
        if (NetworkServer.active)
            return;
        MenuLaunch.SetNotice(MenuLaunch.KickedNotice);
        StopClient();
    }

    public override void OnServerConnect(NetworkConnectionToClient connection)
    {
        base.OnServerConnect(connection);
        ulong lobbyId = Session != null ? Session.LobbyId : 0;
        if (lobbyId != 0)
            connection.Send(new SteamLobbyMessage { lobbyId = lobbyId });
        Travel?.OnServerConnect(connection);
        Clock?.OnServerConnect(connection);
        Agimats?.OnServerConnect(connection);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        Saves?.OnServerDisconnect(connection);
        Travel?.OnServerDisconnect(connection);
        Agimats?.OnServerDisconnect(connection);
        base.OnServerDisconnect(connection);
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        Travel?.OnServerSceneChanged(sceneName);
    }

    public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
    {
        base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);
        if (sceneOperation == SceneOperation.Normal)
            Travel?.OnClientChangeScene(newSceneName);
    }

    public override void OnClientSceneChanged()
    {
        // Persistent players are respawned by the server once this client is ready; only new clients add a player.
        if (PlayerPersistence.LocalPlayerSpawned)
        {
            if (NetworkClient.connection.isAuthenticated && !NetworkClient.ready)
                NetworkClient.Ready();
        }
        else
        {
            base.OnClientSceneChanged();
        }
        Travel?.OnClientSceneChanged();
        EntranceObstacleNetwork.RequestSnapshot();
    }

    public override void OnStopHost()
    {
        Saves?.OnServerStopping();
        Session?.LeaveSessionLobby();
        base.OnStopHost();
    }

    public override void OnStopServer()
    {
        Saves?.OnServerStopping();
        Saves?.ResetServer();
        Travel?.ResetServer();
        Clock?.ResetServer();
        Agimats?.ResetServer();
        WhiteLady?.ResetServer();
        base.OnStopServer();
    }

    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<SteamLobbyMessage>();
        NetworkClient.UnregisterHandler<KickMessage>();
        EntranceObstacleNetwork.UnregisterClientHandlers();
        Session?.LeaveSessionLobby();
        Travel?.ResetClient();
        Clock?.ResetClient();
        Agimats?.ResetClient();
        WhiteLady?.ResetClient();
        base.OnStopClient();
        bool quitting = SteamSession.ApplicationQuitting || MenuLaunch.Quitting || MenuLaunch.ReturningToMenu;
        // A guest whose session ended without choosing to leave was dropped by the host (or never got in).
        if (!quitting && !NetworkServer.active && !MenuLaunch.LeavingDeliberately && !MenuLaunch.HasNotice)
        {
            if (clientWasConnected)
                MenuLaunch.SetNotice(MenuLaunch.HostDisconnectedNotice);
            else if (MenuLaunch.FromMenu)
                MenuLaunch.SetNotice(MenuLaunch.ConnectFailedNotice);
        }
        clientWasConnected = false;
        // Sessions started from the main menu, deliberate leaves and kicks/disconnects return to the menu.
        if (!quitting && (MenuLaunch.FromMenu || MenuLaunch.LeavingDeliberately || MenuLaunch.HasNotice))
            MenuLaunch.ReturnToMenu();
    }
}
