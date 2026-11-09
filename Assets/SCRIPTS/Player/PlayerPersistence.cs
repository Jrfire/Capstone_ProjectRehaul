using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Keeps player objects alive across Mirror scene changes so their state (health, stamina and later inventory)
/// carries over. The host's object survives as-is. Mirror hides and respawns guests' objects on their own clients
/// during the scene change; they are recreated from the server's copy of that same player, so each guest keeps
/// their own server-authoritative health and the exact stamina they sent when travel began.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerPersistence : NetworkBehaviour
{
    private static readonly List<PlayerPersistence> activePlayers = new List<PlayerPersistence>();

    /// <summary>True once this client has owned a player; scene changes then wait for it instead of requesting a new one.</summary>
    public static bool LocalPlayerSpawned { get; private set; }

    /// <summary>Every enabled player object on this peer (on the server: every connected player). Enemies use this as their target list.</summary>
    public static IReadOnlyList<PlayerPersistence> ActivePlayers => activePlayers;

    // Set by RoundClock while this player has boarded the jeepney home and waits for the others.
    [SyncVar(hook = nameof(OnAboardChanged))] private bool aboard;

    /// <summary>True while this player waits aboard the jeepney back to base; enemies ignore them and their controls are locked.</summary>
    public bool IsAboard => aboard;

    // Set by TravelDirector while this player has readied up at the jeepney at base.
    [SyncVar] private bool ready;

    /// <summary>True while this player has pressed E on the jeepney at base and waits for everyone else to ready.</summary>
    public bool IsReady => ready;

    /// <summary>The local player's object on this peer, or null.</summary>
    public static PlayerPersistence Local =>
        NetworkClient.localPlayer != null && NetworkClient.localPlayer.TryGetComponent(out PlayerPersistence player) ? player : null;

    /// <summary>
    /// True while the local player has readied at the jeepney (or boarded it away from base). Readied players cannot use
    /// the map terminal, the Albularyo or the invite terminal.
    /// </summary>
    public static bool LocalReadied
    {
        get
        {
            PlayerPersistence local = Local;
            return local != null && (local.ready || local.aboard);
        }
    }

    private PlayerTrance trance;

    /// <summary>True while this player is in a Santelmo trance: they look dead to others and enemies ignore them.</summary>
    public bool IsInTrance => trance != null && trance.InTrance;

    private void Awake() => trance = GetComponent<PlayerTrance>();

    [Server]
    public void ServerSetReady(bool value) => ready = value;

    private void OnEnable() => activePlayers.Add(this);

    private void OnDisable()
    {
        activePlayers.Remove(this);
        PlayerControlLock.Release(this);
    }

    [Server]
    public void ServerSetAboard(bool value) => aboard = value;

    private void OnAboardChanged(bool previous, bool current) => ApplyAboardLock();

    public override void OnStopClient() => PlayerControlLock.Release(this);

    private void ApplyAboardLock()
    {
        if (!isLocalPlayer)
            return;
        if (aboard)
            PlayerControlLock.Acquire(this);
        else
            PlayerControlLock.Release(this);
    }

    public override void OnStartServer() => Persist();

    public override void OnStartClient() => Persist();

    public override void OnStartLocalPlayer()
    {
        LocalPlayerSpawned = true;
        ApplyAboardLock();
    }

    public static void ResetLocalPlayerState() => LocalPlayerSpawned = false;

    private void Persist()
    {
        if (!Application.isPlaying || gameObject.scene.name == "DontDestroyOnLoad")
            return;
        if (transform.parent != null)
            transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
    }
}
