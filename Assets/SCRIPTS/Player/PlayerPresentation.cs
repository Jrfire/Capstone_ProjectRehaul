using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerPresentation : NetworkBehaviour
{
    [SerializeField] private GameObject firstPersonView;
    [SerializeField] private GameObject worldView;

    private bool viewsConfigured;
    private bool clientStarted;
    private bool localPlayerStarted;

    private void Awake()
    {
        viewsConfigured = firstPersonView != null && worldView != null && firstPersonView != worldView;

        if (!viewsConfigured)
        {
            Debug.LogError("PlayerPresentation requires separate first-person and world-view roots.", this);
            enabled = false;
            return;
        }

        RefreshViews();
    }

    public override void OnStartClient()
    {
        clientStarted = true;
        localPlayerStarted = isLocalPlayer;
        RefreshViews();
    }

    public override void OnStartLocalPlayer()
    {
        localPlayerStarted = true;
        RefreshViews();
    }

    public override void OnStopLocalPlayer()
    {
        localPlayerStarted = false;
        RefreshViews();
    }

    public override void OnStopClient()
    {
        clientStarted = false;
        localPlayerStarted = false;
        RefreshViews();
    }

    private void OnEnable()
    {
        RefreshViews();
    }

    private void OnDisable()
    {
        RefreshViews();
    }

    private void RefreshViews()
    {
        if (!viewsConfigured)
            return;

        bool visible = isActiveAndEnabled && clientStarted;
        firstPersonView.SetActive(visible && localPlayerStarted);
        worldView.SetActive(visible && !localPlayerStarted);
    }
}
