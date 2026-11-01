using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity), typeof(CharacterController))]
public sealed class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField, Min(0f)] private float movementSpeed = 5f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    private CharacterController characterController;
    private InputActionAsset runtimeInputActions;
    private InputAction movementAction;
    private float verticalVelocity;

    public bool IsMoving { get; private set; }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        characterController.enabled = false;
    }

    public override void OnStartLocalPlayer()
    {
        InputAction configuredAction = inputActions != null
            ? inputActions.FindAction("Player/Movement")
            : null;

        if (configuredAction == null || configuredAction.expectedControlType != "Vector2")
        {
            Debug.LogError("PlayerMovement requires an input asset with a Vector2 Player/Movement action.", this);
            enabled = false;
            return;
        }

        runtimeInputActions = Instantiate(inputActions);
        movementAction = runtimeInputActions.FindAction("Player/Movement", true);
        SetMovementEnabled(isActiveAndEnabled);
    }

    public override void OnStopLocalPlayer()
    {
        ReleaseInput();
    }

    public override void OnStopClient()
    {
        ReleaseInput();
    }

    private void OnEnable()
    {
        if (isLocalPlayer && movementAction != null)
            SetMovementEnabled(true);
    }

    private void OnDisable()
    {
        SetMovementEnabled(false);
    }

    private void OnDestroy()
    {
        ReleaseInput();
    }

    private void Update()
    {
        if (!isLocalPlayer || movementAction == null || !movementAction.enabled)
            return;

        if (SteamSession.Instance != null && SteamSession.Instance.OverlayActive)
        {
            IsMoving = false;
            return;
        }

        Vector2 input = Vector2.ClampMagnitude(movementAction.ReadValue<Vector2>(), 1f);
        Vector3 direction = transform.TransformDirection(new Vector3(input.x, 0f, input.y));

        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity -= gravity * Time.deltaTime;
        Vector3 velocity = direction * movementSpeed + Vector3.up * verticalVelocity;
        Vector3 before = transform.position;
        characterController.Move(velocity * Time.deltaTime);
        Vector3 displacement = transform.position - before;
        IsMoving = new Vector2(displacement.x, displacement.z).sqrMagnitude > 0.000001f;
    }

    private void SetMovementEnabled(bool active)
    {
        if (movementAction != null)
        {
            if (active)
                movementAction.Enable();
            else
                movementAction.Disable();
        }

        if (characterController != null)
            characterController.enabled = active;

        if (!active)
        {
            verticalVelocity = 0f;
            IsMoving = false;
        }
    }

    private void ReleaseInput()
    {
        SetMovementEnabled(false);
        movementAction = null;

        if (runtimeInputActions != null)
        {
            Destroy(runtimeInputActions);
            runtimeInputActions = null;
        }
    }
}
