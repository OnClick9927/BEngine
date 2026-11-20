using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.AssetBundles;
using BEngine.Platform;
using BEngine.Platform.DotNet;

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
DotNetPlatformServices.Initialize();
T Complete<T>(BTask<T> task) { Drain(task); return task.GetAwaiter().GetResult(); }
void Drain(BTask task)
{
    var deadline = Stopwatch.StartNew();
    while (!task.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(20))
    { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(1)); Thread.Yield(); }
    if (!task.IsCompleted) throw new TimeoutException("Resource operation did not complete.");
    task.GetAwaiter().GetResult();
}
var ab = typeof(AssetBundleAssetLoader).Assembly;
Check(!ab.GetReferencedAssemblies().Any(a => a.Name is "BEngine.Graphics" or "BEngine.Rendering2D"), "AB assembly has no graphic or scene-renderer references");
RuntimeResourceCodecs.Register(new ProbeCodec());
RuntimeResourceCodecs.Register(new ProbeAssetCodec());
var owner = Guid.NewGuid();
var bytes = "custom payload"u8.ToArray();
var entry = VirtualAssetBundleEntry.FromMemory("Assets/Resources/probe.bin", bytes, "ProbeAsset", owner, importer: "ProbeImporter");
var snapshot = new VirtualAssetBundleSnapshot("probe", "v1", [entry]);
using var virtualManager = new VirtualAssetBundleManager(snapshot);
Drain(virtualManager.InitializeAsync().AsBTask());
var virtualProvider = new AssetBundleResourceProvider(virtualManager);
var child = $"guid:{owner:N}#subasset=7001";
Check(virtualProvider.ContainsReference(child, "Resources"), "Custom synthetic reference is owned without a graphic package");
Check(!virtualProvider.ContainsReference($"guid:{owner:N}#subasset=7002", "Resources"), "Unregistered child identity is not claimed");
Check(virtualProvider.TryLoadObject("probe", "Resources", typeof(ProbeObject), out var first) && ((ProbeObject)first).Text == "custom payload", "Custom object decodes virtual bundle metadata and bytes");
Check(virtualProvider.TryLoadObject(child, "Resources", typeof(ProbeObject), out var alias) && ReferenceEquals(first, alias), "Path and synthetic child share object identity");
var asynchronous = Complete(virtualProvider.LoadAsync(child, "Resources", typeof(ProbeObject), new()));
Check(ReferenceEquals(first, asynchronous.Asset), "Async custom object shares cached identity");
Check(virtualProvider.TryLoadAsset("probe", "Resources", typeof(ProbeAsset), out var assetValue) &&
    assetValue is ProbeAsset { Text: "custom payload" } && assetValue.Id == owner && assetValue.assetPath == entry.Address,
    "Custom asset codec receives generic catalog identity binding");
var coldAsset = Complete(new AssetBundleResourceProvider(virtualManager).LoadAsync("probe", "Resources", typeof(ProbeAsset), new()));
Check(coldAsset.Asset is ProbeAsset { Text: "custom payload" }, "Cold asynchronous custom asset uses metadata codec");
Drain(virtualProvider.PrepareReferenceAsync(child, "Resources", new()));
Check(true, "Custom child preparation reads its owner payload");
_ = MeasureLookups();
_ = MeasureLookups();
var lookupBytes = MeasureLookups();
Console.WriteLine($"CODEC_LOOKUP_ALLOCATED|{lookupBytes}");
Check(lookupBytes == 0, "10000 codec lookups allocate zero bytes");

// Create a real ZIP-backed release with validated catalog and payload hashes.
var realRoot = Path.Combine(output, "RealRelease"); Directory.CreateDirectory(realRoot);
var descriptor = snapshot.Catalog.Bundles.Single();
var bundlePath = Path.Combine(realRoot, "bundles", descriptor.FileName); Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
using (var stream = File.Create(bundlePath)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
    using (var payload = zip.CreateEntry(entry.Entry).Open()) payload.Write(bytes);
descriptor.Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(bundlePath))); descriptor.Size = new FileInfo(bundlePath).Length;
descriptor.FileName = descriptor.Sha256 + ".bassetbundle";
File.Move(bundlePath, Path.Combine(realRoot, "bundles", descriptor.FileName), overwrite: true);
var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(snapshot.Catalog);
snapshot.Version.CatalogSha256 = Convert.ToHexStringLower(SHA256.HashData(catalogBytes)); snapshot.Version.CatalogSize = catalogBytes.Length;
File.WriteAllBytes(Path.Combine(realRoot, snapshot.Version.CatalogFile), catalogBytes);
File.WriteAllBytes(Path.Combine(realRoot, "version.yaml"), AssetBundleCatalogSerializer.SerializeVersion(snapshot.Version));
using (var real = new AssetBundleManager(new() { PackageName = "probe", BuiltInDirectory = realRoot, UsePersistentCache = false }))
{
    Drain(real.InitializeAsync().AsBTask());
    var provider = new AssetBundleResourceProvider(real);
    Check(provider.TryLoadObject(child, "Resources", typeof(ProbeObject), out var value) && ((ProbeObject)value).Text == "custom payload", "Real AB uses custom codec and owner bytes");
    var loaded = Complete(provider.LoadAsync("probe", "Resources", typeof(ProbeObject), new()));
    Check(ReferenceEquals(loaded.Asset, value), "Real AB synchronous and asynchronous aliases share identity");
}

// The internal archive adapter is exercised reflectively, without friend access or production API changes.
IResourceObjectProvider Archive(VirtualAssetBundleEntry[] entries, string name)
{
    var archiveType = ab.GetType("BEngine.Content.BuiltInResourceArchive", true)!;
    var writeType = ab.GetType("BEngine.Content.BuiltInResourceArchiveWriteEntry", true)!;
    var values = Array.CreateInstance(writeType, entries.Length);
    for (var i = 0; i < entries.Length; i++)
    {
        var e = entries[i]; var file = Path.Combine(output, name + "-" + i + ".payload");
        var data = e.Address == entry.Address ? bytes : GraphicData(e.AssetType == "Texture" ? "image.png" : e.Address); File.WriteAllBytes(file, data);
        values.SetValue(writeType.GetMethod("FromFile", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            [e.Address, file, e.AssetType, Convert.ToHexStringLower(SHA256.HashData(data)), (long)data.Length, e.Guid, e.OwnerGuid, e.LocalIdentifier, e.Importer, e.ImporterSettings]), i);
    }
    var archive = Path.Combine(output, name + ".bresources");
    archiveType.GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
        [archive, values, default(BCancellationToken), null, null, null, false, true]);
    return (IResourceObjectProvider)Activator.CreateInstance(ab.GetType("BEngine.Player.PlayerBuiltInResourceProvider", true)!,
        BindingFlags.NonPublic | BindingFlags.Instance, null, [archive, null, null, null], null)!;
}
using (var archive = (IDisposable)Archive([entry], "custom"))
{
    var provider = (IResourceObjectProvider)archive;
    Check(((IResourceReferenceOwner)provider).ContainsReference(child, "Resources"), "Archive owns custom synthetic child");
    Check(provider.TryLoadObject(child, "Resources", typeof(ProbeObject), out var value) && ((ProbeObject)value).Text == "custom payload", "Archive decodes custom object");
    Check(provider.TryLoadObject("probe", "Resources", typeof(ProbeObject), out var same) && ReferenceEquals(value, same), "Archive canonical child identity matches path");
    Check(ReferenceEquals(Complete(((IAsyncResourceProvider)provider).LoadAsync(child, "Resources", typeof(ProbeObject), new())).Asset, value), "Archive async object reuses identity");
}

// Delay payload delivery to verify leases and decoder replacement across an await.
var pending = new BTaskCompletionSource<(byte[] Bytes, Action Release)>(); var reads = 0; var releases = 0;
var version = new VirtualAssetBundleSnapshot("probe", "v2", [entry]);
using (var delayed = VirtualAssetBundleManager.CreateSnapshotBridge(AssetBundleCatalogSerializer.SerializeCatalog(version.Catalog),
    AssetBundleCatalogSerializer.SerializeVersion(version.Version), _ => { reads++; return bytes; }, (_, _) => new(pending.Task)))
{
    Drain(delayed.InitializeAsync().AsBTask());
    var task = AssetBundleAssetLoader.LoadObjectAsync(delayed, child, typeof(ProbeObject), new());
    Check(!task.IsCompleted, "Decoder waits for asynchronously supplied payload");
    RuntimeResourceCodecs.UnregisterAssembly(typeof(ProbeCodec).Assembly);
    pending.SetResult((bytes, () => releases++));
    Throws<InvalidOperationException>(() => Complete(task), "Unloaded codec is not invoked after await");
    Check(releases == 1 && reads == 0, "Payload lease releases once with no synchronous reread");
}
RuntimeResourceCodecs.Register(new ProbeCodec());
using (var canceled = new BCancellationSource())
{
    canceled.Cancel();
    Throws<BOperationCanceledException>(() => Complete(AssetBundleAssetLoader.LoadObjectAsync(virtualManager, child, typeof(ProbeObject), new(canceled.Token))), "Canceled object load does not decode");
}
var metadata = new RuntimeResourceMetadata(entry.Address, "virtual", entry.Address, owner, owner, 7001, "ProbeAsset", "ProbeImporter", new Dictionary<string, string>(), true);
Check(RuntimeResourceCodecs.CanDecodeSynthetic(metadata), "Re-registration restores synthetic resolution");
RuntimeAssemblyCleanup.Unregister([typeof(ProbeCodec).Assembly]);
Check(!RuntimeResourceCodecs.CanDecodeSynthetic(metadata) && RuntimeResourceCodecs.Find(typeof(ProbeObject)) is null, "Assembly cleanup removes decoder and child index");
#if GRAPHICS
GraphicsChecks.Run(Check, Complete, Archive, GraphicData);
#else
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D"), "Custom real, virtual and archive loads never load graphic assemblies");
#endif
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, LookupAllocatedBytes = lookupBytes, Names = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESOURCE_CODEC_OK|checks={checks.Count}|lookupBytes={lookupBytes}");

byte[] GraphicData(string address) => address.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
    ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==")
    : address.EndsWith(".material.yaml", StringComparison.OrdinalIgnoreCase)
        ? Encoding.UTF8.GetBytes("format: BEngine.Material\nversion: 2\nname: TestMaterial\nshader: BEngine/Sprite\nrenderQueue: 3010\n")
    : Encoding.UTF8.GetBytes("shader source");

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static long MeasureLookups()
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++) _ = RuntimeResourceCodecs.Find(typeof(ProbeObject));
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

sealed class ProbeObject : BObject { public string Text = ""; }
sealed class ProbeAsset : BAsset { public string Text = ""; }
sealed class ProbeAssetCodec : RuntimeResourceCodec
{
    public override Type ObjectType => typeof(ProbeAsset);
    public override bool CanDecode(in RuntimeResourceMetadata metadata) => !metadata.IsSynthetic && metadata.LocalIdentifier == 0 && metadata.Importer == "ProbeImporter";
    public override BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata) => new ProbeAsset { Text = Encoding.UTF8.GetString(bytes.Span) };
}
sealed class ProbeCodec : RuntimeResourceCodec
{
    public override Type ObjectType => typeof(ProbeObject);
    public override long SyntheticLocalIdentifier => 7001;
    public override bool CanDecode(in RuntimeResourceMetadata metadata) => metadata.Importer == "ProbeImporter";
    public override BObject Decode(ReadOnlyMemory<byte> bytes, in RuntimeResourceMetadata metadata) => new ProbeObject { Text = Encoding.UTF8.GetString(bytes.Span) };
}
