# Editor Window Menus

每个 `EditorWindow` 都会显示锁定按钮和三点菜单。系统自动追加 `Lock` 与 `Close Tab`，窗口可以通过三种方式增加自己的命令。

## AddItemsToMenu

```csharp
using BEngine.Editor;

public sealed class BuildToolsWindow : EditorWindow
{
    public override void AddItemsToMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("Rebuild"), false, Rebuild);
        menu.AddItem(new GUIContent("Live Refresh"), isLocked, () => isLocked = !isLocked);
    }

    private void Rebuild() { }
}
```

## ContextMenu

无参实例方法可以直接使用运行时 `ContextMenu` 特性：

```csharp
[ContextMenu("Tools/Clear Cache")]
private void ClearCache() { }
```

## Static MenuItem

编辑器程序集也可以在不修改窗口类的情况下扩展指定窗口：

```csharp
[MenuItem("CONTEXT/BuildToolsWindow/Rebuild All")]
private static void RebuildAll(MenuCommand command)
{
    var window = (BuildToolsWindow)command.context!;
}
```

`AddItemsToMenu`、`ContextMenu` 和 `CONTEXT/<WindowType>` 命令会合并到同一个三点菜单。命令异常会写入 Console，不会中断窗口绘制。
