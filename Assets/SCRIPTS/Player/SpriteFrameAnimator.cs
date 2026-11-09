using UnityEngine;

/// <summary>Loops a list of sprites on a SpriteRenderer (local, non-networked visuals such as the pocket Santelmo).</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class SpriteFrameAnimator : MonoBehaviour
{
    [SerializeField] private Sprite[] frames = new Sprite[0];
    [SerializeField, Min(0.1f)] private float framesPerSecond = 8f;

    private SpriteRenderer spriteRenderer;
    private float startTime;

    public Sprite[] Frames => frames;
    public float FramesPerSecond => framesPerSecond;

    public void Configure(Sprite[] newFrames, float fps)
    {
        frames = newFrames ?? new Sprite[0];
        framesPerSecond = Mathf.Max(0.1f, fps);
    }

    private void Awake() => spriteRenderer = GetComponent<SpriteRenderer>();

    private void OnEnable() => startTime = Time.time;

    private void Update()
    {
        if (frames == null || frames.Length == 0)
            return;
        int index = Mathf.FloorToInt((Time.time - startTime) * framesPerSecond) % frames.Length;
        if (frames[index] != null)
            spriteRenderer.sprite = frames[index];
    }
}
