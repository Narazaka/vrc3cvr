using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(VRC3CVRCollisionTagConvertionConfig))]
public class VRC3CVRCollisionTagConvertionConfigDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.PropertyField(position, property, label, true);
        EditorGUI.EndProperty();
    }
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
