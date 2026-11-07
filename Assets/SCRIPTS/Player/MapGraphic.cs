using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sprite-free UI map drawn as a mesh: a grid background aligned to the street cells, every street block of a
/// <see cref="MapManager"/> as a filled square whose outline has holes at its entrances, a red line across entrances
/// with an uncleared <see cref="EntranceObstacle"/>, and a dot for the local player. World +Z is up and +X is right.
/// Filler blocks and plugged gap cells are drawn underneath as closed squares in their own (dimmer) colours.
/// Block contents (buildings, decorations) are not drawn.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MapGraphic : MaskableGraphic
{
    [Header("Grid background")]
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.96f);
    [SerializeField] private Color gridColor = new Color(0.45f, 0.55f, 0.65f, 0.18f);
    [SerializeField] private Color majorGridColor = new Color(0.45f, 0.55f, 0.65f, 0.4f);
    [SerializeField, Min(0f), Tooltip("Grid spacing in metres. 0 = the street blocks' cell size (6 m).")]
    private float gridCellSize;
    [SerializeField, Min(0), Tooltip("Every Nth grid line is drawn brighter. 0 = no major lines.")]
    private int majorLineEvery = 4;
    [SerializeField, Min(0.5f)] private float gridLineWidth = 1f;
    [SerializeField, Min(1f), Tooltip("Pixels per grid cell when there is no map to fit (e.g. at base).")]
    private float emptyGridPixels = 40f;

    [Header("Blocks")]
    [SerializeField] private Color blockFillColor = new Color(0.2f, 0.21f, 0.23f, 0.9f);
    [SerializeField] private Color blockOutlineColor = new Color(0.86f, 0.83f, 0.76f, 1f);
    [SerializeField, Min(0.5f)] private float outlineWidth = 3f;
    [SerializeField, Tooltip("Line across an entrance blocked by an uncleared obstacle.")]
    private Color obstacleColor = new Color(0.9f, 0.08f, 0.08f, 1f);
    [SerializeField, Min(0.5f)] private float obstacleWidth = 5f;
    [SerializeField, Min(0f), Tooltip("Empty space (metres) kept around the street when fitting it into the panel.")]
    private float paddingMeters = 6f;

    [Header("Filler blocks")]
    [SerializeField, Tooltip("Draw the map's filler blocks and plugged gap cells (no entrances) as closed squares under the street.")]
    private bool drawFillers = true;
    [SerializeField] private Color fillerFillColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);
    [SerializeField] private Color fillerOutlineColor = new Color(0.55f, 0.53f, 0.49f, 0.8f);
    [SerializeField, Min(0.5f)] private float fillerOutlineWidth = 2f;

    [Header("Player")]
    [SerializeField] private Color playerColor = new Color(0.2f, 0.55f, 1f, 1f);
    [SerializeField] private Color playerOutlineColor = Color.white;
    [SerializeField, Min(1f)] private float playerDotRadius = 7f;
    [SerializeField, Min(0f)] private float playerOutlineWidth = 2f;

    private MapManager map;
    private bool hasPlayer;
    private Vector3 playerPosition;

    public MapManager Map => map;
    public int BlocksDrawn { get; private set; }
    /// <summary>Filler blocks plus plugged gap cells drawn as closed squares.</summary>
    public int FillersDrawn { get; private set; }
    public int GapsDrawn { get; private set; }
    public int ObstaclesDrawn { get; private set; }
    public bool PlayerDrawn { get; private set; }
    /// <summary>Where the player dot was last drawn, in this graphic's local space.</summary>
    public Vector2 PlayerPoint { get; private set; }
    public Color PlayerColor => playerColor;
    public Color ObstacleColor => obstacleColor;

    /// <summary>Sets what to draw; call every frame while visible so the dot and obstacle states stay current.</summary>
    public void Show(MapManager source, bool showPlayer, Vector3 player)
    {
        map = source;
        hasPlayer = showPlayer;
        playerPosition = player;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh) => DrawTo(vh);

    /// <summary>Builds the whole map mesh into <paramref name="vh"/>.</summary>
    public void DrawTo(VertexHelper vh)
    {
        vh.Clear();
        BlocksDrawn = FillersDrawn = GapsDrawn = ObstaclesDrawn = 0;
        PlayerDrawn = false;
        Rect rect = rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        Quad(vh, rect.min, new Vector2(rect.xMin, rect.yMax), rect.max, new Vector2(rect.xMax, rect.yMin), backgroundColor);
        List<StreetBlock> blocks = StreetBlocks();
        List<Vector3[]> fillers = FillerSquares();
        Frame frame = FitFrame(rect, blocks, fillers);
        DrawGrid(vh, rect, frame);

        foreach (Vector3[] square in fillers)
        {
            Quad(vh, frame.ToMap(square[0]), frame.ToMap(square[1]), frame.ToMap(square[2]), frame.ToMap(square[3]), fillerFillColor);
            FillersDrawn++;
        }
        foreach (Vector3[] square in fillers)
        {
            for (int corner = 0; corner < 4; corner++)
                Line(vh, frame.ToMap(square[corner]), frame.ToMap(square[(corner + 1) % 4]), fillerOutlineWidth, fillerOutlineColor, true);
        }

        foreach (StreetBlock block in blocks)
        {
            Vector2 half = block.Size * 0.5f;
            Quad(vh, ToMap(frame, block, -half.x, -half.y), ToMap(frame, block, -half.x, half.y),
                ToMap(frame, block, half.x, half.y), ToMap(frame, block, half.x, -half.y), blockFillColor);
            BlocksDrawn++;
        }
        var obstacles = new List<(Vector2 a, Vector2 b)>();
        foreach (StreetBlock block in blocks)
            DrawOutline(vh, frame, block, obstacles);
        foreach ((Vector2 a, Vector2 b) in obstacles)
        {
            Line(vh, a, b, obstacleWidth, obstacleColor, false);
            ObstaclesDrawn++;
        }

        if (hasPlayer)
        {
            Vector2 point = frame.ToMap(playerPosition);
            point.x = Mathf.Clamp(point.x, rect.xMin + playerDotRadius, rect.xMax - playerDotRadius);
            point.y = Mathf.Clamp(point.y, rect.yMin + playerDotRadius, rect.yMax - playerDotRadius);
            if (playerOutlineWidth > 0f)
                Circle(vh, point, playerDotRadius + playerOutlineWidth, playerOutlineColor);
            Circle(vh, point, playerDotRadius, playerColor);
            PlayerPoint = point;
            PlayerDrawn = true;
        }
    }

    /// <summary>Converts a world position to this graphic's local space with the current map fit.</summary>
    public Vector2 WorldToMap(Vector3 world) => FitFrame(rectTransform.rect, StreetBlocks(), FillerSquares()).ToMap(world);

    /// <summary>World corners (clockwise from -X/-Z) of every filler block and plugged gap cell to draw.</summary>
    private List<Vector3[]> FillerSquares()
    {
        var squares = new List<Vector3[]>();
        if (!drawFillers || map == null || !map.IsBuilt)
            return squares;
        for (int i = 0; i < map.FillerCount; i++)
        {
            StreetBlock filler = map.GetFiller(i);
            if (filler == null)
                continue;
            Vector2 half = filler.Size * 0.5f;
            squares.Add(new[]
            {
                filler.transform.TransformPoint(new Vector3(-half.x, 0f, -half.y)),
                filler.transform.TransformPoint(new Vector3(-half.x, 0f, half.y)),
                filler.transform.TransformPoint(new Vector3(half.x, 0f, half.y)),
                filler.transform.TransformPoint(new Vector3(half.x, 0f, -half.y)),
            });
        }
        foreach (Vector3[] cell in map.PluggedCells)
        {
            if (cell != null && cell.Length == 4)
                squares.Add(cell);
        }
        return squares;
    }

    private List<StreetBlock> StreetBlocks()
    {
        var blocks = new List<StreetBlock>();
        if (map == null || !map.IsBuilt)
            return blocks;
        for (int i = 0; i < map.BlockCount; i++)
        {
            StreetBlock block = map.GetBlock(i);
            if (block != null)
                blocks.Add(block);
        }
        return blocks;
    }

    private readonly struct Frame
    {
        public readonly Vector2 WorldCenter;
        public readonly Vector2 MapCenter;
        public readonly float Scale;
        public readonly Vector2 GridOrigin;
        public readonly float Cell;

        public Frame(Vector2 worldCenter, Vector2 mapCenter, float scale, Vector2 gridOrigin, float cell)
        {
            WorldCenter = worldCenter;
            MapCenter = mapCenter;
            Scale = scale;
            GridOrigin = gridOrigin;
            Cell = cell;
        }

        public Vector2 ToMap(Vector3 world) => MapCenter + (new Vector2(world.x, world.z) - WorldCenter) * Scale;
        public float ToWorldX(float mapX) => WorldCenter.x + (mapX - MapCenter.x) / Scale;
        public float ToWorldZ(float mapY) => WorldCenter.y + (mapY - MapCenter.y) / Scale;
    }

    private Frame FitFrame(Rect rect, List<StreetBlock> blocks, List<Vector3[]> fillers)
    {
        float cell = gridCellSize > 0f ? gridCellSize : blocks.Count > 0 && blocks[0].CellSize.x > 0f ? blocks[0].CellSize.x : 6f;
        if (blocks.Count == 0)
            return new Frame(Vector2.zero, rect.center, emptyGridPixels / cell, Vector2.zero, cell);

        bool any = false;
        var bounds = new Rect();
        void Include(Vector3 world)
        {
            var point = new Vector2(world.x, world.z);
            if (!any)
            {
                bounds = new Rect(point, Vector2.zero);
                any = true;
            }
            else
            {
                bounds.xMin = Mathf.Min(bounds.xMin, point.x);
                bounds.yMin = Mathf.Min(bounds.yMin, point.y);
                bounds.xMax = Mathf.Max(bounds.xMax, point.x);
                bounds.yMax = Mathf.Max(bounds.yMax, point.y);
            }
        }
        foreach (StreetBlock block in blocks)
        {
            Vector2 half = block.Size * 0.5f;
            for (int corner = 0; corner < 4; corner++)
                Include(block.transform.TransformPoint(new Vector3(corner % 2 == 0 ? -half.x : half.x, 0f, corner < 2 ? -half.y : half.y)));
        }
        foreach (Vector3[] square in fillers)
        {
            foreach (Vector3 corner in square)
                Include(corner);
        }
        float width = Mathf.Max(bounds.width + paddingMeters * 2f, 1f);
        float height = Mathf.Max(bounds.height + paddingMeters * 2f, 1f);
        float scale = Mathf.Min(rect.width / width, rect.height / height);
        Vector3 origin = blocks[0].transform.TransformPoint(new Vector3(-blocks[0].Size.x * 0.5f, 0f, -blocks[0].Size.y * 0.5f));
        return new Frame(bounds.center, rect.center, scale, new Vector2(origin.x, origin.z), cell);
    }

    private void DrawGrid(VertexHelper vh, Rect rect, Frame frame)
    {
        if (frame.Cell <= 0f || frame.Cell * frame.Scale < 3f)
            return;
        int first = Mathf.CeilToInt((frame.ToWorldX(rect.xMin) - frame.GridOrigin.x) / frame.Cell);
        int last = Mathf.FloorToInt((frame.ToWorldX(rect.xMax) - frame.GridOrigin.x) / frame.Cell);
        for (int i = first; i <= last; i++)
        {
            float x = frame.ToMap(new Vector3(frame.GridOrigin.x + i * frame.Cell, 0f, 0f)).x;
            Line(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), gridLineWidth, GridColor(i), false);
        }
        first = Mathf.CeilToInt((frame.ToWorldZ(rect.yMin) - frame.GridOrigin.y) / frame.Cell);
        last = Mathf.FloorToInt((frame.ToWorldZ(rect.yMax) - frame.GridOrigin.y) / frame.Cell);
        for (int i = first; i <= last; i++)
        {
            float y = frame.ToMap(new Vector3(0f, 0f, frame.GridOrigin.y + i * frame.Cell)).y;
            Line(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), gridLineWidth, GridColor(i), false);
        }
    }

    private Color GridColor(int index) => majorLineEvery > 0 && index % majorLineEvery == 0 ? majorGridColor : gridColor;

    private void DrawOutline(VertexHelper vh, Frame frame, StreetBlock block, List<(Vector2, Vector2)> obstacles)
    {
        Vector2 half = block.Size * 0.5f;
        foreach (BlockSide side in BlockSideUtility.All)
        {
            bool vertical = side == BlockSide.West || side == BlockSide.East;
            float fixedCoordinate = side switch
            {
                BlockSide.West => -half.x,
                BlockSide.East => half.x,
                BlockSide.South => -half.y,
                _ => half.y
            };
            float extent = vertical ? half.y : half.x;
            Vector2 Local(float along) => vertical ? new Vector2(fixedCoordinate, along) : new Vector2(along, fixedCoordinate);

            var gaps = new List<(float from, float to, EntranceObstacle obstacle)>();
            foreach (StreetBlockEntrance entrance in block.Entrances)
            {
                if (entrance == null || entrance.Side != side)
                    continue;
                float center = vertical ? entrance.LocalCenter.z : entrance.LocalCenter.x;
                gaps.Add((Mathf.Max(-extent, center - entrance.Width * 0.5f), Mathf.Min(extent, center + entrance.Width * 0.5f), entrance.Obstacle));
            }
            gaps.Sort((a, b) => a.from.CompareTo(b.from));

            float start = -extent;
            foreach ((float from, float to, EntranceObstacle obstacle) in gaps)
            {
                if (from > start)
                    Segment(vh, frame, block, Local(start), Local(from));
                start = Mathf.Max(start, to);
                GapsDrawn++;
                if (obstacle != null && !obstacle.IsCleared)
                {
                    Vector2 a = Local(from), b = Local(to);
                    obstacles.Add((ToMap(frame, block, a.x, a.y), ToMap(frame, block, b.x, b.y)));
                }
            }
            if (start < extent)
                Segment(vh, frame, block, Local(start), Local(extent));
        }
    }

    private void Segment(VertexHelper vh, Frame frame, StreetBlock block, Vector2 from, Vector2 to)
    {
        Vector3 a = block.transform.TransformPoint(new Vector3(from.x, 0f, from.y));
        Vector3 b = block.transform.TransformPoint(new Vector3(to.x, 0f, to.y));
        // Skip the stretches where touching town blocks had their walls removed.
        var cuts = new List<(float from, float to)>();
        if (map != null)
        {
            Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
            float lengthSq = ab.sqrMagnitude;
            foreach (Vector3[] seam in map.OpenSeams)
            {
                if (seam == null || seam.Length != 2 || lengthSq < 0.0001f || !OnLine(a, ab, seam[0]) || !OnLine(a, ab, seam[1]))
                    continue;
                float t0 = Vector2.Dot(new Vector2(seam[0].x - a.x, seam[0].z - a.z), ab) / lengthSq;
                float t1 = Vector2.Dot(new Vector2(seam[1].x - a.x, seam[1].z - a.z), ab) / lengthSq;
                if (t0 > t1)
                    (t0, t1) = (t1, t0);
                if (t1 > 0f && t0 < 1f)
                    cuts.Add((Mathf.Max(0f, t0), Mathf.Min(1f, t1)));
            }
        }
        cuts.Sort((x, y) => x.from.CompareTo(y.from));
        float start = 0f;
        foreach ((float cutFrom, float cutTo) in cuts)
        {
            if (cutFrom > start)
                Line(vh, frame.ToMap(Vector3.Lerp(a, b, start)), frame.ToMap(Vector3.Lerp(a, b, cutFrom)), outlineWidth, blockOutlineColor, true);
            start = Mathf.Max(start, cutTo);
        }
        if (start < 1f)
            Line(vh, frame.ToMap(Vector3.Lerp(a, b, start)), frame.ToMap(b), outlineWidth, blockOutlineColor, true);
    }

    private static bool OnLine(Vector3 origin, Vector2 direction, Vector3 point)
    {
        Vector2 offset = new Vector2(point.x - origin.x, point.z - origin.z);
        return Mathf.Abs(offset.x * direction.y - offset.y * direction.x) / direction.magnitude <= 0.05f;
    }

    private static Vector2 ToMap(Frame frame, StreetBlock block, float localX, float localZ) =>
        frame.ToMap(block.transform.TransformPoint(new Vector3(localX, 0f, localZ)));

    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color, bool squareCaps)
    {
        Vector2 direction = b - a;
        float length = direction.magnitude;
        if (length < 0.0001f)
            return;
        direction /= length;
        if (squareCaps)
        {
            a -= direction * (width * 0.5f);
            b += direction * (width * 0.5f);
        }
        Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
        Quad(vh, a - normal, a + normal, b + normal, b - normal, color);
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int index = vh.currentVertCount;
        Color32 color32 = color;
        vh.AddVert(a, color32, Vector2.zero);
        vh.AddVert(b, color32, Vector2.zero);
        vh.AddVert(c, color32, Vector2.zero);
        vh.AddVert(d, color32, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }

    private static void Circle(VertexHelper vh, Vector2 center, float radius, Color color)
    {
        const int segments = 24;
        int index = vh.currentVertCount;
        Color32 color32 = color;
        vh.AddVert(center, color32, Vector2.zero);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color32, Vector2.zero);
        }
        for (int i = 0; i < segments; i++)
            vh.AddTriangle(index, index + 1 + (i + 1) % segments, index + 1 + i);
    }
}
