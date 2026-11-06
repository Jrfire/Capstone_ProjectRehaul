using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Albularyo NPC (E): opens the <see cref="AlbularyoScreen"/> where players sacrifice agimats from the shared
/// <see cref="AgimatBank"/>. The host only accepts sacrifices from players near an active Albularyo.
/// </summary>
[DisallowMultipleComponent]
public sealed class AlbularyoInteractable : Interactable
{
    [SerializeField, Tooltip("Sacrifice UI prefab, instantiated once and shared by every Albularyo.")]
    private AlbularyoScreen screenPrefab;

    private static readonly List<AlbularyoInteractable> active = new List<AlbularyoInteractable>();

    public AlbularyoScreen ScreenPrefab => screenPrefab;
    public static IReadOnlyList<AlbularyoInteractable> Active => active;

    public override string Prompt => "Talk";

    public override bool CanInteract(PlayerInteraction interactor) =>
        screenPrefab != null && AgimatBank.Instance != null &&
        (TravelDirector.Instance == null || !TravelDirector.Instance.IsTraveling) &&
        (RoundClock.Instance == null || !RoundClock.Instance.enabled || RoundClock.Instance.Phase != RoundPhase.GameOver);

    public override void Interact(PlayerInteraction interactor)
    {
        if (CanInteract(interactor))
            AlbularyoScreen.Open(screenPrefab);
    }

    /// <summary>True when an active Albularyo stands within <paramref name="range"/> metres of <paramref name="position"/>.</summary>
    public static bool AnyWithin(Vector3 position, float range)
    {
        foreach (AlbularyoInteractable albularyo in active)
        {
            if (albularyo != null && (albularyo.transform.position - position).sqrMagnitude <= range * range)
                return true;
        }
        return false;
    }

    private void OnEnable() => active.Add(this);

    private void OnDisable() => active.Remove(this);
}
