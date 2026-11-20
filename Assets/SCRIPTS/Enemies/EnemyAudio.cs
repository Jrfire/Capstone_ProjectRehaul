using Capstone.Audio;
using Mirror;
using UnityEngine;

/// <summary>
/// Enemy sounds (every peer, from the synced position and chase target). Movement: one clip of
/// <see cref="movementSounds"/> (Tikbalang footsteps, Manananggal wing flaps) every <see cref="movementInterval"/>
/// seconds while it moves, as a 3D sound everyone nearby hears; raise <see cref="movementMaxDistance"/> for loud,
/// easy-to-locate sounds. Chase: only the chased player hears <see cref="chaseSound"/> (non-positional, once or looped)
/// and the <see cref="chaseTheme"/> music, which outranks scene music until the chase ends. Movement sounds can echo
/// (<see cref="movementEcho"/>) and get quieter/drier while chasing; <see cref="chaseLoopForAll"/> (e.g. the
/// Tikbalang's laugh) loops at the enemy for everyone while it chases anyone.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyBrain))]
public sealed class EnemyAudio : MonoBehaviour
{
    public const int ChaseMusicPriority = 10;

    [Header("Movement")]
    [SerializeField, Tooltip("Footsteps or wing flaps, played one at a time while moving.")]
    private AudioClipSet movementSounds = new AudioClipSet();
    [SerializeField, Min(0.05f), Tooltip("Seconds between movement sounds.")]
    private float movementInterval = 0.45f;
    [SerializeField, Tooltip("Optional faster interval used above Fast Speed (e.g. while chasing); 0 = always Movement Interval.")]
    private float fastMovementInterval = 0.3f;
    [SerializeField, Tooltip("Speed (m/s) from which Fast Movement Interval applies.")]
    private float fastSpeed = 4f;
    [SerializeField, Tooltip("Speed (m/s) that counts as moving.")]
    private float movingSpeed = 0.4f;
    [SerializeField, Tooltip("Full volume within this distance.")]
    private float movementMinDistance = 2f;
    [SerializeField, Tooltip("Silent beyond this distance.")]
    private float movementMaxDistance = 25f;
    [SerializeField, Range(0f, 1f), Tooltip("Echo of each movement sound (0 = none), e.g. Manananggal flaps echoing over the streets.")]
    private float movementEcho;
    [SerializeField, Range(0f, 2f), Tooltip("Movement volume multiplier while it chases someone (e.g. quieter flaps on a dive).")]
    private float chaseMovementVolume = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Movement echo while it chases someone.")]
    private float chaseMovementEcho;

    [Header("Chase (heard by everyone nearby)")]
    [SerializeField, Tooltip("3D loop played from the enemy while it chases anyone (e.g. the Tikbalang's laughing).")]
    private AudioClipSet chaseLoopForAll = new AudioClipSet();
    [SerializeField] private float chaseLoopMinDistance = 3f;
    [SerializeField] private float chaseLoopMaxDistance = 35f;

    [Header("Chase (heard only by the chased player)")]
    [SerializeField, Tooltip("Sound when the chase starts (or looping during it).")]
    private AudioClipSet chaseSound = new AudioClipSet();
    [SerializeField, Tooltip("Loop the chase sound for the whole chase instead of playing it once.")]
    private bool loopChaseSound;
    [SerializeField, Tooltip("Chase theme music for the chased player.")]
    private AudioClip chaseTheme;
    [SerializeField, Range(0f, 1f)] private float chaseThemeVolume = 1f;

    private EnemyBrain brain;
    private Vector3 lastPosition;
    private float smoothedSpeed;
    private float stepTimer;
    private bool chasingLocal;
    private AudioSource chaseLoop;
    private AudioSource publicChaseLoop;

    public bool ChasingLocalPlayer => chasingLocal;
    /// <summary>True while it chases anyone (synced <see cref="EnemyBrain.ChasedPlayer"/>), on every peer.</summary>
    public bool ChasingAnyone { get; private set; }
    public bool PublicChaseLoopPlaying => publicChaseLoop != null && publicChaseLoop.isPlaying;
    public float CurrentMovementVolume => ChasingAnyone ? chaseMovementVolume : 1f;
    public float CurrentMovementEcho => ChasingAnyone ? chaseMovementEcho : movementEcho;
    public float MovementMaxDistance => movementMaxDistance;
    public bool IsMovingAudibly { get; private set; }
    public int MovementSoundsPlayed { get; private set; }
    public int ChaseSoundsPlayed { get; private set; }
    public AudioClip ChaseTheme { get => chaseTheme; set => chaseTheme = value; }

    private void Awake()
    {
        brain = GetComponent<EnemyBrain>();
        lastPosition = transform.position;
    }

    private void OnDisable()
    {
        SetChasingLocal(false);
        SetPublicChaseLoop(false);
    }

    private void OnDestroy() => AudioManager.ReleaseMusic(this);

    private void Update()
    {
        bool present = brain == null || brain.IsPresent;
        ChasingAnyone = present && brain != null && brain.ChasedPlayer != null;
        UpdateMovement(present);
        SetPublicChaseLoop(ChasingAnyone);
        NetworkIdentity local = NetworkClient.localPlayer;
        SetChasingLocal(present && NetworkClient.active && local != null && brain != null && brain.ChasedPlayer == local);
    }

    private void SetPublicChaseLoop(bool playing)
    {
        if (!playing || !chaseLoopForAll.HasClips)
        {
            if (publicChaseLoop != null && publicChaseLoop.isPlaying)
                publicChaseLoop.Stop();
            return;
        }
        if (publicChaseLoop == null)
        {
            publicChaseLoop = gameObject.AddComponent<AudioSource>();
            AudioManager.Configure(publicChaseLoop, AudioChannel.Sfx, 1f, chaseLoopMinDistance, chaseLoopMaxDistance);
            publicChaseLoop.loop = true;
        }
        if (publicChaseLoop.isPlaying)
            return;
        publicChaseLoop.clip = chaseLoopForAll.Pick();
        publicChaseLoop.volume = chaseLoopForAll.volume;
        publicChaseLoop.pitch = chaseLoopForAll.PickPitch();
        publicChaseLoop.Play();
    }

    private void UpdateMovement(bool present)
    {
        Vector3 position = transform.position;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = (position - lastPosition).magnitude / dt;
        lastPosition = position;
        if (speed > 40f || !present)
            speed = 0f;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-10f * dt));
        IsMovingAudibly = present && smoothedSpeed > movingSpeed;
        if (!IsMovingAudibly)
        {
            stepTimer = Mathf.Min(stepTimer, movementInterval * 0.5f);
            return;
        }
        stepTimer -= dt;
        if (stepTimer > 0f)
            return;
        stepTimer = fastMovementInterval > 0f && smoothedSpeed >= fastSpeed ? fastMovementInterval : movementInterval;
        MovementSoundsPlayed++;
        AudioManager.PlayAtWithEcho(movementSounds, position, AudioChannel.Sfx, movementMinDistance, movementMaxDistance,
            CurrentMovementEcho, CurrentMovementVolume);
    }

    private void SetChasingLocal(bool chasing)
    {
        if (chasing == chasingLocal)
            return;
        chasingLocal = chasing;
        if (chasing)
        {
            if (brain == null || brain.PlaysChaseSound)
            {
                ChaseSoundsPlayed++;
                if (loopChaseSound && chaseSound.HasClips)
                {
                    if (chaseLoop == null)
                        chaseLoop = gameObject.AddComponent<AudioSource>();
                    AudioManager.Configure(chaseLoop, AudioChannel.Sfx, 0f);
                    chaseLoop.clip = chaseSound.Pick();
                    chaseLoop.volume = chaseSound.volume;
                    chaseLoop.loop = true;
                    chaseLoop.Play();
                }
                else
                    AudioManager.Play2D(chaseSound, AudioChannel.Sfx);
            }
            if (brain == null || brain.PlaysChaseTheme)
                AudioManager.RequestMusic(this, chaseTheme, ChaseMusicPriority, chaseThemeVolume);
        }
        else
        {
            if (chaseLoop != null)
                chaseLoop.Stop();
            AudioManager.ReleaseMusic(this);
        }
    }
}
