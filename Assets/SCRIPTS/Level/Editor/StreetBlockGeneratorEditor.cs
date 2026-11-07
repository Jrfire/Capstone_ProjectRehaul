using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StreetBlockGenerator))]
public sealed class StreetBlockGeneratorEditor : Editor
{
    private const string DefaultFloorPath = "Assets/Prefab/Streets Object/Path_Road.prefab";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var generator = (StreetBlockGenerator)target;
        StreetBlock block = generator.GetComponent<StreetBlock>();

        EditorGUILayout.Space();
        if (generator.IsFiller)
        {
            EditorGUILayout.HelpBox(
                "Filler block (0 entrances): the inside is packed with buildings separated by narrow alleys (see Filler settings); " +
                "Middle Building Count is ignored. Add it to a MapManager's Filler Blocks pool.", MessageType.Info);
        }
        if (generator.townBlock)
        {
            EditorGUILayout.HelpBox(
                "Town block: its edge walls are removed by MapManager wherever another town block touches it, and it never holds " +
                "entrance obstacles. Needs 2+ entrances; add it to a MapManager's Town Blocks pool.", MessageType.Info);
        }
        if (block != null && block.Size.x > 0f && block.IsTownBlock != generator.townBlock)
        {
            EditorGUILayout.HelpBox("Town Block changed since the last generation; click Generate to apply it.", MessageType.Warning);
        }
        if (block != null && block.Size.x > 0f)
        {
            EditorGUILayout.HelpBox(
                $"Generated: {block.Size.x:0.##} x {block.Size.y:0.##} m, {block.SizeInCells.x}x{block.SizeInCells.y} cells of " +
                $"{block.CellSize.x:0.##} x {block.CellSize.y:0.##} m, {block.Entrances.Count} entrance(s).", MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(targets.Length != 1))
        {
            if (GUILayout.Button("Generate"))
                StreetBlockBuilder.Generate(generator);
            if (GUILayout.Button("New Seed && Generate"))
            {
                Undo.RecordObject(generator, "New Street Block Seed");
                generator.seed = Random.Range(int.MinValue, int.MaxValue);
                StreetBlockBuilder.Generate(generator);
            }
            if (GUILayout.Button("Clear Generated"))
                StreetBlockBuilder.Clear(generator);
            if (GUILayout.Button("Save As Prefab"))
                SaveAsPrefab(generator, true);
        }
    }

    /// <summary>Saves the block (with its generator, so it can be reopened and regenerated) to the output folder.</summary>
    public static GameObject SaveAsPrefab(StreetBlockGenerator generator, bool interactive)
    {
        StreetBlock block = generator.GetComponent<StreetBlock>();
        if (block.Size.x <= 0f || generator.transform.Find(StreetBlockBuilder.GeneratedName) == null)
        {
            Debug.LogError("Generate the street block before saving it as a prefab.", generator);
            return null;
        }
        if (PrefabUtility.IsPartOfPrefabInstance(generator.gameObject))
        {
            Debug.LogError("This object is already a prefab instance; open its prefab to edit it instead.", generator);
            return null;
        }

        string folder = generator.prefabFolder.TrimEnd('/');
        string name = string.IsNullOrWhiteSpace(generator.prefabName) ? generator.name : generator.prefabName.Trim();
        if (!folder.StartsWith("Assets") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Debug.LogError("Prefab Folder must be inside Assets and Prefab Name must be a valid file name.", generator);
            return null;
        }
        EnsureFolder(folder);
        string path = $"{folder}/{name}.prefab";
        if (interactive && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null &&
            !EditorUtility.DisplayDialog("Overwrite street block?", $"{path} already exists. Overwrite it?", "Overwrite", "Cancel"))
            return null;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(generator.gameObject, path, out bool success);
        if (!success)
        {
            Debug.LogError($"Could not save {path}.", generator);
            return null;
        }
        Debug.Log($"Saved street block to {path}.", prefab);
        return prefab;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    [MenuItem("Assets/Capstone/Regenerate Selected Street Block Prefabs", true)]
    private static bool CanRegenerateSelectedPrefabs() => SelectedBlockPrefabPaths().Length > 0;

    /// <summary>Rebuilds saved block prefabs with their stored settings and seed (e.g. after generator fixes). Manual edits under Generated are replaced.</summary>
    [MenuItem("Assets/Capstone/Regenerate Selected Street Block Prefabs", false, 20)]
    private static void RegenerateSelectedPrefabs()
    {
        string[] paths = SelectedBlockPrefabPaths();
        if (!EditorUtility.DisplayDialog("Regenerate street blocks",
                $"Regenerate {paths.Length} street block prefab(s) with their saved settings and seed? Manual changes under 'Generated' are replaced.",
                "Regenerate", "Cancel"))
            return;

        int succeeded = 0;
        foreach (string path in paths)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (StreetBlockBuilder.Generate(contents.GetComponent<StreetBlockGenerator>()))
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    succeeded++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        Debug.Log($"Regenerated {succeeded} of {paths.Length} street block prefab(s).");
    }

    private static string[] SelectedBlockPrefabPaths() => Selection.GetFiltered<GameObject>(SelectionMode.Assets)
        .Where(asset => asset.GetComponent<StreetBlockGenerator>() != null)
        .Select(AssetDatabase.GetAssetPath)
        .Where(path => path.EndsWith(".prefab"))
        .ToArray();

    [MenuItem("GameObject/Capstone/Street Block Generator", false, 10)]
    private static void CreateGenerator(MenuCommand command)
    {
        var gameObject = new GameObject("Street Block");
        GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
        gameObject.AddComponent<StreetBlock>();
        var generator = gameObject.AddComponent<StreetBlockGenerator>();
        generator.floorTile = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultFloorPath);
        generator.seed = Random.Range(int.MinValue, int.MaxValue);
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Street Block Generator");
        Selection.activeObject = gameObject;
    }
}
