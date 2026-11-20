using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform.DotNet;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.IMGUI"),
    "Editor snapshots need no rendering or GUI package");
var own = Assembly.GetExecutingAssembly();
Check(!typeof(EditorObjectSnapshot).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName == own.GetName().Name),
    "Independent snapshot consumer has no Core friendship");
Throws<ArgumentNullException>(() => EditorObjectSnapshotAdapters.Register(null!), "Null providers are rejected");
Throws<ArgumentException>(() => EditorObjectSnapshotAdapters.Register(new InvalidAdapter()), "Non-object types are rejected");
Throws<ArgumentException>(() => EditorObjectSnapshotAdapters.Register(new OpenAdapter()), "Open object types are rejected");
Throws<ArgumentNullException>(() => EditorObjectSnapshotAdapters.UnregisterAssembly(null!), "Null retirement owners are rejected");
var target = new SampleAsset { name = "original", PublicValue = 3 }; target.SetHidden(7);
var ordinary = EditorObjectSnapshot.Capture(target);
target.PublicValue = 4; target.SetHidden(8); ordinary.Restore();
Check(target.PublicValue == 3 && target.Hidden == 8, "Unregistered objects retain ordinary serialized-only snapshots");
EditorObjectSnapshotAdapters.Register(new HiddenAdapter());
EditorObjectSnapshotAdapters.Register(new BaseAdapter());
var snapshot = EditorObjectSnapshot.Capture(target);
target.PublicValue = 6; target.SetHidden(19); target.SetBaseHidden(12); target.name = "edited";
snapshot.Restore(); snapshot.Restore();
Check(target.Hidden == 8 && target.PublicValue == 3 && target.name == "original", "Extension state and serialized values restore repeatedly");
Check(target.BaseHidden == 0, "A base-type contribution composes with the concrete-type contribution");
Check(snapshot.EstimatedBytes > ordinary.EstimatedBytes, "Retained payload participates in the history budget");
var destination = new SampleAsset { name = "destination", PublicValue = 1 }; var id = destination.Id;
destination.SetHidden(99); snapshot.CopyValuesTo(destination);
Check(destination.Hidden == 8 && destination.PublicValue == 3 && destination.name == "original" && destination.Id == id,
    "Copying applies owned extension data while preserving destination identity");
target.SetHidden(22); snapshot.Restore();
Check(target.Hidden == 8, "Copied data does not consume or alias the captured state");
Throws<ArgumentException>(() => snapshot.CopyValuesTo(new OtherAsset()), "Value copying rejects a different object type");
EditorObjectSnapshotAdapters.Register(new HiddenAdapter());
target.name = "preserve on retired";
Throws<ObjectDisposedException>(snapshot.Restore, "Replaced provider invalidates older snapshots explicitly");
Check(target.name == "preserve on retired", "Retired snapshots fail before any ordinary property mutation");
destination.name = "preserve destination";
Throws<ObjectDisposedException>(() => snapshot.CopyValuesTo(destination), "Retired state cannot copy stale provider data");
Check(destination.name == "preserve destination", "A rejected copy preserves destination properties");
var replacement = EditorObjectSnapshot.Capture(target); target.SetHidden(31); replacement.Restore();
Check(target.Hidden == 8, "Replacement provider serves new captures and invalidates cached lookups");
EditorObjectSnapshotAdapters.UnregisterAssembly(own);
Throws<ObjectDisposedException>(replacement.Restore, "Assembly retirement revokes captured provider callbacks");
var noExtension = EditorObjectSnapshot.Capture(target); target.SetHidden(56); noExtension.Restore();
Check(target.Hidden == 56, "Retirement removes the provider from already-cached target types");
var (weak, retained) = CollectibleProbe.Create(args[1], target);
for (var i = 0; weak.IsAlive && i < 20; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
Check(!weak.IsAlive, "A retained Core snapshot does not pin a retired extension or its load context");
Throws<ObjectDisposedException>(retained.Restore, "Collected extension state is rejected without calling retired code");
GC.KeepAlive(retained);
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }));
Console.WriteLine("CORE_SNAPSHOT_OK " + checks.Count);

class BaseAsset : BAsset
{
    private int _baseHidden;
    public int BaseHidden => _baseHidden;
    public void SetBaseHidden(int value) => _baseHidden = value;
}
class SampleAsset : BaseAsset
{
    private int _hidden;
    public int PublicValue;
    public int Hidden => _hidden;
    public void SetHidden(int value) => _hidden = value;
}
sealed class OtherAsset : BAsset { }
sealed class GenericAsset<T> : BAsset { }
sealed class HiddenAdapter : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => typeof(SampleAsset);
    public override object Capture(BObject target) => new[] { ((SampleAsset)target).Hidden };
    public override void Restore(BObject target, object? payload) => ((SampleAsset)target).SetHidden(((int[])payload!)[0]);
}
sealed class BaseAdapter : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => typeof(BaseAsset);
    public override object Capture(BObject target) => ((BaseAsset)target).BaseHidden;
    public override void Restore(BObject target, object? payload) => ((BaseAsset)target).SetBaseHidden((int)payload!);
}
sealed class InvalidAdapter : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => typeof(string);
    public override object Capture(BObject target) => 0;
    public override void Restore(BObject target, object? payload) { }
}
sealed class OpenAdapter : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => typeof(GenericAsset<>);
    public override object Capture(BObject target) => 0;
    public override void Restore(BObject target, object? payload) { }
}
sealed class ParametricAdapter<T> : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => typeof(SampleAsset);
    public override object Capture(BObject target) => 0;
    public override void Restore(BObject target, object? payload) { }
}
sealed class TargetTypeAdapter(Type objectType) : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => objectType;
    public override object Capture(BObject target) => 0;
    public override void Restore(BObject target, object? payload) { }
}
static class CollectibleProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (WeakReference, EditorObjectSnapshot) Create(string path, BObject target)
    {
        var context = new AssemblyLoadContext("snapshot extension", true);
        context.Resolving += (_, name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name && AssemblyLoadContext.GetLoadContext(a) == AssemblyLoadContext.Default);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
        var type = assembly.GetType("ExternalSnapshotAdapter", true)!;
        EditorObjectSnapshotAdapters.Register((EditorObjectSnapshotAdapter)Activator.CreateInstance(type)!);
        EditorObjectSnapshotAdapters.Register((EditorObjectSnapshotAdapter)Activator.CreateInstance(typeof(ParametricAdapter<>).MakeGenericType(type))!);
        EditorObjectSnapshotAdapters.Register(new TargetTypeAdapter(typeof(GenericAsset<>).MakeGenericType(type)));
        var snapshot = EditorObjectSnapshot.Capture(target);
        RuntimeAssemblyCleanup.Unregister([assembly]);
        var weak = new WeakReference(context); context.Unload(); return (weak, snapshot);
    }
}
