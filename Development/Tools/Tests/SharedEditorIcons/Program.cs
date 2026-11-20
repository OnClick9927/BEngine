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

var output = Path.GetFullPath(args[0]);
var legacy = args.Length > 1 && args[1] == "legacy";
Directory.CreateDirectory(output);
BEngine.Platform.DotNet.DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);
using var attached = EditorServiceScope.Attach(services);
var checks = new List<string>();
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
void Throws<T>(Action action, string label) where T : Exception
{ try { action(); } catch (T) { Check(true, label); return; } throw new Exception(label); }

// This snapshot runs in a separate process for each ownership combination.
// A current-only process has no Graphics, IMGUI or default workbench references.
var snapshot = new SortedDictionary<string, string?>();
foreach (var nested in new[] { typeof(EditorBuiltinIcons.Assets), typeof(EditorBuiltinIcons.Components), typeof(EditorBuiltinIcons.Toolbar) })
    foreach (var field in nested.GetFields(BindingFlags.Public | BindingFlags.Static))
        snapshot[$"Constant/{nested.Name}/{field.Name}"] = field.GetRawConstantValue() as string;
foreach (var name in new[] { "GameObject Icon", "Transform Icon", "Camera Icon", "MonoBehaviour Icon", "SpriteRenderer Icon", "cs Script Icon", "Folder Icon", "FolderOpened Icon", "SceneAsset Icon", "Material Icon", "Texture2D Icon", "GUISkin Icon", "Font Icon", "Undo", "Redo", "FoldoutOpen", "PlayButton", "d_PlayButton", "D_Folder Icon", "Unknown", "d_Unknown" })
    snapshot["Name/" + name] = EditorBuiltinIcons.Resolve(name);
var suffixes = new[] { "prefab.yaml", "scene.yaml", "material.yaml", "asmdef.yaml", "guiskin.yaml", "bpackage", "shader", "cs", "html", "htm", "css", "png", "jpg", "jpeg", "bmp", "gif", "tga", "webp", "svg", "fbx", "obj", "gltf", "glb", "ttf", "otf", "woff", "woff2", "yaml", "yml", "json", "xml", "txt", "md", "log", "unknown" };
// The old Graphics module loads AssetPipeline as a dependency. Compare the
// catalog's extension fallback with the same (empty) format-provider fixture,
// rather than conflating another module's registrations with this API's paths.
foreach (var suffix in suffixes) AssetTypeRegistry.Unregister("." + suffix);
foreach (var suffix in suffixes)
    snapshot["Asset/" + suffix] = EditorAssetIcons.GetIconPath(Path.Combine(output, "absent." + suffix));
foreach (var type in new[] { typeof(MonoScript), typeof(Scene), typeof(PrefabAsset), typeof(Transform), typeof(MonoBehaviour), typeof(BEngine.TextAsset), typeof(ScriptableObject), typeof(Component), typeof(Plain), typeof(Attributed) })
    snapshot["Type/" + type.Name] = EditorIconRegistry.GetComponentIconPath(type);
EditorBuiltinIcons.IsDarkTheme = static () => true;
snapshot["Theme/Dark"] = EditorBuiltinIcons.engineLogo;
EditorBuiltinIcons.IsDarkTheme = static () => false;
snapshot["Theme/Light"] = EditorBuiltinIcons.engineLogo;
EditorBuiltinIcons.IsDarkTheme = null;
snapshot["Theme/Default"] = EditorBuiltinIcons.engineLogo;
var folder = Path.Combine(output, "Folder"); Directory.CreateDirectory(folder);
snapshot["Folder/Empty"] = EditorAssetIcons.GetIconPath(folder);
File.WriteAllText(Path.Combine(folder, "hidden.meta"), "metadata");
snapshot["Folder/OnlyMeta"] = EditorAssetIcons.GetIconPath(folder);
File.WriteAllText(Path.Combine(folder, "visible.txt"), "visible");
snapshot["Folder/Closed"] = EditorAssetIcons.GetIconPath(folder);
snapshot["Folder/Open"] = EditorAssetIcons.GetIconPath(folder, true);
File.Delete(Path.Combine(folder, "visible.txt")); File.Delete(Path.Combine(folder, "hidden.meta"));
AssetTypeRegistry.Register(".sharedicon44", "Shared icon", "custom-icon.png");
Check(EditorAssetIcons.GetIconPath("absent.sharedicon44") == "custom-icon.png", "Custom asset extensions keep their icon");
AssetTypeRegistry.Unregister(".sharedicon44");
Throws<ArgumentException>(() => EditorBuiltinIcons.Resolve(" "), "Blank icon name rejected");
Throws<ArgumentException>(() => EditorAssetIcons.GetIconPath(""), "Blank asset path rejected");
Throws<ArgumentNullException>(() => EditorIconRegistry.GetIconPath(null!), "Null icon type rejected");
File.WriteAllText(Path.Combine(output, "Snapshot.json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

if (!legacy)
{
    Check(typeof(EditorBuiltinIcons).Assembly == typeof(EditorIconRegistry).Assembly, "Shared icon catalog belongs to shared editor services");
    Check(typeof(EditorAssetIcons).Assembly == typeof(EditorIconRegistry).Assembly, "Asset icon lookup belongs to shared editor services");
    RuntimeTypeCache.RegisterAssemblies([typeof(EditorBuiltinIcons).Assembly]);
    foreach (var type in new[] { typeof(EditorBuiltinIcons), typeof(EditorBuiltinIcons.Assets), typeof(EditorBuiltinIcons.Components), typeof(EditorBuiltinIcons.Toolbar), typeof(EditorAssetIcons) })
        foreach (var version in new[] { "", ", Version=0.1.0.0, Culture=neutral, PublicKeyToken=null" })
            Check(RuntimeTypeCache.ResolveSerializedType(type.FullName + ", BEngine.Graphics.Editor" + version) == type, "Old identity resolves: " + type.FullName + version);
    var a = Provider.Create("A"); var b = Provider.Create("B");
    var builtin = EditorBuiltinIcons.Resolve("PlayButton");
    a.Call("Name", "PlayButton", "a.png"); b.Call("Name", "PlayButton", "b.png");
    Check(EditorBuiltinIcons.Resolve("d_playbutton") == "b.png", "Latest name provider wins case insensitively");
    RuntimeAssemblyCleanup.UnregisterExtensions(a.Assembly);
    Check(EditorBuiltinIcons.Resolve("PlayButton") == "b.png", "Retiring a hidden name provider preserves the active one");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorBuiltinIcons.Resolve("PlayButton") == builtin, "Retiring all name providers restores the builtin alias");
    a.Call("Name", "PlayButton", "a.png"); b.Call("Name", "PlayButton", "b.png");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorBuiltinIcons.Resolve("PlayButton") == "a.png", "Retiring the overriding name provider restores the survivor");

    a.Call("Type", typeof(Plain), "type-a.png", false); b.Call("Type", typeof(Plain), "type-b.png", false);
    Check(EditorIconRegistry.GetIconPath(typeof(Plain)) == "type-b.png", "Latest exact type provider wins");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorIconRegistry.GetIconPath(typeof(Plain)) == "type-a.png", "Unloading overrides restores the previous type provider and invalidates cached results");
    a.Call("Type", typeof(Parent), "parent.png", true);
    b.Call("Type", typeof(Child), "child.png", false);
    Check(EditorIconRegistry.GetIconPath(typeof(Child)) == "child.png", "Exact types outrank inherited registrations");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorIconRegistry.GetIconPath(typeof(Child)) == "parent.png", "Inherited lookup is restored after exact provider retirement");
    a.Call("Type", typeof(object), "object.png", true);
    Check(EditorIconRegistry.GetIconPath(typeof(Child)) == "parent.png", "Inherited matches retain visible registration order");
    RuntimeAssemblyCleanup.UnregisterExtensions(a.Assembly);
    Check(EditorIconRegistry.GetIconPath(typeof(Plain)) is null, "Retiring type providers invalidates resolved null/non-null cache");

    a.Call("TypeFallback", (Func<Type, string?>)(static _ => "fallback-a.png"));
    b.Call("TypeFallback", (Func<Type, string?>)(static _ => "fallback-b.png"));
    Check(EditorIconRegistry.GetIconPath(typeof(Plain)) == "fallback-b.png", "Latest type fallback wins");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorIconRegistry.GetIconPath(typeof(Plain)) == "fallback-a.png", "Caller ownership retires a fallback even when its delegate is defined elsewhere");
    RuntimeAssemblyCleanup.UnregisterExtensions(a.Assembly);
    Check(EditorIconRegistry.GetIconPath(typeof(Plain)) is null, "Default type fallback survives extension retirement");
    a.Call("ComponentFallback", (Func<Type, string>)(static _ => "component-a.png"));
    b.Call("ComponentFallback", (Func<Type, string>)(static _ => "component-b.png"));
    Check(EditorIconRegistry.GetComponentIconPath(typeof(Plain)) == "component-b.png", "Latest component fallback wins");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorIconRegistry.GetComponentIconPath(typeof(Plain)) == "component-a.png", "Component fallback restores the surviving owner");
    RuntimeAssemblyCleanup.UnregisterExtensions(a.Assembly);
    Check(EditorIconRegistry.GetComponentIconPath(typeof(Plain)) == EditorBuiltinIcons.Components.Default, "Built-in component fallback survives extension retirement");

    a.Call("Theme", (Func<bool>)(static () => true)); b.Call("Theme", (Func<bool>)(static () => false));
    Check(EditorBuiltinIcons.engineLogo == "Icons/BEngine.Dark.png", "Latest theme provider chooses the light surface logo");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorBuiltinIcons.engineLogo == "Icons/BEngine.png", "Theme retirement restores the surviving provider");
    b.Call("Theme", (Func<bool>)(static () => false));
    RuntimeAssemblyCleanup.UnregisterExtensions(a.Assembly);
    Check(EditorBuiltinIcons.engineLogo == "Icons/BEngine.Dark.png", "Out-of-order theme retirement preserves the current provider");
    RuntimeAssemblyCleanup.UnregisterExtensions(b.Assembly);
    Check(EditorBuiltinIcons.IsDarkTheme is null, "Theme retirement releases all retired callbacks");
    var dynamicTheme = (Func<bool>)a.Method("Bool").CreateDelegate(typeof(Func<bool>));
    b.Call("Theme", dynamicTheme);
    RuntimeAssemblyCleanup.UnregisterExtensions(a.Assembly);
    Check(EditorBuiltinIcons.IsDarkTheme is null, "Theme callback definition ownership also retires delegates");
    a.Call("Theme", (Func<bool>)(static () => false));
    EditorBuiltinIcons.IsDarkTheme = null;
    Check(EditorBuiltinIcons.IsDarkTheme is null, "An explicit null still clears the theme channel");

    for (var i = 0; i < 100; i++)
    {
        a.Call("Name", "replaced", i.ToString());
        a.Call("Type", typeof(Plain), i.ToString(), false);
        a.Call("Theme", (Func<bool>)(static () => false));
    }
    RuntimeAssemblyCleanup.UnregisterExtensions(a.Assembly);
    Check(EditorBuiltinIcons.Resolve("replaced") == "replaced" && EditorIconRegistry.GetIconPath(typeof(Plain)) is null && EditorBuiltinIcons.IsDarkTheme is null, "Repeated same-owner replacement retires completely");
    var weak = RetireCollectibleProvider();
    for (var i = 0; i < 12 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    Check(!weak.IsAlive, "Retired icon names, types, fallbacks and theme do not pin a collectible provider");
    var forbidden = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name is { } name && (name.StartsWith("BEngine.Graphics") || name.StartsWith("BEngine.Rendering2D") || name.StartsWith("BEngine.IMGUI") || name.StartsWith("BEngine.EditorWorkbench"))).Select(assembly => assembly.GetName().Name).ToArray();
    Check(forbidden.Length == 0, "All metadata and ownership tests run without loading Graphics, Rendering2D, IMGUI or EditorWorkbench: " + string.Join(',', forbidden));
}

// Compare stable lookup and uncached resolution separately. Re-registration is
// outside the measured region; it clears the resolution cache through public API.
for (var i = 0; i < 2000; i++) _ = EditorIconRegistry.GetIconPath(typeof(Attributed));
const int queries = 20000;
var before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
for (var i = 0; i < queries; i++) _ = EditorIconRegistry.GetIconPath(typeof(Attributed));
timer.Stop(); var warmBytes = GC.GetAllocatedBytesForCurrentThread() - before;
long coldBytes = 0; long coldTicks = 0;
for (var i = 0; i < 1000; i++)
{
    EditorIconRegistry.Register(typeof(Parent), "parent.png", true);
    before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
    _ = EditorIconRegistry.GetIconPath(typeof(Child));
    coldTicks += Stopwatch.GetTimestamp() - started; coldBytes += GC.GetAllocatedBytesForCurrentThread() - before;
}
File.WriteAllText(Path.Combine(output, "Benchmark.json"), JsonSerializer.Serialize(new { WarmQueries = queries, WarmElapsedMs = timer.Elapsed.TotalMilliseconds, WarmAllocatedBytes = warmBytes, ColdQueries = 1000, ColdElapsedMs = coldTicks * 1000d / Stopwatch.Frequency, ColdAllocatedBytes = coldBytes }));
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Legacy = legacy, OwnerAssembly = typeof(EditorBuiltinIcons).Assembly.GetName().Name, SnapshotEntries = snapshot.Count, Items = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} checks; {snapshot.Count} snapshot entries");

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference RetireCollectibleProvider()
{
    var provider = Provider.Create("Collectible");
    provider.Call("Name", "collectible", "collectible.png");
    provider.Call("Type", provider.Type, "collectible.png", true);
    provider.Call("Theme", (Func<bool>)provider.Method("Bool").CreateDelegate(typeof(Func<bool>)));
    provider.Call("TypeFallback", (Func<Type, string?>)provider.Method("Icon").CreateDelegate(typeof(Func<Type, string?>)));
    provider.Call("ComponentFallback", (Func<Type, string>)provider.Method("Icon").CreateDelegate(typeof(Func<Type, string>)));
    _ = EditorIconRegistry.GetIconPath(provider.Type);
    var weak = new WeakReference(provider.Assembly);
    RuntimeAssemblyCleanup.Unregister([provider.Assembly]);
    return weak;
}

class Plain;
class Parent;
sealed class Child : Parent;
[EditorIcon("attributed.png")]
sealed class Attributed;

sealed class Provider(Type type)
{
    public Type Type => type;
    public Assembly Assembly => type.Assembly;
    public MethodInfo Method(string name) => type.GetMethod(name)!;
    public void Call(string name, params object?[] arguments) => Method(name).Invoke(null, arguments);
    public static Provider Create(string label)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("IconProvider" + label + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Provider").DefineType("Provider", TypeAttributes.Public | TypeAttributes.Sealed);
        foreach (var (name, target) in new[] {
            ("Name", typeof(EditorIconRegistry).GetMethod(nameof(EditorIconRegistry.RegisterName))!),
            ("Type", typeof(EditorIconRegistry).GetMethod(nameof(EditorIconRegistry.Register))!),
            ("TypeFallback", typeof(EditorIconRegistry).GetMethod(nameof(EditorIconRegistry.SetTypeFallback))!),
            ("ComponentFallback", typeof(EditorIconRegistry).GetMethod(nameof(EditorIconRegistry.SetComponentFallback))!),
            ("Theme", typeof(EditorBuiltinIcons).GetProperty(nameof(EditorBuiltinIcons.IsDarkTheme))!.SetMethod!) })
        {
            var parameters = target.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            var method = builder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), parameters);
            method.SetImplementationFlags(MethodImplAttributes.NoInlining);
            var il = method.GetILGenerator();
            for (var i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, target); il.Emit(OpCodes.Ret);
        }
        var boolean = builder.DefineMethod("Bool", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes).GetILGenerator();
        boolean.Emit(OpCodes.Ldc_I4_0); boolean.Emit(OpCodes.Ret);
        var icon = builder.DefineMethod("Icon", MethodAttributes.Public | MethodAttributes.Static, typeof(string), [typeof(Type)]).GetILGenerator();
        icon.Emit(OpCodes.Ldstr, "collectible.png"); icon.Emit(OpCodes.Ret);
        return new Provider(builder.CreateType()!);
    }
}
