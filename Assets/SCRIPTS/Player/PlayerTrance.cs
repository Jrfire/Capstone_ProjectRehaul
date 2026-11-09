using System;
using System.Collections.Generic;
using Capstone.Audio;
using Mirror;
using UnityEngine;

/// <summary>
/// The Santelmo's trance (NetworkPlayer). The server sets <see cref="Gaze"/> (0..1) while a Santelmo stares at this
/// player: the owner's view blurs and fades to black. At 1 the server starts the trance: everyone else sees a dead body
/// (enemies ignore the player), while the owner is moved, locally only, into a repeating pocket street with an inferno
/// hue and a local Santelmo chasing them. Surviving <see cref="SurviveSeconds"/> returns them to their body; being caught
/// kills them there. The body's position never changes for others because the owner's NetworkTransform pauses.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerTrance : NetworkBehaviour
{
    [Header("Gaze (before the trance)")]
    [SerializeField, Min(0f), Tooltip("Blur at full gaze (PlayerCameraEffects.TranceBlur).")]
    private float gazeBlur = 1f;
    [SerializeField, Min(0.1f), Tooltip("Higher = the screen stays clearer longer before going black.")]
    private float gazeFadeExponent = 1.6f;

    [Header("Pocket dimension")]
    [SerializeField, Min(10f)] private float surviveSeconds = 60f;
    [SerializeField, Min(0f), Tooltip("Seconds before the end when the chaser slows and the screen starts turning white.")]
    private float finalPhaseSeconds = 10f;
    [SerializeField, Tooltip("Where the owner's pocket street is built (far from any map).")]
    private Vector3 pocketOrigin = new Vector3(4000f, -800f, 4000f);
    [SerializeField, Range(3, 9), Tooltip("Repeated street blocks in the line (the player is kept in the middle one).")]
    private int pocketCopies = 5;
    [SerializeField, Min(1f), Tooltip("Corridor length per segment when no street block fits (fallback corridor).")]
    private float fallbackSegmentLength = 24f;
    [SerializeField, Tooltip("Optional local (non-networked) chaser visual. A glowing placeholder is used when empty.")]
    private GameObject pocketChaserPrefab;
    [SerializeField, Min(0f)] private float chaserSpeed = 5.8f;
    [SerializeField, Min(0f), Tooltip("Chaser speed during the final phase.")]
    private float chaserSlowSpeed = 2.5f;
    [SerializeField, Min(0f), Tooltip("Head start in metres behind the player.")]
    private float chaserStartDistance = 16f;
    [SerializeField, Min(0.1f)] private float catchDistance = 1.2f;
    [SerializeField, Min(0f)] private float chaserHeight = 1.3f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 1.5f;
    [SerializeField, Min(0f)] private float returnFadeSeconds = 1.5f;

    [Header("Inferno look")]
    [SerializeField] private Color infernoTint = new Color(1f, 0.38f, 0.08f, 0.55f);
    [SerializeField] private Color infernoFog = new Color(0.22f, 0.05f, 0.01f, 1f);
    [SerializeField, Min(0f)] private float infernoFogDensity = 1.4f;
    [SerializeField] private Color infernoAmbient = new Color(0.12f, 0.035f, 0.01f, 1f);
    [SerializeField] private Color infernoLightColor = new Color(1f, 0.4f, 0.1f, 1f);
    [SerializeField, Min(0f)] private float infernoLightIntensity = 4f;
    [SerializeField, Min(0f)] private float infernoLightRange = 22f;

    [Header("Audio")]
    [SerializeField, Tooltip("Optional music while in the pocket (Music channel, priority 20).")]
    private AudioClip pocketTheme;

    [Header("Server")]
    [SerializeField, Min(0f), Tooltip("Earliest elapsed trance time the server accepts as survived (anti-cheat slack).")]
    private float serverMinimumSurviveSeconds = 55f;
    [SerializeField, Min(10f), Tooltip("The server ends a trance that lasts longer than this (lost messages).")]
    private float serverTimeoutSeconds = 80f;

    [SyncVar(hook = nameof(OnTranceChanged))] private bool inTrance;
    [SyncVar] private float gaze;

    private PlayerHealth health;
    private PlayerMovement movement;
    private PlayerCameraEffects cameraEffects;
    private NetworkTransformBase networkTransform;
    private NetworkIdentity serverSource;
    private double serverStartTime;

    private GameObject pocketRoot;
    private Transform chaser;
    private Vector3 pocketAxis = Vector3.right;
    private Vector3 pocketCenter;
    private float segmentLength;
    private Vector3 bodyPosition;
    private Quaternion bodyRotation;
    private float pocketTime;
    private bool resultSent;
    private bool localCaught;
    private bool pocketActive;
    private float returnFade;
    private bool fadeIsWhite;
    private Color savedAmbient;
    private bool effectsOwned;

    /// <summary>Raised on every peer when this player enters or leaves the trance.</summary>
    public event Action<bool> TranceChanged;

    public bool InTrance => inTrance;
    /// <summary>Server-synced gaze progress (0..1) of the Santelmo staring at this player.</summary>
    public float Gaze => gaze;
    public float SurviveSeconds => surviveSeconds;
    public bool PocketActive => pocketActive;
    public float PocketTime => pocketTime;
    public Transform PocketChaser => chaser;
    public GameObject PocketRoot => pocketRoot;
    public float CurrentChaserSpeed => pocketTime >= surviveSeconds - finalPhaseSeconds ? chaserSlowSpeed : chaserSpeed;

    /// <summary>True if <paramref name="target"/> is a player currently in a trance.</summary>
    public static bool IsInTrance(Component target) =>
        target != null && target.TryGetComponent(out PlayerTrance trance) && trance.inTrance;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        movement = GetComponent<PlayerMovement>();
        networkTransform = GetComponent<NetworkTransformBase>();
        cameraEffects = GetComponentInChildren<PlayerCameraEffects>(true);
    }

    // ---------------------------------------------------------------- server

    /// <summary>Server: sets the gaze progress shown on the owner's screen (0 clears it).</summary>
    [Server]
    public void ServerSetGaze(float progress) => gaze = inTrance ? 0f : Mathf.Clamp01(progress);

    /// <summary>Server: puts this living player into the trance; <paramref name="source"/> becomes the killer if caught.</summary>
    [Server]
    public bool ServerStartTrance(NetworkIdentity source)
    {
        if (inTrance || health.IsDead || (TryGetComponent(out PlayerPersistence persistence) && persistence.IsAboard))
            return false;
        serverSource = source;
        serverStartTime = NetworkTime.time;
        gaze = 0f;
        inTrance = true;
        return true;
    }

    /// <summary>Server: ends the trance; a caught player dies (killed by the trance's Santelmo).</summary>
    [Server]
    public void ServerEndTrance(bool caught)
    {
        if (!inTrance)
            return;
        NetworkIdentity source = serverSource;
        serverSource = null;
        gaze = 0f;
        inTrance = false;
        if (caught && !health.IsDead)
            health.TakeDamage(health.CurrentHealth, source);
    }

    public float ServerElapsed => inTrance ? (float)(NetworkTime.time - serverStartTime) : 0f;

    [Command]
    private void CmdSurvived()
    {
        if (inTrance && ServerElapsed >= Mathf.Min(serverMinimumSurviveSeconds, surviveSeconds))
            ServerEndTrance(false);
    }

    [Command]
    private void CmdCaught()
    {
        if (inTrance)
            ServerEndTrance(true);
    }

    private void ServerUpdate()
    {
        if (!inTrance)
            return;
        bool loading = TravelDirector.Instance != null && TravelDirector.Instance.ServerLoadingActive;
        if (health.IsDead || loading || ServerElapsed > serverTimeoutSeconds)
            ServerEndTrance(false);
    }

    public override void OnStopServer()
    {
        serverSource = null;
    }

    // ---------------------------------------------------------------- every peer

    private void OnTranceChanged(bool previous, bool current)
    {
        if (isOwned)
        {
            if (current)
                BeginPocket();
            else
                EndPocket();
        }
        TranceChanged?.Invoke(current);
    }

    private void Update()
    {
        if (isServer)
            ServerUpdate();
        if (!isOwned)
            return;
        if (pocketActive)
            TickPocket(Time.deltaTime);
        UpdateScreenEffects(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (pocketActive)
            EndPocket();
        ClearEffects();
    }

    public override void OnStopClient()
    {
        if (pocketActive)
            EndPocket();
        ClearEffects();
    }

    // ---------------------------------------------------------------- owner: screen

    private void UpdateScreenEffects(float deltaTime)
    {
        if (cameraEffects == null)
            cameraEffects = GetComponentInChildren<PlayerCameraEffects>(true);
        if (cameraEffects == null)
            return;

        if (pocketActive)
        {
            effectsOwned = true;
            cameraEffects.TranceBlur = 0f;
            cameraEffects.TranceTint = infernoTint;
            cameraEffects.FogAmbientOverride = infernoFog;
            cameraEffects.FogDensityMultiplier = infernoFogDensity;
            float black = fadeInSeconds > 0f ? 1f - Mathf.Clamp01(pocketTime / fadeInSeconds) : 0f;
            float white = finalPhaseSeconds > 0f
                ? Mathf.Clamp01((pocketTime - (surviveSeconds - finalPhaseSeconds)) / finalPhaseSeconds)
                : (pocketTime >= surviveSeconds ? 1f : 0f);
            cameraEffects.ScreenFade = white > 0f ? new Color(1f, 1f, 1f, white) : new Color(0f, 0f, 0f, black);
            return;
        }

        if (returnFade > 0f)
        {
            effectsOwned = true;
            returnFade = Mathf.Max(0f, returnFade - deltaTime);
            float amount = returnFadeSeconds > 0f ? returnFade / returnFadeSeconds : 0f;
            cameraEffects.TranceTint = Color.clear;
            cameraEffects.FogAmbientOverride = Color.clear;
            cameraEffects.FogDensityMultiplier = 1f;
            cameraEffects.TranceBlur = 0f;
            cameraEffects.ScreenFade = fadeIsWhite ? new Color(1f, 1f, 1f, amount) : new Color(0f, 0f, 0f, amount);
            return;
        }

        bool alive = health == null || !health.IsDead;
        float progress = alive ? gaze : 0f;
        if (progress > 0f)
        {
            effectsOwned = true;
            cameraEffects.TranceBlur = progress * gazeBlur;
            cameraEffects.ScreenFade = new Color(0f, 0f, 0f, Mathf.Pow(progress, gazeFadeExponent));
        }
        else if (effectsOwned)
            ClearEffects();
    }

    private void ClearEffects()
    {
        if (!effectsOwned)
            return;
        effectsOwned = false;
        if (cameraEffects != null)
            cameraEffects.ClearScreenOverrides();
    }

    // ---------------------------------------------------------------- owner: pocket dimension

    private void BeginPocket()
    {
        if (pocketActive)
            return;
        pocketActive = true;
        pocketTime = 0f;
        resultSent = false;
        localCaught = false;
        returnFade = 0f;
        bodyPosition = transform.position;
        bodyRotation = transform.rotation;
        // Others (and, for guests, the server) keep the body where it is.
        if (networkTransform != null)
            networkTransform.enabled = false;

        BuildPocket(out Vector3 start);
        MoveLocally(start, Quaternion.LookRotation(pocketAxis, Vector3.up));
        SpawnChaser(start - pocketAxis * chaserStartDistance);
        savedAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientLight = infernoAmbient;
        if (pocketTheme != null)
            AudioManager.RequestMusic(this, pocketTheme, 20);
    }

    private void EndPocket()
    {
        if (!pocketActive)
            return;
        pocketActive = false;
        bool survived = !localCaught && (health == null || !health.IsDead);
        fadeIsWhite = survived;
        // Caught: start from black so the body never flashes before the death screen takes over.
        returnFade = returnFadeSeconds;
        if (pocketRoot != null)
            Destroy(pocketRoot);
        pocketRoot = null;
        chaser = null;
        RenderSettings.ambientLight = savedAmbient;
        AudioManager.ReleaseMusic(this);
        MoveLocally(bodyPosition, bodyRotation);
        if (networkTransform != null)
            networkTransform.enabled = true;
        if (movement != null && isActiveAndEnabled && NetworkClient.ready)
            movement.TeleportTo(bodyPosition, bodyRotation);
    }

    private void MoveLocally(Vector3 position, Quaternion rotation)
    {
        CharacterController controller = GetComponent<CharacterController>();
        bool controllerEnabled = controller != null && controller.enabled;
        if (controller != null)
            controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (controller != null)
            controller.enabled = controllerEnabled;
        Physics.SyncTransforms();
    }

    private void TickPocket(float deltaTime)
    {
        if (pocketRoot == null)
        {
            // The scene changed underneath us; the server ends the trance on travel.
            return;
        }
        pocketTime += deltaTime;
        WrapPlayer();
        if (chaser != null)
        {
            Vector3 target = transform.position + Vector3.up * chaserHeight;
            Vector3 toTarget = target - chaser.position;
            float distance = toTarget.magnitude;
            float maxLag = segmentLength * 1.6f;
            if (distance > maxLag)
            {
                chaser.position = target - toTarget / distance * maxLag;
                toTarget = target - chaser.position;
                distance = maxLag;
            }
            if (distance > 0.001f)
                chaser.position += toTarget / distance * Mathf.Min(distance, CurrentChaserSpeed * deltaTime);
            Vector3 flat = transform.position + Vector3.up * chaserHeight - chaser.position;
            if (!resultSent && flat.magnitude <= catchDistance)
            {
                resultSent = true;
                localCaught = true;
                if (NetworkClient.active)
                    CmdCaught();
            }
        }
        if (!resultSent && pocketTime >= surviveSeconds)
        {
            resultSent = true;
            if (NetworkClient.active)
                CmdSurvived();
        }
    }

    /// <summary>Keeps the player in the middle segment by shifting player and chaser by whole segments (identical geometry).</summary>
    private void WrapPlayer()
    {
        if (segmentLength <= 0f)
            return;
        float along = Vector3.Dot(transform.position - pocketCenter, pocketAxis);
        if (Mathf.Abs(along) <= segmentLength * 0.5f)
            return;
        Vector3 shift = -pocketAxis * (Mathf.Sign(along) * segmentLength);
        MoveLocally(transform.position + shift, transform.rotation);
        if (chaser != null)
            chaser.position += shift;
    }

    private void SpawnChaser(Vector3 feet)
    {
        GameObject visual;
        if (pocketChaserPrefab != null)
            visual = Instantiate(pocketChaserPrefab, pocketRoot.transform);
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(pocketRoot.transform, false);
            visual.transform.localScale = Vector3.one * 0.6f;
            Light glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(visual.transform, false);
            glow.type = LightType.Point;
            glow.color = infernoLightColor;
            glow.range = 8f;
            glow.intensity = 6f;
            EnemyFearSource source = visual.AddComponent<EnemyFearSource>();
            source.visibilityRenderer = visual.GetComponent<Renderer>();
        }
        visual.name = "Pocket Santelmo";
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            Destroy(collider);
        chaser = visual.transform;
        chaser.position = feet + Vector3.up * chaserHeight;
    }

    private void BuildPocket(out Vector3 start)
    {
        pocketRoot = new GameObject($"Santelmo Pocket ({name})");
        pocketRoot.transform.position = pocketOrigin;
        pocketCenter = pocketOrigin;
        if (!TryBuildStreetLine(out start))
            BuildFallbackCorridor(out start);
        // Inferno lamps over every segment.
        int half = pocketCopies / 2;
        for (int i = -half; i <= half; i++)
        {
            var lamp = new GameObject($"Inferno Light {i}").AddComponent<Light>();
            lamp.transform.SetParent(pocketRoot.transform, false);
            lamp.transform.position = pocketCenter + pocketAxis * (i * segmentLength) + Vector3.up * 5f;
            lamp.type = LightType.Point;
            lamp.color = infernoLightColor;
            lamp.intensity = infernoLightIntensity;
            lamp.range = infernoLightRange;
            lamp.shadows = LightShadows.None;
        }
    }

    private bool TryBuildStreetLine(out Vector3 start)
    {
        start = pocketOrigin;
        MapManager map = MapManager.Active;
        if (map == null)
            return false;
        var options = new List<(StreetBlock block, bool alongX)>();
        foreach (IReadOnlyList<StreetBlock> pool in new[] { map.ConnectorBlocks, map.TownBlocks })
        {
            if (pool == null)
                continue;
            foreach (StreetBlock block in pool)
            {
                if (block == null)
                    continue;
                if (IsStraight(block, BlockSide.West, BlockSide.East, true))
                    options.Add((block, true));
                else if (IsStraight(block, BlockSide.South, BlockSide.North, false))
                    options.Add((block, false));
            }
        }
        if (options.Count == 0)
            return false;

        (StreetBlock prefab, bool alongX) = options[UnityEngine.Random.Range(0, options.Count)];
        pocketAxis = alongX ? Vector3.right : Vector3.forward;
        segmentLength = alongX ? prefab.Size.x : prefab.Size.y;
        StreetBlockEntrance entrance = prefab.GetEntrance(alongX ? BlockSide.West : BlockSide.South);
        // Walk along the entrance line through the block; start on the walkway at its centre crossing.
        Vector3 lateral = alongX ? new Vector3(0f, 0f, entrance.LocalCenter.z) : new Vector3(entrance.LocalCenter.x, 0f, 0f);

        int half = pocketCopies / 2;
        for (int i = -half; i <= half; i++)
        {
            StreetBlock copy = Instantiate(prefab, pocketRoot.transform);
            copy.name = $"{prefab.name} (Pocket {i})";
            copy.transform.SetPositionAndRotation(pocketOrigin + pocketAxis * (i * segmentLength), Quaternion.identity);
            StripCopy(copy, alongX);
        }
        start = pocketOrigin + lateral + Vector3.up * 0.1f;
        pocketCenter = pocketOrigin + lateral;
        return true;
    }

    private static bool IsStraight(StreetBlock block, BlockSide a, BlockSide b, bool alongX)
    {
        StreetBlockEntrance first = block.GetEntrance(a);
        StreetBlockEntrance second = block.GetEntrance(b);
        if (first == null || second == null)
            return false;
        float offsetA = alongX ? first.LocalCenter.z : first.LocalCenter.x;
        float offsetB = alongX ? second.LocalCenter.z : second.LocalCenter.x;
        return Mathf.Abs(offsetA - offsetB) < 0.05f;
    }

    /// <summary>Removes gates/barricades and networked bits, and walls off any side entrance so the line has no exits.</summary>
    private static void StripCopy(StreetBlock copy, bool alongX)
    {
        foreach (EntranceObstacle obstacle in copy.GetComponentsInChildren<EntranceObstacle>(true))
            Destroy(obstacle.gameObject);
        foreach (NetworkIdentity identity in copy.GetComponentsInChildren<NetworkIdentity>(true))
            Destroy(identity.gameObject);
        foreach (StreetBlockEntrance entrance in copy.Entrances)
        {
            bool onAxis = alongX
                ? entrance.Side == BlockSide.West || entrance.Side == BlockSide.East
                : entrance.Side == BlockSide.South || entrance.Side == BlockSide.North;
            if (onAxis)
                continue;
            GameObject plug = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plug.name = $"Pocket Plug {entrance.Side}";
            plug.transform.SetParent(copy.transform, false);
            bool sideAlongX = entrance.Side == BlockSide.South || entrance.Side == BlockSide.North;
            plug.transform.localPosition = entrance.LocalCenter + Vector3.up * 2f;
            plug.transform.localScale = sideAlongX ? new Vector3(entrance.Width + 1f, 4f, 0.6f) : new Vector3(0.6f, 4f, entrance.Width + 1f);
            Renderer renderer = plug.GetComponent<Renderer>();
            renderer.sharedMaterial = PlugMaterial;
        }
        copy.enabled = false;
    }

    private static Material plugMaterial;

    private static Material PlugMaterial
    {
        get
        {
            if (plugMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                plugMaterial = new Material(shader) { name = "Pocket Plug", color = new Color(0.12f, 0.06f, 0.04f) };
            }
            return plugMaterial;
        }
    }

    private void BuildFallbackCorridor(out Vector3 start)
    {
        pocketAxis = Vector3.right;
        segmentLength = fallbackSegmentLength;
        int half = pocketCopies / 2;
        float length = segmentLength * pocketCopies;
        void Box(string boxName, Vector3 center, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = boxName;
            box.transform.SetParent(pocketRoot.transform, false);
            box.transform.position = center;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = PlugMaterial;
        }
        Box("Pocket Floor", pocketOrigin + Vector3.down * 0.5f, new Vector3(length, 1f, 8f));
        Box("Pocket Wall North", pocketOrigin + new Vector3(0f, 2f, 4.3f), new Vector3(length, 5f, 0.6f));
        Box("Pocket Wall South", pocketOrigin + new Vector3(0f, 2f, -4.3f), new Vector3(length, 5f, 0.6f));
        // Repeating pillars make the loop readable.
        for (int i = -half; i <= half; i++)
        {
            Vector3 segment = pocketOrigin + pocketAxis * (i * segmentLength);
            Box($"Pocket Pillar {i}a", segment + new Vector3(0f, 2f, 3.4f), new Vector3(1f, 5f, 1f));
            Box($"Pocket Pillar {i}b", segment + new Vector3(0f, 2f, -3.4f), new Vector3(1f, 5f, 1f));
        }
        pocketCenter = pocketOrigin;
        start = pocketOrigin + Vector3.up * 0.1f;
    }
}
