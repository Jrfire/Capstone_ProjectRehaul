using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera), typeof(PlayerCameraEffects))]
public sealed class PlayerFearEffects : MonoBehaviour
{
    [Header("Response")]
    [Min(0.1f)] public float buildUpSpeed = 5f;
    [Min(0.1f)] public float recoverySpeed = 2f;
    [Tooltip("Solid colliders on these layers block sight. Triggers and this player's colliders are ignored.")]
    public LayerMask occlusionMask = Physics.DefaultRaycastLayers;

    [Header("Panic appearance")]
    [Range(0f, 1f)] public float vignetteStrength = 0.65f;
    [Range(0f, 1f), Tooltip("Opacity of the vertical film scratches at full panic.")]
    public float verticalGrainStrength = 0.2f;
    [Min(1f)] public float verticalLineCount = 60f;
    [Range(0.05f, 2f), Tooltip("Relative scratch width: 1 matches the original thickness; 0.5 is half as thick. Very thin lines may disappear below screen resolution.")]
    public float verticalLineThickness = 0.5f;
    [Min(0f)] public float filmJitterSpeed = 12f;
    [Range(0f, 1f), Tooltip("Extra static noise on top of the film grain at full panic (0 = off). Kept faint so the street stays readable while fleeing.")]
    public float staticStrength = 0.35f;
    [Min(1f), Tooltip("How many times per second the static pattern changes.")]
    public float staticFramesPerSecond = 24f;
    [Range(0f, 5f), Tooltip("Maximum FOV oscillation in degrees. Set to zero to disable camera shudder.")]
    public float fovShudder = 1.25f;
    [Min(0f)] public float shudderFrequency = 9f;

    /// <summary>Sight-driven panic (drives the FOV shudder). Hidden or occluded enemies never raise it.</summary>
    public float CurrentIntensity { get; private set; }
    public float VisibleIntensity { get; private set; }
    /// <summary>Unsmoothed strength of the closest enemy, ignoring sight.</summary>
    public float ProximityIntensity { get; private set; }
    /// <summary>Smoothed max of sight and proximity panic; drives only the vignette and vertical lines.</summary>
    public float OverlayIntensity { get; private set; }
    internal bool CanAffectCamera => isActiveAndEnabled && viewCamera != null &&
        viewCamera.isActiveAndEnabled && (identity == null || identity.isLocalPlayer);

    private Camera viewCamera;
    private NetworkIdentity identity;
    private readonly Plane[] frustum = new Plane[6];
    private readonly Vector3[] samples = new Vector3[7];
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private float baseFov;
    private float appliedFov;
    private bool warnedHitLimit;

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
        identity = GetComponentInParent<NetworkIdentity>();
    }

    private void OnEnable()
    {
        baseFov = viewCamera.fieldOfView;
        appliedFov = baseFov;
    }

    private void LateUpdate()
    {
        if (!CanAffectCamera)
        {
            ResetEffects();
            return;
        }

        // Use the unshuddered FOV for sight checks so the effect cannot trigger itself at screen edges.
        if (!Mathf.Approximately(viewCamera.fieldOfView, appliedFov))
            baseFov = viewCamera.fieldOfView;
        viewCamera.fieldOfView = baseFov;
        GeometryUtility.CalculateFrustumPlanes(viewCamera, frustum);
        VisibleIntensity = 0f;
        ProximityIntensity = 0f;
        foreach (EnemyFearSource source in EnemyFearSource.ActiveSources)
        {
            VisibleIntensity = Mathf.Max(VisibleIntensity, EvaluateSource(source));
            ProximityIntensity = Mathf.Max(ProximityIntensity, EvaluateProximity(source));
        }

        float speed = VisibleIntensity > CurrentIntensity ? buildUpSpeed : recoverySpeed;
        CurrentIntensity = Mathf.MoveTowards(CurrentIntensity, VisibleIntensity,
            Mathf.Max(0.1f, speed) * Time.deltaTime);
        float overlayTarget = Mathf.Max(VisibleIntensity, ProximityIntensity);
        float overlaySpeed = overlayTarget > OverlayIntensity ? buildUpSpeed : recoverySpeed;
        OverlayIntensity = Mathf.Max(CurrentIntensity, Mathf.MoveTowards(OverlayIntensity, overlayTarget,
            Mathf.Max(0.1f, overlaySpeed) * Time.deltaTime));
        float time = Time.time * Mathf.Max(0f, shudderFrequency) * Mathf.PI * 2f;
        float oscillation = (Mathf.Sin(time) + 0.35f * Mathf.Sin(time * 2.37f)) / 1.35f;
        appliedFov = Mathf.Clamp(baseFov + oscillation * Mathf.Clamp(fovShudder, 0f, 5f) *
            CurrentIntensity, 1f, 179f);
        viewCamera.fieldOfView = appliedFov;
    }

    private float EvaluateProximity(EnemyFearSource source)
    {
        Renderer body = source.visibilityRenderer;
        if (body == null || !body.gameObject.activeInHierarchy)
            return 0f;
        return source.ProximityStrengthAtDistance(Vector3.Distance(viewCamera.transform.position, body.bounds.center));
    }

    private float EvaluateSource(EnemyFearSource source)
    {
        Renderer body = source.visibilityRenderer;
        if (body == null || !body.enabled || !body.gameObject.activeInHierarchy ||
            body.forceRenderingOff || body.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
            (viewCamera.cullingMask & (1 << body.gameObject.layer)) == 0)
            return 0f;

        Bounds bounds = body.bounds;
        float strength = source.StrengthAtDistance(Vector3.Distance(viewCamera.transform.position, bounds.center));
        if (strength <= VisibleIntensity || !GeometryUtility.TestPlanesAABB(frustum, bounds))
            return 0f;

        samples[0] = bounds.center;
        // Face centers let a partially revealed enemy count even when its center is hidden.
        samples[1] = bounds.center + Vector3.right * bounds.extents.x;
        samples[2] = bounds.center - Vector3.right * bounds.extents.x;
        samples[3] = bounds.center + Vector3.up * bounds.extents.y;
        samples[4] = bounds.center - Vector3.up * bounds.extents.y;
        samples[5] = bounds.center + Vector3.forward * bounds.extents.z;
        samples[6] = bounds.center - Vector3.forward * bounds.extents.z;
        foreach (Vector3 point in samples)
        {
            Vector3 viewport = viewCamera.WorldToViewportPoint(point);
            if (viewport.z >= viewCamera.nearClipPlane && viewport.z <= viewCamera.farClipPlane &&
                viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f &&
                HasLineOfSight(point, source.transform))
                return strength;
        }
        return 0f;
    }

    private bool HasLineOfSight(Vector3 point, Transform source)
    {
        Vector3 origin = viewCamera.transform.position;
        Vector3 ray = point - origin;
        int count = Physics.RaycastNonAlloc(origin, ray.normalized, hits, ray.magnitude,
            occlusionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length)
        {
            if (!warnedHitLimit)
            {
                Debug.LogWarning("Panic sight ray hit its 32-collider limit; treating this ray as blocked. Narrow the occlusion mask.", this);
                warnedHitLimit = true;
            }
            return false;
        }
        Transform playerRoot = identity != null ? identity.transform : transform;
        for (int index = 0; index < count; index++)
        {
            Transform hit = hits[index].transform;
            if (!hit.IsChildOf(source) && !hit.IsChildOf(playerRoot))
                return false;
        }
        return true;
    }

    private void OnDisable()
    {
        ResetEffects();
    }

    internal void ShiftBaseFov(float delta)
    {
        baseFov += delta;
        appliedFov += delta;
    }

    private void ResetEffects()
    {
        CurrentIntensity = 0f;
        VisibleIntensity = 0f;
        ProximityIntensity = 0f;
        OverlayIntensity = 0f;
        if (viewCamera != null)
        {
            if (Mathf.Approximately(viewCamera.fieldOfView, appliedFov))
                viewCamera.fieldOfView = baseFov;
            else
                baseFov = viewCamera.fieldOfView;
            appliedFov = viewCamera.fieldOfView;
        }
    }
}
