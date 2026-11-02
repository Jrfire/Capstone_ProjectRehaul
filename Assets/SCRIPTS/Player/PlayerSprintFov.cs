using UnityEngine;

[DefaultExecutionOrder(90)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class PlayerSprintFov : MonoBehaviour
{
    [Range(0f, 30f), Tooltip("Degrees added to the camera FOV while sprinting.")]
    public float fovIncrease = 6f;
    [Min(0.1f), Tooltip("Degrees per second used to ease into and out of the sprint FOV.")]
    public float transitionSpeed = 40f;

    public float CurrentOffset { get; private set; }

    private Camera viewCamera;
    private PlayerMovement movement;
    private PlayerFearEffects fear;

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
        movement = GetComponentInParent<PlayerMovement>();
        fear = GetComponent<PlayerFearEffects>();
    }

    private void LateUpdate()
    {
        bool active = viewCamera.isActiveAndEnabled && movement != null &&
            movement.isLocalPlayer && movement.IsSprinting;
        float target = active ? fovIncrease : 0f;
        ApplyOffset(Mathf.MoveTowards(CurrentOffset, target, transitionSpeed * Time.deltaTime));
    }

    private void OnDisable()
    {
        ApplyOffset(0f);
    }

    private void ApplyOffset(float offset)
    {
        float delta = offset - CurrentOffset;
        if (delta == 0f || viewCamera == null)
            return;

        // Only the change is applied so manual FOV edits and the panic shudder are preserved.
        viewCamera.fieldOfView += delta;
        CurrentOffset = offset;
        if (fear != null)
            fear.ShiftBaseFov(delta);
    }
}
