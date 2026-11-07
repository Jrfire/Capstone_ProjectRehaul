using System;
using Mirror;
using UnityEngine;

public enum AgimatType : byte
{
    Gold = 0,
    Silver = 1,
    Bronze = 2,
}

/// <summary>The shared agimat inventory and quota progress, sent by the host to every client on each change.</summary>
public struct AgimatStateMessage : NetworkMessage
{
    public int gold;
    public int silver;
    public int bronze;
    public int turnedIn;
    public int quota;
    public int round;
    public int money;
    public int roundReward;
}

/// <summary>Client request to sacrifice one agimat of a type at an Albularyo.</summary>
public struct AgimatSacrificeRequest : NetworkMessage
{
    public AgimatType type;
}

/// <summary>
/// Global agimat inventory and money (pesos) shared by every player (gold, silver and bronze counts), on the persistent
/// NetworkManager object. Host-authoritative: the server changes it and broadcasts <see cref="AgimatStateMessage"/>.
/// Counts and money persist across scenes and rounds and reset only with the game
/// (<see cref="RoundClock.ServerGameReset"/>); a team wipe away from base halves both. Sacrificing at the Albularyo pays
/// that type's reward and adds to the round's turned-in count; reaching the quota (<see cref="QuotaFor"/>) completes the
/// round (timer reset, quota raised) and clears the turned-in count and round reward.
/// </summary>
[DisallowMultipleComponent]
public sealed class AgimatBank : MonoBehaviour
{
    public const int TypeCount = 3;

    [SerializeField, Min(1f), Tooltip("Players must be within this distance of an Albularyo for the host to accept a sacrifice.")]
    private float sacrificeRange = 6f;
    [SerializeField, Min(1f), Tooltip("Players must be within this distance of an agimat pickup for the host to accept picking it up.")]
    private float pickupRange = 5f;

    [Header("Rewards (pesos per sacrificed agimat)")]
    [SerializeField, Min(0)] private int goldReward = 20;
    [SerializeField, Min(0)] private int silverReward = 10;
    [SerializeField, Min(0)] private int bronzeReward = 5;

    /// <summary>The Philippine peso sign (₱).</summary>
    public const string PesoSign = "\u20B1";

    private readonly int[] serverCounts = new int[TypeCount];
    private int serverTurnedIn;
    private int serverMoney;
    private int serverRoundReward;
    private RoundClock subscribedClock;
    private int lastSentQuota = -1;

    private AgimatStateMessage clientState;
    private bool clientHasState;

    public static AgimatBank Instance { get; private set; }

    /// <summary>Raised on clients (and the host) whenever the shared state changes.</summary>
    public event Action Changed;
    /// <summary>Server: an agimat was sacrificed (type, new turned-in count).</summary>
    public event Action<AgimatType, int> ServerSacrificed;

    private bool UseServerState => NetworkServer.active || !clientHasState;

    public int TurnedIn => UseServerState ? serverTurnedIn : clientState.turnedIn;
    /// <summary>Shared money in pesos.</summary>
    public int Money => UseServerState ? serverMoney : clientState.money;
    /// <summary>Pesos earned from this round's sacrifices so far (cleared with the turned-in count).</summary>
    public int RoundReward => UseServerState ? serverRoundReward : clientState.roundReward;
    public int Quota => UseServerState ? QuotaFor(CurrentRound) : clientState.quota;
    public int Total => Count(AgimatType.Gold) + Count(AgimatType.Silver) + Count(AgimatType.Bronze);
    /// <summary>True while turned-in plus held agimats can still meet this round's quota.</summary>
    public bool QuotaReachable => TurnedIn + Total >= Quota;

    private static int CurrentRound => RoundClock.Instance != null ? RoundClock.Instance.Round : 1;

    public int Count(AgimatType type)
    {
        int index = (int)type;
        if (index < 0 || index >= TypeCount)
            return 0;
        if (UseServerState)
            return serverCounts[index];
        switch (type)
        {
            case AgimatType.Gold: return clientState.gold;
            case AgimatType.Silver: return clientState.silver;
            default: return clientState.bronze;
        }
    }

    /// <summary>Pesos paid for sacrificing one agimat of a type (gold 20, silver 10, bronze 5 by default).</summary>
    public int RewardFor(AgimatType type)
    {
        switch (type)
        {
            case AgimatType.Gold: return goldReward;
            case AgimatType.Silver: return silverReward;
            case AgimatType.Bronze: return bronzeReward;
            default: return 0;
        }
    }

    public static string FormatPeso(int amount) => PesoSign + amount;

    /// <summary>
    /// Agimats to sacrifice in a round: 5, 7, 11, 19, 35... (3 + 2^round; each round adds twice the previous increase).
    /// </summary>
    public static int QuotaFor(int round)
    {
        round = Mathf.Clamp(round, 1, 29);
        return 3 + (1 << round);
    }

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
        Unsubscribe();
        Instance = null;
    }

    // ---------------------------------------------------------------- client

    public void RegisterClientHandlers() => NetworkClient.ReplaceHandler<AgimatStateMessage>(OnClientState);

    public void ResetClient()
    {
        clientHasState = false;
        clientState = default;
        Changed?.Invoke();
    }

    /// <summary>Client: asks the host to sacrifice one agimat of <paramref name="type"/> (must be near an Albularyo).</summary>
    public void RequestSacrifice(AgimatType type)
    {
        if (NetworkClient.isConnected && Count(type) > 0)
            NetworkClient.Send(new AgimatSacrificeRequest { type = type });
    }

    private void OnClientState(AgimatStateMessage message)
    {
        clientState = message;
        clientHasState = true;
        if (!NetworkServer.active)
            Changed?.Invoke();
    }

    // ---------------------------------------------------------------- server

    public void RegisterServerHandlers()
    {
        NetworkServer.ReplaceHandler<AgimatSacrificeRequest>(OnServerSacrifice);
        NetworkServer.ReplaceHandler<AgimatPickupRequest>(OnServerPickup);
        Subscribe();
        Broadcast();
    }

    public void ResetServer()
    {
        Unsubscribe();
        Array.Clear(serverCounts, 0, TypeCount);
        serverTurnedIn = 0;
        serverMoney = 0;
        serverRoundReward = 0;
        lastSentQuota = -1;
    }

    public void OnServerConnect(NetworkConnectionToClient connection) => connection.Send(BuildMessage());

    private void Update()
    {
        if (!NetworkServer.active || Instance != this)
            return;
        Subscribe();
        // The quota follows the round, which can change without passing through the bank (e.g. the game reset).
        if (Quota != lastSentQuota)
            Broadcast();
    }

    private void Subscribe()
    {
        RoundClock clock = RoundClock.Instance;
        if (clock == subscribedClock)
            return;
        Unsubscribe();
        subscribedClock = clock;
        if (clock == null)
            return;
        clock.QuotaReachable = () => QuotaReachable;
        clock.ServerGameReset += OnServerGameReset;
        clock.ServerAllPlayersDied += ServerHalveForTeamWipe;
    }

    private void Unsubscribe()
    {
        if (subscribedClock == null)
            return;
        subscribedClock.QuotaReachable = null;
        subscribedClock.ServerGameReset -= OnServerGameReset;
        subscribedClock.ServerAllPlayersDied -= ServerHalveForTeamWipe;
        subscribedClock = null;
    }

    /// <summary>Server: adds agimats of a type to the shared inventory (e.g. a pickup). Negative amounts remove them.</summary>
    public void ServerAdd(AgimatType type, int amount)
    {
        int index = (int)type;
        if (!NetworkServer.active || index < 0 || index >= TypeCount || amount == 0)
            return;
        serverCounts[index] = Mathf.Max(0, serverCounts[index] + amount);
        Broadcast();
    }

    /// <summary>Server: adds pesos to the shared money. Negative amounts remove them (never below zero).</summary>
    public void ServerAddMoney(int amount)
    {
        if (!NetworkServer.active || amount == 0)
            return;
        serverMoney = Mathf.Max(0, serverMoney + amount);
        Broadcast();
    }

    /// <summary>Server: spends pesos if the team has enough (for a future store). Returns false otherwise.</summary>
    public bool ServerTrySpend(int amount)
    {
        if (!NetworkServer.active || amount < 0 || serverMoney < amount)
            return false;
        serverMoney -= amount;
        Broadcast();
        return true;
    }

    /// <summary>
    /// Server: the team-wipe penalty (every player died away from base): each agimat type and the money are halved,
    /// rounding down. Turned-in agimats are kept.
    /// </summary>
    public void ServerHalveForTeamWipe()
    {
        if (!NetworkServer.active)
            return;
        for (int index = 0; index < TypeCount; index++)
            serverCounts[index] /= 2;
        serverMoney /= 2;
        Broadcast();
    }

    /// <summary>
    /// Server: sacrifices one agimat. Reaching the quota completes the round (resetting the timer and the turned-in
    /// count; the next round's quota is higher). Returns false when nothing could be sacrificed.
    /// </summary>
    public bool ServerSacrifice(AgimatType type)
    {
        int index = (int)type;
        if (!NetworkServer.active || index < 0 || index >= TypeCount || serverCounts[index] <= 0)
            return false;
        RoundClock clock = RoundClock.Instance;
        if (clock != null && clock.Phase == RoundPhase.GameOver)
            return false;
        if (TravelDirector.Instance != null && TravelDirector.Instance.ServerTraveling)
            return false;

        serverCounts[index]--;
        serverTurnedIn++;
        int reward = RewardFor(type);
        serverMoney += reward;
        serverRoundReward += reward;
        ServerSacrificed?.Invoke(type, serverTurnedIn);
        if (serverTurnedIn >= Quota)
        {
            serverTurnedIn = 0;
            serverRoundReward = 0;
            if (clock != null)
                clock.ServerCompleteRound();
        }
        Broadcast();
        return true;
    }

    private void OnServerSacrifice(NetworkConnectionToClient connection, AgimatSacrificeRequest request)
    {
        Vector3? position = connection?.identity != null ? connection.identity.transform.position : (Vector3?)null;
        if (!position.HasValue || !AlbularyoInteractable.AnyWithin(position.Value, sacrificeRange))
        {
            Debug.LogWarning($"Rejected agimat sacrifice from connection {connection?.connectionId}: not near an Albularyo.", this);
            return;
        }
        if (connection.identity.TryGetComponent(out PlayerHealth health) && health.IsDead)
            return;
        ServerSacrifice(request.type);
    }

    private void OnServerPickup(NetworkConnectionToClient connection, AgimatPickupRequest request)
    {
        if (connection?.identity == null || !NetworkServer.spawned.TryGetValue(request.netId, out NetworkIdentity target) ||
            target == null || !target.TryGetComponent(out AgimatPickup pickup))
            return;
        if (connection.identity.TryGetComponent(out PlayerHealth health) && health.IsDead)
            return;
        if ((pickup.transform.position - connection.identity.transform.position).sqrMagnitude > pickupRange * pickupRange)
        {
            Debug.LogWarning($"Rejected agimat pickup from connection {connection.connectionId}: too far away.", this);
            return;
        }
        pickup.ServerCollect();
    }

    private void OnServerGameReset()
    {
        Array.Clear(serverCounts, 0, TypeCount);
        serverTurnedIn = 0;
        serverMoney = 0;
        serverRoundReward = 0;
        Broadcast();
    }

    private void Broadcast()
    {
        if (!NetworkServer.active)
            return;
        AgimatStateMessage message = BuildMessage();
        lastSentQuota = message.quota;
        NetworkServer.SendToAll(message);
        Changed?.Invoke();
    }

    private AgimatStateMessage BuildMessage() => new AgimatStateMessage
    {
        gold = serverCounts[(int)AgimatType.Gold],
        silver = serverCounts[(int)AgimatType.Silver],
        bronze = serverCounts[(int)AgimatType.Bronze],
        turnedIn = serverTurnedIn,
        quota = QuotaFor(CurrentRound),
        round = CurrentRound,
        money = serverMoney,
        roundReward = serverRoundReward,
    };

    [ContextMenu("Debug/Add 100 Pesos")]
    private void DebugAddMoney() => ServerAddMoney(100);

    [ContextMenu("Debug/Add 1 Gold Agimat")]
    private void DebugAddGold() => ServerAdd(AgimatType.Gold, 1);

    [ContextMenu("Debug/Add 1 Silver Agimat")]
    private void DebugAddSilver() => ServerAdd(AgimatType.Silver, 1);

    [ContextMenu("Debug/Add 1 Bronze Agimat")]
    private void DebugAddBronze() => ServerAdd(AgimatType.Bronze, 1);

    [ContextMenu("Debug/Add 5 Of Each Agimat")]
    private void DebugAddFive()
    {
        ServerAdd(AgimatType.Gold, 5);
        ServerAdd(AgimatType.Silver, 5);
        ServerAdd(AgimatType.Bronze, 5);
    }
}
