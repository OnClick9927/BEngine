# Serialized Inspector

编辑器默认 Inspector 只通过 `SerializedObject` 和 `SerializedProperty` 读写对象。任何继承 `BObject` 的组件或资源对象都可以创建序列化包装：

```csharp
using BEngine.Editor;

using var serializedObject = new SerializedObject(target);
serializedObject.Update();

var health = serializedObject.FindProperty("health")!;
health.intValue = 100;

serializedObject.ApplyModifiedProperties();
```

公开字段、公开可读写属性和带 `[SerializeField]` 的私有字段会进入属性列表；`[HideInInspector]` 不会出现在 `NextVisible` 和默认 Inspector 中。通过 `FindProperty` 仍可显式访问隐藏属性。

## Property Drawer

Drawer 脚本应放在工程的 `Assets/Editor` 目录。可以按 `PropertyAttribute` 或字段值类型注册：

```csharp
using BEngine;
using BEngine.Editor;
using BEngine.UIElements;

public sealed class HealthAttribute : PropertyAttribute;

[CustomPropertyDrawer(typeof(HealthAttribute))]
public sealed class HealthDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var slider = new Slider(property.displayName, 0, 100);
        slider.SetValueWithoutNotify(property.intValue);
        slider.valueChanged += value =>
        {
            property.intValue = (int)value;
            property.serializedObject.ApplyModifiedProperties();
        };
        return slider;
    }
}
```

```csharp
[CustomPropertyDrawer(typeof(MyValue), useForChildren: true)]
public sealed class MyValueDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var root = new VisualElement();
        // 使用 property.boxedValue 或 FindPropertyRelative 读写，并向 root 添加 UIElement。
        return root;
    }
}
```

Drawer 可读取 `attribute`、`fieldInfo` 和 `memberInfo`。属性修改会统一进入 Undo/Redo，并在 `ApplyModifiedProperties` 后标记对象和场景为已修改。
