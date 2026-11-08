using UnityEngine;

[DisallowMultipleComponent]
public sealed class SteamInviteInteractable : Interactable
{
    public override string Prompt => "Invite Steam friends";

    // Readied players wait at the jeepney.
    public override bool CanInteract(PlayerInteraction interactor) => !PlayerPersistence.LocalReadied;

    public override void Interact(PlayerInteraction interactor) => InviteFriends();

    public bool InviteFriends()
    {
        if (SteamSession.Instance == null)
        {
            Debug.LogError("Steam invites require a SteamSession on NetworkManager.", this);
            return false;
        }

        return SteamSession.Instance.OpenInviteDialog();
    }
}
