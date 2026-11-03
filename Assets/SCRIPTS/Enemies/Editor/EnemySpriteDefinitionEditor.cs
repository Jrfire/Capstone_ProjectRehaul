using UnityEditor;

[CustomEditor(typeof(EnemySpriteDefinition))]
public sealed class EnemySpriteDefinitionEditor : Editor
{
    private readonly bool[] showFrames = new bool[2];

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Idle and Moving use the same rules as player characters: Automatic slices left to right, top to bottom and splits frames equally into West, South, East, North; Manual lets you assign each frame.", MessageType.Info);
        serializedObject.Update();
        var definition = (EnemySpriteDefinition)target;
        CharacterAnimationInspector.Draw(serializedObject, definition.GetAnimation(EnemyAnimationState.Idle), "idle", "Idle", ref showFrames[0]);
        CharacterAnimationInspector.Draw(serializedObject, definition.GetAnimation(EnemyAnimationState.Moving), "moving", "Moving", ref showFrames[1]);
        serializedObject.ApplyModifiedProperties();
    }
}
