using System;
using UnityEngine;

/// <summary>Sides of a street block in its local space, in the project's West, South, East, North order.</summary>
public enum BlockSide
{
    West,
    South,
    East,
    North
}

[Flags]
public enum BlockSides
{
    None = 0,
    West = 1 << 0,
    South = 1 << 1,
    East = 1 << 2,
    North = 1 << 3,
    All = West | South | East | North
}

public static class BlockSideUtility
{
    public static readonly BlockSide[] All = { BlockSide.West, BlockSide.South, BlockSide.East, BlockSide.North };

    public static BlockSides ToFlag(this BlockSide side) => (BlockSides)(1 << (int)side);

    public static BlockSide Opposite(this BlockSide side) => (BlockSide)(((int)side + 2) % 4);

    /// <summary>Local direction pointing out of the block through this side.</summary>
    public static Vector3 Outward(this BlockSide side) => side switch
    {
        BlockSide.West => Vector3.left,
        BlockSide.South => Vector3.back,
        BlockSide.East => Vector3.right,
        _ => Vector3.forward
    };

    /// <summary>Local yaw that turns a prefab's +Z front to face into the block from this side.</summary>
    public static float InwardYaw(this BlockSide side) => side switch
    {
        BlockSide.West => 90f,
        BlockSide.South => 0f,
        BlockSide.East => 270f,
        _ => 180f
    };
}
