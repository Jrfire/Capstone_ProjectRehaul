using Mirror;
using UnityEngine;

/// <summary>
/// Cheap contact shadow: a soft dark circle laid flat on the ground under <see cref="Follow"/> (e.g. a snapped
/// WorldSprites child). Runs locally on every peer after presentation scripts so it never lags the visible sprite.
/// Needs a MeshRenderer with a quad (and the Capstone/Blob Shadow material) on the same object; it has no collider.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class BlobShadow : MonoBehaviour
{
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    [SerializeField, Tooltip("Transform whose horizontal position the shadow follows. Defaults to the parent.")]
    private Transform follow;
    [SerializeField, Min(0.05f), Tooltip("Shadow diameter in metres.")]
    private float diameter = 1.3f;
    [SerializeField, Range(0f, 1f)] private float opacity = 0.7f;
    [SerializeField, Min(0.1f), Tooltip("How far below the followed transform the ground is searched for.")]
    private float maxGroundDistance = 5f;
    [SerializeField, Min(0f), Tooltip("Lift above the ground to avoid z-fighting.")]
    private float groundOffset = 0.02f;
    [SerializeField] private LayerMask groundMask = Physics.DefaultRaycastLayers;

    private readonly RaycastHit[] hits = new RaycastHit[8];
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock block;
    private Transform ignoredRoot;

    public Transform Follow => follow;
    public float Diameter => diameter;
    public float Opacity => opacity;
    public bool IsGrounded { get; private set; }

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (follow == null)
            follow = transform.parent;
        NetworkIdentity identity = GetComponentInParent<NetworkIdentity>();
        ignoredRoot = identity != null ? identity.transform : transform.parent;
        block = new MaterialPropertyBlock();
    }

    private void OnEnable() => Refresh();

    private void LateUpdate() => Refresh();

    /// <summary>Moves the shadow onto the ground below the followed transform; hides it when no ground is found.</summary>
    public void Refresh()
    {
        if (meshRenderer == null)
            return;
        RaycastHit ground = default;
        IsGrounded = follow != null && TryFindGround(follow.position, out ground);
        meshRenderer.enabled = IsGrounded;
        if (!IsGrounded)
            return;

        transform.SetPositionAndRotation(ground.point + ground.normal * groundOffset,
            Quaternion.FromToRotation(Vector3.up, ground.normal) * Quaternion.Euler(90f, 0f, 0f));
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(diameter / SafeScale(parentScale.x), diameter / SafeScale(parentScale.z),
            1f / SafeScale(parentScale.y));

        meshRenderer.GetPropertyBlock(block);
        block.SetFloat(OpacityId, opacity);
        meshRenderer.SetPropertyBlock(block);
    }

    private bool TryFindGround(Vector3 origin, out RaycastHit ground)
    {
        ground = default;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, maxGroundDistance, groundMask,
            QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (ignoredRoot != null && hit.collider.transform.IsChildOf(ignoredRoot))
                continue;
            if (hit.distance < best)
            {
                best = hit.distance;
                ground = hit;
            }
        }
        return best < float.PositiveInfinity;
    }

    private static float SafeScale(float value) => Mathf.Abs(value) < 0.0001f ? 1f : Mathf.Abs(value);
}
