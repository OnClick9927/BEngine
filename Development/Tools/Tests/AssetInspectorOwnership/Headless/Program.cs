using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
BEngine.Platform.DotNet.DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services); using var scope = EditorServiceScope.Attach(services);
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
// Force the real provider's module initializer, without loading a GUI or workbench.
RuntimeHelpers.RunModuleConstructor(typeof(TextureImporter).Module.ModuleHandle);
var gui = new RecordingGUI(); var texture = new Texture(16, 24);
var inspector = EditorAssetAdapters.GetInspector(texture)!;
Check(inspector.GetType().Assembly == typeof(TextureImporter).Assembly, "Texture authoring belongs to Graphics Editor");
inspector.DrawInformation(texture, gui);
Check(gui.Calls.SequenceEqual(new[] { "Dimensions=16 x 24" }), "Texture dimensions preserve the old Inspector information");
var unavailable = (Texture)Activator.CreateInstance(typeof(Texture), nonPublic: true)!;
gui.Calls.Clear(); inspector.DrawInformation(unavailable, gui);
Check(gui.Calls.SequenceEqual(new[] { "Dimensions=Unavailable" }), "Invalid texture dimensions preserve the unavailable state");
Check(EditorAssetAdapters.IsReadOnly(texture), "Imported textures remain read-only");
var importer = new TextureImporter();
using (var serialized = new SerializedObject(importer))
{
    var expected = new[] { "textureType", "sRGBTexture", "alphaIsTransparency", "isReadable", "Space:4", "Heading:Sampling", "filterMode", "wrapMode", "generateMipMaps", "maxTextureSize", "compressionFormat", "Space:4", "Heading:Platform Overrides (Player Build)", "platformSettings", "userData" };
    gui.Calls.Clear(); var result = inspector.DrawImportSettings(importer, serialized, gui);
    Check(result.Handled && !result.Changed && gui.Calls.SequenceEqual(expected), "Texture import fields and group order match the pre-migration Inspector");
    importer.textureType = TextureImporterType.Sprite; serialized.Update(); gui.Calls.Clear();
    inspector.DrawImportSettings(importer, serialized, gui);
    Check(gui.Calls.SequenceEqual(expected.Take(4).Concat(new[] { "Space:4", "Heading:Sprite", "pixelsPerUnit", "spritePivotX", "spritePivotY" }).Concat(expected.Skip(4))), "Sprite-only import fields keep their condition and ordering");
    gui.EditProperty = "maxTextureSize"; gui.Calls.Clear();
    inspector.DrawImportSettings(importer, serialized, gui);
    Check(importer.maxTextureSize != 512 && serialized.hasModifiedProperties, "Importer widgets preserve buffered serialization");
    Check(serialized.ApplyModifiedProperties() && importer.maxTextureSize == 512, "Importer edits still apply through SerializedObject");
    Undo.PerformUndo(); Check(importer.maxTextureSize != 512, "Importer field edits remain undoable");
    gui.EditProperty = null;
}
Undo.ClearAll();
var rt = new RenderTexture(32, 48); var renderInspector = EditorAssetAdapters.GetInspector(rt)!;
Check(renderInspector.GetType().Assembly == typeof(TextureImporter).Assembly && !renderInspector.ShowImportSettings, "RenderTexture uses Graphics authoring without import settings");
Check(!EditorAssetAdapters.IsReadOnly(rt), "RenderTexture retains its editable status");
using (var serialized = new SerializedObject(rt))
{
    gui.Calls.Clear(); var result = renderInspector.DrawProperties(rt, serialized, gui);
    Check(result.Handled && !result.Changed && !Undo.canUndo, "An unchanged RenderTexture repaint creates no undo record");
    Check(gui.Calls.SequenceEqual(new[] { "Begin", "Width", "Height", "Format", "Depth Buffer", "Auto Generate Mips", "Filter Mode", "Wrap Mode", "End" }), "RenderTexture preserves all seven controls in their original order");
    gui.Edit = true; result = renderInspector.DrawProperties(rt, serialized, gui);
    Check(result.Handled && result.Changed && rt.width == 1 && rt.height == 96 && !rt.useDepth && rt.autoGenerateMips, "RenderTexture changes clamp dimensions and update flags");
    Check(rt.filterMode == TextureFilterMode.Point && rt.wrapMode == TextureWrapMode.Repeat && EditorUtility.IsDirty(rt), "RenderTexture sampling edits mark the asset dirty");
    Undo.PerformUndo(); Check(rt.useDepth && !rt.autoGenerateMips && rt.filterMode == TextureFilterMode.Bilinear && rt.wrapMode == TextureWrapMode.Clamp, "RenderTexture Undo restores serialized render and sampling settings");
    Undo.PerformRedo(); Check(!rt.useDepth && rt.autoGenerateMips && rt.filterMode == TextureFilterMode.Point && rt.wrapMode == TextureWrapMode.Repeat, "RenderTexture Redo restores serialized render and sampling settings");
    gui.Edit = false;
}
var script = new MonoScript(); gui.Calls.Clear();
EditorAssetAdapters.GetInspector(script)!.DrawInformation(script, gui);
Check(gui.Calls.SequenceEqual(new[] { "Script Class=None" }) && !EditorAssetAdapters.IsReadOnly(script), "Script metadata uses the shared editor provider and stays editable");
var plain = new PlainAsset(); var child = new ChildAsset();
Check(EditorAssetAdapters.GetInspector(plain) is null, "Unknown assets use generic authoring");
var baseInspector = new TestInspector(); EditorAssetAdapters.Register(new TestAdapter(typeof(PlainAsset), baseInspector));
Check(ReferenceEquals(EditorAssetAdapters.GetInspector(child), baseInspector), "Registration invalidates cached misses and supports derived assets");
Check(EditorAssetAdapters.IsReadOnly(plain) && !EditorAssetAdapters.IsReadOnly(child), "Existing read-only semantics remain exact-type based");
Check(EditorAssetAdapters.MayHaveRepresentations(typeof(PlainAsset)) && !EditorAssetAdapters.MayHaveRepresentations(typeof(ChildAsset)), "Existing representation semantics remain exact-type based");
Check(EditorAssetAdapters.GetRepresentation(plain, typeof(PlainAsset)) == plain, "Existing representation enumeration remains available");
var childInspector = new TestInspector(); EditorAssetAdapters.Register(new TestAdapter(typeof(ChildAsset), childInspector));
Check(ReferenceEquals(EditorAssetAdapters.GetInspector(child), childInspector), "The exact asset provider takes priority");
var replacement = new TestInspector(); EditorAssetAdapters.Register(new TestAdapter(typeof(ChildAsset), replacement));
Check(ReferenceEquals(EditorAssetAdapters.GetInspector(child), replacement), "Same-assembly replacement invalidates the presentation cache");
EditorAssetAdapters.Invalidate(); Check(replacement.Invalidations == 1, "Editor invalidation still reaches adapters");
var owner = Provider.Create(); owner.Register(new TestAdapter(typeof(OwnedAsset), new TestInspector()));
var owned = new OwnedAsset(); Check(EditorAssetAdapters.GetInspector(owned) != null, "A separate caller may register a shared adapter implementation");
RuntimeAssemblyCleanup.UnregisterExtensions(owner.Type.Assembly);
Check(EditorAssetAdapters.GetInspector(owned) == null, "Retiring the caller removes registrations defined in another assembly");
var implementation = Provider.Create(typeof(TestInspector)); var dynamicInspector = (IEditorAssetInspector)Activator.CreateInstance(implementation.Type)!;
EditorAssetAdapters.Register(new TestAdapter(typeof(OwnedAsset), dynamicInspector));
RuntimeAssemblyCleanup.UnregisterExtensions(implementation.Type.Assembly);
Check(EditorAssetAdapters.GetInspector(owned) == null, "Retiring an inspector implementation removes its shared adapter and cached result");
var typeOwner = Provider.Create(typeof(PlainAsset)); var dynamicAsset = (BAsset)Activator.CreateInstance(typeOwner.Type)!;
EditorAssetAdapters.Register(new TestAdapter(typeOwner.Type, baseInspector)); _ = EditorAssetAdapters.GetInspector(dynamicAsset);
RuntimeAssemblyCleanup.UnregisterExtensions(typeOwner.Type.Assembly);
Check(EditorAssetAdapters.GetInspector(dynamicAsset) == baseInspector, "Retiring a registered asset type releases the exact registration and uses the surviving base provider");
var weak = ExerciseCollectible();
for (var i = 0; i < 12 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
Check(!weak.IsAlive, "Retired inspector registrations and resolved asset types do not pin a collectible assembly");
_ = MeasureLookup(texture);
var start = Stopwatch.GetTimestamp(); var allocated = MeasureLookup(texture);
var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
Check(allocated == 0, "100,000 stable Inspector lookups allocate zero bytes; observed " + allocated);
var forbidden = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name!).Where(n => n.StartsWith("BEngine.IMGUI") || n.StartsWith("BEngine.EditorWorkbench") || n.StartsWith("BEngine.Rendering2D")).ToArray();
Check(forbidden.Length == 0, "Graphics authoring works without loading IMGUI, EditorWorkbench or Rendering2D");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks, LookupBytes = allocated, LookupMilliseconds = elapsed }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("ASSET_INSPECTOR_HEADLESS_OK|" + checks.Count);

[MethodImpl(MethodImplOptions.NoInlining)]
static long MeasureLookup(BAsset asset)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100000; i++) _ = EditorAssetAdapters.GetInspector(asset);
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference ExerciseCollectible()
{
    var provider = Provider.Create(typeof(PlainAsset)); var asset = (BAsset)Activator.CreateInstance(provider.Type)!;
    EditorAssetAdapters.Register(new TestAdapter(provider.Type, new TestInspector()));
    _ = EditorAssetAdapters.GetInspector(asset); var weak = new WeakReference(provider.Type.Assembly);
    RuntimeAssemblyCleanup.Unregister([provider.Type.Assembly]); BObject.DestroyImmediate(asset); return weak;
}
public class PlainAsset : BAsset;
public sealed class ChildAsset : PlainAsset;
public sealed class OwnedAsset : BAsset;
public class TestInspector : IEditorAssetInspector { public int Invalidations; }
public class TestAdapter(Type type, IEditorAssetInspector inspector) : EditorAssetAdapter
{
    public override Type AssetType => type;
    public override IEditorAssetInspector Inspector => inspector;
    public override bool MayHaveRepresentations => true;
    public override IEnumerable<BObject> GetRepresentations(BObject asset) => [asset];
    public override void Invalidate() { if (inspector is TestInspector value) value.Invalidations++; }
}
sealed class RecordingGUI : IEditorInspectorGUI
{
    public readonly List<string> Calls = []; public bool Edit; public string? EditProperty;
    public void Heading(string text) => Calls.Add("Heading:" + text);
    public void Space(Fix64 pixels) => Calls.Add("Space:" + pixels);
    public void ReadOnlyText(string label, string value) => Calls.Add(label + "=" + value);
    public void PropertyField(SerializedProperty property, bool includeChildren)
    { if (!includeChildren) throw new Exception("Children omitted"); Calls.Add(property.propertyPath); if (property.propertyPath == EditProperty) property.intValue = 512; }
    public void BeginChangeCheck() => Calls.Add("Begin");
    public bool EndChangeCheck() { Calls.Add("End"); return Edit; }
    public int IntField(string label, int value) { Calls.Add(label); return Edit ? label == "Width" ? -8 : 96 : value; }
    public bool Toggle(string label, bool value) { Calls.Add(label); return Edit ? !value : value; }
    public Enum EnumPopup(string label, Enum value) { Calls.Add(label); return Edit ? label switch { "Filter Mode" => TextureFilterMode.Point, "Wrap Mode" => TextureWrapMode.Repeat, _ => value } : value; }
}
sealed class Provider(Type type)
{
    public Type Type => type;
    public void Register(EditorAssetAdapter adapter) => type.GetMethod("Register")!.Invoke(null, [adapter]);
    public static Provider Create(Type? parent = null)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("InspectorProvider" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Provider").DefineType("Provider", TypeAttributes.Public, parent ?? typeof(object));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var method = builder.DefineMethod("Register", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(EditorAssetAdapter)]);
        method.SetImplementationFlags(MethodImplAttributes.NoInlining);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(EditorAssetAdapters).GetMethod(nameof(EditorAssetAdapters.Register))!); il.Emit(OpCodes.Ret);
        return new Provider(builder.CreateType()!);
    }
}
