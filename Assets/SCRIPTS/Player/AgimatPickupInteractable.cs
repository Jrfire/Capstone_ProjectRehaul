using UnityEngine;

/// <summary>E on an <see cref="AgimatPickup"/>: asks the host to add it to the shared agimat inventory.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AgimatPickup))]
public sealed class AgimatPickupInteractable : Interactable
{
    [SerializeField] private string prompt = "Pick up";

    private AgimatPickup pickup;

    public override string Prompt => prompt;

    private void Awake() => pickup = GetComponent<AgimatPickup>();

    public override bool CanInteract(PlayerInteraction interactor) =>
        pickup != null && !pickup.Collected && AgimatBank.Instance != null &&
        (TravelDirector.Instance == null || !TravelDirector.Instance.IsTraveling);

    public override void Interact(PlayerInteraction interactor)
    {
        if (CanInteract(interactor))
            pickup.RequestPickup();
    }
}
