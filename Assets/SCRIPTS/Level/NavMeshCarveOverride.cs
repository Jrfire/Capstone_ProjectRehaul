using UnityEngine;

public enum NavMeshCarveMode
{
    /// <summary>MapManager carves every collider's box (decorations scaled by Decoration Carve Scale).</summary>
    Default,
    /// <summary>One carving box with the footprint below replaces the colliders' boxes.</summary>
    Custom,
    /// <summary>Enemies may walk through this object.</summary>
    None,
}

/// <summary>
/// Put on a street prefab (decoration, building, obstacle) to control the NavMesh hole MapManager carves for every
/// collider under it. Unity still expands the carved hole by the NavMesh agent radius (MapManager's Bake Agent Radius).
/// </summary>
[DisallowMultipleComponent]
public sealed class NavMeshCarveOverride : MonoBehaviour
{
    [SerializeField] private NavMeshCarveMode mode = NavMeshCarveMode.Custom;
    [SerializeField, Tooltip("Custom: footprint centre in this object's local space.")]
    private Vector3 center = new Vector3(0f, 0.5f, 0f);
    [SerializeField, Tooltip("Custom: footprint width (X) and depth (Z) in this object's local space.")]
    private Vector2 size = new Vector2(0.3f, 0.3f);
    [SerializeField, Min(0.01f), Tooltip("Custom: box height in this object's local space.")]
    private float height = 1f;

    public NavMeshCarveMode Mode => mode;
    public Vector3 Center => center;
    public Vector3 BoxSize => new Vector3(Mathf.Max(0.01f, size.x), height, Mathf.Max(0.01f, size.y));

    private void OnDrawGizmosSelected()
    {
        if (mode != NavMeshCarveMode.Custom)
            return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.3f, 0.9f, 0.8f);
        Gizmos.DrawWireCube(center, BoxSize);
    }
}
