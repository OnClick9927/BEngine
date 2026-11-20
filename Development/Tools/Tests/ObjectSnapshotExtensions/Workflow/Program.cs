using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform, TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeHelpers.RunModuleConstructor(typeof(TextureImporter).Module.ModuleHandle);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
using var target = new RenderTexture(8, 7) { name = "before", format = RenderTextureFormat.Rgba8, useDepth = true };
var identity = target.Id; var snapshot = EditorObjectSnapshot.Capture(target);
target.Resize(15, 13); target.name = "after"; target.format = RenderTextureFormat.R8; target.useDepth = false;
snapshot.Restore();
var dimensionsRestored = target.width == 8 && target.height == 7;
Check(target.name == "before" && target.format == RenderTextureFormat.Rgba8 && target.useDepth, "Ordinary descriptor fields restore alongside dimensions");
#if LEGACY
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, DimensionsRestored = dimensionsRestored, Width = target.width, Height = target.height, Checks = checks.Count, Details = checks }));
Console.WriteLine("BASELINE_DIMENSIONS_RESTORED " + dimensionsRestored);
#else
Check(dimensionsRestored && target.Id == identity, "Core snapshot restores RenderTexture dimensions without changing its identity");
Check(!typeof(EditorObjectSnapshot).Assembly.GetReferencedAssemblies().Any(a => a.Name == "BEngine.Graphics"), "Core has no Graphics assembly dependency");
Check(!typeof(RenderTexture).Assembly.GetReferencedAssemblies().Any(a => a.Name!.EndsWith(".Editor")), "Runtime resources do not depend on editor snapshot mechanisms");
using var copy = new RenderTexture(3, 3); var copyIdentity = copy.Id;
snapshot.CopyValuesTo(copy);
Check(copy.width == 8 && copy.height == 7 && copy.Id == copyIdentity, "Value copying restores dimensions and keeps destination identity");
using var device = new ScopedTargetDevice();
using (RenderTextureScope.Push(device, target)) { }
var originalNative = device.Targets.Last();
Undo.ClearAll(); Undo.RecordObject(target, "resize"); target.Resize(19, 17); target.filterMode = TextureFilterMode.Point;
using (RenderTextureScope.Push(device, target))
{
    var editedNative = device.Current!;
    Undo.PerformUndo();
    Check(target.width == 8 && target.height == 7 && target.filterMode == TextureFilterMode.Bilinear, "Undo restores dimensions and filtering together");
    Check(editedNative.Disposals == 0 && ReferenceEquals(device.Current, editedNative), "Undo during a borrowed target scope defers native release");
}
Check(device.Targets.Last().Disposals == 1 && originalNative.Disposals == 1, "Retired native targets release once after the final binding");
using (RenderTextureScope.Push(device, target)) Check(device.Current!.Width == 8 && device.Current.Height == 7, "Next draw creates the restored target dimensions");
Undo.PerformRedo();
Check(target.width == 19 && target.height == 17 && target.filterMode == TextureFilterMode.Point, "Redo captures and restores the inverse extension state");
for (var i = 0; i < 4; i++) { Undo.PerformUndo(); Undo.PerformRedo(); }
Check(target.width == 19 && target.height == 17 && target.Id == identity, "Repeated history navigation keeps resource state and identity");
Undo.ClearAll();
#pragma warning disable CS0618 // Existing snapshot APIs remain usable by already-shipped tools.
Undo.SetSnapshotTarget(target, "drag resize"); Undo.CreateSnapshot(); target.Resize(31, 29); Undo.RestoreSnapshot();
Check(target.width == 19 && target.height == 17, "Interactive snapshot restore uses the same public extension mechanism");
Undo.ClearSnapshotTarget();
#pragma warning restore CS0618
target.Resize(8, 7); target.Release(); var released = EditorObjectSnapshot.Capture(target);
target.name = "released edited"; released.Restore();
Check(!target.IsCreated(), "Restoring an idle descriptor does not allocate a native GPU target");
using (RenderTextureScope.Push(device, copy)) { }
copy.Release(); Check(!copy.IsCreated(), "Independent destination resources retain their own lifecycle");
Undo.ClearAll(); Undo.maximumHistoryBytes = snapshot.EstimatedBytes - 1; Undo.RecordObject(target, "budget");
Check(!Undo.canUndo, "Snapshot payload is subject to the existing history memory budget");
Undo.maximumHistoryBytes = 128L * 1024 * 1024; Undo.ClearAll();
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, DimensionsRestored = true, Checks = checks.Count, Details = checks }));
Console.WriteLine("WORKFLOW_SNAPSHOT_OK " + checks.Count);
#endif
