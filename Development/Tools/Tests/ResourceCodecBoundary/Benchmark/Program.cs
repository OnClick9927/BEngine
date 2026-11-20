using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.AssetBundles;
using BEngine.Platform.DotNet;

DotNetPlatformServices.Initialize();
RuntimeHelpers.RunModuleConstructor(typeof(Texture).Module.ModuleHandle);
var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
var entry = VirtualAssetBundleEntry.FromMemory("Assets/Resources/icon.png", bytes, "Texture", importer: "TextureImporter");
var snapshot = new VirtualAssetBundleSnapshot("benchmark", "v1", [entry]);
using var manager = new VirtualAssetBundleManager(snapshot);
manager.InitializeAsync().GetAwaiter().GetResult();
(double Milliseconds, long AllocatedBytes) Sample()
{
    const int iterations = 5000;
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    for (var i = 0; i < iterations; i++)
    {
        var value = AssetBundleAssetLoader.LoadAsset(manager, entry.Address, typeof(Texture));
        if (value is not Texture { width: 1, height: 1 }) throw new Exception("Invalid texture.");
        BObject.DestroyImmediate(value);
    }
    return (Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
}
_ = Sample(); _ = Sample();
var samples = Enumerable.Range(0, 7).Select(_ => Sample()).ToArray();
var synchronousReads = 0; var asynchronousReads = 0; var releases = 0;
using var bridge = VirtualAssetBundleManager.CreateSnapshotBridge(AssetBundleCatalogSerializer.SerializeCatalog(snapshot.Catalog),
    AssetBundleCatalogSerializer.SerializeVersion(snapshot.Version), _ => { synchronousReads++; return bytes; },
    (_, _) => { asynchronousReads++; return BValueTask.FromResult((bytes, (Action)(() => releases++))); });
bridge.InitializeAsync().GetAwaiter().GetResult();
var operation = AssetBundleAssetLoader.LoadAssetAsync(bridge, entry.Address, typeof(Texture), new());
while (!operation.IsCompleted) BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(1));
var loaded = operation.GetAwaiter().GetResult();
if (loaded is not Texture { width: 1, height: 1 }) throw new Exception("Invalid async texture.");
BObject.DestroyImmediate(loaded);
var result = new { Iterations = 5000, Samples = samples.Select(x => new { x.Milliseconds, x.AllocatedBytes }),
    MedianMilliseconds = samples.Select(x => x.Milliseconds).Order().ElementAt(3),
    MedianAllocatedBytes = samples.Select(x => x.AllocatedBytes).Order().ElementAt(3), synchronousReads, asynchronousReads, releases };
File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
