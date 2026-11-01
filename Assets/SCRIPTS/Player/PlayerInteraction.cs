using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerInteraction : NetworkBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Camera playerCamera;
    [SerializeField, Min(0.1f)] private float useDistance = 3.5f;
    [SerializeField] private LayerMask interactionLayers = Physics.DefaultRaycastLayers;

    private InputAction useAction;
    public SteamInviteInteractable CurrentTarget { get; private set; }

    public override void OnStartLocalPlayer()
    {
        InputAction configuredAction = inputActions != null ? inputActions.FindAction("Player/Use") : null;
        if (configuredAction == null || configuredAction.type != InputActionType.Button ||
            playerCamera == null || !playerCamera.transform.IsChildOf(transform))
        {
            Debug.LogError("PlayerInteraction requires a Button Player/Use action and its own child player camera.", this);
            enabled = false;
            return;
        }

        useAction = configuredAction.Clone();
        if (isActiveAndEnabled)
            useAction.Enable();
    }

    public override void OnStopLocalPlayer() => ReleaseInput();
    public override void OnStopClient() => ReleaseInput();

    private void OnEnable()
    {
        if (isLocalPlayer)
            useAction?.Enable();
    }

    private void OnDisable()
    {
        useAction?.Disable();
        CurrentTarget = null;
    }

    private void OnDestroy() => ReleaseInput();

    private void Update()
    {
        CurrentTarget = null;
        if (!isLocalPlayer || useAction == null || !useAction.enabled ||
            !playerCamera.isActiveAndEnabled ||
            (SteamSession.Instance != null && SteamSession.Instance.OverlayActive))
            return;

        var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, useDistance, interactionLayers, QueryTriggerInteraction.Ignore))
        {
            SteamInviteInteractable target = hit.collider.GetComponentInParent<SteamInviteInteractable>();
            if (target != null && target.isActiveAndEnabled)
                CurrentTarget = target;
        }

        if (CurrentTarget != null && useAction.WasPressedThisFrame())
            CurrentTarget.InviteFriends();
    }

    private void OnGUI()
    {
        if (isLocalPlayer && CurrentTarget != null)
            GUI.Box(new Rect(Screen.width / 2f - 120, Screen.height - 70, 240, 30), "E: Invite Steam friends");
    }

    private void ReleaseInput()
    {
        useAction?.Dispose();
        useAction = null;
        CurrentTarget = null;
    }
}
