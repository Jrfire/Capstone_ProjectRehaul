using UnityEngine;

public enum EntranceSideMode
{
    [Tooltip("Pick Entrance Count sides at random from the seed.")]
    Random,
    [Tooltip("Use exactly the sides ticked in Manual Sides.")]
    Manual
}

public enum WallMode
{
    [Tooltip("Walls only fill edge gaps that buildings leave open.")]
    FillGaps,
    [Tooltip("Walls wrap the whole block edge except entrances; buildings sit inside, behind the walls.")]
    WrapEdges
}

public enum MiddleBuildingFacing
{
    [Tooltip("Front (+Z) turns to the closest entrance walkway.")]
    TowardWalkway,
    [Tooltip("Random multiple of 90 degrees.")]
    Random
}

public enum EntrancePlacement
{
    [Tooltip("Middle cell of the side; on even cell counts one of the two middle cells.")]
    Centered,
    [Tooltip("Any cell along the side.")]
    Random
}

/// <summary>
/// Authoring settings for a pre-made street block. Use the Generate button in the Inspector (Editor only);
/// the generated content and the <see cref="StreetBlock"/> layout are what the level generator uses at runtime.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(StreetBlock))]
public sealed class StreetBlockGenerator : MonoBehaviour
{
    [Header("Floor")]
    [Tooltip("One floor tile. Its size is measured from its renderers/colliders.")]
    public GameObject floorTile;
    [Tooltip("Number of floor tiles along X and Z. Must be a multiple of Tiles Per Cell.")]
    public Vector2Int sizeInTiles = new Vector2Int(4, 4);
    [Min(1), Tooltip("Tiles per side of one shared grid cell. Keep this the same on every block so blocks line up.")]
    public int tilesPerCell = 2;
    [Tooltip("Adds one box collider under the whole floor if the tile has no collider.")]
    public bool addFloorCollider = true;

    [Header("Entrances")]
    public EntranceSideMode sideMode = EntranceSideMode.Random;
    [Range(0, 4), Tooltip("Used by Random side mode.")]
    public int entranceCount = 2;
    [Tooltip("Used by Manual side mode.")]
    public BlockSides manualSides = BlockSides.South | BlockSides.North;
    public EntrancePlacement entrancePlacement = EntrancePlacement.Centered;
    [Min(2), Tooltip("Opening width in floor tiles. Must not exceed Tiles Per Cell.")]
    public int entranceWidthTiles = 2;

    [Header("Obstacles")]
    [Tooltip("Prefabs carrying an EntranceObstacle. Their +Z front faces into the block.")]
    public GameObject[] obstaclePrefabs = new GameObject[0];
    [Min(0), Tooltip("How many entrances receive a random obstacle.")]
    public int obstacleCount;

    [Header("Buildings (front = +Z)")]
    public GameObject[] buildingPrefabs = new GameObject[0];
    [Min(0f), Tooltip("Gap in metres between neighbouring buildings along an edge.")]
    public float buildingSpacing;

    [Header("Middle Buildings")]
    [Min(0), Tooltip("Buildings placed inside the block, away from the edges, after the edges are built and sealed. 0 = edges only.")]
    public int middleBuildingCount;
    [Tooltip("Prefabs for middle buildings (front = +Z). Empty = use Building Prefabs.")]
    public GameObject[] middleBuildingPrefabs = new GameObject[0];
    [Min(0f), Tooltip("Walkable gap in metres kept around each middle building (to walls, edge buildings, other middle buildings and the floor edge).")]
    public float middleBuildingClearance = 1.5f;
    public MiddleBuildingFacing middleBuildingFacing = MiddleBuildingFacing.TowardWalkway;

    [Header("Filler (blocks with 0 entrances)")]
    [Tooltip("Prefabs packed inside filler blocks (front = +Z). Empty = Middle Building Prefabs, else Building Prefabs.")]
    public GameObject[] fillerBuildingPrefabs = new GameObject[0];
    [Min(0.3f), Tooltip("Width in metres of the narrow alleys left between packed filler buildings (and between them and the edge).")]
    public float fillerAlleyWidth = 1.2f;
    [Tooltip("Edge buildings of filler blocks face outward, so neighbouring streets see their fronts instead of their backs.")]
    public bool fillerEdgeBuildingsFaceOutward = true;

    /// <summary>A block with no entrances is a non-enterable filler, packed with buildings and narrow alleys.</summary>
    public bool IsFiller => (sideMode == EntranceSideMode.Random ? entranceCount : CountManualSides()) == 0;

    private int CountManualSides()
    {
        int count = 0;
        foreach (BlockSide side in BlockSideUtility.All)
            count += (manualSides & side.ToFlag()) != 0 ? 1 : 0;
        return count;
    }

    [Header("Walls")]
    [Tooltip("Fill Gaps: walls close only edge spans buildings leave open. Wrap Edges: walls run around the whole edge (except entrances) and buildings are set back inside them.")]
    public WallMode wallMode = WallMode.FillGaps;
    [Tooltip("Wall pieces placed along the edge. Front = +Z. Spans no piece fits get invisible colliders.")]
    public GameObject[] wallPrefabs = new GameObject[0];
    [Min(0.1f), Tooltip("Height of the invisible boundary colliders.")]
    public float boundaryHeight = 4f;

    [Header("Decorations")]
    public GameObject[] decorationPrefabs = new GameObject[0];
    [Min(0)] public int decorationCount = 8;
    [Min(0f), Tooltip("Minimum clearance in metres around each decoration.")]
    public float decorationSpacing = 0.25f;
    public bool randomDecorationYaw = true;

    [Header("Colliders")]
    [Tooltip("Adds a box collider to placed buildings, walls, obstacles and decorations that have none.")]
    public bool addMissingColliders = true;

    [Header("Generation")]
    public int seed = 12345;

    [Header("Prefab output")]
    public string prefabFolder = "Assets/PREFABS/StreetBlocks";
    public string prefabName = "StreetBlock";
}
