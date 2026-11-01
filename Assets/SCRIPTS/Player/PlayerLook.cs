using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerLook : NetworkBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Camera playerCamera;
    [SerializeField, Min(0.001f)] private float mouseSensitivity = 0.15f;
    [SerializeField, Range(1f, 89f)] private float pitchLimit = 85f;

    private InputAction lookAction;
    private float pitch;
    private bool ownsCursor;
    private bool captureRequested;
    private bool cursorCaptured;
    private bool hasFocus;

    public override void OnStartLocalPlayer()
    {
        InputAction configuredAction = inputActions != null ? inputActions.FindAction("Player/Look") : null;
        if (configuredAction == null || configuredAction.expectedControlType != "Vector2" ||
            playerCamera == null || !playerCamera.transform.IsChildOf(transform))
        {
            Debug.LogError("PlayerLook requires a Vector2 Player/Look action and its own child player camera.", this);
            enabled = false;
            return;
        }

        lookAction = configuredAction.Clone();
        lookAction.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
        pitch = Mathf.DeltaAngle(0f, playerCamera.transform.localEulerAngles.x);
        ownsCursor = true;
        hasFocus = Application.isFocused;
        if (isActiveAndEnabled)
        {
            lookAction.Enable();
            captureRequested = true;
        }
    }

    public override void OnStopLocalPlayer() => ReleaseInput();
    public override void OnStopClient() => ReleaseInput();

    private void OnEnable()
    {
        if (lookAction != null && isLocalPlayer)
        {
            lookAction.Enable();
            captureRequested = true;
        }
    }

    private void OnDisable()
    {
        lookAction?.Disable();
        captureRequested = false;
        SetCursorCaptured(false);
    }

    private void OnDestroy() => ReleaseInput();

    private void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        if (!focused)
        {
            captureRequested = false;
            SetCursorCaptured(false);
        }
    }

    private void Update()
    {
        if (lookAction == null || !isLocalPlayer || !lookAction.enabled)
            return;

        bool overlayActive = SteamSession.Instance != null && SteamSession.Instance.OverlayActive;
        if (hasFocus && !overlayActive && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            captureRequested = !captureRequested;

        bool wasCaptured = cursorCaptured;
        SetCursorCaptured(captureRequested && hasFocus && !overlayActive && playerCamera.isActiveAndEnabled);
        // Discard mouse motion from the frame that returns from the HUD or Steam overlay.
        if (!cursorCaptured || !wasCaptured)
            return;

        Vector2 delta = lookAction.ReadValue<Vector2>() * mouseSensitivity;
        transform.Rotate(Vector3.up, delta.x, Space.World);
        // Mouse delta already measures movement this frame; multiplying by deltaTime changes sensitivity.
        pitch = Mathf.Clamp(pitch - delta.y, -pitchLimit, pitchLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void SetCursorCaptured(bool captured)
    {
        if (!ownsCursor)
            return;

        cursorCaptured = captured;
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private void ReleaseInput()
    {
        SetCursorCaptured(false);
        ownsCursor = false;
        captureRequested = false;
        lookAction?.Dispose();
        lookAction = null;
    }
}
