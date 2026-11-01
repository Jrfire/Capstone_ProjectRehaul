using System;
using System.Globalization;
using Mirror;
using Mirror.FizzySteam;
using Steamworks;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SteamNetworkManager), typeof(NetworkManagerHUD), typeof(FizzySteamworks))]
public sealed class SteamSession : MonoBehaviour
{
    public const string LobbyProjectKey = "capstone_project";
    public const string LobbyProjectValue = "rehaul_v1";
    public const string LobbyHostKey = "host_steam_id";

    private NetworkManager manager;
    private Callback<GameRichPresenceJoinRequested_t> joinRequested;
    private Callback<GameOverlayActivated_t> overlayActivated;
    private Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
    private CallResult<LobbyCreated_t> lobbyCreated;
    private CallResult<LobbyEnter_t> lobbyEntered;
    private bool creatingLobby;
    private ulong joiningLobby;
    private bool joinNetworkAfterLobby;
    private int publishedPlayerCount = -1;
    private string failureMessage;

    public static SteamSession Instance { get; private set; }
    public bool Initialized { get; private set; }
    public bool OverlayActive { get; private set; }
    public string LocalSteamId { get; private set; }
    public ulong LobbyId { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Ignoring a duplicate SteamSession; only one Steam runtime may be active.", this);
            enabled = false;
            return;
        }

        Instance = this;
        manager = GetComponent<NetworkManager>();
        try
        {
            ESteamAPIInitResult result = SteamAPI.InitEx(out string error);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                FailInitialization($"Steam initialization failed ({result}): {error}. Start Steam and check steam_appid.txt.");
                return;
            }

            Initialized = true;
            LocalSteamId = SteamUser.GetSteamID().m_SteamID.ToString(CultureInfo.InvariantCulture);
            manager.networkAddress = LocalSteamId;
            joinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnJoinRequested);
            overlayActivated = Callback<GameOverlayActivated_t>.Create(
                callback => OverlayActive = callback.m_bActive != 0);
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(
                callback => AcceptLobbyInvite(callback.m_steamIDLobby.m_SteamID));
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
        }
        catch (DllNotFoundException exception)
        {
            FailInitialization($"Steam native library is missing: {exception.Message}");
        }
        catch (EntryPointNotFoundException exception)
        {
            FailInitialization($"Steam native library does not match Steamworks.NET: {exception.Message}");
        }
        catch (BadImageFormatException exception)
        {
            FailInitialization($"Steam native library has the wrong architecture: {exception.Message}");
        }
    }

    private void Start()
    {
        if (!Initialized)
            return;

        SteamApps.GetLaunchCommandLine(out string launchArguments, 1024);
        string[] arguments = string.IsNullOrWhiteSpace(launchArguments)
            ? Environment.GetCommandLineArgs()
            : launchArguments.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        int lobbyIndex = Array.IndexOf(arguments, "+connect_lobby");
        if (lobbyIndex >= 0)
        {
            if (lobbyIndex + 1 < arguments.Length &&
                ulong.TryParse(arguments[lobbyIndex + 1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) &&
                IsValidLobbyId(id))
                AcceptLobbyInvite(id);
            else
                Debug.LogError("Steam launch invite has an invalid +connect_lobby ID.", this);
            return;
        }
        int connectIndex = Array.IndexOf(arguments, "+connect");
        if (connectIndex < 0)
            return;
        if (connectIndex + 1 >= arguments.Length)
        {
            Debug.LogError("Steam launch invite has a +connect flag without a host SteamID64.", this);
            return;
        }

        TryJoinHost("+connect " + arguments[connectIndex + 1]);
    }

    private void Update()
    {
        if (Initialized)
        {
            SteamAPI.RunCallbacks();
            if (LobbyId != 0)
            {
                int count = NetworkServer.active ? NetworkServer.connections.Count :
                    SteamMatchmaking.GetNumLobbyMembers(new CSteamID(LobbyId));
                if (publishedPlayerCount != count)
                    PublishSessionPresence();
            }
        }
    }

    public void CreateSessionLobby()
    {
        if (!Initialized)
        {
            Debug.LogError("Cannot create a Steam invite lobby before Steam initialization.", this);
            return;
        }
        if (creatingLobby || LobbyId != 0)
            return;
        creatingLobby = true;
        lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, manager.maxConnections));
    }

    private void OnLobbyCreated(LobbyCreated_t callback, bool ioFailure)
    {
        creatingLobby = false;
        if (ioFailure || callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError($"Steam invite lobby creation failed: {callback.m_eResult} (I/O failure: {ioFailure}).", this);
            return;
        }
        var lobby = new CSteamID(callback.m_ulSteamIDLobby);
        if (!NetworkServer.active)
        {
            SteamMatchmaking.LeaveLobby(lobby);
            return;
        }
        LobbyId = lobby.m_SteamID;
        if (!SteamMatchmaking.SetLobbyData(lobby, LobbyProjectKey, LobbyProjectValue) ||
            !SteamMatchmaking.SetLobbyData(lobby, LobbyHostKey, LocalSteamId))
        {
            Debug.LogError("Steam could not publish the invite lobby's project/host metadata.", this);
            LeaveSessionLobby();
            return;
        }
        PublishSessionPresence();
        NetworkServer.SendToAll(new SteamLobbyMessage { lobbyId = LobbyId });
    }

    public static bool IsValidLobbyId(ulong value)
    {
        var id = new CSteamID(value);
        return id.IsValid() && id.GetEAccountType() == EAccountType.k_EAccountTypeChat && id.IsLobby();
    }

    public static bool TryParseLobbyHost(string project, string host, ulong owner, out string hostId)
    {
        hostId = null;
        if (project != LobbyProjectValue || !TryParseHostId(host, out string parsedHost) ||
            owner.ToString(CultureInfo.InvariantCulture) != parsedHost)
            return false;
        hostId = parsedHost;
        return true;
    }

    public void AcceptLobbyInvite(ulong id)
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.LogWarning("Disconnect your current session before accepting a Steam invite.", this);
            return;
        }
        JoinLobby(id, true);
    }

    public void JoinSessionLobby(ulong id)
    {
        if (id == LobbyId)
            return;
        JoinLobby(id, false);
    }

    private void JoinLobby(ulong id, bool connectAfterEntry)
    {
        if (!Initialized || !IsValidLobbyId(id))
        {
            Debug.LogError("Cannot join a Steam lobby: Steam is not initialized or the lobby ID is invalid.", this);
            return;
        }
        if (joiningLobby != 0)
        {
            Debug.LogWarning("A Steam lobby join is already in progress.", this);
            return;
        }
        LeaveSessionLobby();
        joiningLobby = id;
        joinNetworkAfterLobby = connectAfterEntry;
        lobbyEntered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
    }

    private void OnLobbyEntered(LobbyEnter_t callback, bool ioFailure)
    {
        ulong expectedLobby = joiningLobby;
        bool connectAfterEntry = joinNetworkAfterLobby;
        joiningLobby = 0;
        joinNetworkAfterLobby = false;
        var lobby = new CSteamID(callback.m_ulSteamIDLobby);
        if (ioFailure || callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            Debug.LogError($"Steam lobby entry failed: {(EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse} (I/O failure: {ioFailure}).", this);
            return;
        }
        string host = SteamMatchmaking.GetLobbyData(lobby, LobbyHostKey);
        if (expectedLobby != lobby.m_SteamID ||
            !TryParseLobbyHost(SteamMatchmaking.GetLobbyData(lobby, LobbyProjectKey), host,
                SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID, out string hostId) ||
            (!connectAfterEntry && !NetworkClient.active))
        {
            Debug.LogWarning("Rejected a stale lobby or a lobby without this project's active host.", this);
            SteamMatchmaking.LeaveLobby(lobby);
            return;
        }
        if (!connectAfterEntry && manager.networkAddress != hostId)
        {
            Debug.LogError("The Steam lobby host does not match the connected network session.", this);
            SteamMatchmaking.LeaveLobby(lobby);
            return;
        }
        LobbyId = lobby.m_SteamID;
        if (connectAfterEntry && !TryJoinHost("+connect " + hostId))
            LeaveSessionLobby();
        else
            PublishSessionPresence();
    }

    private void PublishSessionPresence()
    {
        if (LobbyId == 0)
            return;
        string host = NetworkServer.active ? LocalSteamId : manager.networkAddress;
        int count = NetworkServer.active ? NetworkServer.connections.Count :
            SteamMatchmaking.GetNumLobbyMembers(new CSteamID(LobbyId));
        if (!SteamFriends.SetRichPresence("connect", "+connect " + host) ||
            !SteamFriends.SetRichPresence("steam_player_group", LobbyId.ToString(CultureInfo.InvariantCulture)) ||
            !SteamFriends.SetRichPresence("steam_player_group_size", count.ToString(CultureInfo.InvariantCulture)))
            Debug.LogError("Steam could not publish joinable session presence.", this);
        if (NetworkServer.active &&
            !SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId), count < manager.maxConnections))
            Debug.LogError("Steam could not update lobby joinability.", this);
        publishedPlayerCount = count;
    }

    public void LeaveSessionLobby()
    {
        if (!Initialized)
            return;
        if (LobbyId != 0)
        {
            var lobby = new CSteamID(LobbyId);
            if (SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID())
            {
                if (!SteamMatchmaking.SetLobbyJoinable(lobby, false))
                    Debug.LogWarning("Steam could not close the departing host's lobby.", this);
                if (!SteamMatchmaking.DeleteLobbyData(lobby, LobbyHostKey))
                    Debug.LogWarning("Steam could not remove the departing host's lobby address.", this);
            }
            SteamMatchmaking.LeaveLobby(lobby);
        }
        // Keep pending API results alive so late successes can leave rather than leak a lobby.
        joinNetworkAfterLobby = false;
        LobbyId = 0;
        publishedPlayerCount = -1;
        SteamFriends.ClearRichPresence();
    }

    public bool OpenInviteDialog()
    {
        if (!Initialized || !NetworkClient.isConnected)
        {
            Debug.LogWarning("Steam invites require Steam initialization and an active host or client connection.", this);
            return false;
        }
        if (!(manager.transport is FizzySteamworks))
        {
            Debug.LogError("Steam invites require FizzySteamworks as NetworkManager's transport.", this);
            return false;
        }

        string host = NetworkServer.active ? LocalSteamId : manager.networkAddress;
        if (!TryParseHostId(host, out string hostId))
        {
            Debug.LogError("Cannot invite friends: the current host address is not a valid SteamID64.", this);
            return false;
        }
        if (!SteamUtils.IsOverlayEnabled())
        {
            Debug.LogWarning("Steam Overlay is unavailable. Enable it in Steam and test with a standalone build.", this);
            return false;
        }

        if (LobbyId == 0)
        {
            if (NetworkServer.active && !creatingLobby)
                CreateSessionLobby();
            Debug.LogWarning("The Steam invite lobby is not ready. Wait for lobby creation/entry, then try E again.", this);
            return false;
        }
        SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
        return true;
    }

    public bool TryJoinHost(string connectString)
    {
        if (!Initialized)
        {
            Debug.LogError("Cannot accept a Steam invite before Steam initialization.", this);
            return false;
        }
        if (!TryParseConnectString(connectString, out string hostId))
        {
            Debug.LogWarning("Rejected a Steam invite with an invalid +connect SteamID64.", this);
            return false;
        }
        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.LogWarning("Disconnect your current session before accepting a Steam invite.", this);
            return false;
        }
        if (hostId == LocalSteamId)
        {
            Debug.LogWarning("Cannot connect to your own Steam account. Use a different account for the guest.", this);
            return false;
        }
        if (!(manager.transport is FizzySteamworks))
        {
            Debug.LogError("Cannot accept a Steam invite without the FizzySteamworks transport.", this);
            return false;
        }

        manager.networkAddress = hostId;
        manager.StartClient();
        return true;
    }

    public static bool TryParseConnectString(string connectString, out string hostId)
    {
        hostId = null;
        if (string.IsNullOrWhiteSpace(connectString))
            return false;
        string[] parts = connectString.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == "+connect" && TryParseHostId(parts[1], out hostId);
    }

    public static bool TryParseHostId(string address, out string hostId)
    {
        hostId = null;
        if (!ulong.TryParse(address, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
            return false;
        var id = new CSteamID(value);
        if (!id.IsValid() || id.GetEAccountType() != EAccountType.k_EAccountTypeIndividual)
            return false;
        hostId = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private void OnJoinRequested(GameRichPresenceJoinRequested_t callback)
    {
        TryJoinHost(callback.m_rgchConnect);
    }

    private void FailInitialization(string message)
    {
        if (Initialized)
        {
            joinRequested?.Dispose();
            overlayActivated?.Dispose();
            lobbyJoinRequested?.Dispose();
            lobbyCreated?.Dispose();
            lobbyEntered?.Dispose();
            SteamAPI.Shutdown();
            Initialized = false;
        }

        failureMessage = message;
        Debug.LogError(message, this);
        GetComponent<NetworkManagerHUD>().enabled = false;
        GetComponent<FizzySteamworks>().enabled = false;
        manager.enabled = false;
    }

    private void OnGUI()
    {
        if (Instance != this)
            return;
        GUI.Box(new Rect(10, 5, Mathf.Min(Screen.width - 20, 900), failureMessage == null ? 30 : 80),
            failureMessage ?? $"Your SteamID64: {LocalSteamId} | Client address must be the host's SteamID64.");
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (Instance != this)
            return;
        if (Initialized)
        {
            if (manager != null)
            {
                if (manager.mode == NetworkManagerMode.Host)
                    manager.StopHost();
                else
                {
                    if (NetworkClient.active)
                        manager.StopClient();
                    if (NetworkServer.active)
                        manager.StopServer();
                }
            }

            joinRequested?.Dispose();
            overlayActivated?.Dispose();
            LeaveSessionLobby();
            lobbyJoinRequested?.Dispose();
            lobbyCreated?.Dispose();
            lobbyEntered?.Dispose();
            SteamAPI.Shutdown();
            Initialized = false;
        }

        OverlayActive = false;
        Instance = null;
    }
}
