using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Client-side look of an enemy. The server publishes whether it moves and the yaw it moves/faces toward; every client
/// picks West/South/East/North relative to its own camera (same rule as players), billboards the WorldSprites child
/// toward that camera and moves the sprite in low-framerate steps so motion reads as snappy, low-poly animation.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyPresentation : NetworkBehaviour
{
    [SerializeField] private EnemySpriteDefinition sprites;
    [SerializeField, Tooltip("Child SpriteRenderer that shows the enemy to every player.")]
    private SpriteRenderer worldSprites;
    [SerializeField, Min(1f), Tooltip("How many times per second the visible sprite catches up with the real position. Lower = choppier.")]
    private float snapsPerSecond = 12f;
    [SerializeField, Tooltip("Size in metres of the dark silhouette shown until Idle/Moving frames are configured.")]
    private Vector2 placeholderSize = new Vector2(1.2f, 2.8f);
    [SerializeField, Tooltip("Also tilt the sprite up/down so it always faces the viewing camera (for flyers seen from below/above). Off = upright billboard.")]
    private bool billboardPitch;

    [SyncVar] private float facingYaw;
    [SyncVar] private bool moving;
    [SyncVar] private double animationStartedAt;

    private static Sprite placeholder;
    private static Vector2 placeholderBuiltSize;
    private readonly HashSet<EnemyAnimationState> warned = new HashSet<EnemyAnimationState>();
    private Vector3 spriteOffset;
    private Vector3 snappedRoot;
    private float nextSnapTime;
    private NetworkIdentity cachedViewerOwner;
    private Camera cachedViewer;

    public EnemySpriteDefinition Sprites => sprites;
    public SpriteRenderer WorldSprites => worldSprites;
    public float SnapsPerSecond => snapsPerSecond;
    public float FacingYaw => facingYaw;
    public bool IsMoving => moving;
    public EnemyAnimationState AnimationState => moving ? EnemyAnimationState.Moving : EnemyAnimationState.Idle;
    public CharacterDirection DisplayedDirection { get; private set; } = CharacterDirection.South;
    public bool BillboardPitch { get => billboardPitch; set => billboardPitch = value; }

    /// <summary>Faces the sprite straight at the camera, tilting it when seen from above or below; upright when level.</summary>
    public static Quaternion FullBillboardRotation(Vector3 cameraPosition, Vector3 spritePosition, float viewerYaw)
    {
        Vector3 ray = spritePosition - cameraPosition;
        Vector3 flat = new Vector3(ray.x, 0f, ray.z);
        if (flat.sqrMagnitude < 0.0001f)
            flat = Quaternion.Euler(0f, viewerYaw, 0f) * Vector3.forward;
        // Directly above/below the camera the flat direction keeps the up vector well defined.
        Vector3 up = Vector3.Cross(ray.sqrMagnitude > 0.0001f ? ray : flat, Vector3.Cross(Vector3.up, flat)).normalized;
        return ray.sqrMagnitude > 0.0001f && up.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(ray, up)
            : Quaternion.LookRotation(flat, Vector3.up);
    }

    private void Awake()
    {
        if (worldSprites == null)
            worldSprites = GetComponentInChildren<SpriteRenderer>(true);
        if (worldSprites != null)
        {
            spriteOffset = worldSprites.transform.position - transform.position;
            if (worldSprites.sprite == null)
                worldSprites.sprite = GetPlaceholder(placeholderSize);
        }
        snappedRoot = transform.position;
    }

    /// <summary>Server: publishes the motion the clients animate. Animation restarts when moving starts or stops.</summary>
    [Server]
    public void SetMotion(bool isMoving, float yaw)
    {
        if (float.IsNaN(yaw) || float.IsInfinity(yaw))
            return;
        yaw = Mathf.Repeat(yaw, 360f);
        if (isMoving != moving)
        {
            moving = isMoving;
            animationStartedAt = NetworkTime.time;
        }
        if (Mathf.Abs(Mathf.DeltaAngle(facingYaw, yaw)) > 0.5f)
            facingYaw = yaw;
    }

    /// <summary>Snaps the visible sprite to the current position immediately (used after teleports).</summary>
    public void SnapNow()
    {
        snappedRoot = transform.position;
        nextSnapTime = Time.time + 1f / snapsPerSecond;
        if (worldSprites != null)
            worldSprites.transform.position = snappedRoot + spriteOffset;
    }

    private void LateUpdate()
    {
        if (worldSprites == null)
            return;

        // Large jumps (teleports, spawning) are shown immediately instead of waiting for the next step.
        if (Time.time >= nextSnapTime || (transform.position - snappedRoot).sqrMagnitude > 25f)
        {
            snappedRoot = transform.position;
            nextSnapTime = Time.time + 1f / snapsPerSecond;
        }
        worldSprites.transform.position = snappedRoot + spriteOffset;

        Camera viewer = ResolveViewer();
        if (viewer != null)
        {
            float viewerYaw = viewer.transform.eulerAngles.y;
            DisplayedDirection = PlayerVision.DirectionFromYaw(viewerYaw, facingYaw);
            worldSprites.transform.rotation = billboardPitch
                ? FullBillboardRotation(viewer.transform.position, worldSprites.transform.position, viewerYaw)
                : PlayerVision.BillboardRotation(viewer.transform.position, worldSprites.transform.position, viewerYaw);
        }

        EnemyAnimationState state = AnimationState;
        CharacterAnimation animation = sprites != null ? sprites.GetAnimation(state) : null;
        if (animation == null || !animation.IsReady)
        {
            if (warned.Add(state))
                Debug.LogWarning($"'{name}' has no complete {state} frames; showing a placeholder silhouette. Configure its Enemy Sprite Definition.", this);
            worldSprites.sprite = GetPlaceholder(placeholderSize);
            return;
        }

        Sprite frame = animation.GetFrame(DisplayedDirection, NetworkTime.time - animationStartedAt, true);
        worldSprites.sprite = frame != null ? frame : GetPlaceholder(placeholderSize);
    }

    private Camera ResolveViewer()
    {
        NetworkIdentity local = NetworkClient.localPlayer;
        if (local != null)
        {
            if (local != cachedViewerOwner)
            {
                cachedViewerOwner = local;
                cachedViewer = local.GetComponentInChildren<Camera>(true);
            }
            if (cachedViewer != null && cachedViewer.isActiveAndEnabled)
                return cachedViewer;
        }
        return Camera.main;
    }

    private static Sprite GetPlaceholder(Vector2 size)
    {
        size = new Vector2(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y));
        if (placeholder != null && placeholderBuiltSize == size)
            return placeholder;

        const float pixelsPerUnit = 10f;
        int width = Mathf.Max(1, Mathf.RoundToInt(size.x * pixelsPerUnit));
        int height = Mathf.Max(1, Mathf.RoundToInt(size.y * pixelsPerUnit));
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Enemy Placeholder",
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(12, 10, 14, 255);
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        placeholder = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        placeholder.name = "Enemy Placeholder";
        placeholder.hideFlags = HideFlags.HideAndDontSave;
        placeholderBuiltSize = size;
        return placeholder;
    }
}
