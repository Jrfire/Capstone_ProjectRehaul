using Mirror;

public struct SteamLobbyMessage : NetworkMessage
{
    public ulong lobbyId;
}

public sealed class SteamNetworkManager : NetworkManager
{
    private SteamSession Session => TryGetComponent(out SteamSession session) ? session : null;
    private TravelDirector Travel => TryGetComponent(out TravelDirector director) && director.enabled ? director : null;
    private RoundClock Clock => TryGetComponent(out RoundClock clock) && clock.enabled ? clock : null;

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
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        EntranceObstacleNetwork.RegisterClientHandlers();
        NetworkClient.RegisterHandler<SteamLobbyMessage>(message =>
            Session?.JoinSessionLobby(message.lobbyId));
        Travel?.RegisterClientHandlers();
        Clock?.RegisterClientHandlers();
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        EntranceObstacleNetwork.RequestSnapshot();
    }

    public override void OnServerConnect(NetworkConnectionToClient connection)
    {
        base.OnServerConnect(connection);
        ulong lobbyId = Session != null ? Session.LobbyId : 0;
        if (lobbyId != 0)
            connection.Send(new SteamLobbyMessage { lobbyId = lobbyId });
        Travel?.OnServerConnect(connection);
        Clock?.OnServerConnect(connection);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        Travel?.OnServerDisconnect(connection);
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
        Session?.LeaveSessionLobby();
        base.OnStopHost();
    }

    public override void OnStopServer()
    {
        Travel?.ResetServer();
        Clock?.ResetServer();
        base.OnStopServer();
    }

    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<SteamLobbyMessage>();
        EntranceObstacleNetwork.UnregisterClientHandlers();
        Session?.LeaveSessionLobby();
        Travel?.ResetClient();
        Clock?.ResetClient();
        base.OnStopClient();
    }
}
