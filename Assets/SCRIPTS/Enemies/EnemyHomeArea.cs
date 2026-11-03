using UnityEngine;

/// <summary>The area an enemy guards: a placed street block, or an axis-aligned box when no map exists.</summary>
public readonly struct EnemyHomeArea
{
    public readonly StreetBlock Block;
    public readonly Vector3 Center;
    public readonly Vector2 HalfSize;

    private EnemyHomeArea(StreetBlock block, Vector3 center, Vector2 halfSize)
    {
        Block = block;
        Center = center;
        HalfSize = halfSize;
    }

    public bool IsValid => Block != null || HalfSize.x > 0f;

    public static EnemyHomeArea FromBlock(StreetBlock block) =>
        new EnemyHomeArea(block, block.transform.position, block.Size * 0.5f);

    public static EnemyHomeArea FromBox(Vector3 center, Vector2 halfSize) =>
        new EnemyHomeArea(null, center, new Vector2(Mathf.Max(0.5f, halfSize.x), Mathf.Max(0.5f, halfSize.y)));

    public bool Contains(Vector3 position)
    {
        if (Block != null)
            return Block.ContainsXZ(position);
        return Mathf.Abs(position.x - Center.x) <= HalfSize.x && Mathf.Abs(position.z - Center.z) <= HalfSize.y;
    }

    public Vector3 RandomPoint()
    {
        var local = new Vector3(Random.Range(-HalfSize.x, HalfSize.x), 0f, Random.Range(-HalfSize.y, HalfSize.y));
        return Block != null ? Block.transform.TransformPoint(local) : Center + local;
    }
}
