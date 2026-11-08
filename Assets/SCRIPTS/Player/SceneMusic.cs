using Capstone.Audio;
using UnityEngine;

/// <summary>
/// A map scene's arrival music. Put it on any object in a location scene (e.g. StreetsTest's MapManager object) and drag
/// in the OST: it starts when players land after the loading screen (<see cref="TravelDirector.ClientArrived"/>), or on
/// Start when the scene was opened without travelling. Chase themes outrank it; it stops with the scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneMusic : MonoBehaviour
{
    public const int Priority = 0;

    [SerializeField, Tooltip("Music played when players arrive in this scene (empty = silence).")]
    private AudioClip arrivalTheme;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SerializeField, Tooltip("Keep looping; off = play once.")]
    private bool loop;

    public AudioClip ArrivalTheme { get => arrivalTheme; set => arrivalTheme = value; }
    public bool Started { get; private set; }

    private void OnEnable() => TravelDirector.ClientArrived += Play;

    private void OnDisable()
    {
        TravelDirector.ClientArrived -= Play;
        AudioManager.ReleaseMusic(this);
    }

    private void Start()
    {
        if (TravelDirector.Instance == null || !TravelDirector.Instance.IsTraveling)
            Play();
    }

    /// <summary>Starts the arrival theme.</summary>
    public void Play()
    {
        Started = true;
        AudioManager.RequestMusic(this, arrivalTheme, Priority, volume, loop);
    }
}
