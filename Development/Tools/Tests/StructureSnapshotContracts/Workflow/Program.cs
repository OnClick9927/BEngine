using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform, TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);
// Fixture-only service attachment; all tested edits use public APIs.
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
using var scene = new Scene("workflow"); using var other = new Scene("other");
var parent = scene.CreateGameObject("parent");
var root = scene.CreateGameObject("root"); root.transform.SetParent(parent.transform);
var child = scene.CreateGameObject("child"); child.transform.SetParent(root.transform);
var component = child.AddComponent<StructureValue>(); component.Count = 5; component.Borrowed = root;
Undo.ClearAll();
var events = 0;
EditorApplication.hierarchyChanged += OnHierarchyChanged;
void OnHierarchyChanged() => events++;
try
{
    var neutral = EditorHierarchySnapshot.Capture(root); root.name = "changed";
    neutral.Restore();
    Check(events == 0 && !EditorUtility.IsDirty(root), "Core restoration does not emit workflow notifications or dirty markers");
    Undo.RegisterFullObjectHierarchyUndo(root, "structure edit");
    root.name = "edited"; component.Count = 22;
    child.transform.SetParent(null, false); Scene.MoveGameObjectToScene(child, other);
    var extra = scene.CreateGameObject("extra"); extra.transform.SetParent(root.transform);
    var added = root.AddComponent<StructureDependent>();
    var required = root.GetComponent<StructureRequired>();
    Undo.PerformUndo();
    Check(root.name == "root" && component.Count == 5, "Hierarchy Undo restores retained values");
    Check(ReferenceEquals(child.scene, scene) && ReferenceEquals(child.transform.parent, root.transform), "Hierarchy Undo restores a cross-scene child");
    Check(extra.scene is null && root.GetComponent<StructureDependent>() is null, "Hierarchy Undo removes newly introduced structure");
    Check(events > 0 && EditorUtility.IsDirty(root) && EditorUtility.IsDirty(component), "The workflow adds hierarchy notifications and dirty markers");
    Undo.PerformRedo();
    Check(root.name == "edited" && component.Count == 22 && ReferenceEquals(child.scene, other), "Hierarchy Redo restores changed values and scene membership");
    Check(ReferenceEquals(extra.scene, scene) && ReferenceEquals(root.GetComponent<StructureDependent>(), added) &&
          ReferenceEquals(root.GetComponent<StructureRequired>(), required), "Hierarchy Redo preserves introduced object and component identities");
    for (var i = 0; i < 3; i++) { Undo.PerformUndo(); Undo.PerformRedo(); }
    Check(root.components.Count == 3 && scene.gameObjects.Count(g => ReferenceEquals(g, extra)) == 1,
        "Repeated workflow Undo/Redo introduces no duplicates");
    Undo.ClearAll();
    Undo.DestroyObjectImmediate(root);
    Check(root.scene is null && extra.scene is null, "Structural delete removes the entire hierarchy");
    Undo.PerformUndo();
    Check(ReferenceEquals(root.scene, scene) && ReferenceEquals(root.transform.parent, parent.transform) &&
          ReferenceEquals(extra.transform.parent, root.transform), "Structural Undo restores scene and parent relationships");
    Check(ReferenceEquals(root.GetComponent<StructureDependent>(), added), "Structural Undo keeps component identities");
    Undo.PerformRedo(); Check(root.scene is null && extra.scene is null, "Structural Redo removes restored nodes");
    Undo.PerformUndo(); Undo.ClearAll();
    Undo.DestroyObjectImmediate(required!);
    Check(!Undo.canUndo && ReferenceEquals(root.GetComponent<StructureRequired>(), required), "Rejected component removals do not record false history");
    var newComponent = root.AddComponent<StructureValue>(); newComponent.Count = 31;
    var originalOrder = root.components.ToArray();
    Undo.RegisterCreatedObjectUndo(newComponent, "created component");
    Undo.PerformUndo(); Check(root.GetComponent<StructureValue>() is null, "Created-component Undo removes the component");
    Undo.PerformRedo();
    Check(ReferenceEquals(root.GetComponent<StructureValue>(), newComponent) && root.components.SequenceEqual(originalOrder),
        "Created-component Redo restores original identity and order");
    Undo.ClearAll();
    var created = scene.CreateGameObject("created"); created.transform.SetParent(parent.transform);
    Undo.RegisterCreatedObjectUndo(created, "created object");
    Undo.PerformUndo(); Check(created.scene is null, "Created-object Undo detaches the object");
    Undo.PerformRedo();
    Check(ReferenceEquals(created.scene, scene) && ReferenceEquals(created.transform.parent, parent.transform), "Created-object Redo restores its original location");
    Undo.ClearAll();
    newComponent.Count = 99;
    using var preview = new StructurePreview(newComponent, 13);
    EditorPreviewRegistry.Register(preview);
    Undo.RegisterFullObjectHierarchyUndo(root, "preview hierarchy");
    Check(preview.Captures > 0 && newComponent.Count == 99, "Hierarchy capture asks the workflow for preview-original values");
    newComponent.Count = 100; Undo.PerformUndo();
    Check(newComponent.Count == 13 && preview.Disposals == 1, "Hierarchy Undo restores preview originals and stops preview once");
    Undo.PerformRedo(); Check(newComponent.Count == 100, "Hierarchy Redo retains the edited live value");
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Graphics"),
        "Structural workflows do not require GUI or rendering packages");
}
finally { EditorApplication.hierarchyChanged -= OnHierarchyChanged; Undo.ClearAll(); }
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_STRUCTURE_WORKFLOW_OK " + checks.Count);

sealed class StructurePreview(StructureValue target, int original) : IEditorPreview
{
    public int Captures;
    public int Disposals;
    public T CaptureOriginalState<T>(BObject value, Func<T> capture)
    {
        Captures++; var displayed = target.Count;
        try { target.Count = original; return capture(); }
        finally { target.Count = displayed; }
    }
    public void Dispose() { if (Disposals == 0) { Disposals++; EditorPreviewRegistry.Unregister(this); } }
}
