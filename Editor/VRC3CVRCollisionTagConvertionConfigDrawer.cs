using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(VRC3CVRCollisionTagConvertionConfig))]
public class VRC3CVRCollisionTagConvertionConfigDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var pos = EditorGUI.IndentedRect(position);
        var indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUI.BeginProperty(pos, label, property);
        EditorGUI.PropertyField(pos, property, label, true);
        EditorGUI.EndProperty();
        EditorGUI.indentLevel = indent;
    }
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
