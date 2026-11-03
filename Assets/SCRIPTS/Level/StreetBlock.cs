using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class StreetBlockEntrance
{
    [SerializeField] private BlockSide side;
    [SerializeField, Tooltip("Grid cell along this side, counted from the West/South corner.")]
    private int cellIndex;
    [SerializeField] private float width;
    [SerializeField, Tooltip("Middle of the opening on the block edge, at floor height, in block space.")]
    private Vector3 localCenter;
    [SerializeField] private EntranceObstacle obstacle;

    public StreetBlockEntrance(BlockSide side, int cellIndex, float width, Vector3 localCenter, EntranceObstacle obstacle)
    {
        this.side = side;
        this.cellIndex = cellIndex;
        this.width = width;
        this.localCenter = localCenter;
        this.obstacle = obstacle;
    }

    public BlockSide Side => side;
    public int CellIndex => cellIndex;
    public float Width => width;
    public Vector3 LocalCenter => localCenter;
    public EntranceObstacle Obstacle => obstacle;

    /// <summary>Destroys this entrance's obstacle (e.g. a placed spawn block's entrance must always be open).</summary>
    public void RemoveObstacle()
    {
        if (obstacle != null)
        {
            // Immediate, so NavMesh carving collected later this frame never sees it.
            UnityEngine.Object.DestroyImmediate(obstacle.gameObject);
        }
        obstacle = null;
    }
}

/// <summary>
/// Layout data of a pre-made street block: its floor footprint on the shared grid and its open entrances.
/// The block's origin is the centre of the floor set at floor height; nothing in it extends past the footprint.
/// </summary>
[DisallowMultipleComponent]
public sealed class StreetBlock : MonoBehaviour
{
    [SerializeField] private Vector2Int sizeInTiles;
    [SerializeField] private Vector2 tileSize;
    [SerializeField, Min(1)] private int tilesPerCell = 1;
    [SerializeField] private int seed;
    [SerializeField] private List<StreetBlockEntrance> entrances = new List<StreetBlockEntrance>();
    [SerializeField, Tooltip("Walkable floor used to bake the NavMesh. Defaults to the generator's Generated/Floor group.")]
    private Transform floorRoot;

    public Vector2Int SizeInTiles => sizeInTiles;
    public Vector2 TileSize => tileSize;
    public int TilesPerCell => tilesPerCell;
    public Vector2Int SizeInCells => new Vector2Int(sizeInTiles.x / tilesPerCell, sizeInTiles.y / tilesPerCell);
    /// <summary>Size of one shared grid cell in metres (X, Z). Blocks connect only if this matches.</summary>
    public Vector2 CellSize => tileSize * tilesPerCell;
    /// <summary>Footprint in metres (X, Z).</summary>
    public Vector2 Size => new Vector2(sizeInTiles.x * tileSize.x, sizeInTiles.y * tileSize.y);
    public int Seed => seed;
    public IReadOnlyList<StreetBlockEntrance> Entrances => entrances;
    /// <summary>The walkable floor that MapManager bakes into the NavMesh; everything else in the block becomes an obstacle.</summary>
    public Transform FloorRoot => floorRoot != null ? floorRoot : transform.Find("Generated/Floor");

    /// <summary>True if <paramref name="worldPosition"/> lies over this block's footprint (height is ignored).</summary>
    public bool ContainsXZ(Vector3 worldPosition, float margin = 0f)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector2 half = Size * 0.5f;
        return Mathf.Abs(local.x) <= half.x + margin && Mathf.Abs(local.z) <= half.y + margin;
    }

    public bool HasEntrance(BlockSide side) => GetEntrance(side) != null;

    public StreetBlockEntrance GetEntrance(BlockSide side)
    {
        foreach (StreetBlockEntrance entrance in entrances)
        {
            if (entrance.Side == side)
                return entrance;
        }
        return null;
    }

    /// <summary>Called by the street block generator after it rebuilds the block.</summary>
    public void ApplyLayout(Vector2Int sizeInTiles, Vector2 tileSize, int tilesPerCell, int seed,
        IEnumerable<StreetBlockEntrance> entrances)
    {
        this.sizeInTiles = sizeInTiles;
        this.tileSize = tileSize;
        this.tilesPerCell = Mathf.Max(1, tilesPerCell);
        this.seed = seed;
        this.entrances = new List<StreetBlockEntrance>(entrances);
    }

    private void OnDrawGizmos()
    {
        Vector2 size = Size;
        if (size.x <= 0f || size.y <= 0f)
            return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(0f, 0.01f, 0f), new Vector3(size.x, 0f, size.y));
        Vector2 cell = CellSize;
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.25f);
        for (float x = -size.x * 0.5f + cell.x; x < size.x * 0.5f - 0.001f; x += cell.x)
            Gizmos.DrawLine(new Vector3(x, 0.01f, -size.y * 0.5f), new Vector3(x, 0.01f, size.y * 0.5f));
        for (float z = -size.y * 0.5f + cell.y; z < size.y * 0.5f - 0.001f; z += cell.y)
            Gizmos.DrawLine(new Vector3(-size.x * 0.5f, 0.01f, z), new Vector3(size.x * 0.5f, 0.01f, z));

        foreach (StreetBlockEntrance entrance in entrances)
        {
            Gizmos.color = entrance.Obstacle != null ? Color.red : Color.green;
            Vector3 outward = entrance.Side.Outward();
            Vector3 along = Vector3.Cross(Vector3.up, outward) * (entrance.Width * 0.5f);
            Vector3 center = entrance.LocalCenter + Vector3.up * 0.05f;
            Gizmos.DrawLine(center - along, center + along);
            Gizmos.DrawLine(center, center + outward);
            Gizmos.DrawWireCube(center + Vector3.up, new Vector3(Mathf.Abs(along.x) * 2f + 0.05f, 2f, Mathf.Abs(along.z) * 2f + 0.05f));
        }
    }
}
