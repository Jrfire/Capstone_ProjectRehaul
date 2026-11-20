using Capstone.Audio;
using UnityEngine;

/// <summary>
/// Tiktik tapping (every peer, 3D from its synced position). While Stalking it taps in uneven bursts: a few rapid or
/// slow taps, then a random pause (e.g. 3 quick taps, a 2 s pause, 2 slow taps...). While Attacking (and visible) the taps
/// get faster and louder the closer it is to its victim. Use this instead of EnemyAudio's movement sounds.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TiktikBrain))]
public sealed class TiktikAudio : MonoBehaviour
{
    [SerializeField] private AudioClipSet taps = new AudioClipSet();
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 30f;

    [Header("Stalking bursts")]
    [SerializeField, Tooltip("Taps per burst (min, max).")]
    private Vector2Int burstTaps = new Vector2Int(2, 4);
    [SerializeField, Tooltip("Gap between taps of a rapid burst (min, max seconds).")]
    private Vector2 rapidGap = new Vector2(0.12f, 0.3f);
    [SerializeField, Tooltip("Gap between taps of a slow burst (min, max seconds).")]
    private Vector2 slowGap = new Vector2(0.5f, 0.9f);
    [SerializeField, Range(0f, 1f), Tooltip("Chance a burst is rapid rather than slow.")]
    private float rapidChance = 0.6f;
    [SerializeField, Tooltip("Pause between bursts (min, max seconds).")]
    private Vector2 burstPause = new Vector2(1.2f, 3f);

    [Header("Attacking")]
    [SerializeField, Tooltip("Seconds between taps at Close Distance or nearer.")]
    private float closeInterval = 0.07f;
    [SerializeField, Tooltip("Seconds between taps at Far Distance or farther.")]
    private float farInterval = 0.4f;
    [SerializeField] private float closeDistance = 2f;
    [SerializeField] private float farDistance = 20f;
    [SerializeField, Range(0f, 1f), Tooltip("Volume multiplier when far (full volume when close).")]
    private float farVolume = 0.6f;

    private TiktikBrain brain;
    private float nextTapAt;
    private int burstLeft;
    private bool burstRapid;
    private bool wasStalking;

    public int TapsPlayed { get; private set; }
    public int BurstsStarted { get; private set; }
    public float LastInterval { get; private set; }

    private void Awake() => brain = GetComponent<TiktikBrain>();

    private void Update()
    {
        if (brain == null)
            return;
        if (brain.IsAttacking)
        {
            wasStalking = false;
            UpdateAttack();
            return;
        }
        bool stalking = brain.StateName == "Stalking";
        if (!stalking)
        {
            wasStalking = false;
            return;
        }
        if (!wasStalking)
        {
            wasStalking = true;
            burstLeft = 0;
            nextTapAt = Time.time + RandomIn(burstPause);
        }
        UpdateStalk();
    }

    private void UpdateStalk()
    {
        if (Time.time < nextTapAt)
            return;
        if (burstLeft <= 0)
        {
            BurstsStarted++;
            burstLeft = Random.Range(Mathf.Min(burstTaps.x, burstTaps.y), Mathf.Max(burstTaps.x, burstTaps.y) + 1);
            burstRapid = Random.value < rapidChance;
        }
        Tap(1f);
        burstLeft--;
        LastInterval = burstLeft > 0 ? RandomIn(burstRapid ? rapidGap : slowGap) : RandomIn(burstPause);
        nextTapAt = Time.time + LastInterval;
    }

    private void UpdateAttack()
    {
        if (!brain.IsPresent)
            return;
        float distance = farDistance;
        if (brain.Victim != null)
            distance = Vector3.Distance(brain.Victim.transform.position, transform.position);
        float closeness = 1f - Mathf.InverseLerp(closeDistance, farDistance, distance);
        LastInterval = IntervalAt(distance);
        if (Time.time < nextTapAt)
        {
            // Speed up at once when it closes in instead of waiting out a long gap.
            nextTapAt = Mathf.Min(nextTapAt, Time.time + LastInterval);
            return;
        }
        Tap(Mathf.Lerp(farVolume, 1f, closeness));
        nextTapAt = Time.time + LastInterval;
    }

    /// <summary>Seconds between attack taps at <paramref name="distance"/> from the victim.</summary>
    public float IntervalAt(float distance) =>
        Mathf.Lerp(closeInterval, farInterval, Mathf.InverseLerp(closeDistance, farDistance, distance));

    private void Tap(float volumeScale)
    {
        TapsPlayed++;
        AudioSource played = AudioManager.PlayAt(taps, transform.position + Vector3.up, AudioChannel.Sfx, minDistance, maxDistance);
        if (played != null)
            played.volume = Mathf.Clamp01(played.volume * volumeScale);
    }

    private static float RandomIn(Vector2 range) => Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
}
