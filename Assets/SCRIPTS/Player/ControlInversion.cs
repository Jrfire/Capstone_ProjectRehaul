using UnityEngine;

/// <summary>
/// Local control scramble (the blood-moon Tikbalang's ability): while active, A/D strafing and the mouse's horizontal and
/// vertical look are inverted for the local player. <see cref="PlayerMovement"/> and <see cref="PlayerLook"/> read it.
/// </summary>
public static class ControlInversion
{
    private static float activeUntil = float.NegativeInfinity;

    public static bool Active => Time.time < activeUntil;
    public static float RemainingSeconds => Mathf.Max(0f, activeUntil - Time.time);

    public static void Begin(float seconds) => activeUntil = Mathf.Max(activeUntil, Time.time + Mathf.Max(0f, seconds));

    public static void Clear() => activeUntil = float.NegativeInfinity;
}
