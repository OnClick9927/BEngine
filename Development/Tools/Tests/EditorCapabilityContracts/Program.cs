using System.Reflection;
using System.Text;
using System.Text.Json;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;

var checks = new List<string>();
void Check(string name, bool ok) { if (!ok) throw new Exception(name); checks.Add(name); }
void Throws<T>(string name, Action action) where T : Exception
{
    try { action(); } catch (T) { Check(name, true); return; }
    throw new Exception(name);
}
BEngine.Platform.DotNet.DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
var fallbackPaths = EditorServices.GetRequiredService<PathUtility>();
Check("Optional missing service returns null", EditorServices.GetService<Missing>() is null);
Throws<InvalidOperationException>("Required missing service fails clearly", () => EditorServices.GetRequiredService<Missing>());
Throws<ArgumentNullException>("Null host rejected", () => EditorServiceScope.Attach(null!));

var pathService = DispatchProxy.Create<IPathService, PathProbe>();
var pathProbe = (PathProbe)(object)pathService;
var paths = new PathUtility(pathService);
var fileService = DispatchProxy.Create<IFileSystem, FileProbe>();
var files = (FileProbe)(object)fileService;
var fileUtility = new FileUtility(fileService);
foreach (var (path, extension, expected) in new[] {
    ("virtual.xyz", "bin", "virtual.bin"), ("virtual.xyz", ".bin", "virtual.bin"),
    ("virtual.xyz", "", "virtual"), ("plain", "bin", "plain.bin"),
    ("dir.name/plain", ".bin", "dir.name/plain.bin"), ("archive.tar.gz", "br", "archive.tar.br") })
    Check("Change extension: " + path + " -> " + extension, paths.ChangeExtension(path, extension) == expected);
Check("Extension query delegates to platform", paths.HasExtension("virtual.xyz") && pathProbe.ExtensionCalls >= 7);
Check("No extension remains false", !paths.HasExtension("plain"));
Throws<ArgumentNullException>("Null path rejected", () => paths.ChangeExtension(null!, "x"));
Throws<ArgumentNullException>("Null extension rejected", () => paths.ChangeExtension("file", null!));
fileUtility.AppendAllText("shader.log", "着色器");
fileUtility.AppendAllText("shader.log", "\ncompile✓");
Check("Append preserves existing Unicode text", Encoding.UTF8.GetString(files.Data) == "着色器\ncompile✓");
Check("Append uses no BOM", !files.Data.AsSpan().StartsWith(Encoding.UTF8.Preamble));
Check("Append disposes both streams", files.Disposals == 2);
Check("Append uses sequential mode and read sharing", files.ValidOpenOptions);
files.FailOpen = true;
Throws<IOException>("Append failures propagate", () => fileUtility.AppendAllText("shader.log", "bad"));
Check("Failed open preserves prior contents", Encoding.UTF8.GetString(files.Data) == "着色器\ncompile✓");
files.FailOpen = false;

var platformA = DispatchProxy.Create<IPlatformService, PlatformProbe>();
((PlatformProbe)(object)platformA).Name = "host-A";
var platformB = DispatchProxy.Create<IPlatformService, PlatformProbe>();
((PlatformProbe)(object)platformB).Name = "host-B";
using var hostA = new ServiceCollection().AddSingleton(paths).AddSingleton(fileUtility)
    .AddSingleton(new PlatformUtility(platformA)).AddSingleton<Marker>().BuildServiceProvider();
using var hostB = new ServiceCollection().AddSingleton(new PlatformUtility(platformB))
    .AddSingleton<Marker>().BuildServiceProvider();
var scopeA = EditorServiceScope.Attach(hostA);
var markerA = hostA.GetRequiredService<Marker>();
Check("Host service identity retained", ReferenceEquals(EditorServices.GetRequiredService<Marker>(), markerA));
Check("Optional and required lookup agree", ReferenceEquals(EditorServices.GetService<Marker>(), markerA));
Check("Platform capability follows active host", EditorApplication.applicationContentsPath == "host-A");
Check("Package facade uses host process path", EditorApplication.applicationPath == "host-A.exe");
Check("Injected path utility is used", ReferenceEquals(EditorServices.GetRequiredService<PathUtility>(), paths));
RuntimeServiceContributions.Register(static services => services.AddSingleton<LateOwned>());
var ownedA = EditorServices.GetRequiredService<LateOwned>();
var scopeB = EditorServiceScope.Attach(hostB);
var markerB = hostB.GetRequiredService<Marker>();
Check("Nested scope overrides active host", ReferenceEquals(EditorServices.GetRequiredService<Marker>(), markerB));
Check("Package facade follows nested host", EditorApplication.applicationContentsPath == "host-B");
var ownedB = EditorServices.GetRequiredService<LateOwned>();
Check("Host replacement disposes old contributions", ownedA.Disposals == 1);
scopeA.Dispose();
Check("Out-of-order scope disposal preserves active host", ReferenceEquals(EditorServices.GetRequiredService<Marker>(), markerB));
Check("Scope does not own provider", markerA.Disposals == 0 && markerB.Disposals == 0);
scopeB.Dispose(); scopeB.Dispose();
Check("Detach releases contributions exactly once", ownedB.Disposals == 1);
Check("Detached host unavailable", EditorServices.GetService<Marker>() is null);
Check("Fallback path capability restored after contribution invalidation",
    EditorServices.GetRequiredService<PathUtility>().GetFullPath(".") == fallbackPaths.GetFullPath("."));
using (EditorServiceScope.Attach(hostA))
{
    Check("Disposed scope can be replaced with fresh scope", EditorApplication.applicationContentsPath == "host-A");
    var first = EditorServices.GetRequiredService<LateOwned>();
    RuntimeServiceContributions.UnregisterAssembly(typeof(LateOwned).Assembly);
    Check("Unloaded service disappears", EditorServices.GetService<LateOwned>() is null);
    Check("Unload releases contributed service", first.Disposals == 1);
    Check("Unload preserves host services", ReferenceEquals(EditorServices.GetRequiredService<Marker>(), markerA));
}
hostA.Dispose(); hostB.Dispose();
Check("Host providers release their own instances exactly once", markerA.Disposals == 1 && markerB.Disposals == 1);
var result = new { Passed = true, Checks = checks.Count, Details = checks };
File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_CAPABILITY_CONTRACTS_OK|" + checks.Count);

public sealed class Missing;
public sealed class Marker : IDisposable { public int Disposals; public void Dispose() => Disposals++; }
public sealed class LateOwned : IDisposable { public int Disposals; public void Dispose() => Disposals++; }
public class PlatformProbe : DispatchProxy
{
    public string Name = "";
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_BaseDirectory" => Name,
        "get_ProcessPath" => Name + ".exe",
        _ => throw new NotSupportedException(method.Name)
    };
}
public class PathProbe : DispatchProxy
{
    public int ExtensionCalls;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name != nameof(IPathService.GetExtension)) throw new NotSupportedException(method.Name);
        ExtensionCalls++;
        return Path.GetExtension((string)args![0]!);
    }
}
public class FileProbe : DispatchProxy
{
    public byte[] Data = [];
    public int Disposals;
    public bool ValidOpenOptions = true, FailOpen;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name != nameof(IFileSystem.OpenFile)) throw new NotSupportedException(method.Name);
        if (FailOpen) throw new IOException("Injected open failure");
        ValidOpenOptions &= (string)args![0]! == "shader.log" && (FileMode)args[1]! == FileMode.Append &&
            (FileAccess)args[2]! == FileAccess.Write && (FileShare)args[3]! == FileShare.Read &&
            (int)args[4]! == 4096 && (FileOptions)args[5]! == FileOptions.SequentialScan;
        var stream = new SaveStream(bytes => { Data = bytes; Disposals++; });
        stream.Write(Data);
        return stream;
    }
    private sealed class SaveStream(Action<byte[]> save) : MemoryStream
    {
        private bool _saved;
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_saved) { _saved = true; save(ToArray()); }
            base.Dispose(disposing);
        }
    }
}
