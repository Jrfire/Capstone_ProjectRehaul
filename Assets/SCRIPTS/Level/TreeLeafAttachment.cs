using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses a bare (dead-branch) tree with a leaf sprite: crossed, static leaf cards are placed at seeded points sampled
/// from the upper part of the tree's own mesh, so the leaves sit on the branches. Use the context menu
/// <b>Regenerate Leaves</b> (prefabs are baked in the Editor; the cards are plain child SpriteRenderers, no runtime cost).
/// </summary>
[DisallowMultipleComponent]
public sealed class TreeLeafAttachment : MonoBehaviour
{
    public const string CanopyName = "Leaves (Generated)";

    [SerializeField] private Sprite leafSprite;
    [SerializeField, Tooltip("Material for the leaf cards (empty = the default sprite material). Solid Sprite gives fog-correct depth.")]
    private Material leafMaterial;
    [SerializeField] private Color tint = new Color(0.45f, 0.55f, 0.4f, 1f);
    [SerializeField, Min(1)] private int clusters = 18;
    [SerializeField, Min(0.05f), Tooltip("Width of one leaf card in metres.")]
    private float leafSize = 1.4f;
    [SerializeField, Range(0f, 1f), Tooltip("Size variation (0 = all the same).")]
    private float sizeJitter = 0.35f;
    [SerializeField, Range(0f, 0.95f), Tooltip("Leaves only grow above this fraction of the tree's height (0 = from the ground).")]
    private float canopyStart = 0.45f;
    [SerializeField, Tooltip("Each cluster is two crossed cards so it reads from every side.")]
    private bool crossed = true;
    [SerializeField] private int seed = 1;
    [SerializeField, Tooltip("Generate at Awake when no baked leaves exist (needs a Read/Write enabled mesh in builds).")]
    private bool generateIfMissing;

    public Sprite LeafSprite { get => leafSprite; set => leafSprite = value; }
    public Material LeafMaterial { get => leafMaterial; set => leafMaterial = value; }
    public int Clusters { get => clusters; set => clusters = Mathf.Max(1, value); }
    public int Seed { get => seed; set => seed = value; }
    public Transform Canopy => transform.Find(CanopyName);

    private void Awake()
    {
        if (generateIfMissing && Canopy == null)
            RegenerateLeaves();
    }

    [ContextMenu("Regenerate Leaves")]
    public void RegenerateLeaves()
    {
        ClearLeaves();
        if (leafSprite == null)
            return;
        List<Vector3> points = SampleBranchPoints(out Vector3 center);
        if (points.Count == 0)
            return;

        var canopy = new GameObject(CanopyName).transform;
        canopy.SetParent(transform, false);
        var random = new System.Random(seed);
        float spriteWidth = Mathf.Max(0.0001f, Mathf.Max(leafSprite.bounds.size.x, leafSprite.bounds.size.y));
        for (int index = 0; index < clusters; index++)
        {
            Vector3 point = points[random.Next(points.Count)];
            float size = leafSize * (1f + ((float)random.NextDouble() * 2f - 1f) * sizeJitter);
            // Push slightly outward from the trunk so cards hang off the branch tips.
            Vector3 outward = point - new Vector3(center.x, point.y, center.z);
            if (outward.sqrMagnitude > 0.0001f)
                point += outward.normalized * size * 0.25f;
            float yaw = (float)random.NextDouble() * 360f;
            float tilt = ((float)random.NextDouble() * 2f - 1f) * 20f;
            var cluster = new GameObject($"Leaf {index + 1}").transform;
            cluster.SetParent(canopy, false);
            cluster.localPosition = point;
            cluster.localRotation = Quaternion.Euler(tilt, yaw, 0f);
            cluster.localScale = Vector3.one * (size / spriteWidth);
            AddCard(cluster, 0f, random.Next(2) == 0);
            if (crossed)
                AddCard(cluster, 90f, random.Next(2) == 0);
        }
    }

    [ContextMenu("Clear Leaves")]
    public void ClearLeaves()
    {
        Transform existing;
        while ((existing = Canopy) != null)
        {
            existing.SetParent(null);
            if (Application.isPlaying)
                Destroy(existing.gameObject);
            else
                DestroyImmediate(existing.gameObject);
        }
    }

    private void AddCard(Transform cluster, float yaw, bool flip)
    {
        var card = new GameObject("Card");
        card.transform.SetParent(cluster, false);
        card.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        SpriteRenderer renderer = card.AddComponent<SpriteRenderer>();
        renderer.sprite = leafSprite;
        renderer.color = tint;
        renderer.flipX = flip;
        if (leafMaterial != null)
            renderer.sharedMaterial = leafMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>Mesh vertices (in this transform's space) from the upper part of the tree, plus the tree's centre.</summary>
    private List<Vector3> SampleBranchPoints(out Vector3 center)
    {
        var all = new List<Vector3>();
        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.transform.IsChildOf(transform) && filter.transform.parent != null && filter.transform.parent.name == CanopyName)
                continue;
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || (!mesh.isReadable && !Application.isEditor))
                continue;
            Matrix4x4 matrix = toLocal * filter.transform.localToWorldMatrix;
            foreach (Vector3 vertex in mesh.vertices)
                all.Add(matrix.MultiplyPoint3x4(vertex));
        }
        center = Vector3.zero;
        if (all.Count == 0)
            return all;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 point in all)
        {
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
            center += point;
        }
        center /= all.Count;
        float threshold = Mathf.Lerp(minY, maxY, canopyStart);
        all.RemoveAll(point => point.y < threshold);
        return all;
    }
}