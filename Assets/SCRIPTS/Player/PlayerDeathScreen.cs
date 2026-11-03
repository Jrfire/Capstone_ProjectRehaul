using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum DeathScreenPhase
{
    Alive,
    Jumpscare,
    FadingOut,
    Spectating,
    AllDead
}

/// <summary>
/// Local death sequence on the persistent PlayerHud. When the local player's health reaches 0, a creature kill shows a
/// black screen with the killer's <see cref="EnemyJumpscare"/> front view zooming in, then fades to black; a death with
/// no killer (e.g. being left behind) fades to black after <see cref="noKillerDelay"/>. If other players are alive the
/// screen fades back in on their POV; Player/SpectateNext (left click) cycles through them. With nobody left alive the
/// screen stays black until the server returns everyone to base. Respawning (health above 0) restores the normal view.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class PlayerDeathScreen : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField, Tooltip("Full-screen black Image behind the jumpscare.")]
    private Image blackout;
    [SerializeField, Tooltip("Centred Image showing the killer's front view.")]
    private Image jumpscareImage;
    [SerializeField, Tooltip("Optional hint shown while spectating.")]
    private Text spectatingLabel;

    [Header("Jumpscare")]
    [Min(0.01f), Tooltip("Seconds the creature's image takes to zoom into the screen.")]
    public float zoomSeconds = 0.45f;
    [Range(0.01f, 1f), Tooltip("Starting size of the image relative to its final size.")]
    public float zoomStartScale = 0.15f;
    [Min(0f), Tooltip("Seconds the full-size image holds before fading.")]
    public float holdSeconds = 0.6f;

    [Header("Fades")]
    [Min(0f), Tooltip("Seconds after a death with no killer before the screen fades to black.")]
    public float noKillerDelay = 1f;
    [Min(0.01f), Tooltip("Seconds to fade to black (and back in when spectating starts).")]
    public float fadeSeconds = 0.6f;
    [Min(0f), Tooltip("Seconds the screen stays black before fading in on another player's POV.")]
    public float blackHoldSeconds = 0.4f;

    private static PlayerDeathScreen active;

    private readonly List<PlayerPersistence> livingPlayers = new List<PlayerPersistence>();
    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    private InputAction spectateAction;
    private PlayerHealth observedHealth;
    private Camera localCamera;
    private Vector3 cameraLocalPosition;
    private Quaternion cameraLocalRotation;
    private bool cameraDetached;
    private bool jumpscareShown;
    private float phaseTime;
    private float spectateFadeIn;

    /// <summary>The player whose POV the local (dead) player is watching, or null.</summary>
    public static GameObject SpectatedPlayer =>
        active != null && active.Target != null ? active.Target.gameObject : null;

    public DeathScreenPhase Phase { get; private set; }
    public PlayerPersistence Target { get; private set; }
    public Sprite JumpscareSprite => jumpscareImage != null ? jumpscareImage.sprite : null;
    public bool JumpscareShown => jumpscareShown;
    public Image Blackout => blackout;
    public Image JumpscareImage => jumpscareImage;
    public Text SpectatingLabel => spectatingLabel;
    public float BlackoutAlpha => blackout != null && blackout.enabled ? blackout.color.a : 0f;

    private void Awake()
    {
        if (blackout != null)
            blackout.raycastTarget = false;
        if (jumpscareImage != null)
        {
            jumpscareImage.raycastTarget = false;
            jumpscareImage.preserveAspect = true;
        }
        HideAll();
    }

    private void OnEnable()
    {
        active = this;
        InputAction configured = inputActions != null ? inputActions.FindAction("Player/SpectateNext") : null;
        if (configured == null || configured.type != InputActionType.Button)
        {
            Debug.LogError("PlayerDeathScreen requires a Button Player/SpectateNext action.", this);
            return;
        }
        spectateAction = configured.Clone();
        spectateAction.Enable();
    }

    private void OnDisable()
    {
        Revive();
        observedHealth = null;
        spectateAction?.Dispose();
        spectateAction = null;
        if (active == this)
            active = null;
    }

    private void LateUpdate()
    {
        NetworkIdentity localPlayer = NetworkClient.isConnected ? NetworkClient.localPlayer : null;
        PlayerHealth health = localPlayer != null ? localPlayer.GetComponent<PlayerHealth>() : null;
        if (health != observedHealth)
        {
            Revive();
            observedHealth = health;
        }

        if (health == null || !health.IsDead)
        {
            if (Phase != DeathScreenPhase.Alive)
                Revive();
            return;
        }

        if (Phase == DeathScreenPhase.Alive)
            BeginDeath(health);

        phaseTime += Time.unscaledDeltaTime;
        if (spectateAction != null && spectateAction.WasPressedThisFrame())
            SpectateNext();
        Tick();
    }

    /// <summary>Cycles to the next living player (left click while spectating).</summary>
    public void SpectateNext()
    {
        if (Phase != DeathScreenPhase.Spectating)
            return;
        CollectLivingPlayers();
        if (livingPlayers.Count == 0)
            return;
        int index = livingPlayers.IndexOf(Target);
        Target = livingPlayers[(index + 1) % livingPlayers.Count];
        UpdateLabel();
    }

    private void BeginDeath(PlayerHealth health)
    {
        Phase = DeathScreenPhase.Jumpscare;
        phaseTime = 0f;
        Target = null;
        PlayerControlLock.Acquire(this);
        HideLocalCanvases(health.gameObject);

        EnemyJumpscare jumpscare = EnemyJumpscare.Find(health.KillerAssetId);
        jumpscareShown = jumpscare != null && jumpscare.FrontView != null && jumpscareImage != null;
        if (jumpscareShown)
        {
            jumpscareImage.sprite = jumpscare.FrontView;
            float height = ((RectTransform)transform).rect.height * jumpscare.FinalScreenHeight;
            float aspect = jumpscare.FrontView.rect.width / Mathf.Max(1f, jumpscare.FrontView.rect.height);
            RectTransform image = jumpscareImage.rectTransform;
            image.sizeDelta = new Vector2(height * aspect, height);
            // The pivot is the focus point, so it sits at the screen centre and the zoom scales around it.
            image.anchorMin = image.anchorMax = new Vector2(0.5f, 0.5f);
            image.pivot = jumpscare.FocusPoint;
            image.anchoredPosition = Vector2.zero;
        }
        else if (health.KillerAssetId != 0)
        {
            Debug.LogWarning($"No EnemyJumpscare front view is assigned on the killer prefab (asset id {health.KillerAssetId}).", this);
        }
    }

    private void Tick()
    {
        switch (Phase)
        {
            case DeathScreenPhase.Jumpscare:
                if (jumpscareShown)
                {
                    float zoom = Mathf.Clamp01(phaseTime / zoomSeconds);
                    // Accelerating zoom so the face lunges at the screen.
                    float scale = Mathf.Lerp(zoomStartScale, 1f, zoom * zoom);
                    SetBlackout(1f);
                    SetJumpscare(1f, scale);
                    if (phaseTime >= zoomSeconds + holdSeconds)
                        SetPhase(DeathScreenPhase.FadingOut);
                }
                else
                {
                    SetBlackout(0f);
                    if (phaseTime >= noKillerDelay)
                        SetPhase(DeathScreenPhase.FadingOut);
                }
                break;

            case DeathScreenPhase.FadingOut:
            {
                float fade = Mathf.Clamp01(phaseTime / fadeSeconds);
                if (jumpscareShown)
                {
                    SetBlackout(1f);
                    SetJumpscare(1f - fade, 1f);
                }
                else
                {
                    SetBlackout(fade);
                }
                if (phaseTime >= fadeSeconds + blackHoldSeconds)
                {
                    SetJumpscare(0f, 1f);
                    StartSpectatingOrWait();
                }
                break;
            }

            case DeathScreenPhase.AllDead:
                SetBlackout(1f);
                StartSpectatingOrWait();
                break;

            case DeathScreenPhase.Spectating:
                if (Target == null || !IsAlive(Target))
                {
                    CollectLivingPlayers();
                    if (livingPlayers.Count == 0)
                    {
                        // The last spectated player died: fade back to black and wait for the trip home.
                        SetPhase(DeathScreenPhase.AllDead);
                        SetLabelVisible(false);
                        break;
                    }
                    Target = livingPlayers[0];
                    UpdateLabel();
                }
                spectateFadeIn = Mathf.Clamp01(phaseTime / fadeSeconds);
                SetBlackout(1f - spectateFadeIn);
                FollowTarget();
                break;
        }
    }

    private void StartSpectatingOrWait()
    {
        CollectLivingPlayers();
        if (livingPlayers.Count == 0)
        {
            if (Phase != DeathScreenPhase.AllDead)
                SetPhase(DeathScreenPhase.AllDead);
            SetBlackout(1f);
            return;
        }
        Target = livingPlayers[0];
        SetPhase(DeathScreenPhase.Spectating);
        UpdateLabel();
        FollowTarget();
    }

    private void FollowTarget()
    {
        if (Target == null || !Target.TryGetComponent(out PlayerVision targetVision) || targetVision.PlayerCamera == null)
            return;
        Camera camera = LocalCamera();
        if (camera == null)
            return;
        if (!cameraDetached)
        {
            cameraLocalPosition = camera.transform.localPosition;
            cameraLocalRotation = camera.transform.localRotation;
            cameraDetached = true;
        }

        float pitch = Target.TryGetComponent(out PlayerFlashlight flashlight) ? flashlight.SyncedPitch : 0f;
        camera.transform.SetPositionAndRotation(targetVision.PlayerCamera.transform.position,
            Quaternion.Euler(pitch, Target.transform.eulerAngles.y, 0f));
    }

    private Camera LocalCamera()
    {
        if (localCamera == null && observedHealth != null && observedHealth.TryGetComponent(out PlayerVision vision))
            localCamera = vision.PlayerCamera;
        return localCamera;
    }

    private void CollectLivingPlayers()
    {
        livingPlayers.Clear();
        GameObject self = observedHealth != null ? observedHealth.gameObject : null;
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (player != null && player.gameObject != self && IsAlive(player))
                livingPlayers.Add(player);
        }
        livingPlayers.Sort((a, b) => a.netId.CompareTo(b.netId));
    }

    private static bool IsAlive(PlayerPersistence player) =>
        player != null && player.isActiveAndEnabled &&
        (!player.TryGetComponent(out PlayerHealth health) || !health.IsDead);

    private void UpdateLabel()
    {
        if (spectatingLabel == null)
            return;
        int index = livingPlayers.IndexOf(Target) + 1;
        spectatingLabel.text = livingPlayers.Count > 1
            ? $"Spectating ({index}/{livingPlayers.Count}) - Left click: next player"
            : "Spectating";
        SetLabelVisible(true);
    }

    private void SetPhase(DeathScreenPhase phase)
    {
        Phase = phase;
        phaseTime = 0f;
    }

    private void Revive()
    {
        if (cameraDetached && localCamera != null)
        {
            localCamera.transform.localPosition = cameraLocalPosition;
            localCamera.transform.localRotation = cameraLocalRotation;
        }
        cameraDetached = false;
        localCamera = null;
        foreach (Canvas canvas in hiddenCanvases)
        {
            if (canvas != null)
                canvas.enabled = true;
        }
        hiddenCanvases.Clear();
        if (Phase != DeathScreenPhase.Alive)
            PlayerControlLock.Release(this);
        Phase = DeathScreenPhase.Alive;
        Target = null;
        jumpscareShown = false;
        HideAll();
    }

    private void HideLocalCanvases(GameObject player)
    {
        // First-person sprites (hands, items) would float over another player's POV.
        foreach (Canvas canvas in player.GetComponentsInChildren<Canvas>())
        {
            if (canvas.enabled)
            {
                canvas.enabled = false;
                hiddenCanvases.Add(canvas);
            }
        }
    }

    private void HideAll()
    {
        SetBlackout(0f);
        SetJumpscare(0f, 1f);
        SetLabelVisible(false);
    }

    private void SetBlackout(float alpha)
    {
        if (blackout == null)
            return;
        Color color = blackout.color;
        color.a = alpha;
        blackout.color = color;
        blackout.enabled = alpha > 0f;
    }

    private void SetJumpscare(float alpha, float scale)
    {
        if (jumpscareImage == null)
            return;
        Color color = jumpscareImage.color;
        color.a = alpha;
        jumpscareImage.color = color;
        jumpscareImage.rectTransform.localScale = new Vector3(scale, scale, 1f);
        jumpscareImage.enabled = alpha > 0f && jumpscareImage.sprite != null;
    }

    private void SetLabelVisible(bool visible)
    {
        if (spectatingLabel != null)
            spectatingLabel.enabled = visible;
    }
}
