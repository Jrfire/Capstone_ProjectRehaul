using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity), typeof(PlayerMovement))]
public sealed class PlayerVision : NetworkBehaviour
{
    [SerializeField] private CharacterDefinition characterDefinition;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private SpriteRenderer worldSprites;

    [SyncVar] private float facingYaw;
    [SyncVar] private CharacterAnimationState animationState;
    [SyncVar] private double animationStartedAt;

    private PlayerMovement movement;
    private CharacterAnimationState? requestedState;
    private bool restartRequested;
    private bool hasSentVision;
    private float lastSentYaw;
    private CharacterAnimationState lastSentState;
    private double nextSendTime;
    private CharacterDefinition renderedDefinition;
    private readonly HashSet<CharacterAnimationState> missingStates = new HashSet<CharacterAnimationState>();

    public float FacingYaw => facingYaw;
    public CharacterAnimationState AnimationState => animationState;
    public CharacterDirection DisplayedDirection { get; private set; }

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        if (characterDefinition == null || playerCamera == null || worldSprites == null ||
            !playerCamera.transform.IsChildOf(transform) || !worldSprites.transform.IsChildOf(transform) ||
            worldSprites.transform == transform)
        {
            Debug.LogError("PlayerVision requires a CharacterDefinition, a child player camera, and a separate child WorldSprites renderer.", this);
            enabled = false;
        }
    }

    public override void OnStartServer()
    {
        if (playerCamera == null)
            return;
        facingYaw = playerCamera.transform.eulerAngles.y;
        animationStartedAt = NetworkTime.time;
    }

    public override void OnStartLocalPlayer()
    {
        hasSentVision = false;
        requestedState = null;
    }

    public override void OnStopLocalPlayer()
    {
        requestedState = null;
        restartRequested = false;
    }

    public void SetAnimationState(CharacterAnimationState state)
    {
        if (!isLocalPlayer)
        {
            Debug.LogError("Only the local player can request its animation state.", this);
            return;
        }
        if (state < CharacterAnimationState.Idle || state > CharacterAnimationState.Dead)
            throw new ArgumentOutOfRangeException(nameof(state));

        requestedState = state;
        restartRequested = true;
    }

    public void ResumeMovementAnimation()
    {
        if (!isLocalPlayer)
        {
            Debug.LogError("Only the local player can resume its movement animation.", this);
            return;
        }
        requestedState = null;
    }

    private void LateUpdate()
    {
        if (isLocalPlayer && NetworkClient.ready)
            PublishVision();

        if (!isClient || isLocalPlayer || !worldSprites.gameObject.activeInHierarchy)
            return;

        NetworkIdentity localPlayer = NetworkClient.localPlayer;
        if (localPlayer == null || !localPlayer.TryGetComponent(out PlayerVision viewer) ||
            viewer.playerCamera == null ||
            !viewer.playerCamera.isActiveAndEnabled)
        {
            worldSprites.sprite = null;
            return;
        }

        Camera camera = viewer.playerCamera;
        DisplayedDirection = DirectionFromYaw(camera.transform.eulerAngles.y, facingYaw);
        worldSprites.transform.rotation = BillboardRotation(camera.transform.position,
            worldSprites.transform.position, camera.transform.eulerAngles.y);

        if (renderedDefinition != characterDefinition)
        {
            renderedDefinition = characterDefinition;
            missingStates.Clear();
        }

        CharacterAnimation animation = characterDefinition.GetAnimation(animationState);
        if (!animation.IsReady)
        {
            worldSprites.sprite = null;
            if (missingStates.Add(animationState))
                Debug.LogWarning($"Configure complete {animationState} frames and a positive FPS in '{characterDefinition.name}' before displaying it.", this);
            return;
        }

        bool loop = animationState == CharacterAnimationState.Idle ||
            animationState == CharacterAnimationState.Walk || animationState == CharacterAnimationState.Run;
        Sprite frame = animation.GetFrame(DisplayedDirection, NetworkTime.time - animationStartedAt, loop);
        worldSprites.sprite = frame;
        if (frame == null && missingStates.Add(animationState))
            Debug.LogError($"The {animationState} animation contains a missing sprite. Regenerate its sheet.", this);
    }

    private void PublishVision()
    {
        CharacterAnimationState state = requestedState ??
            (movement.IsMoving ? CharacterAnimationState.Walk : CharacterAnimationState.Idle);
        float yaw = playerCamera.transform.eulerAngles.y;
        bool stateChanged = !hasSentVision || state != lastSentState || restartRequested;
        if (!stateChanged && (NetworkTime.time < nextSendTime ||
            Mathf.Abs(Mathf.DeltaAngle(lastSentYaw, yaw)) < 1f))
            return;

        CmdUpdateVision(yaw, state, restartRequested);
        lastSentYaw = yaw;
        lastSentState = state;
        hasSentVision = true;
        restartRequested = false;
        nextSendTime = NetworkTime.time + 0.1d;
    }

    [Command]
    private void CmdUpdateVision(float yaw, CharacterAnimationState state, bool restart)
    {
        if (float.IsNaN(yaw) || float.IsInfinity(yaw) ||
            state < CharacterAnimationState.Idle || state > CharacterAnimationState.Dead)
        {
            Debug.LogWarning("Rejected an invalid player facing or animation state.", this);
            return;
        }

        facingYaw = Mathf.Repeat(yaw, 360f);
        if (animationState != state || restart)
        {
            animationState = state;
            animationStartedAt = NetworkTime.time;
        }
    }

    public static CharacterDirection DirectionFromYaw(float viewerYaw, float observedYaw)
    {
        if (float.IsNaN(viewerYaw) || float.IsInfinity(viewerYaw))
            throw new ArgumentOutOfRangeException(nameof(viewerYaw));
        if (float.IsNaN(observedYaw) || float.IsInfinity(observedYaw))
            throw new ArgumentOutOfRangeException(nameof(observedYaw));

        float angle = Mathf.DeltaAngle(viewerYaw, observedYaw);
        if (angle >= 45f && angle < 135f)
            return CharacterDirection.East;
        if (angle >= -135f && angle < -45f)
            return CharacterDirection.West;
        return angle >= 135f || angle < -135f ? CharacterDirection.South : CharacterDirection.North;
    }

    public static Quaternion BillboardRotation(Vector3 cameraPosition, Vector3 spritePosition, float viewerYaw)
    {
        // Looking along the camera-to-sprite ray keeps artwork's horizontal axis from being mirrored.
        Vector3 ray = spritePosition - cameraPosition;
        ray.y = 0f;
        return ray.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(ray, Vector3.up)
            : Quaternion.Euler(0f, viewerYaw, 0f);
    }
}
