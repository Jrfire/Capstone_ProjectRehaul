using UnityEngine;

/// <summary>
/// Pre-placed interactable (the back of the jeepney). At base every living player presses E to ready up (again to
/// cancel); the trip to the destination confirmed on a <see cref="MapTerminal"/> starts once everyone is ready.
/// Away from base it always heads home: each player boards it individually, which starts the
/// <see cref="RoundClock"/> return countdown.
/// </summary>
[DisallowMultipleComponent]
public sealed class TravelInteractable : Interactable
{
    private static bool AwayFromBase => RoundClock.Instance != null && RoundClock.Instance.enabled && !RoundClock.Instance.InBase;

    public override string Prompt
    {
        get
        {
            if (AwayFromBase)
                return RoundClock.LocalPlayerAboard ? "Waiting for the others" : "Board the jeepney back to base";
            PlayerPersistence local = PlayerPersistence.Local;
            if (local != null && local.IsReady)
                return "Cancel ready";
            return TravelDirector.Instance != null ? $"Ready to travel to {TravelDirector.Instance.SelectedDestination}" : "Ready";
        }
    }

    public override bool CanInteract(PlayerInteraction interactor)
    {
        if (AwayFromBase)
            return RoundClock.Instance.CanBoard;
        return TravelDirector.Instance != null && TravelDirector.Instance.CanTravel &&
            (RoundClock.Instance == null || !RoundClock.Instance.enabled || RoundClock.Instance.CanAfford(TravelDirector.Instance.SelectedDestination));
    }

    public override void Interact(PlayerInteraction interactor)
    {
        if (AwayFromBase)
            RoundClock.Instance.RequestBoard();
        else
            TravelDirector.Instance.RequestTravel();
    }
}
