using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Text.Json;

var binaries = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var fixtures = Path.Combine(output, "Fixtures");
var descriptor = Path.Combine(fixtures, "editor.application.json");
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}
var host = Assembly.LoadFrom(Path.Combine(binaries, "BEngine.Editor.Host.dll"));
var run = host.GetType("BEngine.Editor.Host.Program", true)!.GetMethod("RunApplication", BindingFlags.NonPublic | BindingFlags.Static)!;
int Run(string mode = "legacy")
{
    try { return (int)run.Invoke(null, [fixtures, new[] { mode }])!; }
    catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
}
void Write(object? bootstraps = null, bool legacy = false)
{
    var data = new Dictionary<string, object?> { ["version"] = 1, ["assembly"] = "BEngine.BootstrapEntryProbe.dll", ["type"] = "BootstrapProbe.Entry" };
    if (!legacy) data.Add("bootstrapAssemblies", bootstraps);
    File.WriteAllText(descriptor, JsonSerializer.Serialize(data));
}
Write(legacy: true);
Check(Run() == 7, "Version 1 descriptors without bootstraps remain valid");
Write(Array.Empty<string>());
Check(Run() == 7, "Empty bootstrap list remains valid");
foreach (var name in new[] { "../outside.dll", "nested/probe.dll", @"nested\probe.dll", "C:probe.dll", "probe.exe", "" })
{
    Write(new[] { name });
    Throws<InvalidDataException>(() => Run(), "Reject nonlocal or non-DLL bootstrap: " + name);
}
Write(new[] { "BEngine.BootstrapPolicyProbe.dll", "bengine.bootstrapPolicyProbe.DLL" });
Throws<InvalidDataException>(() => Run(), "Reject duplicate bootstrap identity ignoring case");
Write(new[] { "BEngine.BootstrapEntryProbe.dll" });
Throws<InvalidDataException>(() => Run(), "Reject entry assembly repeated as bootstrap");
Write("invalid");
Throws<InvalidDataException>(() => Run(), "Reject malformed bootstrap list");
Write(new[] { "MissingProbe.dll" });
Throws<FileNotFoundException>(() => Run(), "Missing bootstrap fails before editor invocation");
var deps = Path.Combine(fixtures, "BEngine.BootstrapPolicyProbe.deps.json");
var savedDeps = File.ReadAllText(deps);
File.Move(deps, deps + ".held");
try
{
    Write(new[] { "BEngine.BootstrapPolicyProbe.dll" });
    Throws<FileNotFoundException>(() => Run(), "Missing bootstrap dependency metadata fails clearly");
}
finally { File.Move(deps + ".held", deps); }
Write(new[] { "BEngine.BootstrapPolicyProbe.dll" });
Check(Run("load") == 42, "Bootstrap resolver loads independent policy and its transitive dependency");
Check(AppContext.GetData("BootstrapPolicyInitialized") is true, "Dynamically discovered policy runs its module initializer");
Throws<FileNotFoundException>(() => Assembly.Load("BEngine.BootstrapLateProbe"), "Bootstrap resolver is detached after normal return");
Throws<InvalidOperationException>(() => Run("throw"), "Entry exception is preserved");
Throws<FileNotFoundException>(() => Assembly.Load("BEngine.BootstrapLateProbe"), "Bootstrap resolver is detached after exception");
using (var stream = File.OpenRead(Path.Combine(binaries, "BEngine.EditorWorkbench.Host.dll")))
using (var pe = new PEReader(stream))
{
    var reader = pe.GetMetadataReader();
    Check(!reader.AssemblyReferences.Any(h => reader.GetString(reader.GetAssemblyReference(h).Name).StartsWith("BEngine.Application", StringComparison.Ordinal)),
        "Published workbench has no Application assembly reference");
}
var publishedDescriptor = JsonDocument.Parse(File.ReadAllText(Path.Combine(binaries, "editor.application.json")));
Check(publishedDescriptor.RootElement.GetProperty("bootstrapAssemblies").EnumerateArray().Any(item => item.GetString() == "BEngine.Application.Editor.dll"),
    "Published descriptor declares framework policy through tooling metadata");
var declaredBootstraps = publishedDescriptor.RootElement.GetProperty("bootstrapAssemblies").EnumerateArray().Select(item => item.GetString()!).ToArray();
Check(declaredBootstraps.All(name => File.Exists(Path.Combine(binaries, name)) && File.Exists(Path.Combine(binaries, Path.ChangeExtension(name, ".deps.json")))) ,
    "All declared bootstrap assemblies and dependency metadata survive host publication");
var missingRuntimeAssets = new List<string>();
foreach (var name in declaredBootstraps)
{
    using var dependencyDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(binaries, Path.ChangeExtension(name, ".deps.json"))));
    var dependencyRoot = dependencyDocument.RootElement;
    var target = dependencyRoot.GetProperty("targets").GetProperty(dependencyRoot.GetProperty("runtimeTarget").GetProperty("name").GetString()!);
    foreach (var library in target.EnumerateObject())
        if (library.Value.TryGetProperty("runtime", out var runtimeAssets))
            foreach (var asset in runtimeAssets.EnumerateObject())
                if (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(binaries, Path.GetFileName(asset.Name))))
                    missingRuntimeAssets.Add(asset.Name);
}
Check(missingRuntimeAssets.Count == 0, "All bootstrap runtime dependencies survive incremental host cleanup: " + string.Join(", ", missingRuntimeAssets));
using (var workbenchDeps = JsonDocument.Parse(File.ReadAllText(Path.Combine(binaries, "BEngine.EditorWorkbench.Host.deps.json"))))
    Check(!workbenchDeps.RootElement.GetProperty("libraries").EnumerateObject().Any(item => item.Name.StartsWith("BEngine.Application/", StringComparison.Ordinal) || item.Name.StartsWith("BEngine.Application.Editor/", StringComparison.Ordinal)),
        "Workbench publication dependency closure excludes the game framework");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_BOOTSTRAP_BOUNDARY_OK " + checks.Count);
