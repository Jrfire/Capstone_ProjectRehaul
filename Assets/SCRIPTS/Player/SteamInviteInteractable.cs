using UnityEngine;

[DisallowMultipleComponent]
public sealed class SteamInviteInteractable : MonoBehaviour
{
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
