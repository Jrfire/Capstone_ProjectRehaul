using System.Collections.Generic;

/// <summary>
/// Local-only flag that hides the gameplay HUD readouts (health, stamina, clock and agimat counts) while a full menu
/// such as the Albularyo screen is open. Each system hides it with its own key so overlapping menus release independently.
/// </summary>
public static class HudVisibility
{
    private static readonly HashSet<object> owners = new HashSet<object>();

    public static bool Hidden => owners.Count > 0;

    public static void Hide(object owner)
    {
        if (owner != null)
            owners.Add(owner);
    }

    public static void Show(object owner)
    {
        if (owner != null)
            owners.Remove(owner);
    }

    public static void ShowAll() => owners.Clear();
}
