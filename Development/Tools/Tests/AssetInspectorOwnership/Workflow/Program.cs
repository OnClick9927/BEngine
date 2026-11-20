using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Inspector = BEngine.Editor.Editor;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", Path.Combine(output, "EditorData"));
DotNetPlatformServices.Initialize(); WindowsEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services); using var attached = EditorServiceScope.Attach(services);
RuntimeTypeCache.RegisterAssemblies([typeof(BAssetEditor).Assembly, typeof(TextureImporter).Assembly, Assembly.GetExecutingAssembly()]);
var checks = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
var begin = typeof(GUI).GetMethod("BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
var end = typeof(GUI).GetMethod("EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
var commands = new List<GpuCanvasCommand>();
void Frame(Action draw)
{
    commands.Clear(); begin.Invoke(null, [new Event { type = EventType.Repaint, rawType = EventType.Repaint }, 800, 2400, commands]);
    try { draw(); } finally { end.Invoke(null, null); }
}
var snapshots = new SortedDictionary<string, GpuCanvasCommand[]>();
foreach (var (label, asset) in new (string, BAsset)[] {
    ("Texture", new Texture(16, 24) { name = "Fixture texture" }),
    ("UnavailableTexture", (Texture)Activator.CreateInstance(typeof(Texture), nonPublic: true)!),
    ("RenderTexture", new RenderTexture(32, 48) { name = "Fixture render texture" }),
    ("Script", new MonoScript { name = "Fixture script" }),
    ("Generic", new PlainAsset { name = "Fixture generic", Count = 31 }) })
{
    RuntimeAssetReferences.BindReference(asset, "memory-fixture:" + label,
        "E:/Project/_BZP/BEngine/Temp/AI45Metadata/Absent/" + label + ".fixture");
    using var editor = Inspector.CreateEditor(asset);
    Frame(editor.OnInspectorGUI); snapshots.Add(label, commands.ToArray());
    Check(commands.Count > 0 && !editor.hasUnsavedChanges, label + " repaints without fabricated edits");
    if (asset is RenderTexture)
    {
        using var compatible = Inspector.CreateEditor(asset, typeof(RenderTextureEditor));
        Frame(compatible.OnInspectorGUI); Check(commands.SequenceEqual(snapshots[label]), "Explicit RenderTextureEditor remains behavior-compatible");
    }
    BObject.DestroyImmediate(asset);
}
File.WriteAllText(Path.Combine(output, "Snapshot.json"), JsonSerializer.Serialize(snapshots));
Undo.ClearAll();
var undoTarget = new RenderTexture(32, 48);
Undo.RecordObject(undoTarget, "Edit Render Texture"); undoTarget.Resize(1, 96); undoTarget.useDepth = false;
Undo.PerformUndo();
File.WriteAllText(Path.Combine(output, "UndoSnapshot.json"), JsonSerializer.Serialize(new { undoTarget.width, undoTarget.height, undoTarget.useDepth }));
Check(undoTarget.useDepth, "RenderTexture Undo preserves existing serialized-property behavior");
Undo.ClearAll(); BObject.DestroyImmediate(undoTarget);
#if !LEGACY_INSPECTOR
var provider = new CustomInspector(); EditorAssetAdapters.Register(new CustomAdapter(provider));
var target = new CustomAsset { name = "Custom authoring" };
RuntimeAssetReferences.BindReference(target, "memory-fixture:Custom", "E:/Project/_BZP/BEngine/Temp/AI45Metadata/Absent/Custom.fixture");
using (var editor = Inspector.CreateEditor(target))
{
    Frame(() => { GUI.changed = true; editor.OnInspectorGUI(); });
    Check(provider.Information == 1 && provider.Properties == 1, "Generic IMGUI authoring dispatches package information and controls");
    Check(editor.hasUnsavedChanges, "Explicit provider changes survive an already-set GUI.changed flag");
    Check(commands.Any(c => c.Content == "Package-specific metadata"), "Shared presentation widgets render through real IMGUI controls");
    provider.Handled = false; Frame(editor.OnInspectorGUI);
    Check(commands.Any(c => c.Content == "Count"), "Unhandled package sections fall back to the generic serialized Inspector");
    editor.DiscardChanges(); Check(!editor.hasUnsavedChanges, "Discard still clears generic authoring state");
}
Check(!typeof(BAssetEditor).Assembly.GetReferencedAssemblies().Any(a => a.Name == "BEngine.Graphics.Editor"), "IMGUI has no compiled dependency on Graphics Editor");
Check(!typeof(TextureImporter).Assembly.GetReferencedAssemblies().Any(a => a.Name is "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor"), "Graphics authoring introduces no reverse GUI dependency");
BObject.DestroyImmediate(target);
#endif
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("ASSET_INSPECTOR_WORKFLOW_OK|" + checks.Count);

public sealed class PlainAsset : BAsset { public int Count; }
#if !LEGACY_INSPECTOR
public sealed class CustomAsset : BAsset { public int Count; }
sealed class CustomAdapter(CustomInspector inspector) : EditorAssetAdapter
{
    public override Type AssetType => typeof(CustomAsset);
    public override bool ReadOnly => false;
    public override IEditorAssetInspector Inspector => inspector;
}
sealed class CustomInspector : IEditorAssetInspector
{
    public int Information, Properties; public bool Handled = true;
    public void DrawInformation(BAsset asset, IEditorInspectorGUI gui) { Information++; gui.Heading("Package-specific metadata"); }
    public EditorInspectorResult DrawProperties(BAsset asset, SerializedObject serialized, IEditorInspectorGUI gui)
    { Properties++; return new(Handled, Handled); }
}
#endif
