using System.Collections.Generic;

/// <summary>
/// Local-only lock that pauses the owner's movement, look and interaction while a menu, fade or loading screen is active.
/// Each system acquires the lock with its own key so overlapping locks release independently.
/// </summary>
public static class PlayerControlLock
{
    private static readonly HashSet<object> owners = new HashSet<object>();

    public static bool IsLocked => owners.Count > 0;

    public static void Acquire(object owner)
    {
        if (owner != null)
            owners.Add(owner);
    }

    public static void Release(object owner)
    {
        if (owner != null)
            owners.Remove(owner);
    }

    public static bool IsHeldBy(object owner) => owner != null && owners.Contains(owner);

    public static void ReleaseAll() => owners.Clear();
}
