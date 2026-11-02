using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity), typeof(CharacterController))]
public sealed class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField, Min(0f)] private float movementSpeed = 5f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    private CharacterController characterController;
    private PlayerStamina stamina;
    private InputActionAsset runtimeInputActions;
    private InputAction movementAction;
    private InputAction sprintAction;
    private float verticalVelocity;

    public bool IsMoving { get; private set; }
    public bool IsSprinting { get; private set; }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        characterController.enabled = false;
        stamina = GetComponent<PlayerStamina>();
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
        sprintAction = runtimeInputActions.FindAction("Player/Sprint");
        if (sprintAction == null || sprintAction.type != InputActionType.Button)
        {
            Debug.LogError("PlayerMovement needs a Button Player/Sprint action to sprint; walking still works.", this);
            sprintAction = null;
        }
        if (stamina == null)
            Debug.LogError("PlayerMovement needs a PlayerStamina component to sprint; walking still works.", this);
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
            IsSprinting = false;
            return;
        }

        Vector2 input = Vector2.ClampMagnitude(movementAction.ReadValue<Vector2>(), 1f);
        Vector3 direction = transform.TransformDirection(new Vector3(input.x, 0f, input.y));
        bool sprintRequested = sprintAction != null && stamina != null && sprintAction.IsPressed() &&
            input.sqrMagnitude > 0.0001f && stamina.CanUseStamina;

        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity -= gravity * Time.deltaTime;
        float speed = movementSpeed * (sprintRequested ? sprintMultiplier : 1f);
        Vector3 velocity = direction * speed + Vector3.up * verticalVelocity;
        Vector3 before = transform.position;
        characterController.Move(velocity * Time.deltaTime);
        Vector3 displacement = transform.position - before;
        IsMoving = new Vector2(displacement.x, displacement.z).sqrMagnitude > 0.000001f;
        // Stamina only drains when sprinting actually moved the player, not when pushing into a wall.
        IsSprinting = sprintRequested && IsMoving && stamina.TrySprint(Time.deltaTime);
    }

    private void SetMovementEnabled(bool active)
    {
        foreach (InputAction action in new[] { movementAction, sprintAction })
        {
            if (action == null)
                continue;
            if (active)
                action.Enable();
            else
                action.Disable();
        }

        if (characterController != null)
            characterController.enabled = active;

        if (!active)
        {
            verticalVelocity = 0f;
            IsMoving = false;
            IsSprinting = false;
        }
    }

    private void ReleaseInput()
    {
        SetMovementEnabled(false);
        movementAction = null;
        sprintAction = null;

        if (runtimeInputActions != null)
        {
            Destroy(runtimeInputActions);
            runtimeInputActions = null;
        }
    }
}
