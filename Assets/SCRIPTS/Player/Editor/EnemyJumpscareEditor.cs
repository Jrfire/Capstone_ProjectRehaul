using UnityEditor;
using UnityEngine;

/// <summary>Front-view preview for <see cref="EnemyJumpscare"/>: click or drag to set the focus point.</summary>
[CustomEditor(typeof(EnemyJumpscare))]
public sealed class EnemyJumpscareEditor : Editor
{
    private const float PreviewHeight = 260f;
    private const float PreviewAspect = 16f / 9f;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        var sprite = (Sprite)serializedObject.FindProperty("frontView").objectReferenceValue;
        if (sprite == null)
        {
            serializedObject.ApplyModifiedProperties();
            return;
        }

        SerializedProperty focus = serializedObject.FindProperty("focusPoint");
        float screenHeight = serializedObject.FindProperty("finalScreenHeight").floatValue;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Click or drag on the preview to set the Focus Point (the cross). The box shows what a 16:9 screen sees at the end of the zoom; raise Final Screen Height to zoom in closer.", MessageType.None);

        Rect area = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        Rect spriteRect = sprite.rect;
        float aspect = spriteRect.width / Mathf.Max(1f, spriteRect.height);
        float height = Mathf.Min(area.height, area.width / aspect);
        var image = new Rect(area.center.x - height * aspect * 0.5f, area.y, height * aspect, height);

        EditorGUI.DrawRect(image, new Color(0f, 0f, 0f, 0.6f));
        Texture texture = sprite.texture;
        var uv = new Rect(spriteRect.x / texture.width, spriteRect.y / texture.height,
            spriteRect.width / texture.width, spriteRect.height / texture.height);
        GUI.DrawTextureWithTexCoords(image, texture, uv, true);

        Event current = Event.current;
        if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0 &&
            image.Contains(current.mousePosition))
        {
            focus.vector2Value = new Vector2(
                Mathf.Round(Mathf.InverseLerp(image.xMin, image.xMax, current.mousePosition.x) * 1000f) / 1000f,
                Mathf.Round(Mathf.InverseLerp(image.yMax, image.yMin, current.mousePosition.y) * 1000f) / 1000f);
            GUI.changed = true;
            current.Use();
        }

        Vector2 point = focus.vector2Value;
        var centre = new Vector2(Mathf.Lerp(image.xMin, image.xMax, point.x), Mathf.Lerp(image.yMax, image.yMin, point.y));
        float viewHeight = image.height / Mathf.Max(0.1f, screenHeight);
        var view = new Rect(centre.x - viewHeight * PreviewAspect * 0.5f, centre.y - viewHeight * 0.5f,
            viewHeight * PreviewAspect, viewHeight);
        Handles.DrawSolidRectangleWithOutline(view, Color.clear, new Color(1f, 0.85f, 0.2f, 0.9f));
        EditorGUI.DrawRect(new Rect(centre.x - 8f, centre.y - 1f, 16f, 2f), Color.red);
        EditorGUI.DrawRect(new Rect(centre.x - 1f, centre.y - 8f, 2f, 16f), Color.red);

        serializedObject.ApplyModifiedProperties();
    }
}
