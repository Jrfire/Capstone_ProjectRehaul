using Mirror;

/// <summary>
/// Something a player's weapon (left mouse with a Weapon item, see <see cref="PlayerItemUse"/>) can hit, e.g. an enemy.
/// Put it on the object that owns the hit collider or one of its parents. Called on the server only.
/// </summary>
public interface IWeaponTarget
{
    /// <summary>Server: reacts to a hit by <paramref name="weapon"/>. Returns false if the hit had no effect.</summary>
    bool ServerTakeWeaponHit(ItemDefinition weapon, NetworkIdentity attacker);
}
