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
    [SerializeField, Min(0.1f), Tooltip("Maximum aim distance for nametags. Other players' names use all of it; objects are also limited by their Nametag's Max Distance.")]
    private float nametagDistance = 12f;
    [SerializeField] private LayerMask interactionLayers = Physics.DefaultRaycastLayers;

    private InputAction useAction;
    public Interactable CurrentTarget { get; private set; }
    /// <summary>Name of the aimed player or <see cref="Nametag"/> object, or null. Read by the HUD's NametagHud.</summary>
    public string AimedName { get; private set; }
    /// <summary>The aimed other player, if any.</summary>
    public PlayerName AimedPlayer { get; private set; }

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
        ClearAim();
    }

    private void OnDestroy() => ReleaseInput();

    private void Update()
    {
        ClearAim();
        if (!isLocalPlayer || useAction == null || !useAction.enabled ||
            !playerCamera.isActiveAndEnabled || PlayerControlLock.IsLocked ||
            (SteamSession.Instance != null && SteamSession.Instance.OverlayActive))
            return;

        var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        float reach = Mathf.Max(useDistance, nametagDistance);
        bool hasHit = Physics.Raycast(ray, out RaycastHit hit, reach, interactionLayers, QueryTriggerInteraction.Ignore);
        bool hitPlayer = hasHit && hit.collider.GetComponentInParent<PlayerName>() != null;

        // Other players' controllers are disabled on this peer, so test their capsules; nearer solids still block.
        float playerReach = Mathf.Min(nametagDistance, hasHit && !hitPlayer ? hit.distance : reach);
        PlayerName aimed = FindAimedPlayer(ray, playerReach);
        if (aimed != null)
        {
            AimedPlayer = aimed;
            AimedName = aimed.DisplayName;
        }
        else if (hasHit && !hitPlayer)
        {
            Nametag nametag = hit.collider.GetComponentInParent<Nametag>();
            if (nametag != null && nametag.isActiveAndEnabled && hit.distance <= Mathf.Min(nametag.MaxDistance, nametagDistance) &&
                !string.IsNullOrWhiteSpace(nametag.DisplayName))
                AimedName = nametag.DisplayName;

            Interactable target = hit.distance <= useDistance ? hit.collider.GetComponentInParent<Interactable>() : null;
            if (target != null && target.isActiveAndEnabled && target.CanInteract(this))
                CurrentTarget = target;
        }

        if (CurrentTarget != null && useAction.WasPressedThisFrame())
            CurrentTarget.Interact(this);
    }

    private PlayerName FindAimedPlayer(Ray ray, float maxDistance)
    {
        PlayerName best = null;
        float bestDistance = float.MaxValue;
        foreach (PlayerName player in PlayerName.Active)
        {
            if (player == null || player.gameObject == gameObject)
                continue;
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead)
                continue;
            if (player.TryAimDistance(ray, maxDistance, out float distance) && distance < bestDistance)
            {
                best = player;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void ClearAim()
    {
        CurrentTarget = null;
        AimedName = null;
        AimedPlayer = null;
    }

    private void ReleaseInput()
    {
        useAction?.Dispose();
        useAction = null;
        ClearAim();
    }
}
