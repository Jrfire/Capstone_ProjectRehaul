using Mirror;

/// <summary>
/// Something a thrown bawang (garlic, <see cref="ThrowableProjectile"/>) affects when it hits one of its colliders, e.g.
/// the Manananggal or the Tiktik. Put it on the object owning the collider or a parent. Called on the server only.
/// </summary>
public interface IGarlicTarget
{
    /// <summary>Server: reacts to a garlic hit (repelled for <paramref name="seconds"/>). Returns false if it had no effect.</summary>
    bool ServerTakeGarlic(NetworkIdentity thrower, float seconds);
}
