using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class CharacterDefinitionTests
{
    private string testFolder;
    private CharacterDefinition definition;

    [SetUp]
    public void SetUp()
    {
        testFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/__CharacterDefinitionTests");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(testFolder));
        definition = ScriptableObject.CreateInstance<CharacterDefinition>();
        AssetDatabase.CreateAsset(definition, testFolder + "/Character.asset");
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(testFolder);
    }

    [Test]
    public void StatesAndDirectionsUseTheRequiredOrderAndDeadIsNotDirectional()
    {
        Assert.That(Enum.GetNames(typeof(CharacterAnimationState)),
            Is.EqualTo(new[] { "Idle", "Walk", "Run", "Attack", "Dead" }));
        Assert.That(Enum.GetNames(typeof(CharacterDirection)),
            Is.EqualTo(new[] { "West", "South", "East", "North" }));
        foreach (CharacterAnimationState state in Enum.GetValues(typeof(CharacterAnimationState)))
            Assert.That(definition.GetAnimation(state).Directional, Is.EqualTo(state != CharacterAnimationState.Dead));
    }

    [Test]
    public void GridReadsLeftToRightThenTopToBottom()
    {
        Rect[] grid = CharacterSheetImporter.BuildGrid(64, 64, new Vector2Int(16, 16), true);
        Assert.That(grid.Length, Is.EqualTo(16));
        for (int index = 0; index < grid.Length; index++)
            Assert.That(grid[index], Is.EqualTo(new Rect(index % 4 * 16, 48 - index / 4 * 16, 16, 16)));
    }

    [TestCase(64, 64, 0, 16)]
    [TestCase(65, 64, 16, 16)]
    [TestCase(48, 16, 16, 16)]
    public void InvalidDirectionalGridsAreRejected(int width, int height, int cellWidth, int cellHeight)
    {
        Assert.Throws<ArgumentException>(() =>
            CharacterSheetImporter.BuildGrid(width, height, new Vector2Int(cellWidth, cellHeight), true));
    }

    [Test]
    public void SixteenImportedFramesBecomeFourPerDirectionWithStableReferences()
    {
        Texture2D sheet = CreateSheet(64, 64);
        Configure(CharacterAnimationState.Idle, sheet, 16, 16, 8f);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Idle);
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Idle);
        Assert.That(animation.IsReady, Is.True);
        Assert.That(animation.FramesPerDirection, Is.EqualTo(4));
        Rect[] grid = CharacterSheetImporter.BuildGrid(64, 64, new Vector2Int(16, 16), true);
        for (int index = 0; index < 16; index++)
            Assert.That(animation.Frames[index].rect, Is.EqualTo(grid[index]));

        foreach (CharacterDirection direction in Enum.GetValues(typeof(CharacterDirection)))
        {
            int start = (int)direction * 4;
            Assert.That(animation.GetFrame(direction, 0d, true), Is.EqualTo(animation.Frames[start]));
            Assert.That(animation.GetFrame(direction, 0.125d, true), Is.EqualTo(animation.Frames[start + 1]));
            Assert.That(animation.GetFrame(direction, 0.5d, true), Is.EqualTo(animation.Frames[start]));
            Assert.That(animation.GetFrame(direction, 99d, false), Is.EqualTo(animation.Frames[start + 3]));
        }

        long[] ids = animation.Frames.Select(LocalId).ToArray();
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Idle);
        Assert.That(animation.Frames.Select(LocalId), Is.EqualTo(ids));
        string definitionPath = AssetDatabase.GetAssetPath(definition);
        AssetDatabase.ImportAsset(definitionPath, ImportAssetOptions.ForceUpdate);
        CharacterAnimation reloaded = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(definitionPath)
            .GetAnimation(CharacterAnimationState.Idle);
        Assert.That(reloaded.IsReady, Is.True);
        Assert.That(reloaded.Frames.Select(LocalId), Is.EqualTo(ids));
    }

    [Test]
    public void DeadUsesEveryFrameAndDoesNotSplitOrLoop()
    {
        Texture2D sheet = CreateSheet(48, 16);
        Configure(CharacterAnimationState.Dead, sheet, 16, 16, 8f);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Dead);
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Dead);
        Assert.That(animation.FramesPerDirection, Is.EqualTo(3));
        foreach (CharacterDirection direction in Enum.GetValues(typeof(CharacterDirection)))
        {
            Assert.That(animation.GetFrame(direction, 0.125d, false), Is.EqualTo(animation.Frames[1]));
            Assert.That(animation.GetFrame(direction, 100d, false), Is.EqualTo(animation.Frames[2]));
        }
    }

    [Test]
    public void InvalidGridDoesNotMutateTheImporter()
    {
        Texture2D sheet = CreateSheet(48, 16);
        Configure(CharacterAnimationState.Walk, sheet, 16, 16, 8f);
        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sheet));
        string before = EditorJsonUtility.ToJson(importer);
        Assert.Throws<ArgumentException>(() => CharacterSheetImporter.Generate(definition, CharacterAnimationState.Walk));
        Assert.That(EditorJsonUtility.ToJson(importer), Is.EqualTo(before));
        Assert.That(definition.GetAnimation(CharacterAnimationState.Walk).Frames.Count, Is.Zero);
    }

    [Test]
    public void FpsCanChangeWithoutRegenerationButGridChangesInvalidateFrames()
    {
        Texture2D sheet = CreateSheet(64, 64);
        Configure(CharacterAnimationState.Attack, sheet, 16, 16, 8f);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Attack);
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Attack);
        Configure(CharacterAnimationState.Attack, sheet, 16, 16, 4f);
        Assert.That(animation.IsReady, Is.True);
        Assert.That(animation.GetFrame(CharacterDirection.West, 0.125d, false), Is.EqualTo(animation.Frames[0]));
        Assert.That(animation.GetFrame(CharacterDirection.West, 0.25d, false), Is.EqualTo(animation.Frames[1]));
        Configure(CharacterAnimationState.Attack, sheet, 32, 32, 4f);
        Assert.That(animation.IsReady, Is.False);
        Assert.Throws<InvalidOperationException>(() => animation.GetFrame(CharacterDirection.West, 0d, false));
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Attack);
        Assert.That(animation.FramesPerDirection, Is.EqualTo(1));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidFpsIsRejected(float fps)
    {
        Texture2D sheet = CreateSheet(64, 64);
        Configure(CharacterAnimationState.Idle, sheet, 16, 16, fps);
        Assert.Throws<ArgumentException>(() => CharacterSheetImporter.Generate(definition, CharacterAnimationState.Idle));
    }

    [TestCase(0f, 0f, CharacterDirection.North)]
    [TestCase(0f, 90f, CharacterDirection.East)]
    [TestCase(0f, 180f, CharacterDirection.South)]
    [TestCase(0f, 270f, CharacterDirection.West)]
    [TestCase(90f, 180f, CharacterDirection.East)]
    [TestCase(350f, 80f, CharacterDirection.East)]
    [TestCase(0f, 45f, CharacterDirection.East)]
    [TestCase(0f, 135f, CharacterDirection.South)]
    public void DirectionDependsOnBothCameraHeadings(float viewer, float observed, CharacterDirection expected)
    {
        Assert.That(PlayerVision.DirectionFromYaw(viewer, observed), Is.EqualTo(expected));
    }

    [Test]
    public void BillboardRotationsAreIndependentUprightAndNotMirrored()
    {
        Quaternion first = PlayerVision.BillboardRotation(new Vector3(0f, 2f, -5f), Vector3.zero, 0f);
        Quaternion second = PlayerVision.BillboardRotation(new Vector3(5f, 2f, 0f), Vector3.zero, 270f);
        Assert.That(Quaternion.Angle(first, Quaternion.identity), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(first, second), Is.EqualTo(90f).Within(0.001f));
        Assert.That(Vector3.Dot(first * Vector3.right, Vector3.right), Is.GreaterThan(0.999f));
        Assert.That(Vector3.Dot(second * Vector3.right, Vector3.forward), Is.GreaterThan(0.999f));
        Assert.That(Vector3.Dot(second * Vector3.up, Vector3.up), Is.GreaterThan(0.999f));
        Quaternion overhead = PlayerVision.BillboardRotation(Vector3.up, Vector3.zero, 90f);
        Assert.That(Quaternion.Angle(overhead, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(0.001f));
    }

    [Test]
    public void ManualFramesAreEditableAndPersistWithoutASheetOrGridSettings()
    {
        Texture2D sheet = CreateSheet(64, 64);
        Configure(CharacterAnimationState.Idle, sheet, 16, 16, 8f);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Idle);
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Idle);
        Sprite[] originals = animation.Frames.ToArray();
        animation.SetManualFrameCount(4);
        Configure(CharacterAnimationState.Idle, null, 0, 0, 8f);
        var serialized = new SerializedObject(definition);
        SerializedProperty idle = serialized.FindProperty("idle");
        idle.FindPropertyRelative("pixelsPerUnit").floatValue = 0f;
        idle.FindPropertyRelative("frames").GetArrayElementAtIndex(0).objectReferenceValue = originals[15];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(animation.AssignmentMode, Is.EqualTo(CharacterFrameAssignment.Manual));
        Assert.That(animation.IsReady, Is.True);
        Assert.That(animation.NeedsGeneration, Is.False);
        Assert.That(animation.GetFrame(CharacterDirection.West, 0d, true), Is.EqualTo(originals[15]));
        foreach (CharacterDirection direction in Enum.GetValues(typeof(CharacterDirection)))
            Assert.That(animation.GetFrame(direction, 0.125d, true), Is.EqualTo(originals[(int)direction * 4 + 1]));

        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssetIfDirty(definition);
        string path = AssetDatabase.GetAssetPath(definition);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        CharacterAnimation reloaded = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path)
            .GetAnimation(CharacterAnimationState.Idle);
        Assert.That(reloaded.AssignmentMode, Is.EqualTo(CharacterFrameAssignment.Manual));
        Assert.That(reloaded.FramesPerDirection, Is.EqualTo(4));
        Assert.That(reloaded.IsReady, Is.True);
        Assert.That(reloaded.GetFrame(CharacterDirection.West, 0d, true), Is.EqualTo(originals[15]));
    }

    [Test]
    public void ManualResizingPreservesEachDirectionsFramesAndExposesMissingSlots()
    {
        Texture2D sheet = CreateSheet(64, 64);
        Configure(CharacterAnimationState.Walk, sheet, 16, 16, 8f);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Walk);
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Walk);
        Sprite[] originals = animation.Frames.ToArray();
        animation.SetManualFrameCount(2);
        Assert.That(animation.Frames.Count, Is.EqualTo(8));
        Assert.That(animation.IsReady, Is.True);
        for (int direction = 0; direction < 4; direction++)
        {
            Assert.That(animation.Frames[direction * 2], Is.EqualTo(originals[direction * 4]));
            Assert.That(animation.Frames[direction * 2 + 1], Is.EqualTo(originals[direction * 4 + 1]));
        }

        animation.SetManualFrameCount(3);
        Assert.That(animation.Frames.Count, Is.EqualTo(12));
        Assert.That(animation.IsReady, Is.False);
        Assert.Throws<InvalidOperationException>(() => animation.GetFrame(CharacterDirection.West, 0d, true));
        Sprite[] filled = animation.Frames.ToArray();
        for (int direction = 0; direction < 4; direction++)
        {
            Assert.That(filled[direction * 3], Is.EqualTo(originals[direction * 4]));
            Assert.That(filled[direction * 3 + 1], Is.EqualTo(originals[direction * 4 + 1]));
            Assert.That(filled[direction * 3 + 2], Is.Null);
            filled[direction * 3 + 2] = originals[direction * 4 + 2];
        }
        animation.AssignFrames(filled);
        Assert.That(animation.IsReady, Is.True);
        Assert.Throws<ArgumentException>(() => animation.AssignFrames(originals));
        Assert.That(animation.Frames, Is.EqualTo(filled));
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("walk").FindPropertyRelative("frames").GetArrayElementAtIndex(0)
            .objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(animation.IsReady, Is.False);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void InvalidManualCountsDoNotChangeFramesOrAssignmentMode(int count)
    {
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Idle);
        Assert.Throws<ArgumentOutOfRangeException>(() => animation.SetManualFrameCount(count));
        Assert.That(animation.AssignmentMode, Is.EqualTo(CharacterFrameAssignment.Automatic));
        Assert.That(animation.Frames.Count, Is.Zero);
    }

    [Test]
    public void ManualDeadUsesOneSequenceAndStillRequiresValidFps()
    {
        Texture2D sheet = CreateSheet(48, 16);
        Configure(CharacterAnimationState.Dead, sheet, 16, 16, 8f);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Dead);
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Dead);
        animation.SetManualFrameCount(3);
        Configure(CharacterAnimationState.Dead, null, 0, 0, 8f);
        Assert.That(animation.IsReady, Is.True);
        foreach (CharacterDirection direction in Enum.GetValues(typeof(CharacterDirection)))
            Assert.That(animation.GetFrame(direction, 10d, false), Is.EqualTo(animation.Frames[2]));
        Configure(CharacterAnimationState.Dead, null, 0, 0, float.NaN);
        Assert.That(animation.IsReady, Is.False);
    }

    [Test]
    public void AutomaticGenerationCannotOverwriteManualFramesWithoutSwitchingModes()
    {
        Texture2D sheet = CreateSheet(64, 64);
        Configure(CharacterAnimationState.Idle, sheet, 16, 16, 8f);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Idle);
        CharacterAnimation animation = definition.GetAnimation(CharacterAnimationState.Idle);
        animation.SetManualFrameCount(4);
        Sprite[] before = animation.Frames.ToArray();
        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sheet));
        string importerBefore = EditorJsonUtility.ToJson(importer);
        Assert.Throws<InvalidOperationException>(() => CharacterSheetImporter.Generate(definition, CharacterAnimationState.Idle));
        Assert.That(animation.Frames, Is.EqualTo(before));
        Assert.That(EditorJsonUtility.ToJson(importer), Is.EqualTo(importerBefore));

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("idle").FindPropertyRelative("assignmentMode").enumValueIndex =
            (int)CharacterFrameAssignment.Automatic;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(animation.IsReady, Is.False);
        CharacterSheetImporter.Generate(definition, CharacterAnimationState.Idle);
        Assert.That(animation.IsReady, Is.True);
        Assert.That(animation.FramesPerDirection, Is.EqualTo(4));
    }

    private Texture2D CreateSheet(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var colors = new Color[width * height];
        colors[0] = Color.white;
        texture.SetPixels(colors);
        texture.Apply();
        string path = AssetDatabase.GenerateUniqueAssetPath(testFolder + "/Sheet.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private void Configure(CharacterAnimationState state, Texture2D sheet, int width, int height, float fps)
    {
        var serialized = new SerializedObject(definition);
        SerializedProperty animation = serialized.FindProperty(state.ToString().ToLowerInvariant());
        animation.FindPropertyRelative("spriteSheet").objectReferenceValue = sheet;
        animation.FindPropertyRelative("cellSize").vector2IntValue = new Vector2Int(width, height);
        animation.FindPropertyRelative("framesPerSecond").floatValue = fps;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static long LocalId(Sprite sprite)
    {
        Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long id), Is.True);
        return id;
    }
}
