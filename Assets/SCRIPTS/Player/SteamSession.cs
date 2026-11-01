using System;
using System.Globalization;
using Mirror;
using Mirror.FizzySteam;
using Steamworks;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager), typeof(NetworkManagerHUD), typeof(FizzySteamworks))]
public sealed class SteamSession : MonoBehaviour
{
    private NetworkManager manager;
    private Callback<GameRichPresenceJoinRequested_t> joinRequested;
    private Callback<GameOverlayActivated_t> overlayActivated;
    private string failureMessage;

    public static SteamSession Instance { get; private set; }
    public bool Initialized { get; private set; }
    public bool OverlayActive { get; private set; }
    public string LocalSteamId { get; private set; }

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
            SteamAPI.RunCallbacks();
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

        SteamFriends.ActivateGameOverlayInviteDialogConnectString("+connect " + hostId);
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
            SteamAPI.Shutdown();
            Initialized = false;
        }

        OverlayActive = false;
        Instance = null;
    }
}
