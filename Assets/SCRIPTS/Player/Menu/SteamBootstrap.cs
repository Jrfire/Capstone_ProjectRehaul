using System;
using Steamworks;
using UnityEngine;

/// <summary>
/// Initializes Steam in the main menu (before any NetworkManager exists) so the menu can open the Steam friends list
/// and receive "Join Game" / lobby invites, which load the game scene and join through <see cref="MenuLaunch"/>.
/// Persistent; once the game scene's <see cref="SteamSession"/> runs it uses this Steam runtime and pumps callbacks
/// itself. Steam shuts down when the application quits.
/// </summary>
[DefaultExecutionOrder(-2000)]
[DisallowMultipleComponent]
public sealed class SteamBootstrap : MonoBehaviour
{
    private static SteamBootstrap instance;
    private Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
    private Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequested;

    /// <summary>True while Steam was initialized by the bootstrap (SteamSession then skips its own init/shutdown).</summary>
    public static bool SteamReady { get; private set; }
    public static string FailureMessage { get; private set; }
    /// <summary>Tests set this so loading the menu scene never initializes Steam.</summary>
    public static bool SkipSteamInitialization { get; set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        if (SkipSteamInitialization || (SteamSession.Instance != null && SteamSession.Instance.Initialized))
            return;
        try
        {
            ESteamAPIInitResult result = SteamAPI.InitEx(out string error);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                FailureMessage = $"Steam initialization failed ({result}): {error}. Start Steam and check steam_appid.txt.";
                Debug.LogWarning(FailureMessage, this);
                return;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException ||
                                          exception is BadImageFormatException)
        {
            FailureMessage = "Steam native library problem: " + exception.Message;
            Debug.LogWarning(FailureMessage, this);
            return;
        }
        SteamReady = true;
        lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
        richPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoinRequested);
    }

    private void Start()
    {
        if (!SteamReady || !SteamSession.ConsumeLaunchArguments(out ulong lobby, out string hostId))
            return;
        // Launched by a Steam invite: go straight into the game and join.
        if (lobby != 0)
            MenuLaunch.JoinLobby(lobby);
        else if (hostId != null)
            MenuLaunch.JoinHost(hostId);
    }

    private void Update()
    {
        // The game scene's SteamSession pumps callbacks while it exists.
        if (SteamReady && SteamSession.Instance == null)
            SteamAPI.RunCallbacks();
    }

    /// <summary>Opens the Steam overlay's friends list (Join: right-click a friend > Join Game).</summary>
    public static bool OpenFriendsList()
    {
        if (!SteamReady)
        {
            Debug.LogWarning("Steam is not running; the friends list cannot open. " + FailureMessage);
            return false;
        }
        if (!SteamUtils.IsOverlayEnabled())
        {
            Debug.LogWarning("Steam Overlay is unavailable. Enable it in Steam and test with a standalone build.");
            return false;
        }
        SteamFriends.ActivateGameOverlay("Friends");
        return true;
    }

    private static bool InMenu => SteamSession.Instance == null;

    private void OnLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        if (InMenu && SteamSession.IsValidLobbyId(callback.m_steamIDLobby.m_SteamID))
            MenuLaunch.JoinLobby(callback.m_steamIDLobby.m_SteamID);
    }

    private void OnRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback)
    {
        if (InMenu && SteamSession.TryParseConnectString(callback.m_rgchConnect, out string hostId))
            MenuLaunch.JoinHost(hostId);
    }

    private void OnApplicationQuit()
    {
        // While the game scene's SteamSession exists it stops networking first and then shuts Steam down.
        if (!SteamReady || SteamSession.Instance != null)
            return;
        MarkShutdown();
        SteamAPI.Shutdown();
    }

    /// <summary>Called when Steam has been shut down (by this bootstrap or SteamSession at quit).</summary>
    internal static void MarkShutdown() => SteamReady = false;

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
