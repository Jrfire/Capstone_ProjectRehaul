using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

public sealed class StreetNavMeshTests
{
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
                Object.DestroyImmediate(created[i]);
        }
        created.Clear();
    }

    [UnityTest]
    public IEnumerator DecorationsCarveSmallHolesAndOverridesControlTheirFootprint()
    {
        StreetBlock Block(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/PREFABS/StreetBlocks/{name}.prefab").GetComponent<StreetBlock>();

        // A scene copy of a project connector given small decorations; the first two use a custom footprint and no carving.
        StreetBlock crossroad = Track(Object.Instantiate(Block("StreetBlock_2Entrances_1"), new Vector3(0f, -500f, 0f), Quaternion.identity));
        Assert.That(crossroad.DecorationsRoot, Is.Not.Null);
        foreach (Vector3 offset in new[] { new Vector3(1.8f, 0f, 0f), new Vector3(-1.8f, 0f, 0f), new Vector3(0f, 0f, 1.8f), new Vector3(0f, 0f, -1.8f), new Vector3(1.8f, 0f, 1.8f), new Vector3(-1.8f, 0f, -1.8f) })
        {
            GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = $"Test Crate {offset.x:0.#} {offset.z:0.#}";
            crate.transform.SetParent(crossroad.DecorationsRoot, false);
            crate.transform.localPosition = offset + Vector3.up * 0.3f;
            crate.transform.localScale = Vector3.one * 0.6f;
        }
        Transform[] decorations = crossroad.DecorationsRoot.Cast<Transform>().Where(child => child.GetComponentInChildren<Collider>() != null).ToArray();
        var custom = decorations[0].gameObject.AddComponent<NavMeshCarveOverride>();
        var none = decorations[1].gameObject.AddComponent<NavMeshCarveOverride>();
        var noneSettings = new SerializedObject(none);
        noneSettings.FindProperty("mode").enumValueIndex = (int)NavMeshCarveMode.None;
        noneSettings.ApplyModifiedPropertiesWithoutUndo();
        string customName = decorations[0].name, noneName = decorations[1].name;

        var root = Track(new GameObject("Street NavMesh Test"));
        root.SetActive(false);
        var manager = root.AddComponent<MapManager>();
        var so = new SerializedObject(manager);
        void Fill(string property, params StreetBlock[] blocks)
        {
            SerializedProperty list = so.FindProperty(property);
            list.arraySize = blocks.Length;
            for (int i = 0; i < blocks.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = blocks[i];
        }
        Fill("connectorBlocks", crossroad);
        Fill("endCapBlocks", Block("EndBlockStreetBlock_1"));
        Fill("spawnBlocks", Block("SpawnBlockStreetBlock_1"));
        so.FindProperty("connectorBlockCount").intValue = 1;
        so.FindProperty("buildOnStart").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(true);
        Assert.That(manager.Build(3), Is.True);
        Assert.That(manager.HasNavMesh, Is.True);
        Assert.That(manager.BakeAgentRadius, Is.EqualTo(0.3f).Within(0.0001f));
        yield return null;
        yield return null;

        StreetBlock placed = Enumerable.Range(0, manager.BlockCount).Select(manager.GetBlock)
            .Single(block => block.GetComponentsInChildren<NavMeshCarveOverride>().Length == 2);
        Transform customPlaced = placed.GetComponentsInChildren<NavMeshCarveOverride>().Single(o => o.Mode == NavMeshCarveMode.Custom).transform;
        Transform nonePlaced = placed.GetComponentsInChildren<NavMeshCarveOverride>().Single(o => o.Mode == NavMeshCarveMode.None).transform;
        Assert.That(customPlaced.name, Is.EqualTo(customName));
        Assert.That(nonePlaced.name, Is.EqualTo(noneName));
        Assert.That(nonePlaced.GetComponentsInChildren<NavMeshObstacle>(), Is.Empty, "None never carves.");
        NavMeshObstacle[] customCarvers = customPlaced.GetComponentsInChildren<NavMeshObstacle>();
        Assert.That(customCarvers.Length, Is.EqualTo(1), "Custom replaces every collider box with one footprint.");
        Assert.That(customCarvers[0].size, Is.EqualTo(custom.BoxSize));
        Assert.That(customCarvers[0].transform.parent, Is.EqualTo(customPlaced));

        float smallestPadding = float.PositiveInfinity;
        int measured = 0;
        for (int i = 0; i < manager.BlockCount; i++)
        {
            Transform decorationRoot = manager.GetBlock(i).DecorationsRoot;
            if (decorationRoot == null)
                continue;
            foreach (NavMeshObstacle carver in decorationRoot.GetComponentsInChildren<NavMeshObstacle>())
            {
                if (carver.GetComponentInParent<NavMeshCarveOverride>() != null)
                    continue;
                BoxCollider box = carver.transform.parent.GetComponent<BoxCollider>();
                if (box != null)
                {
                    Assert.That(carver.size.x, Is.EqualTo(box.size.x * manager.DecorationCarveScale).Within(0.001f));
                    Assert.That(carver.size.z, Is.EqualTo(box.size.z * manager.DecorationCarveScale).Within(0.001f));
                    Assert.That(carver.size.y, Is.EqualTo(box.size.y).Within(0.001f), "Height is unchanged.");
                }

                // Distance from the footprint centre to the NavMesh = half the smaller side + the carving padding.
                Vector3 centre = carver.transform.TransformPoint(carver.center);
                Vector3 scale = carver.transform.lossyScale;
                float halfSide = Mathf.Min(Mathf.Abs(carver.size.x * scale.x), Mathf.Abs(carver.size.z * scale.z)) * 0.5f;
                centre.y = manager.GetBlock(i).transform.position.y;
                if (!NavMesh.SamplePosition(centre, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    continue;
                Vector3 offset = hit.position - centre;
                smallestPadding = Mathf.Min(smallestPadding, new Vector2(offset.x, offset.z).magnitude - halfSide);
                measured++;
            }
        }
        Assert.That(measured, Is.GreaterThan(3));
        Assert.That(smallestPadding, Is.EqualTo(manager.BakeAgentRadius).Within(0.15f),
            "Carved holes extend only the bake radius (0.3 m, not the Humanoid 0.5 m) beyond the shrunken footprint.");
    }

    private T Track<T>(T obj) where T : Object
    {
        created.Add(obj is Component component ? component.gameObject : obj);
        return obj;
    }
}
