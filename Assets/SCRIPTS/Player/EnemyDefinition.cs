using Mirror;
using UnityEngine;

/// <summary>
/// An enemy type that a travel destination can roll (shown as "Enemies: [Display Name]" on the travel map).
/// Create with Create > Capstone > Enemy Definition and list it in a MapTerminal destination's Enemy Pool.
/// </summary>
[CreateAssetMenu(menuName = "Capstone/Enemy Definition", fileName = "EnemyDefinition")]
public sealed class EnemyDefinition : ScriptableObject
{
    [Tooltip("Name shown on the travel confirmation panel.")]
    public string displayName = "Enemy";
    [Tooltip("Networked enemy prefab spawned by the destination's EnemySpawner (must be a registered spawnable prefab).")]
    public NetworkIdentity prefab;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
}
