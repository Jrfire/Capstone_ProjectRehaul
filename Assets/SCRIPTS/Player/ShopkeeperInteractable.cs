using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The shopkeeper NPC (E): opens the <see cref="ShopScreen"/> where players buy this quota's <see cref="ShopStock"/>
/// with the team's money. The host only accepts purchases from players near an active shopkeeper.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShopkeeperInteractable : Interactable
{
    [SerializeField, Tooltip("Shop UI prefab, instantiated once and shared by every shopkeeper.")]
    private ShopScreen screenPrefab;

    private static readonly List<ShopkeeperInteractable> active = new List<ShopkeeperInteractable>();

    public ShopScreen ScreenPrefab => screenPrefab;
    public static IReadOnlyList<ShopkeeperInteractable> Active => active;

    public override string Prompt => "Shop";

    public override bool CanInteract(PlayerInteraction interactor) =>
        screenPrefab != null && ShopStock.Instance != null && AgimatBank.Instance != null && !PlayerPersistence.LocalReadied &&
        (TravelDirector.Instance == null || !TravelDirector.Instance.IsTraveling) &&
        (RoundClock.Instance == null || !RoundClock.Instance.enabled || RoundClock.Instance.Phase != RoundPhase.GameOver);

    public override void Interact(PlayerInteraction interactor)
    {
        if (CanInteract(interactor))
            ShopScreen.Open(screenPrefab);
    }

    /// <summary>True when an active shopkeeper stands within <paramref name="range"/> metres of <paramref name="position"/>.</summary>
    public static bool AnyWithin(Vector3 position, float range)
    {
        foreach (ShopkeeperInteractable shopkeeper in active)
        {
            if (shopkeeper != null && (shopkeeper.transform.position - position).sqrMagnitude <= range * range)
                return true;
        }
        return false;
    }

    private void OnEnable() => active.Add(this);

    private void OnDisable() => active.Remove(this);
}
