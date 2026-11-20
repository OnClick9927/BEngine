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
// Fixture-only host attachment; the editing operations below use public APIs.
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
Undo.ClearAll();
var borrowed = new StateAsset();
var node = new StateNode { Value = 4, Borrowed = borrowed }; node.Next = node;
var target = new StateAsset { Count = 7, First = node, Second = node, Items = [node, node], Borrowed = borrowed };
Undo.RecordObject(target, "managed graph");
target.Count = 19; target.First = null; target.Items.Clear();
Check(Undo.canUndo && Undo.estimatedHistoryBytes > 256, "Undo retains the Core snapshot memory estimate");
Undo.PerformUndo();
Check(target.Count == 7 && target.First!.Value == 4, "Undo restores retained values through the Core snapshot");
Check(ReferenceEquals(target.First, target.Second) && ReferenceEquals(target.First, target.Items[0]) &&
      ReferenceEquals(target.First, target.First!.Next), "Undo preserves shared managed graphs and cycles");
Check(ReferenceEquals(target.Borrowed, borrowed), "Undo preserves borrowed engine-object identities");
Undo.PerformRedo();
Check(target.Count == 19 && target.First is null && target.Items.Count == 0, "Redo captures the inverse live state");
Undo.ClearAll();
using (var serialized = new SerializedObject(target))
{
    serialized.FindProperty(nameof(StateAsset.Count))!.intValue = 23;
    Check(target.Count == 19 && serialized.hasModifiedProperties, "Inspector changes remain buffered until apply");
    Check(serialized.ApplyModifiedProperties() && target.Count == 23, "Inspector apply writes the buffered value");
    Undo.PerformUndo(); Check(target.Count == 19, "Inspector apply integrates with Undo");
    Undo.PerformRedo(); Check(target.Count == 23, "Inspector apply integrates with Redo");
}
Undo.ClearAll();
target.Count = 99;
using (var preview = new TestPreview(target, 12))
{
    EditorPreviewRegistry.Register(preview);
    Undo.RecordObject(target, "preview original");
    Check(preview.Captures == 1 && target.Count == 99, "Preview policy captures original state without changing the displayed value");
    target.Count = 100;
    Undo.PerformUndo();
    Check(target.Count == 12, "Undo restores the preview's original value rather than its displayed value");
    Check(preview.Disposals == 1, "Undo stops the optional preview exactly once");
}
Undo.ClearAll();
using var scene = new Scene("workflow");
var root = scene.CreateGameObject("root"); var child = scene.CreateGameObject("child"); child.transform.SetParent(root.transform);
var component = child.AddComponent<StateComponent>(); component.Count = 5; component.Borrowed = root;
Undo.RegisterFullObjectHierarchyUndo(root, "hierarchy");
component.Count = 27; child.name = "changed";
Undo.PerformUndo();
Check(child.name == "child" && component.Count == 5 && ReferenceEquals(component.Borrowed, root), "Full hierarchy Undo restores object and component state");
Undo.PerformRedo(); Check(child.name == "changed" && component.Count == 27, "Full hierarchy Redo preserves the inverse state");
Undo.ClearAll();
Undo.DestroyObjectImmediate(component);
Check(child.GetComponent<StateComponent>() is null, "Structural Undo removes the component");
Undo.PerformUndo();
Check(ReferenceEquals(child.GetComponent<StateComponent>(), component) && component.Count == 27, "Structural Undo restores the same component identity and state");
Undo.PerformRedo(); Check(child.GetComponent<StateComponent>() is null, "Structural Redo removes the restored component");
Undo.ClearAll();
var budget = Undo.maximumHistoryBytes;
try
{
    Undo.maximumHistoryBytes = 0; Undo.RecordObject(target, "disabled");
    Check(!Undo.canUndo && Undo.estimatedHistoryBytes == 0, "A zero history budget disables retention");
}
finally { Undo.maximumHistoryBytes = budget; Undo.ClearAll(); }
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Graphics" or "BEngine.Rendering2D"),
    "Editor workflow checks need no GUI, workbench or graphics packages");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_STATE_WORKFLOW_OK " + checks.Count);

sealed class TestPreview(StateAsset target, int original) : IEditorPreview
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
