using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

public abstract class PropertyDrawer
{
    public PropertyAttribute? attribute { get; internal set; }
    public FieldInfo? fieldInfo { get; internal set; }
    public MemberInfo? memberInfo { get; internal set; }
    public virtual void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
        EditorGUI.PropertyField(position, property, label, includeChildren: true);
    public virtual Fix64 GetPropertyHeight(SerializedProperty property, GUIContent label) =>
        EditorGUIUtility.singleLineHeight;
    public virtual bool CanCacheInspectorGUI(SerializedProperty property) => true;
}
