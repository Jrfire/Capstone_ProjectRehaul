using Mirror;

public struct SteamLobbyMessage : NetworkMessage
{
    public ulong lobbyId;
}

public sealed class SteamNetworkManager : NetworkManager
{
    public override void OnStartHost()
    {
        base.OnStartHost();
        GetComponent<SteamSession>().CreateSessionLobby();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        NetworkClient.RegisterHandler<SteamLobbyMessage>(message =>
            GetComponent<SteamSession>().JoinSessionLobby(message.lobbyId));
    }

    public override void OnServerConnect(NetworkConnectionToClient connection)
    {
        base.OnServerConnect(connection);
        ulong lobbyId = GetComponent<SteamSession>().LobbyId;
        if (lobbyId != 0)
            connection.Send(new SteamLobbyMessage { lobbyId = lobbyId });
    }

    public override void OnStopHost()
    {
        GetComponent<SteamSession>().LeaveSessionLobby();
        base.OnStopHost();
    }

    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<SteamLobbyMessage>();
        GetComponent<SteamSession>().LeaveSessionLobby();
        base.OnStopClient();
    }
}
