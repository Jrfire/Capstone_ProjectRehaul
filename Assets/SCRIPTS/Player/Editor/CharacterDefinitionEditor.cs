using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CharacterDefinition))]
public sealed class CharacterDefinitionEditor : Editor
{
    private readonly bool[] showFrames = new bool[5];

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Automatic mode slices left to right, top to bottom and splits frames equally into West, South, East, North. Manual mode lets you choose a shared frame count and assign sprites individually. Dead is one sequence without directions.", MessageType.Info);
        serializedObject.Update();
        var definition = (CharacterDefinition)target;
        for (int index = 0; index < 5; index++)
        {
            var state = (CharacterAnimationState)index;
            CharacterAnimationInspector.Draw(serializedObject, definition.GetAnimation(state),
                state.ToString().ToLowerInvariant(), state.ToString(), ref showFrames[index]);
        }
        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>Shared inspector for one <see cref="CharacterAnimation"/> field (automatic slicing or manual frame slots).</summary>
public static class CharacterAnimationInspector
{
    public static void Draw(SerializedObject serializedObject, CharacterAnimation animation, string propertyName,
        string label, ref bool showFrames)
    {
        UnityEngine.Object owner = serializedObject.targetObject;
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        CharacterFrameAssignment previousMode = animation.AssignmentMode;
        EditorGUILayout.PropertyField(property.FindPropertyRelative("assignmentMode"));
        serializedObject.ApplyModifiedProperties();
        if (previousMode != animation.AssignmentMode && animation.AssignmentMode == CharacterFrameAssignment.Manual)
        {
            Undo.RecordObject(owner, "Enable manual character frames");
            animation.SetManualFrameCount(Math.Max(1, animation.Frames.Count / (animation.Directional ? 4 : 1)));
            EditorUtility.SetDirty(owner);
            serializedObject.Update();
        }

        bool manual = animation.AssignmentMode == CharacterFrameAssignment.Manual;
        EditorGUILayout.PropertyField(property.FindPropertyRelative("spriteSheet"),
            new GUIContent(manual ? "PNG Sheet (Optional)" : "PNG Sprite Sheet"));
        EditorGUILayout.PropertyField(property.FindPropertyRelative("framesPerSecond"));
        if (!manual)
        {
            EditorGUILayout.PropertyField(property.FindPropertyRelative("cellSize"), new GUIContent("Grid Cell Size"));
            EditorGUILayout.PropertyField(property.FindPropertyRelative("pixelsPerUnit"));
        }
        serializedObject.ApplyModifiedProperties();

        if (manual)
        {
            EditorGUILayout.HelpBox("To slice manually, set the PNG's Texture Type to Sprite (2D and UI), Sprite Mode to Multiple, and Apply. Use the Sprite Editor to slice and Apply, then drag the sprite sub-assets into the slots below. Changing the count preserves each direction's leading frames; reducing it discards trailing frames.", MessageType.Info);
            int frameCount = EditorGUILayout.DelayedIntField(
                animation.Directional ? "Frames Per Direction" : "Frame Count", animation.FramesPerDirection);
            if (frameCount != animation.FramesPerDirection)
            {
                try
                {
                    Undo.RecordObject(owner, "Resize manual character frames");
                    animation.SetManualFrameCount(frameCount);
                    EditorUtility.SetDirty(owner);
                    serializedObject.Update();
                }
                catch (ArgumentException exception)
                {
                    ReportError(owner, exception);
                }
            }

            using (new EditorGUI.DisabledScope(animation.SpriteSheet == null))
            {
                if (GUILayout.Button("Open Sprite Editor for Manual Slicing"))
                    OpenSpriteEditor(owner, animation);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Automatic slicing changes the PNG's shared import settings (Multiple, Point filter, centered pivot, no mipmaps). It overwrites this state's frames. Use a dedicated sheet per state and consistent Pixels Per Unit.", MessageType.Info);
            using (new EditorGUI.DisabledScope(animation.SpriteSheet == null))
            {
                if (GUILayout.Button($"Slice {label} Sheet & Assign Frames"))
                {
                    try
                    {
                        CharacterSheetImporter.Generate(owner, animation);
                        serializedObject.Update();
                    }
                    catch (ArgumentException exception)
                    {
                        ReportError(owner, exception);
                    }
                    catch (InvalidOperationException exception)
                    {
                        ReportError(owner, exception);
                    }
                }
            }
        }

        if (!animation.IsReady)
            EditorGUILayout.HelpBox(manual
                ? "Assign every frame slot and a positive FPS before playback."
                : "Assign a PNG and generate this state before playback. Regenerate after changing sheet settings.",
                MessageType.Warning);

        string count = animation.Directional
            ? $"{animation.Frames.Count} total / {animation.FramesPerDirection} per direction"
            : $"{animation.Frames.Count} total (no directions)";
        showFrames = EditorGUILayout.Foldout(showFrames, $"Frames: {count}", true);
        if (!showFrames)
            return;

        using (new EditorGUI.DisabledScope(!manual))
        {
            SerializedProperty frames = property.FindPropertyRelative("frames");
            int directionCount = animation.Directional ? 4 : 1;
            for (int direction = 0; direction < directionCount; direction++)
            {
                if (animation.Directional)
                    EditorGUILayout.LabelField(((CharacterDirection)direction).ToString());
                for (int frame = 0; frame < animation.FramesPerDirection; frame++)
                {
                    EditorGUILayout.PropertyField(frames.GetArrayElementAtIndex(
                        direction * animation.FramesPerDirection + frame), new GUIContent($"Frame {frame + 1}"));
                }
            }
        }
        serializedObject.ApplyModifiedProperties();
    }

    private static void OpenSpriteEditor(UnityEngine.Object owner, CharacterAnimation animation)
    {
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(animation.SpriteSheet)) as TextureImporter;
        if (importer == null || importer.textureType != TextureImporterType.Sprite ||
            importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            ReportError(owner, new InvalidOperationException("Set the PNG's Texture Type to Sprite (2D and UI) and Sprite Mode to Multiple, then Apply before opening the Sprite Editor."));
            return;
        }

        Selection.activeObject = animation.SpriteSheet;
        if (!EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor"))
            ReportError(owner, new InvalidOperationException("Unity could not open the Sprite Editor. Check that the 2D Sprite package is enabled."));
    }

    private static void ReportError(UnityEngine.Object owner, Exception exception)
    {
        Debug.LogError($"Character sheet generation failed: {exception.Message}", owner);
        EditorUtility.DisplayDialog("Cannot generate character frames", exception.Message, "OK");
    }
}