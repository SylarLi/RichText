using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TextEditor = UnityEditor.UI.TextEditor;

[CanEditMultipleObjects]
[CustomEditor(typeof(RichText))]
public class RichTextEditor : TextEditor
{
    private SerializedProperty spriteList;

    protected override void OnEnable()
    {
        base.OnEnable();
        spriteList = serializedObject.FindProperty("inspectorSpriteList");
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(spriteList, new GUIContent("Sprite List"), true);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            (target as RichText).UpdateSpriteList();
            (target as RichText).SetAllDirty();
        }
    }
}
