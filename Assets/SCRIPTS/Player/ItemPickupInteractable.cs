using Mirror;
using UnityEngine;

/// <summary>E on a dropped <see cref="ItemPickup"/>: asks the host to put it into the local player's inventory.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ItemPickup))]
public sealed class ItemPickupInteractable : Interactable
{
    [SerializeField] private string prompt = "Pick up";

    private ItemPickup pickup;

    public override string Prompt => prompt;

    private void Awake() => pickup = GetComponent<ItemPickup>();

    public override bool CanInteract(PlayerInteraction interactor) =>
        pickup != null && !pickup.Collected && interactor != null && interactor.TryGetComponent(out PlayerInventory _) &&
        (TravelDirector.Instance == null || !TravelDirector.Instance.IsTraveling);

    public override void Interact(PlayerInteraction interactor)
    {
        if (CanInteract(interactor) && interactor.TryGetComponent(out PlayerInventory inventory))
            inventory.RequestPickUp(pickup);
    }
}
