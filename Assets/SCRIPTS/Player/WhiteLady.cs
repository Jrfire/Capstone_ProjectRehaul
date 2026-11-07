using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The White Lady: a local-only easter-egg apparition, created on the one client chosen by
/// <see cref="WhiteLadyDirector"/>. Nobody else has a copy, so only that player can see or trigger her, and she is no
/// enemy (no damage, senses, fear effects or enemy pool). Her opacity flickers so she looks ethereal.
/// <list type="bullet">
/// <item>Lingering: she stands somewhere and vanishes once the player comes within <see cref="vanishDistance"/>. If the
/// player spotted her but walks more than <see cref="farDespawnDistance"/> away instead, she leaves and comes back near
/// that player after <see cref="farRespawnSeconds"/> (lingering again or peeking).</item>
/// <item>Peeking: she peeks around a building/wall corner near the player; once spotted (or approached) she slips behind
/// the corner and vanishes.</item>
/// <item>After vanishing (either way) she reappears close to the player after
/// <see cref="minReappearSeconds"/>-<see cref="maxReappearSeconds"/>; when the player's camera centre lands on her she
/// lunges face-first at the camera jumpscare-style and disappears for good.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class WhiteLady : MonoBehaviour
{
    public enum Phase
    {
        Waiting,
        Lingering,
        Hidden,
        Stalking,
        Lunging,
        Gone,
        Away,
        Peeking,
        Retreating,
    }

    /// <summary>A corner spot: she peeks at <see cref="Peek"/> and slips back to <see cref="Hide"/> (both on the floor).</summary>
    public readonly struct PeekSpot
    {
        public PeekSpot(Vector3 peek, Vector3 hide)
        {
            Peek = peek;
            Hide = hide;
        }

        public Vector3 Peek { get; }
        public Vector3 Hide { get; }
    }

    [SerializeField, Tooltip("Sprite shown (and hidden) for the apparition; billboarded by its own BillboardSprite. Its pivot sits on her face, so the lunge closes in on the face.")]
    private SpriteRenderer worldSprite;
    [SerializeField, Min(0.5f), Tooltip("She vanishes when the player comes this close (horizontal metres) on the first sighting.")]
    private float vanishDistance = 3f;
    [SerializeField, Min(0f)] private float minReappearSeconds = 30f;
    [SerializeField, Min(0f)] private float maxReappearSeconds = 120f;
    [SerializeField, Min(1f), Tooltip("She reappears this close to the player (metres, NavMesh distance range).")]
    private float reappearMinDistance = 6f;
    [SerializeField, Min(1f)] private float reappearMaxDistance = 10f;
    [SerializeField, Min(1f), Tooltip("Furthest camera distance at which looking at her starts the lunge (or counts as spotting her).")]
    private float lookTriggerDistance = 25f;
    [SerializeField, Min(0.05f), Tooltip("Seconds the lunge at the camera takes.")]
    private float lungeSeconds = 0.35f;
    [SerializeField, Min(1f), Tooltip("Sprite scale multiplier reached at the end of the lunge.")]
    private float lungeScale = 1.6f;
    [SerializeField, Min(0f), Tooltip("The lunge ends with her face this far in front of the camera.")]
    private float lungeEndDistance = 0.35f;

    [Header("Flicker")]
    [SerializeField, Range(0f, 1f), Tooltip("Lowest opacity of the ethereal flicker (multiplied with the distance fade).")]
    private float flickerMinAlpha = 0.25f;
    [SerializeField, Min(0f), Tooltip("How fast her opacity drifts up and down.")]
    private float flickerSpeed = 2.5f;
    [SerializeField, Min(0f), Tooltip("Average number of brief drop-outs (she vanishes for a blink) per second.")]
    private float flickerDropoutsPerSecond = 0.35f;
    [SerializeField, Min(0f)] private float flickerDropoutSeconds = 0.08f;

    [Header("Leaving when avoided")]
    [SerializeField, Min(1f), Tooltip("If the player spotted her (Lingering) but moved this far away (horizontal metres), she leaves.")]
    private float farDespawnDistance = 25f;
    [SerializeField, Min(0f), Tooltip("Seconds after leaving before she comes back near that player.")]
    private float farRespawnSeconds = 30f;
    [SerializeField, Min(1f), Tooltip("Distance range from the player where she comes back to linger.")]
    private float respawnMinDistance = 10f;
    [SerializeField, Min(1f)] private float respawnMaxDistance = 18f;
    [SerializeField, Range(1f, 45f), Tooltip("She counts as spotted when the camera points within this angle of her face (with a clear line of sight).")]
    private float spotAngle = 15f;

    [Header("Corner peeking")]
    [SerializeField, Range(0f, 1f), Tooltip("Chance that she peeks around a corner instead of standing in the open (first appearance and every comeback).")]
    private float peekChance = 0.4f;
    [SerializeField, Min(2f), Tooltip("Corners of buildings/walls within this distance of the player are considered.")]
    private float peekSearchRadius = 18f;
    [SerializeField, Min(1f), Tooltip("Closest a peeking spot may be to the player.")]
    private float peekMinDistance = 6f;
    [SerializeField, Range(1f, 30f), Tooltip("Peeking, she is spotted when the camera points within this angle of her face.")]
    private float peekSpotAngle = 10f;
    [SerializeField, Min(0.05f), Tooltip("Seconds she takes to slip behind the corner once spotted.")]
    private float retreatSeconds = 0.25f;
    [SerializeField, Min(1f), Tooltip("Peeking unseen this long, she gives up and comes back later.")]
    private float peekTimeout = 45f;

    private const float BodyRadius = 0.45f;
    private const float PeekOffset = -0.1f;
    private const float HideOffset = 1.1f;

    private System.Random random;
    private float reappearAt;
    private float phaseStart;
    private Vector3 lungeFrom;
    private Vector3 baseScale = Vector3.one;
    private Color baseColor = Color.white;
    private Camera cachedCamera;
    private NetworkIdentity cachedOwner;
    private DistanceFadeSprite fade;
    private float flickerSeed;
    private float dropoutUntil;
    private bool spotted;
    private PeekSpot peekSpot;

    public Phase Current { get; private set; } = Phase.Waiting;
    public SpriteRenderer WorldSprite => worldSprite;
    public float VanishDistance => vanishDistance;
    public float ReappearAt => reappearAt;
    public float FarDespawnDistance => farDespawnDistance;
    public float FarRespawnSeconds => farRespawnSeconds;
    public float PeekChance => peekChance;
    public bool Visible => worldSprite != null && worldSprite.enabled;
    /// <summary>True once the player has spotted her during the current lingering appearance.</summary>
    public bool Spotted => spotted;
    /// <summary>Opacity multiplier from the flicker last frame (0-1).</summary>
    public float FlickerAlpha { get; private set; } = 1f;
    public PeekSpot CurrentPeekSpot => peekSpot;
    /// <summary>World position of her face (the sprite pivot).</summary>
    public Vector3 FacePosition => worldSprite != null ? worldSprite.transform.position : transform.position + Vector3.up * 1.6f;
    private float FaceHeight => worldSprite != null ? worldSprite.transform.position.y - transform.position.y : 1.6f;

    private void Awake()
    {
        if (worldSprite != null)
        {
            baseScale = worldSprite.transform.localScale;
            baseColor = worldSprite.color;
        }
        fade = GetComponent<DistanceFadeSprite>();
        flickerSeed = Random.value * 100f;
        SetVisible(false);
    }

    /// <summary>Starts her at a random spot of the map (seeded), or (by Peek Chance) later around a corner near the player.</summary>
    public void Begin(int seed)
    {
        random = new System.Random(seed);
        if (!TryFindMapSpot(out Vector3 spot))
        {
            Debug.LogWarning("The White Lady found no place to stand; she stays away this time.", this);
            Finish();
            return;
        }
        if (random.NextDouble() < peekChance)
        {
            // Peek first: give the player a moment in the map, then appear at a corner near them.
            EnterAway();
            return;
        }
        transform.position = spot;
        EnterLingering();
    }

    /// <summary>Starts the first sighting at a given spot (tests and debugging).</summary>
    public void BeginAt(Vector3 position, int seed)
    {
        random = new System.Random(seed);
        transform.position = position;
        EnterLingering();
    }

    /// <summary>Starts peeking from <paramref name="spot"/> (tests and debugging).</summary>
    public void BeginPeeking(PeekSpot spot, int seed)
    {
        random ??= new System.Random(seed);
        EnterPeeking(spot);
    }

    /// <summary>Skips the hidden or away wait (tests and debugging).</summary>
    public void ReappearNow() => reappearAt = Time.time;

    private void Update()
    {
        Camera camera = LocalCamera(out bool dead);
        if (Current == Phase.Waiting || Current == Phase.Gone)
            return;
        if (camera == null || dead)
        {
            // Spectating or no local player: she shows herself to nobody.
            SetVisible(false);
            return;
        }
        Vector3 eye = camera.transform.position;

        switch (Current)
        {
            case Phase.Lingering:
                SetVisible(true);
                float distance = HorizontalDistance(eye, transform.position);
                if (distance <= vanishDistance)
                {
                    EnterHidden();
                    break;
                }
                spotted |= IsSpotted(camera, spotAngle);
                if (spotted && distance > farDespawnDistance)
                    EnterAway();
                break;

            case Phase.Away:
                if (Time.time < reappearAt)
                    break;
                Vector3 feet = cachedOwner != null ? cachedOwner.transform.position : eye;
                if (random.NextDouble() < peekChance && TryFindPeekSpot(feet, eye, out PeekSpot corner))
                    EnterPeeking(corner);
                else if (TryFindSpotNear(feet, respawnMinDistance, respawnMaxDistance, out Vector3 back))
                {
                    transform.position = back;
                    EnterLingering();
                }
                else
                    reappearAt = Time.time + 1f;
                break;

            case Phase.Peeking:
                SetVisible(true);
                if (IsLookedAt(camera) || IsSpotted(camera, peekSpotAngle) ||
                    HorizontalDistance(eye, transform.position) <= vanishDistance)
                {
                    Current = Phase.Retreating;
                    phaseStart = Time.time;
                }
                else if (Time.time - phaseStart >= peekTimeout)
                    EnterAway();
                break;

            case Phase.Retreating:
                float r = Mathf.Clamp01((Time.time - phaseStart) / retreatSeconds);
                transform.position = Vector3.Lerp(peekSpot.Peek, peekSpot.Hide, r * (2f - r));
                if (r >= 1f)
                    EnterHidden();
                break;

            case Phase.Hidden:
                if (Time.time < reappearAt)
                    break;
                if (TryFindSpotNear(eye, reappearMinDistance, reappearMaxDistance, out Vector3 near))
                {
                    transform.position = near;
                    Current = Phase.Stalking;
                    SetVisible(true);
                }
                else
                    reappearAt = Time.time + 1f;
                break;

            case Phase.Stalking:
                SetVisible(true);
                if (IsLookedAt(camera))
                {
                    Current = Phase.Lunging;
                    phaseStart = Time.time;
                    lungeFrom = transform.position;
                }
                break;

            case Phase.Lunging:
                float t = Mathf.Clamp01((Time.time - phaseStart) / lungeSeconds);
                // Accelerates into the camera, face first (the sprite pivot is her face).
                float eased = t * t;
                Vector3 target = eye + camera.transform.forward * lungeEndDistance - (FacePosition - transform.position);
                transform.position = Vector3.Lerp(lungeFrom, target, eased);
                if (worldSprite != null)
                    worldSprite.transform.localScale = baseScale * Mathf.Lerp(1f, lungeScale, eased);
                if (t >= 1f)
                    Finish();
                break;
        }
        UpdateFlicker();
    }

    private void EnterLingering()
    {
        spotted = false;
        phaseStart = Time.time;
        Current = Phase.Lingering;
    }

    private void EnterHidden()
    {
        SetVisible(false);
        Current = Phase.Hidden;
        reappearAt = Time.time + Mathf.Lerp(minReappearSeconds, Mathf.Max(minReappearSeconds, maxReappearSeconds), (float)random.NextDouble());
    }

    private void EnterAway()
    {
        SetVisible(false);
        spotted = false;
        Current = Phase.Away;
        reappearAt = Time.time + farRespawnSeconds;
    }

    private void EnterPeeking(PeekSpot spot)
    {
        peekSpot = spot;
        transform.position = spot.Peek;
        phaseStart = Time.time;
        Current = Phase.Peeking;
        SetVisible(true);
    }

    private void UpdateFlicker()
    {
        float alpha = 1f;
        if (Current != Phase.Lunging)
        {
            float drift = Mathf.PerlinNoise(flickerSeed, Time.time * flickerSpeed);
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * flickerSpeed * 2.3f + flickerSeed);
            alpha = Mathf.Lerp(flickerMinAlpha, 1f, Mathf.Clamp01(drift * 0.7f + wave * 0.3f));
            if (Time.time >= dropoutUntil && flickerDropoutsPerSecond > 0f && Random.value < flickerDropoutsPerSecond * Time.deltaTime)
                dropoutUntil = Time.time + flickerDropoutSeconds;
            if (Time.time < dropoutUntil)
                alpha = 0f;
        }
        FlickerAlpha = alpha;
        if (fade != null && fade.isActiveAndEnabled)
            fade.OpacityMultiplier = alpha;
        else if (worldSprite != null)
            worldSprite.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
    }

    private void Finish()
    {
        Current = Phase.Gone;
        SetVisible(false);
        Destroy(gameObject);
    }

    private void SetVisible(bool visible)
    {
        if (worldSprite != null && worldSprite.enabled != visible)
            worldSprite.enabled = visible;
    }

    /// <summary>True when the camera's centre ray hits her sprite bounds with nothing solid in between.</summary>
    public bool IsLookedAt(Camera camera)
    {
        if (worldSprite == null || worldSprite.sprite == null || !worldSprite.enabled)
            return false;
        Ray ray = new Ray(camera.transform.position, camera.transform.forward);
        Bounds bounds = worldSprite.bounds;
        // Billboards are flat; use a square column around the sprite so the ray test is stable from every angle.
        Vector3 size = bounds.size;
        float width = Mathf.Max(size.x, size.z);
        bounds.size = new Vector3(width * 0.6f, size.y * 0.9f, width * 0.6f);
        if (!bounds.IntersectRay(ray, out float distance) || distance > lookTriggerDistance)
            return false;
        return !Physics.Raycast(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    /// <summary>True when the camera points within <paramref name="angle"/> degrees of her face and nothing solid is in between.</summary>
    public bool IsSpotted(Camera camera, float angle)
    {
        if (worldSprite == null || !worldSprite.enabled)
            return false;
        Vector3 toFace = FacePosition - camera.transform.position;
        float distance = toFace.magnitude;
        if (distance > lookTriggerDistance || distance < 0.01f || Vector3.Angle(camera.transform.forward, toFace) > angle)
            return false;
        return !Physics.Raycast(camera.transform.position, toFace / distance, distance - 0.05f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
    }

    private Camera LocalCamera(out bool dead)
    {
        dead = false;
        NetworkIdentity local = NetworkClient.localPlayer;
        if (local == null)
            return null;
        if (local != cachedOwner)
        {
            cachedOwner = local;
            cachedCamera = local.GetComponentInChildren<Camera>(true);
        }
        dead = local.TryGetComponent(out PlayerHealth health) && health.IsDead;
        return cachedCamera;
    }

    /// <summary>
    /// Finds a building/wall corner near the player where she can stand partly visible (<see cref="PeekSpot.Peek"/>) and
    /// a spot just behind it that the player cannot see (<see cref="PeekSpot.Hide"/>).
    /// </summary>
    public bool TryFindPeekSpot(Vector3 playerFeet, Vector3 eye, out PeekSpot spot)
    {
        spot = default;
        random ??= new System.Random(Random.Range(int.MinValue, int.MaxValue));
        Collider[] colliders = Physics.OverlapSphere(playerFeet, peekSearchRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        // Random order, so she does not always choose the same corner.
        for (int i = colliders.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (colliders[i], colliders[j]) = (colliders[j], colliders[i]);
        }
        float faceHeight = FaceHeight;
        foreach (Collider collider in colliders)
        {
            if (cachedOwner != null && collider.transform.IsChildOf(cachedOwner.transform))
                continue;
            Bounds bounds = collider.bounds;
            if (bounds.size.y < 1.8f || bounds.size.x < 0.8f || bounds.size.z < 0.8f || bounds.max.y < playerFeet.y + 1.8f ||
                bounds.min.y > playerFeet.y + 0.5f)
                continue;
            int startCorner = random.Next(4);
            for (int k = 0; k < 4; k++)
            {
                int c = (startCorner + k) % 4;
                float sx = c % 2 == 0 ? -1f : 1f;
                float sz = c < 2 ? -1f : 1f;
                var corner = new Vector3(sx < 0f ? bounds.min.x : bounds.max.x, playerFeet.y, sz < 0f ? bounds.min.z : bounds.max.z);
                // Two faces meet at the corner: hide along one (normal n) while the player stands in front of the other (normal -t).
                for (int face = 0; face < 2; face++)
                {
                    Vector3 n = face == 0 ? new Vector3(sx, 0f, 0f) : new Vector3(0f, 0f, sz);
                    Vector3 t = face == 0 ? new Vector3(0f, 0f, -sz) : new Vector3(-sx, 0f, 0f);
                    if (TryCorner(corner, n, t, playerFeet, eye, faceHeight, out spot))
                        return true;
                }
            }
        }
        return false;
    }

    private bool TryCorner(Vector3 corner, Vector3 n, Vector3 t, Vector3 playerFeet, Vector3 eye, float faceHeight, out PeekSpot spot)
    {
        spot = default;
        Vector3 toEye = eye - corner;
        toEye.y = 0f;
        // The player must face the other side of the corner, not the strip she hides along.
        if (Vector3.Dot(toEye, -t) < 1f || Vector3.Dot(toEye, n) > Vector3.Dot(toEye, -t))
            return false;
        Vector3 peek = corner + n * BodyRadius + t * PeekOffset;
        Vector3 hide = corner + n * BodyRadius + t * HideOffset;
        float distance = HorizontalDistance(peek, playerFeet);
        if (distance < peekMinDistance || distance > peekSearchRadius)
            return false;
        if (!TryGround(peek, playerFeet.y, out float peekY) || !TryGround(hide, playerFeet.y, out float hideY))
            return false;
        peek.y = peekY;
        hide.y = hideY;
        if (Blocked(peek) || Blocked(hide))
            return false;
        // Her face must be visible at the peek and hidden behind the corner at the hide spot.
        if (!ClearLine(eye, peek + Vector3.up * faceHeight) || ClearLine(eye, hide + Vector3.up * faceHeight))
            return false;
        spot = new PeekSpot(peek, hide);
        return true;
    }

    private static bool TryGround(Vector3 point, float referenceY, out float y)
    {
        y = referenceY;
        if (!Physics.Raycast(new Vector3(point.x, referenceY + 1.5f, point.z), Vector3.down, out RaycastHit hit, 3f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        y = hit.point.y;
        return Mathf.Abs(y - referenceY) <= 1f;
    }

    private static bool Blocked(Vector3 feet) =>
        Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.7f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

    private static bool ClearLine(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float length = direction.magnitude;
        return length < 0.01f || !Physics.Raycast(from, direction / length, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private bool TryFindMapSpot(out Vector3 spot)
    {
        spot = default;
        MapManager map = MapManager.Active;
        if (map == null || !map.IsBuilt || map.BlockCount == 0)
            return false;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            int index = random.Next(map.BlockCount);
            if (map.GetRole(index) == MapBlockRole.Spawn && map.BlockCount > 1)
                continue;
            StreetBlock block = map.GetBlock(index);
            if (block == null)
                continue;
            var reachable = map.GetReachableFloor(index);
            if (reachable.Count > 0)
            {
                spot = reachable[random.Next(reachable.Count)];
                return true;
            }
            Vector3 local = new Vector3(((float)random.NextDouble() - 0.5f) * block.Size.x * 0.9f, 0f,
                ((float)random.NextDouble() - 0.5f) * block.Size.y * 0.9f);
            if (NavMesh.SamplePosition(block.transform.TransformPoint(local), out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                spot = hit.position;
                return true;
            }
        }
        return false;
    }

    private bool TryFindSpotNear(Vector3 center, float minDistance, float maxDistance, out Vector3 spot)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Lerp(minDistance, Mathf.Max(minDistance, maxDistance), (float)random.NextDouble());
            Vector3 probe = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            if (NavMesh.SamplePosition(probe, out NavMeshHit hit, 2f, NavMesh.AllAreas) &&
                HorizontalDistance(hit.position, center) >= minDistance * 0.75f)
            {
                spot = hit.position;
                return true;
            }
        }
        // No NavMesh nearby: stand on any solid floor around the player instead.
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 probe = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * minDistance;
            if (Physics.Raycast(probe + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                spot = floor.point;
                return true;
            }
        }
        spot = default;
        return false;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
}
