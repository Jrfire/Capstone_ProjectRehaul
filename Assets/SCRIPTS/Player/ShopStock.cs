using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>The shopkeeper's current stock, broadcast to every client whenever it changes.</summary>
public struct ShopStockMessage : NetworkMessage
{
    public int round;
    public string[] itemIds;
    public int[] prices;
    public bool[] sold;
}

/// <summary>Client to host: buy stock entry <see cref="index"/> (must be near a shopkeeper).</summary>
public struct ShopBuyRequest : NetworkMessage
{
    public int index;
}

public enum ShopBuyResult : byte
{
    Bought = 0,
    SoldOut = 1,
    NotEnoughMoney = 2,
    InventoryFull = 3,
    Refused = 4,
}

/// <summary>Host to the buyer: the outcome of a <see cref="ShopBuyRequest"/>.</summary>
public struct ShopBuyResultMessage : NetworkMessage
{
    public int index;
    public ShopBuyResult result;
}

/// <summary>
/// The shopkeeper's stock, on Base's persistent NetworkManager object (wired by
/// <see cref="SteamNetworkManager"/> like <see cref="AgimatBank"/>). Host-authoritative: at the start of every quota
/// cycle (whenever <see cref="RoundClock.Round"/> changes, including the game reset) the host rolls
/// <see cref="itemsPerQuota"/> distinct items from the <see cref="ItemCatalog"/> whose <see cref="ItemDefinition.ShopPrice"/>
/// is above zero (weighted by <see cref="ItemDefinition.ShopWeight"/>). Each entry can be bought once, by any player
/// near a <see cref="ShopkeeperInteractable"/>, with the team's shared <see cref="AgimatBank.Money"/>; the item goes into
/// the buyer's own <see cref="PlayerInventory"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShopStock : MonoBehaviour
{
    [SerializeField, Tooltip("Items the shop can stock (those with a Shop Price above 0).")]
    private ItemCatalog catalog;
    [SerializeField, Min(1), Tooltip("Different items offered per quota; each can be bought once.")]
    private int itemsPerQuota = 2;
    [SerializeField, Min(1f), Tooltip("Players must be within this distance of a shopkeeper for the host to accept a purchase.")]
    private float buyRange = 6f;

    private readonly List<ItemDefinition> serverItems = new List<ItemDefinition>();
    private readonly List<bool> serverSold = new List<bool>();
    private int serverRound = -1;
    private RoundClock subscribedClock;

    private ShopStockMessage clientState;
    private bool clientHasState;

    public static ShopStock Instance { get; private set; }

    /// <summary>Raised on clients (and the host) whenever the stock changes.</summary>
    public event Action Changed;
    /// <summary>Raised on the client that asked to buy, with the host's answer.</summary>
    public static event Action<int, ShopBuyResult> BuyAnswered;

    public ItemCatalog Catalog => catalog;
    public int ItemsPerQuota => itemsPerQuota;
    public float BuyRange => buyRange;

    private bool UseServerState => NetworkServer.active || !clientHasState;

    /// <summary>Number of stock entries (sold ones included).</summary>
    public int Count => UseServerState ? serverItems.Count : clientState.itemIds != null ? clientState.itemIds.Length : 0;

    /// <summary>The round this stock was rolled for.</summary>
    public int StockRound => UseServerState ? serverRound : clientState.round;

    public ItemDefinition GetItem(int index)
    {
        if (index < 0 || index >= Count)
            return null;
        if (UseServerState)
            return serverItems[index];
        return catalog != null ? catalog.Find(clientState.itemIds[index]) : null;
    }

    public int GetPrice(int index)
    {
        if (index < 0 || index >= Count)
            return 0;
        if (UseServerState)
            return serverItems[index] != null ? serverItems[index].ShopPrice : 0;
        return clientState.prices != null && index < clientState.prices.Length ? clientState.prices[index] : 0;
    }

    public bool IsSold(int index)
    {
        if (index < 0 || index >= Count)
            return true;
        if (UseServerState)
            return serverSold[index];
        return clientState.sold != null && index < clientState.sold.Length && clientState.sold[index];
    }

    /// <summary>Distinct weighted pick of up to <paramref name="count"/> shop items (price above 0).</summary>
    public static List<ItemDefinition> RollStock(ItemCatalog catalog, int count, System.Random random)
    {
        var pool = new List<ItemDefinition>();
        if (catalog != null)
        {
            foreach (ItemDefinition item in catalog.Items)
            {
                if (item != null && item.ShopPrice > 0 && item.ShopWeight > 0f && !pool.Contains(item))
                    pool.Add(item);
            }
        }
        var picked = new List<ItemDefinition>();
        while (picked.Count < count && pool.Count > 0)
        {
            float total = 0f;
            foreach (ItemDefinition item in pool)
                total += item.ShopWeight;
            double roll = random.NextDouble() * total;
            int chosen = pool.Count - 1;
            for (int index = 0; index < pool.Count; index++)
            {
                roll -= pool[index].ShopWeight;
                if (roll < 0d)
                {
                    chosen = index;
                    break;
                }
            }
            picked.Add(pool[chosen]);
            pool.RemoveAt(chosen);
        }
        return picked;
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

    public void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<ShopStockMessage>(message =>
        {
            clientState = message;
            clientHasState = true;
            if (!NetworkServer.active)
                Changed?.Invoke();
        });
        NetworkClient.ReplaceHandler<ShopBuyResultMessage>(message => BuyAnswered?.Invoke(message.index, message.result));
    }

    public void ResetClient()
    {
        clientHasState = false;
        clientState = default;
        Changed?.Invoke();
    }

    /// <summary>Client: asks the host to buy stock entry <paramref name="index"/> for the team's money.</summary>
    public void RequestBuy(int index)
    {
        if (NetworkClient.isConnected && index >= 0 && index < Count && !IsSold(index))
            NetworkClient.Send(new ShopBuyRequest { index = index });
    }

    // ---------------------------------------------------------------- server

    public void RegisterServerHandlers()
    {
        NetworkServer.ReplaceHandler<ShopBuyRequest>(OnServerBuy);
        Subscribe();
        ServerRestockIfNeeded();
        Broadcast();
    }

    public void ResetServer()
    {
        Unsubscribe();
        serverItems.Clear();
        serverSold.Clear();
        serverRound = -1;
    }

    public void OnServerConnect(NetworkConnectionToClient connection) => connection.Send(BuildMessage());

    private void Update()
    {
        if (!NetworkServer.active || Instance != this)
            return;
        Subscribe();
        ServerRestockIfNeeded();
    }

    private static int CurrentRound => RoundClock.Instance != null ? RoundClock.Instance.Round : 1;

    private void ServerRestockIfNeeded()
    {
        if (NetworkServer.active && CurrentRound != serverRound)
            ServerRestock();
    }

    /// <summary>Server: rolls a fresh stock for the current round (called automatically when the round changes).</summary>
    public void ServerRestock()
    {
        if (!NetworkServer.active)
            return;
        serverRound = CurrentRound;
        serverItems.Clear();
        serverSold.Clear();
        serverItems.AddRange(RollStock(catalog, itemsPerQuota, new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue))));
        for (int index = 0; index < serverItems.Count; index++)
            serverSold.Add(false);
        Broadcast();
    }

    /// <summary>
    /// Server: puts back a saved stock (items and what was already bought) so reloading a save never refreshes the
    /// quota's allowance. Unknown item ids are skipped.
    /// </summary>
    public void ServerRestore(int round, IList<string> itemIds, IList<bool> sold)
    {
        if (!NetworkServer.active)
            return;
        serverRound = round;
        serverItems.Clear();
        serverSold.Clear();
        if (itemIds != null)
        {
            for (int index = 0; index < itemIds.Count; index++)
            {
                ItemDefinition item = catalog != null ? catalog.Find(itemIds[index]) : null;
                if (item == null)
                    continue;
                serverItems.Add(item);
                serverSold.Add(sold != null && index < sold.Count && sold[index]);
            }
        }
        Broadcast();
    }

    /// <summary>
    /// Server: <paramref name="buyer"/> buys entry <paramref name="index"/>. The team pays its price from the shared
    /// money and the item goes into the buyer's inventory; the entry is then sold out until the next quota.
    /// </summary>
    public ShopBuyResult ServerBuy(PlayerInventory buyer, int index)
    {
        if (!NetworkServer.active || buyer == null)
            return ShopBuyResult.Refused;
        if (index < 0 || index >= serverItems.Count || serverItems[index] == null || serverSold[index])
            return ShopBuyResult.SoldOut;
        AgimatBank bank = AgimatBank.Instance;
        int price = serverItems[index].ShopPrice;
        if (bank == null || bank.Money < price)
            return ShopBuyResult.NotEnoughMoney;
        if (!buyer.ServerTryAdd(serverItems[index]))
            return ShopBuyResult.InventoryFull;
        bank.ServerTrySpend(price);
        serverSold[index] = true;
        Broadcast();
        return ShopBuyResult.Bought;
    }

    private void OnServerBuy(NetworkConnectionToClient connection, ShopBuyRequest request)
    {
        ShopBuyResult result = ShopBuyResult.Refused;
        NetworkIdentity identity = connection?.identity;
        if (identity == null)
            return;
        bool dead = identity.TryGetComponent(out PlayerHealth health) && health.IsDead;
        bool traveling = TravelDirector.Instance != null && TravelDirector.Instance.ServerTraveling;
        bool gameOver = RoundClock.Instance != null && RoundClock.Instance.enabled && RoundClock.Instance.Phase == RoundPhase.GameOver;
        if (!ShopkeeperInteractable.AnyWithin(identity.transform.position, buyRange))
            Debug.LogWarning($"Rejected shop purchase from connection {connection.connectionId}: not near a shopkeeper.", this);
        else if (!dead && !traveling && !gameOver && identity.TryGetComponent(out PlayerInventory inventory))
            result = ServerBuy(inventory, request.index);
        connection.Send(new ShopBuyResultMessage { index = request.index, result = result });
    }

    private void Subscribe()
    {
        RoundClock clock = RoundClock.Instance;
        if (clock == subscribedClock)
            return;
        Unsubscribe();
        subscribedClock = clock;
        if (clock != null)
            clock.ServerGameReset += ServerRestock;
    }

    private void Unsubscribe()
    {
        if (subscribedClock != null)
            subscribedClock.ServerGameReset -= ServerRestock;
        subscribedClock = null;
    }

    private void Broadcast()
    {
        if (!NetworkServer.active)
            return;
        NetworkServer.SendToAll(BuildMessage());
        Changed?.Invoke();
    }

    private ShopStockMessage BuildMessage()
    {
        var message = new ShopStockMessage
        {
            round = serverRound,
            itemIds = new string[serverItems.Count],
            prices = new int[serverItems.Count],
            sold = new bool[serverItems.Count]
        };
        for (int index = 0; index < serverItems.Count; index++)
        {
            message.itemIds[index] = serverItems[index] != null ? serverItems[index].Id : string.Empty;
            message.prices[index] = serverItems[index] != null ? serverItems[index].ShopPrice : 0;
            message.sold[index] = serverSold[index];
        }
        return message;
    }

    [ContextMenu("Restock Now")]
    private void DebugRestock() => ServerRestock();
}
