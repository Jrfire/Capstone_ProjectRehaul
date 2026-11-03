using UnityEngine;

/// <summary>
/// Pre-placed interactable (the back of the jeepney). At base it starts travel to the destination confirmed on a
/// <see cref="MapTerminal"/>. Away from base it always heads home: each player boards it individually, which starts
/// the <see cref="RoundClock"/> return countdown.
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
            return TravelDirector.Instance != null ? $"Travel to {TravelDirector.Instance.SelectedDestination}" : "Travel";
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
