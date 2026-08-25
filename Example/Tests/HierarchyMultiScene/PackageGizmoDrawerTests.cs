using System.Collections;
using System.Reflection;
using BEngine.Editor;
using BEngine.Navigation2D;
using BEngine.Physics2D;
using BEngine.TiledMap;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class PackageGizmoDrawerTests
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public |
                                                       BindingFlags.NonPublic;
    private static readonly Assembly EditorAssembly = typeof(EditorWindow).Assembly;
    private static readonly Type PassType = TestAssert.RequireType(
        EditorAssembly, "BEngine.Editor.SceneGizmoPass");
    private static readonly Type VisibilityType = TestAssert.RequireType(
        EditorAssembly, "BEngine.Editor.SceneGizmoVisibility");

    internal static void Run()
    {
        LoadEditorExtension("BEngine.Physics2D.Editor");
        LoadEditorExtension("BEngine.Navigation2D.Editor");
        LoadEditorExtension("BEngine.TiledMap.Editor");
        RuntimeTypeCache.Warmup();

        VerifyDrawer<BoxCollider2D>("Physics2D BoxCollider2D", 4);
        VerifyDrawer<CircleCollider2D>("Physics2D CircleCollider2D", 48);
        VerifyDrawer<NavigationSurface2D>("Navigation2D Surface", 4);
        VerifyDrawer<Tilemap>("TiledMap Tilemap", 4);
    }

    private static void VerifyDrawer<T>(string name, int minimumLineCount)
        where T : Component, new()
    {
        var wasEnabled = ReadEnabled();
        var wasVisible = IsVisible(typeof(T));
        var scene = new Scene(name);
        try
        {
            SetEnabled(true);
            SetVisible(typeof(T), true);
            var owner = scene.CreateGameObject(name);
            owner.AddComponent<T>();
            var unselected = Collect([scene], null, 640, 360);
            TestAssert.Require(LineCount(unselected) == 0,
                $"The external {typeof(T).FullName} Gizmo was drawn while its GameObject was not selected.");
            var drawList = Collect([scene], owner, 640, 360);
            TestAssert.Require(LineCount(drawList) >= minimumLineCount,
                $"The external {typeof(T).FullName} Gizmo drawer was not invoked.");
        }
        finally
        {
            SetVisible(typeof(T), wasVisible);
            SetEnabled(wasEnabled);
            scene.Dispose();
        }
    }

    private static void LoadEditorExtension(string assemblyName)
    {
        try
        {
            _ = Assembly.Load(assemblyName);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Could not load external Gizmo assembly '{assemblyName}'.", exception);
        }
    }

    private static object Collect(
        IReadOnlyList<Scene> scenes,
        GameObject? selected,
        int width,
        int height)
    {
        var method = TestAssert.RequireMethod(PassType, "Collect", StaticMembers,
            typeof(IReadOnlyList<Scene>), typeof(GameObject), typeof(int), typeof(int));
        return method.Invoke(null, [scenes, selected, width, height]) ??
               throw new InvalidOperationException("Scene Gizmo collection returned null.");
    }

    private static int LineCount(object drawList)
    {
        var lines = drawList.GetType().GetProperty("Lines", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(drawList) as IEnumerable;
        return lines?.Cast<object>().Count() ?? 0;
    }

    private static bool ReadEnabled() => (bool)(VisibilityType.GetProperty("Enabled", StaticMembers)
        ?.GetValue(null) ?? true);

    private static void SetEnabled(bool value) => VisibilityType.GetProperty("Enabled", StaticMembers)
        ?.SetValue(null, value);

    private static bool IsVisible(Type type) => (bool)(VisibilityType.GetMethod("IsVisible", StaticMembers)
        ?.Invoke(null, [type]) ?? true);

    private static void SetVisible(Type type, bool visible) => VisibilityType.GetMethod("SetVisible", StaticMembers)
        ?.Invoke(null, [type, visible]);
}
