using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>Client request to pick up the agimat with this network id (the host checks range and that the player lives).</summary>
public struct AgimatPickupRequest : NetworkMessage
{
    public uint netId;
}

/// <summary>
/// A networked agimat lying in a map (spawned by the server, e.g. by AgimatSpawner after the map builds). Its type is a
/// server-set SyncVar that picks the sprite and nametag on every peer. Picking it up (E, through
/// <see cref="AgimatPickupInteractable"/>) asks the host, which adds it to the shared <see cref="AgimatBank"/> and
/// destroys it for everyone.
/// </summary>
[DisallowMultipleComponent]
public sealed class AgimatPickup : NetworkBehaviour
{
    [SerializeField, Tooltip("Billboarded sprite that shows the agimat's type.")]
    private SpriteRenderer worldSprite;
    [SerializeField] private Sprite goldSprite;
    [SerializeField] private Sprite silverSprite;
    [SerializeField] private Sprite bronzeSprite;
    [SerializeField, Tooltip("Optional nametag renamed to the type's name (e.g. \"Gold Agimat\").")]
    private Nametag nametag;
    [SerializeField, Tooltip("Nametag text; {0} = Gold/Silver/Bronze.")]
    private string nameFormat = "{0} Agimat";

    [SyncVar(hook = nameof(OnTypeChanged))]
    private AgimatType type = AgimatType.Bronze;

    private static readonly List<AgimatPickup> active = new List<AgimatPickup>();

    public static IReadOnlyList<AgimatPickup> Active => active;
    public AgimatType Type => type;
    public SpriteRenderer WorldSprite => worldSprite;
    public bool Collected { get; private set; }

    public Sprite SpriteFor(AgimatType agimat)
    {
        switch (agimat)
        {
            case AgimatType.Gold: return goldSprite;
            case AgimatType.Silver: return silverSprite;
            default: return bronzeSprite;
        }
    }

    /// <summary>Server (before or after spawning): sets the agimat's type for every peer.</summary>
    public void ServerSetType(AgimatType value)
    {
        type = value;
        Apply();
    }

    private void Awake() => Apply();

    private void OnEnable() => active.Add(this);

    private void OnDisable() => active.Remove(this);

    public override void OnStartClient() => Apply();

    private void OnTypeChanged(AgimatType _, AgimatType __) => Apply();

    private void Apply()
    {
        if (worldSprite != null)
            worldSprite.sprite = SpriteFor(type);
        if (nametag != null)
            nametag.DisplayName = string.Format(nameFormat, type);
    }

    /// <summary>Client: asks the host to pick this agimat up.</summary>
    public void RequestPickup()
    {
        if (NetworkClient.isConnected && netId != 0 && !Collected)
            NetworkClient.Send(new AgimatPickupRequest { netId = netId });
    }

    /// <summary>Server: adds the agimat to the shared bank and destroys it everywhere. Returns false if already taken.</summary>
    public bool ServerCollect()
    {
        if (!NetworkServer.active || Collected)
            return false;
        Collected = true;
        if (AgimatBank.Instance != null)
            AgimatBank.Instance.ServerAdd(type, 1);
        if (isServer)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
        return true;
    }
}
