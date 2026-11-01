using System;
using System.Collections.Generic;
using UnityEngine;

public enum CharacterAnimationState
{
    Idle,
    Walk,
    Run,
    Attack,
    Dead
}

public enum CharacterDirection
{
    West,
    South,
    East,
    North
}

public enum CharacterFrameAssignment
{
    Automatic,
    Manual
}

[Serializable]
public sealed class CharacterAnimation
{
    [SerializeField] private CharacterFrameAssignment assignmentMode;
    [SerializeField, Min(1)] private int manualFramesPerDirection = 1;
    [SerializeField] private Texture2D spriteSheet;
    [SerializeField] private Vector2Int cellSize = new Vector2Int(64, 64);
    [SerializeField, Min(0.01f)] private float framesPerSecond = 8f;
    [SerializeField, Min(0.01f)] private float pixelsPerUnit = 100f;
    [SerializeField, HideInInspector] private bool directional = true;
    [SerializeField, HideInInspector] private Sprite[] frames = Array.Empty<Sprite>();
    [SerializeField, HideInInspector] private Texture2D generatedSheet;
    [SerializeField, HideInInspector] private Vector2Int generatedCellSize;
    [SerializeField, HideInInspector] private float generatedPixelsPerUnit;

    public CharacterAnimation(bool directional)
    {
        this.directional = directional;
    }

    public Texture2D SpriteSheet => spriteSheet;
    public CharacterFrameAssignment AssignmentMode => assignmentMode;
    public Vector2Int CellSize => cellSize;
    public float FramesPerSecond => framesPerSecond;
    public float PixelsPerUnit => pixelsPerUnit;
    public bool Directional => directional;
    public IReadOnlyList<Sprite> Frames => frames;
    public int FramesPerDirection => assignmentMode == CharacterFrameAssignment.Manual
        ? manualFramesPerDirection : frames.Length / (directional ? 4 : 1);
    public bool NeedsGeneration => assignmentMode == CharacterFrameAssignment.Automatic &&
        (generatedSheet != spriteSheet || generatedCellSize != cellSize ||
        generatedPixelsPerUnit != pixelsPerUnit);
    public bool IsReady
    {
        get
        {
            if (frames == null || FramesPerDirection <= 0 ||
                frames.Length / (directional ? 4 : 1) != FramesPerDirection ||
                frames.Length % (directional ? 4 : 1) != 0 ||
                !(framesPerSecond > 0f) || float.IsInfinity(framesPerSecond) ||
                (assignmentMode == CharacterFrameAssignment.Automatic && (spriteSheet == null || NeedsGeneration)))
                return false;

            foreach (Sprite sprite in frames)
            {
                if (sprite == null)
                    return false;
            }
            return true;
        }
    }

    public void SetManualFrameCount(int count)
    {
        int directions = directional ? 4 : 1;
        if (count <= 0 || count > int.MaxValue / directions)
            throw new ArgumentOutOfRangeException(nameof(count), "Frame count must be positive and fit all directions.");
        if (frames.Length % directions != 0)
            throw new InvalidOperationException("Existing frames cannot be split equally into directions.");

        int oldCount = frames.Length / directions;
        var resized = new Sprite[count * directions];
        for (int direction = 0; direction < directions; direction++)
            Array.Copy(frames, direction * oldCount, resized, direction * count, Math.Min(oldCount, count));

        frames = resized;
        manualFramesPerDirection = count;
        assignmentMode = CharacterFrameAssignment.Manual;
        generatedSheet = null;
        generatedCellSize = default;
        generatedPixelsPerUnit = 0f;
    }

    public void AssignFrames(Sprite[] sprites)
    {
        if (sprites == null || sprites.Length == 0 || sprites.Length % (directional ? 4 : 1) != 0)
            throw new ArgumentException("Directional animations need a nonzero multiple of four frames; Dead needs at least one.", nameof(sprites));
        if (assignmentMode == CharacterFrameAssignment.Manual &&
            sprites.Length / (directional ? 4 : 1) != manualFramesPerDirection)
            throw new ArgumentException("The sprites must match the manually configured frame count for every direction.", nameof(sprites));

        foreach (Sprite sprite in sprites)
        {
            if (sprite == null)
                throw new ArgumentException("Animation frames cannot contain missing sprites.", nameof(sprites));
        }

        frames = (Sprite[])sprites.Clone();
        if (assignmentMode == CharacterFrameAssignment.Automatic)
        {
            generatedSheet = spriteSheet;
            generatedCellSize = cellSize;
            generatedPixelsPerUnit = pixelsPerUnit;
        }
    }

    public Sprite GetFrame(CharacterDirection direction, double elapsedSeconds, bool loop)
    {
        if (!IsReady)
            throw new InvalidOperationException("Generate the automatic sheet or assign every manual frame, with a valid FPS, before playing this animation.");
        if (direction < CharacterDirection.West || direction > CharacterDirection.North)
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        double frame = Math.Floor(Math.Max(0d, elapsedSeconds) * framesPerSecond);
        int index = loop
            ? (int)(frame % FramesPerDirection)
            : (int)Math.Min(frame, FramesPerDirection - 1);
        return frames[(directional ? (int)direction * FramesPerDirection : 0) + index];
    }
}

[CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Capstone/Character Definition")]
public sealed class CharacterDefinition : ScriptableObject
{
    [SerializeField] private CharacterAnimation idle = new CharacterAnimation(true);
    [SerializeField] private CharacterAnimation walk = new CharacterAnimation(true);
    [SerializeField] private CharacterAnimation run = new CharacterAnimation(true);
    [SerializeField] private CharacterAnimation attack = new CharacterAnimation(true);
    [SerializeField] private CharacterAnimation dead = new CharacterAnimation(false);

    public CharacterAnimation GetAnimation(CharacterAnimationState state)
    {
        switch (state)
        {
            case CharacterAnimationState.Idle: return idle;
            case CharacterAnimationState.Walk: return walk;
            case CharacterAnimationState.Run: return run;
            case CharacterAnimationState.Attack: return attack;
            case CharacterAnimationState.Dead: return dead;
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }
    }
}
