using UnityEngine;

/// <summary>
/// Marks a wall piece or boundary collider generated along one edge of a town block (block-space side). MapManager
/// removes these where the side touches another town block, so neighbouring town blocks join without walls.
/// </summary>
[DisallowMultipleComponent]
public sealed class StreetEdgeWall : MonoBehaviour
{
    [SerializeField] private BlockSide side;

    public BlockSide Side => side;

    public void SetSide(BlockSide value) => side = value;
}
