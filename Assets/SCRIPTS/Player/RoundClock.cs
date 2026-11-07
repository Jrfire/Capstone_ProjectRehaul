using System;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum RoundPhase : byte
{
    Running = 0,
    /// <summary>The jeepney home is about to leave (midnight or a player boarded); everyone must board in time.</summary>
    ReturnCountdown = 1,
    GameOver = 2,
}

/// <summary>Host-authoritative round clock state, sent a few times per second and on every change.</summary>
public struct RoundClockMessage : NetworkMessage
{
    public float elapsed;
    public bool running;
    public RoundPhase phase;
    public float countdown;
    public int round;
}

/// <summary>
/// Global round timer that lives on the persistent NetworkManager object. Players have until midnight (12 clock
/// hours, 45 real minutes by default, starting at noon) to meet the agimat quota. The clock runs on every map except
/// the base and stops during travel; leaving base for a destination deducts its <see cref="TravelTimeManager"/> cost.
/// At midnight away from base, or when a player boards the jeepney home, a countdown starts: each player must board
/// (E on the jeepney back); anyone left behind dies when the jeepney leaves (health 0, inventory-loss hook) and
/// respawns at base with one heart. Players who die in any other way also respawn at base; if everyone away from base
/// is dead, the jeepney brings them all home after <see cref="allDeadReturnSeconds"/>. With an <see cref="AgimatBank"/>,
/// sacrificing the quota at the Albularyo starts the next round; being at base with no affordable trip left and too few
/// agimats ends the game. Without a bank, arriving at base after midnight resets the clock when <see cref="QuotaMet"/>,
/// otherwise the game is over and everything resets.
/// </summary>
[DisallowMultipleComponent]
public sealed class RoundClock : MonoBehaviour
{
    [SerializeField, Min(0.05f), Tooltip("Real minutes from noon to midnight.")]
    private float roundMinutes = 45f;
    [SerializeField, Min(1), Tooltip("Clock hours in a round (12 = noon to midnight).")]
    private int clockHours = 12;
    [SerializeField, Min(0), Tooltip("The HUD clock warns (blinks and pulses) once this many hours or fewer remain.")]
    private int lowTimeHours = 3;
    [SerializeField, Tooltip("The base scene, where the clock is paused and the quota is checked.")]
    private string baseSceneName = "SampleScene";
    [SerializeField, Min(1f), Tooltip("Seconds everyone has to board the jeepney home.")]
    private float returnCountdownSeconds = 30f;
    [SerializeField, Range(0, PlayerHealth.MaxHealth), Tooltip("Health that dead players (left behind or killed) respawn with at base (2 = one heart).")]
    private int leftBehindHealth = 2;
    [SerializeField, Min(0f), Tooltip("Seconds after every player away from base has died before they are all returned to base (lets the death screen play).")]
    private float allDeadReturnSeconds = 4f;
    [SerializeField, Min(0.5f), Tooltip("Seconds the game over screen stays up before the game resets.")]
    private float gameOverSeconds = 5f;
    [SerializeField, Min(0.05f), Tooltip("Seconds between clock updates sent to clients.")]
    private float syncInterval = 0.25f;

    private float serverElapsed;
    private RoundPhase serverPhase;
    private float serverCountdown;
    private float serverGameOverRemaining;
    private int serverRound = 1;
    private float nextSync;
    private float serverAllDeadTime;
    private float nextStuckCheck;
    private TravelDirector subscribedDirector;
    private readonly System.Collections.Generic.HashSet<PlayerPersistence> serverAwaitingRespawn =
        new System.Collections.Generic.HashSet<PlayerPersistence>();

    private RoundClockMessage clientState = new RoundClockMessage { round = 1 };
    private float clientReceivedAt;
    private bool clientHasState;

    public static RoundClock Instance { get; private set; }

    /// <summary>Server: quota check made on arrival at base after midnight. Unassigned = met (legacy, without an <see cref="AgimatBank"/>).</summary>
    public Func<bool> QuotaMet { get; set; }
    /// <summary>
    /// Server: assigned by <see cref="AgimatBank"/>, which completes rounds itself when the quota is sacrificed
    /// (<see cref="ServerCompleteRound"/>). While assigned, arriving at base after midnight waits for the sacrifice, and
    /// the game is over as soon as players are at base with no affordable trip left while this returns false.
    /// </summary>
    public Func<bool> QuotaReachable { get; set; }
    /// <summary>Server: a player died by being left behind when the jeepney left (hook for inventory loss).</summary>
    public event Action<PlayerPersistence> ServerPlayerLeftBehind;

    /// <summary>Server: true while a dead player (left behind or killed) waits to respawn at base.</summary>
    public bool IsAwaitingRespawn(PlayerPersistence player) =>
        player != null && (serverAwaitingRespawn.Contains(player) || (player.TryGetComponent(out PlayerHealth health) && health.IsDead));
    /// <summary>Server: every player away from base died and the jeepney is bringing them home.</summary>
    public event Action ServerAllPlayersDied;
    /// <summary>Server: the quota was met at midnight and a new round started.</summary>
    public event Action<int> ServerRoundCompleted;
    /// <summary>Server: the game was lost and everything was reset.</summary>
    public event Action ServerGameReset;

    public string BaseSceneName => baseSceneName;
    public int ClockHours => clockHours;
    public int LowTimeHours => lowTimeHours;
    public float RoundSeconds => roundMinutes * 60f;
    public float HourSeconds => RoundSeconds / clockHours;
    public float ReturnCountdownSeconds => returnCountdownSeconds;
    public float GameOverSeconds => gameOverSeconds;
    public bool InBase => SceneManager.GetActiveScene().name == baseSceneName;

    // ---------------------------------------------------------------- shared view (server values on the host)

    private bool UseServerState => NetworkServer.active || !clientHasState;

    /// <summary>Seconds since noon, extrapolated between updates on remote clients.</summary>
    public float Elapsed
    {
        get
        {
            if (UseServerState)
                return serverElapsed;
            float elapsed = clientState.elapsed;
            if (clientState.running)
                elapsed += Time.unscaledTime - clientReceivedAt;
            return Mathf.Clamp(elapsed, 0f, RoundSeconds);
        }
    }

    public RoundPhase Phase => UseServerState ? serverPhase : clientState.phase;
    public int Round => UseServerState ? serverRound : clientState.round;
    public bool IsRunning => UseServerState ? ServerShouldRun : clientState.running;

    public float CountdownRemaining
    {
        get
        {
            if (UseServerState)
                return serverPhase == RoundPhase.ReturnCountdown ? serverCountdown : 0f;
            if (clientState.phase != RoundPhase.ReturnCountdown)
                return 0f;
            return Mathf.Max(0f, clientState.countdown - (Time.unscaledTime - clientReceivedAt));
        }
    }

    public float RemainingSeconds => Mathf.Max(0f, RoundSeconds - Elapsed);
    public int ElapsedHours => ElapsedHoursFor(Elapsed, HourSeconds, clockHours);
    public int RemainingHours => clockHours - ElapsedHours;
    public bool IsMidnight => Elapsed >= RoundSeconds - 0.0001f;
    public bool IsLowTime => IsLowTimeFor(ElapsedHours, clockHours, lowTimeHours);
    /// <summary>Sprite index on the clock sheet: 0 = 12 o'clock (noon and midnight), 1..11 = that hour.</summary>
    public int ClockFrame => FrameFor(Elapsed, HourSeconds, clockHours);

    public static int ElapsedHoursFor(float elapsedSeconds, float hourSeconds, int hours) =>
        hourSeconds <= 0f ? hours : Mathf.Clamp(Mathf.FloorToInt((elapsedSeconds + 0.001f) / hourSeconds), 0, hours);

    public static int FrameFor(float elapsedSeconds, float hourSeconds, int hours) =>
        ElapsedHoursFor(elapsedSeconds, hourSeconds, hours) % Mathf.Max(1, hours);

    public static bool IsLowTimeFor(int elapsedHours, int hours, int lowHours) => hours - elapsedHours <= lowHours;

    /// <summary>True when the trip's time cost leaves some time before midnight (always true outside base).</summary>
    public bool CanAfford(string sceneName)
    {
        if (!InBase || sceneName == baseSceneName)
            return true;
        return TravelTimeManager.CostHours(sceneName) * HourSeconds < RemainingSeconds - 0.0001f;
    }

    /// <summary>True if the local player has boarded the jeepney home.</summary>
    public static bool LocalPlayerAboard =>
        NetworkClient.localPlayer != null && NetworkClient.localPlayer.TryGetComponent(out PlayerPersistence player) && player.IsAboard;

    /// <summary>Client: whether the local player may board the jeepney home right now.</summary>
    public bool CanBoard =>
        NetworkClient.isConnected && NetworkClient.localPlayer != null && !InBase && Phase != RoundPhase.GameOver &&
        !LocalPlayerAboard && (TravelDirector.Instance == null || !TravelDirector.Instance.IsTraveling);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;
        PlayerControlLock.Release(this);
        Unsubscribe();
        Instance = null;
    }

    // ---------------------------------------------------------------- client

    public void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<RoundClockMessage>(OnClientState);
    }

    /// <summary>Client: asks the host to board the jeepney home (only away from base).</summary>
    public void RequestBoard()
    {
        if (CanBoard)
            NetworkClient.Send(new TravelStartRequest());
    }

    private void OnClientState(RoundClockMessage message)
    {
        clientState = message;
        clientReceivedAt = Time.unscaledTime;
        clientHasState = true;
    }

    public void ResetClient()
    {
        clientHasState = false;
        clientState = new RoundClockMessage { round = 1 };
        PlayerControlLock.Release(this);
    }

    private void Update()
    {
        if (NetworkServer.active)
            ServerTick(Time.deltaTime);

        // Game over freezes everyone's controls until the reset.
        if (NetworkClient.active && Phase == RoundPhase.GameOver)
            PlayerControlLock.Acquire(this);
        else
            PlayerControlLock.Release(this);
    }

    // ---------------------------------------------------------------- server

    private bool ServerShouldRun =>
        NetworkServer.active && serverPhase != RoundPhase.GameOver && !InBase && serverElapsed < RoundSeconds &&
        (TravelDirector.Instance == null || !TravelDirector.Instance.ServerTraveling);

    public void RegisterServerHandlers()
    {
        Subscribe();
        nextSync = 0f;
    }

    private void Subscribe()
    {
        TravelDirector director = TravelDirector.Instance;
        if (director == subscribedDirector)
            return;
        Unsubscribe();
        subscribedDirector = director;
        if (director == null)
            return;
        director.ServerTravelStarting += OnServerTravelStarting;
        director.ServerTravelCompleted += OnServerTravelCompleted;
    }

    private void Unsubscribe()
    {
        if (subscribedDirector == null)
            return;
        subscribedDirector.ServerTravelStarting -= OnServerTravelStarting;
        subscribedDirector.ServerTravelCompleted -= OnServerTravelCompleted;
        subscribedDirector = null;
    }

    public void ResetServer()
    {
        Unsubscribe();
        serverElapsed = 0f;
        serverPhase = RoundPhase.Running;
        serverCountdown = 0f;
        serverGameOverRemaining = 0f;
        serverRound = 1;
        serverAllDeadTime = 0f;
        serverAwaitingRespawn.Clear();
    }

    public void OnServerConnect(NetworkConnectionToClient connection) => connection.Send(BuildMessage());

    /// <summary>Server: advances the clock. Public so tests can step it deterministically.</summary>
    public void ServerTick(float deltaTime)
    {
        if (!NetworkServer.active || Instance != this)
            return;
        Subscribe();
        bool traveling = TravelDirector.Instance != null && TravelDirector.Instance.ServerTraveling;

        switch (serverPhase)
        {
            case RoundPhase.GameOver:
                serverGameOverRemaining -= deltaTime;
                if (serverGameOverRemaining <= 0f)
                    ServerResetGame();
                break;

            case RoundPhase.ReturnCountdown:
                AdvanceClock(deltaTime);
                if (traveling)
                    break;
                if (InBase)
                {
                    // Players reached base some other way; nobody needs to board anymore.
                    ClearAboard();
                    SetPhase(RoundPhase.Running);
                    break;
                }
                serverCountdown -= deltaTime;
                if (serverCountdown <= 0f || AllPlayersAboard())
                    ServerDepart();
                break;

            default:
                AdvanceClock(deltaTime);
                if (!traveling && !InBase && serverElapsed >= RoundSeconds)
                    StartReturnCountdown();
                else if (!traveling && InBase && QuotaReachable != null && Time.unscaledTime >= nextStuckCheck)
                {
                    nextStuckCheck = Time.unscaledTime + 0.25f;
                    // Stuck at base: no time to fetch more agimats and not enough to meet the quota.
                    if (!QuotaReachable() && !AnyTripAffordable())
                        ServerGameOver();
                }
                break;
        }

        TickAllDead(deltaTime);

        if (Time.unscaledTime >= nextSync)
            Broadcast();
    }

    private void TickAllDead(float deltaTime)
    {
        bool traveling = TravelDirector.Instance != null && TravelDirector.Instance.ServerTraveling;
        if (serverPhase == RoundPhase.GameOver || traveling || InBase || !AllPlayersDead())
        {
            serverAllDeadTime = 0f;
            return;
        }
        serverAllDeadTime += Mathf.Max(0f, deltaTime);
        if (serverAllDeadTime < allDeadReturnSeconds)
            return;

        serverAllDeadTime = 0f;
        ServerAllPlayersDied?.Invoke();
        SetPhase(RoundPhase.Running);
        ClearAboard();
        if (TravelDirector.Instance == null || !TravelDirector.Instance.ServerBeginTravel(baseSceneName))
        {
            Debug.LogError($"Everyone died but the jeepney could not return to '{baseSceneName}'. Check the base scene name and the Build Profile.", this);
            RespawnDeadPlayers();
        }
    }

    private static bool AllPlayersDead()
    {
        int players = 0;
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity == null || !connection.identity.TryGetComponent(out PlayerHealth health))
                continue;
            if (!health.IsDead)
                return false;
            players++;
        }
        return players > 0;
    }

    private static bool IsDead(PlayerPersistence player) =>
        player.TryGetComponent(out PlayerHealth health) && health.IsDead;

    private void AdvanceClock(float deltaTime)
    {
        if (ServerShouldRun)
            serverElapsed = Mathf.Min(RoundSeconds, serverElapsed + Mathf.Max(0f, deltaTime));
    }

    /// <summary>Server: sets the elapsed time directly (for tests and debugging).</summary>
    public void ServerSetElapsed(float seconds)
    {
        serverElapsed = Mathf.Clamp(seconds, 0f, RoundSeconds);
        Broadcast();
    }

    /// <summary>Server: a player pressed E on the jeepney back away from base.</summary>
    public void ServerBoard(NetworkConnectionToClient connection)
    {
        if (!NetworkServer.active || serverPhase == RoundPhase.GameOver || InBase ||
            (TravelDirector.Instance != null && TravelDirector.Instance.ServerTraveling))
            return;
        PlayerPersistence player = connection?.identity != null ? connection.identity.GetComponent<PlayerPersistence>() : null;
        if (player == null || player.IsAboard || IsDead(player))
            return;

        player.ServerSetAboard(true);
        if (serverPhase == RoundPhase.Running)
            StartReturnCountdown();
        else
            Broadcast();
        if (AllPlayersAboard())
            ServerDepart();
    }

    private void StartReturnCountdown()
    {
        serverCountdown = returnCountdownSeconds;
        SetPhase(RoundPhase.ReturnCountdown);
    }

    /// <summary>True when every living player has boarded (dead players travel along and respawn at base).</summary>
    private static bool AllPlayersAboard()
    {
        int players = 0;
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity == null || !connection.identity.TryGetComponent(out PlayerPersistence player) || IsDead(player))
                continue;
            if (!player.IsAboard)
                return false;
            players++;
        }
        return players > 0;
    }

    private void ServerDepart()
    {
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity == null || !connection.identity.TryGetComponent(out PlayerPersistence player) || player.IsAboard)
                continue;
            if (IsDead(player))
            {
                // Already dead (e.g. killed by a creature): they ride home and respawn, but aren't "left behind" again.
                serverAwaitingRespawn.Add(player);
                continue;
            }
            // Left behind = dead. They travel with the jeepney while dead and respawn at base with one heart.
            if (player.TryGetComponent(out PlayerHealth health))
                health.SetHealth(0);
            serverAwaitingRespawn.Add(player);
            ServerPlayerLeftBehind?.Invoke(player);
        }

        SetPhase(RoundPhase.Running);
        if (TravelDirector.Instance == null || !TravelDirector.Instance.ServerBeginTravel(baseSceneName))
        {
            Debug.LogError($"The jeepney could not return to '{baseSceneName}'. Check the base scene name and the Build Profile.", this);
            ClearAboard();
            RespawnDeadPlayers();
        }
    }

    private void RespawnDeadPlayers()
    {
        foreach (PlayerPersistence player in serverAwaitingRespawn)
        {
            if (player != null && player.TryGetComponent(out PlayerHealth health))
                health.SetHealth(leftBehindHealth);
        }
        serverAwaitingRespawn.Clear();
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity != null && connection.identity.TryGetComponent(out PlayerHealth health) && health.IsDead)
                health.SetHealth(leftBehindHealth);
        }
    }

    private void OnServerTravelStarting(string fromScene, string toScene)
    {
        if (fromScene != baseSceneName || toScene == baseSceneName)
            return;
        float cost = TravelTimeManager.CostHours(toScene) * HourSeconds;
        serverElapsed = Mathf.Min(RoundSeconds, serverElapsed + cost);
        Broadcast();
    }

    private void OnServerTravelCompleted(string sceneName)
    {
        ClearAboard();
        if (sceneName == baseSceneName)
            RespawnDeadPlayers();
        if (sceneName != baseSceneName || serverElapsed < RoundSeconds)
        {
            Broadcast();
            return;
        }

        if (QuotaReachable != null)
        {
            // The round ends by sacrificing at the Albularyo; ServerTick ends the game if that is impossible.
            Broadcast();
            return;
        }

        if (QuotaMet == null || QuotaMet())
        {
            ServerCompleteRound();
        }
        else
        {
            serverGameOverRemaining = gameOverSeconds;
            SetPhase(RoundPhase.GameOver);
        }
    }

    /// <summary>
    /// Server: the quota was met; starts the next round (clock back to noon). Called by <see cref="AgimatBank"/> when
    /// enough agimats are sacrificed, or on arrival at base after midnight without a bank.
    /// </summary>
    public void ServerCompleteRound()
    {
        if (!NetworkServer.active || serverPhase == RoundPhase.GameOver)
            return;
        serverElapsed = 0f;
        serverCountdown = 0f;
        serverRound++;
        SetPhase(RoundPhase.Running);
        ServerRoundCompleted?.Invoke(serverRound);
    }

    /// <summary>
    /// Server: whether any destination listed on a map terminal in this scene can still be reached before midnight.
    /// Without terminals, true until midnight.
    /// </summary>
    public bool AnyTripAffordable()
    {
        bool anyTerminal = false;
        Scene active = SceneManager.GetActiveScene();
        foreach (MapTerminal terminal in FindObjectsByType<MapTerminal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (terminal.gameObject.scene != active)
                continue;
            anyTerminal = true;
            foreach (string destination in terminal.Destinations)
            {
                if (destination != active.name && destination != baseSceneName && TravelDirector.CanLoadScene(destination) &&
                    CanAfford(destination))
                    return true;
            }
        }
        return !anyTerminal && serverElapsed < RoundSeconds;
    }

    /// <summary>Server: shows the game over screen and then resets the game (as when the quota is not met).</summary>
    public void ServerGameOver()
    {
        if (!NetworkServer.active)
            return;
        serverGameOverRemaining = gameOverSeconds;
        SetPhase(RoundPhase.GameOver);
    }

    private void ServerResetGame()
    {
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity == null)
                continue;
            if (connection.identity.TryGetComponent(out PlayerHealth health))
                health.SetHealth(PlayerHealth.MaxHealth);
            if (connection.identity.TryGetComponent(out PlayerStamina stamina))
                stamina.ServerRestore();
            if (connection.identity.TryGetComponent(out PlayerInventory inventory))
                inventory.ServerClear();
            if (connection.identity.TryGetComponent(out PlayerPersistence player))
                player.ServerSetAboard(false);
            TravelDirector.SendArrival(connection);
        }

        serverAwaitingRespawn.Clear();
        serverElapsed = 0f;
        serverRound = 1;
        serverCountdown = 0f;
        SetPhase(RoundPhase.Running);
        ServerGameReset?.Invoke();
    }

    private static void ClearAboard()
    {
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity != null && connection.identity.TryGetComponent(out PlayerPersistence player) && player.IsAboard)
                player.ServerSetAboard(false);
        }
    }

    private void SetPhase(RoundPhase phase)
    {
        serverPhase = phase;
        Broadcast();
    }

    private void Broadcast()
    {
        if (!NetworkServer.active)
            return;
        nextSync = Time.unscaledTime + syncInterval;
        NetworkServer.SendToAll(BuildMessage());
    }

    private RoundClockMessage BuildMessage() => new RoundClockMessage
    {
        elapsed = serverElapsed,
        running = ServerShouldRun,
        phase = serverPhase,
        countdown = serverPhase == RoundPhase.ReturnCountdown ? serverCountdown : 0f,
        round = serverRound,
    };
}
