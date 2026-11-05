using UnityEngine;

/// <summary>
/// Base for objects the local player can use with <c>Player/Use</c> (E) by looking at one of their non-trigger colliders.
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    /// <summary>Usable-action text shown on the HUD nametag's prompt line (NametagHud Prompt Format, "[E] {0}").</summary>
    public abstract string Prompt { get; }

    /// <summary>Whether the local player may currently target and use this object.</summary>
    public virtual bool CanInteract(PlayerInteraction interactor) => true;

    public abstract void Interact(PlayerInteraction interactor);
}
