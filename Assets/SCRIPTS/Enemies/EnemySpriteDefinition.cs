using UnityEngine;

public enum EnemyAnimationState
{
    Idle,
    Moving
}

/// <summary>
/// Directional billboard artwork for an enemy: Idle and Moving, each split into West, South, East, North like
/// <see cref="CharacterDefinition"/>. Frames are sliced automatically from a sheet or assigned manually.
/// </summary>
[CreateAssetMenu(fileName = "EnemySprites", menuName = "Capstone/Enemy Sprite Definition")]
public sealed class EnemySpriteDefinition : ScriptableObject
{
    [SerializeField] private CharacterAnimation idle = new CharacterAnimation(true);
    [SerializeField] private CharacterAnimation moving = new CharacterAnimation(true);

    public CharacterAnimation GetAnimation(EnemyAnimationState state) =>
        state == EnemyAnimationState.Moving ? moving : idle;
}
