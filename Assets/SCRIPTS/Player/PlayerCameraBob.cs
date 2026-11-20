using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Owner-only head bob while walking/sprinting (Settings > Graphics > Camera Bobbing). The offset is applied only while
/// this camera renders and removed right after, so the camera transform used by gameplay (interaction rays, fear
/// checks, spectating) never moves.
/// </summary>
[DefaultExecutionOrder(95)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class PlayerCameraBob : MonoBehaviour
{
    [Min(0.1f), Tooltip("Metres walked per full bob cycle (two footsteps).")]
    public float strideLength = 2.4f;
    [Min(0f), Tooltip("Vertical bob in metres while walking.")]
    public float walkHeight = 0.035f;
    [Min(0f), Tooltip("Vertical bob in metres while sprinting.")]
    public float sprintHeight = 0.06f;
    [Range(0f, 1f), Tooltip("Side sway as a fraction of the vertical bob.")]
    public float swayFraction = 0.5f;
    [Min(0.1f), Tooltip("How quickly the bob fades in/out when starting or stopping.")]
    public float blendSpeed = 6f;

    /// <summary>Parent-space offset (player root: x right, y up) applied during rendering (zero when idle or disabled).</summary>
    public Vector3 CurrentOffset { get; private set; }

    private Camera viewCamera;
    private PlayerMovement movement;
    private PlayerHealth health;
    private Transform root;
    private Vector3 lastRootPosition;
    private float phase;
    private float weight;
    private float amplitude;
    private bool applied;
    private Vector3 appliedOffset;

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
        movement = GetComponentInParent<PlayerMovement>();
        health = GetComponentInParent<PlayerHealth>();
        root = movement != null ? movement.transform : transform.root;
    }

    private void OnEnable()
    {
        lastRootPosition = root.position;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        Restore();
        weight = 0f;
        CurrentOffset = Vector3.zero;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        Vector3 position = root.position;
        Vector3 moved = position - lastRootPosition;
        lastRootPosition = position;
        moved.y = 0f;
        float speed = deltaTime > 0f ? moved.magnitude / deltaTime : 0f;

        bool active = GameSettings.CameraBob && movement != null && movement.isLocalPlayer && movement.IsMoving &&
            (health == null || !health.IsDead) && speed < 20f;
        weight = Mathf.MoveTowards(weight, active ? 1f : 0f, blendSpeed * deltaTime);
        if (active)
        {
            phase = Mathf.Repeat(phase + speed * deltaTime / strideLength * Mathf.PI * 2f, Mathf.PI * 2f);
            amplitude = Mathf.MoveTowards(amplitude, movement.IsSprinting ? sprintHeight : walkHeight, deltaTime * 0.2f);
        }
        else if (weight <= 0f)
        {
            phase = 0f;
        }

        CurrentOffset = weight <= 0f ? Vector3.zero : Offset(phase, amplitude * weight, swayFraction);
    }

    /// <summary>Bob offset for a phase (radians per stride): two dips per stride, one sway left and right.</summary>
    public static Vector3 Offset(float phase, float height, float sway) =>
        new Vector3(Mathf.Sin(phase) * height * sway, -Mathf.Abs(Mathf.Sin(phase)) * height, 0f);

    private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != viewCamera || applied || CurrentOffset == Vector3.zero)
            return;
        appliedOffset = CurrentOffset;
        transform.localPosition += appliedOffset;
        applied = true;
    }

    private void OnEndCamera(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering == viewCamera)
            Restore();
    }

    private void Restore()
    {
        if (!applied)
            return;
        transform.localPosition -= appliedOffset;
        applied = false;
    }
}
