using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactable (E) that opens the travel map listing <see cref="Destinations"/>. Any player may confirm a
/// destination; the trip itself starts from a <see cref="TravelInteractable"/> such as the jeepney.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapTerminal : Interactable
{
    [SerializeField, Tooltip("Destinations players can travel to, each with the enemy types it can roll. Scenes must be in the Build Profile's scene list; the current scene is shown grayed out.")]
    private List<TravelDestination> destinations = new List<TravelDestination>();
    [SerializeField, Tooltip("Travel map UI prefab, instantiated once and shared by every terminal.")]
    private TravelMapScreen mapScreenPrefab;

    private readonly List<string> destinationNames = new List<string>();

    public IReadOnlyList<string> Destinations
    {
        get
        {
            destinationNames.Clear();
            foreach (TravelDestination destination in destinations)
            {
                if (destination != null)
                    destinationNames.Add(destination.Name);
            }
            return destinationNames;
        }
    }

    public IReadOnlyList<TravelDestination> DestinationEntries => destinations;
    public TravelMapScreen MapScreenPrefab => mapScreenPrefab;

    public override string Prompt => "Open travel map";

    /// <summary>The travel map only works at base; elsewhere the jeepney back always heads home.</summary>
    public static bool TravelMapAvailable =>
        RoundClock.Instance == null || !RoundClock.Instance.enabled || RoundClock.Instance.InBase;

    public override bool CanInteract(PlayerInteraction interactor) =>
        mapScreenPrefab != null && TravelMapAvailable && !PlayerPersistence.LocalReadied &&
        (TravelDirector.Instance == null || !TravelDirector.Instance.IsTraveling);

    public override void Interact(PlayerInteraction interactor)
    {
        if (TravelMapAvailable)
            TravelMapScreen.Open(mapScreenPrefab, this);
    }

    private void OnEnable()
    {
        // Terminals inside generated street blocks appear after the scene loads; roll their destinations then.
        if (TravelDirector.Instance != null)
            TravelDirector.Instance.ServerRollDestinations(this);
    }

    public bool Lists(string sceneName) => Find(sceneName) != null;

    public TravelDestination Find(string sceneName)
    {
        foreach (TravelDestination destination in destinations)
        {
            if (destination != null && !string.IsNullOrEmpty(sceneName) && destination.Name == sceneName)
                return destination;
        }
        return null;
    }
}
