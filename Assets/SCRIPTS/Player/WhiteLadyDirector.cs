using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Host to the one chosen client: the White Lady appears to you in this scene.</summary>
public struct WhiteLadyMessage : NetworkMessage
{
    public string sceneName;
    public int seed;
}

/// <summary>
/// Decides, per trip, whether the <see cref="WhiteLady"/> easter egg haunts the arrival map (<see cref="appearChance"/>)
/// and which single living player sees her. Lives on the persistent NetworkManager object. Only the chosen client
/// receives <see cref="WhiteLadyMessage"/> and creates its local apparition, so nobody else can see or trigger her.
/// She is not an enemy and is never part of a destination's enemy pool.
/// </summary>
[DisallowMultipleComponent]
public sealed class WhiteLadyDirector : MonoBehaviour
{
    [SerializeField, Tooltip("Local apparition prefab created only on the chosen player's client.")]
    private WhiteLady whiteLadyPrefab;
    [SerializeField, Range(0f, 1f), Tooltip("Chance per trip into a map (never at base) that she appears.")]
    private float appearChance = 0.25f;

    private TravelDirector subscribedTravel;
    private WhiteLady current;

    public static WhiteLadyDirector Instance { get; private set; }
    public WhiteLady Current => current;
    public WhiteLady Prefab => whiteLadyPrefab;
    public float AppearChance
    {
        get => appearChance;
        set => appearChance = Mathf.Clamp01(value);
    }

    /// <summary>Server: the connection chosen for the last successful roll (null when she did not appear).</summary>
    public NetworkConnectionToClient ServerChosen { get; private set; }

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

    // ---------------------------------------------------------------- server

    public void RegisterServerHandlers() => Subscribe();

    public void ResetServer()
    {
        Unsubscribe();
        ServerChosen = null;
    }

    private void Update()
    {
        if (NetworkServer.active && Instance == this && subscribedTravel != TravelDirector.Instance)
            Subscribe();
    }

    private void Subscribe()
    {
        Unsubscribe();
        subscribedTravel = TravelDirector.Instance;
        if (subscribedTravel != null)
            subscribedTravel.ServerTravelCompleted += OnServerTravelCompleted;
    }

    private void Unsubscribe()
    {
        if (subscribedTravel != null)
            subscribedTravel.ServerTravelCompleted -= OnServerTravelCompleted;
        subscribedTravel = null;
    }

    private void OnServerTravelCompleted(string sceneName)
    {
        ServerChosen = null;
        RoundClock clock = RoundClock.Instance;
        if (clock != null && clock.enabled && sceneName == clock.BaseSceneName)
            return;
        if (MapManager.Active == null || !MapManager.Active.IsBuilt || Random.value >= appearChance)
            return;
        ServerSummon(sceneName, Random.Range(int.MinValue, int.MaxValue));
    }

    /// <summary>Server: picks a random living player and sends only them the White Lady. Returns the chosen connection.</summary>
    public NetworkConnectionToClient ServerSummon(string sceneName, int seed)
    {
        if (!NetworkServer.active)
            return null;
        var living = new System.Collections.Generic.List<NetworkConnectionToClient>();
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity == null)
                continue;
            if (connection.identity.TryGetComponent(out PlayerHealth health) && health.IsDead)
                continue;
            living.Add(connection);
        }
        if (living.Count == 0)
            return null;
        ServerChosen = living[Random.Range(0, living.Count)];
        ServerChosen.Send(new WhiteLadyMessage { sceneName = sceneName, seed = seed });
        return ServerChosen;
    }

    // ---------------------------------------------------------------- client

    public void RegisterClientHandlers() => NetworkClient.ReplaceHandler<WhiteLadyMessage>(OnClientWhiteLady);

    public void ResetClient()
    {
        if (current != null)
            Destroy(current.gameObject);
        current = null;
    }

    private void OnClientWhiteLady(WhiteLadyMessage message)
    {
        if (whiteLadyPrefab == null || message.sceneName != SceneManager.GetActiveScene().name)
            return;
        if (current != null)
            Destroy(current.gameObject);
        // Instantiated into the map scene (not under this persistent object), so she disappears with it.
        current = Instantiate(whiteLadyPrefab);
        current.name = whiteLadyPrefab.name;
        current.Begin(message.seed);
    }
}
