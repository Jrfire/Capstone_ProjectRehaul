using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player flashlight toggled with Player/Flashlight (F). The owner sees a first-person spot light on its camera; other
/// players see a weaker beam on the world body that follows the owner's synced pitch. Both use a generated cookie with a
/// bright hotspot and a brighter rim, and flicker locally while an enemy stands in the beam.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerFlashlight : NetworkBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Camera playerCamera;
    [SerializeField, Tooltip("First-person beam under the owner's camera (FirstPersonView).")]
    private Light ownerLight;
    [SerializeField, Tooltip("Weaker beam under WorldView that other players see.")]
    private Light remoteLight;

    [Header("Beam shape (cookie)")]
    [SerializeField, Range(0.02f, 0.6f), Tooltip("Radius of the bright centre spot, as a fraction of the cone.")]
    private float hotspotSize = 0.2f;
    [SerializeField, Range(0f, 1f)] private float hotspotBrightness = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Brightness between the centre spot and the rim.")]
    private float midBrightness = 0.45f;
    [SerializeField, Range(0.3f, 0.98f), Tooltip("Radius of the brighter reflector rim, as a fraction of the cone.")]
    private float rimRadius = 0.82f;
    [SerializeField, Range(0.01f, 0.3f)] private float rimWidth = 0.07f;
    [SerializeField, Range(0f, 1f)] private float rimBrightness = 0.8f;

    [Header("Flicker while an enemy is in the beam")]
    [SerializeField, Min(0f)] private float flickerSpeed = 14f;
    [SerializeField, Range(0f, 1f), Tooltip("Brightness multiplier during a flicker drop-out.")]
    private float flickerMinimum = 0.12f;
    [SerializeField, Tooltip("Solid colliders on these layers stop the beam from reaching an enemy.")]
    private LayerMask occlusionMask = Physics.DefaultRaycastLayers;

    [SyncVar] private bool isOn = true;
    [SyncVar] private float pitch;

    private InputAction toggleAction;
    private PlayerHealth health;
    private Texture2D cookie;
    private float ownerBaseIntensity;
    private float remoteBaseIntensity;
    private float flickerSeed;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    public bool IsOn => isOn;
    public float SyncedPitch => pitch;
    public Light OwnerLight => ownerLight;
    public Light RemoteLight => remoteLight;
    public Texture2D Cookie => cookie;
    public bool OwnerFlickering { get; private set; }
    public bool RemoteFlickering { get; private set; }

    private void Awake()
    {
        // The owner decides on/off and aim like its own movement; Mirror relays it to everyone else.
        syncDirection = SyncDirection.ClientToServer;
        flickerSeed = Random.Range(0f, 100f);
        health = GetComponent<PlayerHealth>();
        cookie = BuildCookie(128);
        if (ownerLight != null)
        {
            ownerBaseIntensity = ownerLight.intensity;
            ownerLight.cookie = cookie;
        }
        if (remoteLight != null)
        {
            remoteBaseIntensity = remoteLight.intensity;
            remoteLight.cookie = cookie;
        }
    }

    public override void OnStartLocalPlayer()
    {
        InputAction configured = inputActions != null ? inputActions.FindAction("Player/Flashlight") : null;
        if (configured == null || configured.type != InputActionType.Button)
        {
            Debug.LogError("PlayerFlashlight requires a Button Player/Flashlight action.", this);
            return;
        }
        toggleAction = GameSettings.TrackBindings(configured.Clone());
        if (isActiveAndEnabled)
            toggleAction.Enable();
    }

    public override void OnStopLocalPlayer() => ReleaseInput();
    public override void OnStopClient() => ReleaseInput();

    private void OnEnable()
    {
        if (isLocalPlayer)
            toggleAction?.Enable();
    }

    private void OnDisable() => toggleAction?.Disable();

    private void OnDestroy()
    {
        ReleaseInput();
        if (cookie != null)
            Destroy(cookie);
    }

    /// <summary>Owner-only toggle (also used by the F key).</summary>
    public void Toggle()
    {
        if (isOwned)
            isOn = !isOn;
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            if (toggleAction != null && toggleAction.WasPressedThisFrame() && !PlayerControlLock.IsLocked &&
                (SteamSession.Instance == null || !SteamSession.Instance.OverlayActive))
                Toggle();
            if (playerCamera != null)
            {
                float cameraPitch = Mathf.DeltaAngle(0f, playerCamera.transform.localEulerAngles.x);
                if (Mathf.Abs(cameraPitch - pitch) > 0.5f)
                    pitch = cameraPitch;
            }
        }

        if (remoteLight != null)
            remoteLight.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        OwnerFlickering = Apply(ownerLight, ownerBaseIntensity);
        RemoteFlickering = Apply(remoteLight, remoteBaseIntensity);
    }

    private bool Apply(Light beam, float baseIntensity)
    {
        if (beam == null)
            return false;
        // A dead player's flashlight goes dark for everyone.
        bool lit = isOn && (health == null || !health.IsDead);
        beam.enabled = lit;
        if (!lit || !beam.gameObject.activeInHierarchy)
            return false;
        bool flickering = EnemyInBeam(beam);
        beam.intensity = baseIntensity * (flickering ? FlickerFactor(Time.time) : 1f);
        return flickering;
    }

    public float FlickerFactor(float time)
    {
        float noise = Mathf.PerlinNoise(time * Mathf.Max(0f, flickerSpeed), flickerSeed);
        // Hard drop-outs instead of smooth dimming, to match the snappy low-poly look.
        return noise < 0.45f ? flickerMinimum : Mathf.Lerp(0.7f, 1f, noise);
    }

    private bool EnemyInBeam(Light beam)
    {
        Vector3 origin = beam.transform.position;
        Vector3 forward = beam.transform.forward;
        float cosHalfAngle = Mathf.Cos(beam.spotAngle * 0.5f * Mathf.Deg2Rad);
        foreach (EnemyFearSource source in EnemyFearSource.ActiveSources)
        {
            Renderer body = source.visibilityRenderer;
            if (body == null || !body.gameObject.activeInHierarchy)
                continue;
            Vector3 toEnemy = body.bounds.center - origin;
            float distance = toEnemy.magnitude;
            if (distance > beam.range || distance < 0.001f ||
                Vector3.Dot(toEnemy / distance, forward) < cosHalfAngle)
                continue;
            if (Reaches(origin, toEnemy / distance, distance, source.transform))
                return true;
        }
        return false;
    }

    private bool Reaches(Vector3 origin, Vector3 direction, float distance, Transform enemy)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, occlusionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length)
            return false;
        for (int index = 0; index < count; index++)
        {
            Transform hit = hits[index].transform;
            if (!hit.IsChildOf(enemy) && !hit.IsChildOf(transform))
                return false;
        }
        return true;
    }

    private Texture2D BuildCookie(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            name = "Flashlight Beam Cookie",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float radius = new Vector2((x - half) / half, (y - half) / half).magnitude;
                byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(BeamProfile(radius)) * 255f);
                pixels[y * size + x] = new Color32(value, value, value, value);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>Cookie brightness at a normalized cone radius (0 = centre, 1 = cone edge).</summary>
    public float BeamProfile(float radius)
    {
        if (radius >= 1f)
            return 0f;
        float hotspot = hotspotBrightness * (1f - Smooth(0f, hotspotSize, radius));
        float body = midBrightness * (1f - Smooth(rimRadius, 1f, radius));
        float rim = rimBrightness * Mathf.Exp(-Mathf.Pow((radius - rimRadius) / rimWidth, 2f));
        float edgeFade = 1f - Smooth(0.92f, 1f, radius);
        return Mathf.Max(hotspot, Mathf.Max(body, rim)) * edgeFade;
    }

    // HLSL-style smoothstep (Mathf.SmoothStep interpolates between its first two arguments instead).
    private static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private void ReleaseInput()
    {
        toggleAction?.Dispose();
        toggleAction = null;
    }
}
