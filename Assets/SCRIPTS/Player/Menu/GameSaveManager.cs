using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Server-side save slots (on the persistent NetworkManager object, driven by <see cref="SteamNetworkManager"/>).
/// When the host starts with <see cref="MenuLaunch.ActiveSaveSlot"/> set, the slot's file restores the agimat bank,
/// money, round clock and base floor items, and every (re)joining player gets their saved profile (character, health,
/// inventory), so loaded players skip character select. Saving only captures the base scene: on leaving base (before
/// the trip's travel-time cost), on arriving back at base, on completing a round at base and when the host stops at
/// base. Leaving away from base therefore keeps the last base save.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameSaveManager : MonoBehaviour
{
    [SerializeField, Tooltip("Networked pickup used to put the saved base floor items back (must be in the spawn prefabs).")]
    private ItemPickup itemPickupPrefab;

    private readonly Dictionary<string, PlayerSaveData> profiles = new Dictionary<string, PlayerSaveData>();
    private readonly List<FloorItemSaveData> departedBaseItems = new List<FloorItemSaveData>();
    private TravelDirector subscribedTravel;
    private RoundClock subscribedClock;
    private bool hasDepartedBaseItems;
    private bool savedOnStop;

    public static GameSaveManager Instance { get; private set; }
    /// <summary>Save slot this server writes to (0-based), or -1 when the session does not save.</summary>
    public int ActiveSlot { get; private set; } = -1;
    /// <summary>True when the active slot's file was loaded at server start.</summary>
    public bool LoadedFromSave { get; private set; }
    public ItemPickup ItemPickupPrefab => itemPickupPrefab;

    private void OnEnable()
    {
        if (Instance == null)
            Instance = this;
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (Instance == this)
            Instance = null;
    }

    private RoundClock Clock => RoundClock.Instance != null && RoundClock.Instance.enabled ? RoundClock.Instance : null;
    private AgimatBank Bank => AgimatBank.Instance != null && AgimatBank.Instance.enabled ? AgimatBank.Instance : null;
    private bool InBase => Clock == null || Clock.InBase;

    // ---------------------------------------------------------------- server lifecycle

    /// <summary>Server start: subscribes to travel and loads <see cref="MenuLaunch.ActiveSaveSlot"/> (after the clock and bank registered).</summary>
    public void OnServerStarted()
    {
        profiles.Clear();
        departedBaseItems.Clear();
        hasDepartedBaseItems = false;
        savedOnStop = false;
        LoadedFromSave = false;
        ActiveSlot = SaveSystem.IsValidSlot(MenuLaunch.ActiveSaveSlot) ? MenuLaunch.ActiveSaveSlot : -1;
        Subscribe();
        if (ActiveSlot < 0)
            return;

        GameSaveData data = SaveSystem.Load(ActiveSlot);
        if (data == null)
            return;
        LoadedFromSave = true;
        foreach (PlayerSaveData player in data.players)
        {
            if (player != null && !string.IsNullOrEmpty(player.key))
                profiles[player.key] = player;
        }
        Bank?.ServerRestore(data.gold, data.silver, data.bronze, data.turnedIn, data.money, data.roundReward);
        Clock?.ServerRestore(data.round, data.elapsed);
        SpawnFloorItems(data.baseItems);
    }

    /// <summary>Host stopping: saves the base state once (before the players are destroyed).</summary>
    public void OnServerStopping()
    {
        if (!savedOnStop && NetworkServer.active)
            SaveNow();
        savedOnStop = true;
    }

    public void ResetServer()
    {
        Unsubscribe();
        profiles.Clear();
        departedBaseItems.Clear();
        hasDepartedBaseItems = false;
        ActiveSlot = -1;
        LoadedFromSave = false;
    }

    private void Subscribe()
    {
        Unsubscribe();
        subscribedTravel = TravelDirector.Instance;
        if (subscribedTravel != null)
        {
            subscribedTravel.ServerTravelPreparing += OnServerTravelPreparing;
            subscribedTravel.ServerTravelCompleted += OnServerTravelCompleted;
        }
        subscribedClock = Clock;
        if (subscribedClock != null)
            subscribedClock.ServerRoundCompleted += OnServerRoundCompleted;
    }

    private void Unsubscribe()
    {
        if (subscribedTravel != null)
        {
            subscribedTravel.ServerTravelPreparing -= OnServerTravelPreparing;
            subscribedTravel.ServerTravelCompleted -= OnServerTravelCompleted;
            subscribedTravel = null;
        }
        if (subscribedClock != null)
        {
            subscribedClock.ServerRoundCompleted -= OnServerRoundCompleted;
            subscribedClock = null;
        }
    }

    // ---------------------------------------------------------------- players

    /// <summary>The profile key of a connection: its SteamID64 (the host uses its own).</summary>
    public static string KeyFor(NetworkConnectionToClient connection)
    {
        if (connection == null)
            return null;
        if (connection is LocalConnectionToClient)
            return SteamSession.Instance != null && !string.IsNullOrEmpty(SteamSession.Instance.LocalSteamId)
                ? SteamSession.Instance.LocalSteamId
                : "host";
        return connection.address;
    }

    public bool TryGetProfile(NetworkConnectionToClient connection, out PlayerSaveData profile)
    {
        profile = null;
        string key = KeyFor(connection);
        return key != null && profiles.TryGetValue(key, out profile);
    }

    /// <summary>Server, before the new player object is spawned: applies the saved character so the select screen never opens.</summary>
    public void ApplyBeforeSpawn(NetworkConnectionToClient connection, GameObject player)
    {
        if (player == null || !TryGetProfile(connection, out PlayerSaveData profile))
            return;
        if (profile.characterIndex >= 0 && player.TryGetComponent(out PlayerCharacterSelection selection))
            selection.ServerSelect(profile.characterIndex);
    }

    /// <summary>Server, after the player object is spawned: restores saved health and inventory.</summary>
    public void ApplyAfterSpawn(NetworkConnectionToClient connection, GameObject player)
    {
        if (player == null || !TryGetProfile(connection, out PlayerSaveData profile))
            return;
        if (profile.health > 0 && player.TryGetComponent(out PlayerHealth health))
            health.SetHealth(profile.health);
        if (player.TryGetComponent(out PlayerInventory inventory))
            inventory.ServerRestore(profile.passive, profile.slots);
    }

    /// <summary>Server: remembers a leaving player's profile (written to disk with the next base save).</summary>
    public void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        if (connection?.identity != null)
            Capture(connection, connection.identity.gameObject);
    }

    private void Capture(NetworkConnectionToClient connection, GameObject player)
    {
        string key = KeyFor(connection);
        if (string.IsNullOrEmpty(key) || player == null)
            return;
        var profile = new PlayerSaveData { key = key };
        if (player.TryGetComponent(out PlayerName playerName))
            profile.name = playerName.DisplayName;
        if (player.TryGetComponent(out PlayerCharacterSelection selection))
            profile.characterIndex = selection.SelectedIndex;
        if (player.TryGetComponent(out PlayerHealth health))
            profile.health = health.CurrentHealth;
        if (player.TryGetComponent(out PlayerInventory inventory))
        {
            profile.passive = inventory.PassiveItemId;
            profile.slots = new List<string>(inventory.SlotIds());
        }
        profiles[key] = profile;
    }

    private void CaptureConnectedPlayers()
    {
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity != null)
                Capture(connection, connection.identity.gameObject);
        }
    }

    // ---------------------------------------------------------------- saving

    /// <summary>The current state as save data (players and floor items are captured live).</summary>
    public GameSaveData BuildSaveData()
    {
        CaptureConnectedPlayers();
        var data = new GameSaveData();
        AgimatBank bank = Bank;
        if (bank != null)
        {
            data.gold = bank.Count(AgimatType.Gold);
            data.silver = bank.Count(AgimatType.Silver);
            data.bronze = bank.Count(AgimatType.Bronze);
            data.turnedIn = bank.TurnedIn;
            data.money = bank.Money;
            data.roundReward = bank.RoundReward;
        }
        RoundClock clock = Clock;
        if (clock != null)
        {
            data.round = clock.Round;
            data.elapsed = clock.Elapsed;
        }
        data.players.AddRange(profiles.Values);
        data.baseItems.AddRange(CaptureFloorItems());
        return data;
    }

    /// <summary>Server: writes the active slot if the game is at base. Returns true when a file was written.</summary>
    public bool SaveNow()
    {
        if (ActiveSlot < 0 || !NetworkServer.active || !InBase)
            return false;
        return SaveSystem.Write(ActiveSlot, BuildSaveData());
    }

    private static List<FloorItemSaveData> CaptureFloorItems()
    {
        var items = new List<FloorItemSaveData>();
        foreach (ItemPickup pickup in ItemPickup.Active)
        {
            if (pickup != null && !pickup.Collected && pickup.Item != null)
                items.Add(new FloorItemSaveData { itemId = pickup.ItemId, position = pickup.transform.position });
        }
        return items;
    }

    private void SpawnFloorItems(List<FloorItemSaveData> items)
    {
        if (items == null || itemPickupPrefab == null || itemPickupPrefab.Catalog == null)
            return;
        foreach (FloorItemSaveData item in items)
        {
            ItemDefinition definition = item != null ? itemPickupPrefab.Catalog.Find(item.itemId) : null;
            if (definition != null)
                ItemPickup.ServerSpawn(itemPickupPrefab, definition, item.position);
        }
    }

    private void OnServerTravelPreparing(string fromScene, string toScene)
    {
        if (Clock != null && fromScene != Clock.BaseSceneName)
            return;
        // Base pickups are scene objects destroyed by the trip; they are put back when the group returns.
        departedBaseItems.Clear();
        departedBaseItems.AddRange(CaptureFloorItems());
        hasDepartedBaseItems = true;
        SaveNow();
    }

    private void OnServerTravelCompleted(string sceneName)
    {
        if (Clock != null && sceneName != Clock.BaseSceneName)
            return;
        if (hasDepartedBaseItems)
        {
            SpawnFloorItems(departedBaseItems);
            departedBaseItems.Clear();
            hasDepartedBaseItems = false;
        }
        SaveNow();
    }

    private void OnServerRoundCompleted(int _)
    {
        if (InBase)
            SaveNow();
    }
}
