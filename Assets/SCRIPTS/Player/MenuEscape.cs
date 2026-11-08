using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Lets exactly one menu react to each Escape press. Menus that close on Escape call <see cref="TryConsume"/>; the
/// <see cref="PauseMenu"/> runs late in the frame and opens only when nothing else consumed the press.
/// </summary>
public static class MenuEscape
{
    private static int consumedFrame = -1;
    private static uint consumedUpdate;

    public static bool PressedThisFrame => Keyboard.current?.escapeKey.wasPressedThisFrame == true;

    // Keyed on the input update too, so separate presses processed within one frame (manual updates) stay distinct.
    public static bool ConsumedThisFrame => consumedFrame == Time.frameCount && consumedUpdate == InputState.updateCount;

    /// <summary>True (and marks the press as used) when Escape was pressed this frame and nothing used it yet.</summary>
    public static bool TryConsume()
    {
        if (!PressedThisFrame || ConsumedThisFrame)
            return false;
        Consume();
        return true;
    }

    /// <summary>Marks this frame's Escape as used (e.g. it cancelled a key rebind).</summary>
    public static void Consume()
    {
        consumedFrame = Time.frameCount;
        consumedUpdate = InputState.updateCount;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        consumedFrame = -1;
        consumedUpdate = 0;
    }
}
