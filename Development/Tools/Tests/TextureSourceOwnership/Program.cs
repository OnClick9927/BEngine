using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
DotNetPlatformServices.Initialize();
RuntimeHelpers.RunModuleConstructor(typeof(Texture).Module.ModuleHandle);
var files = RuntimeServices.GetRequiredService<FileUtility>();
var paths = RuntimeServices.GetRequiredService<PathUtility>();
var platform = RuntimeServices.GetRequiredService<PlatformUtility>();
var reader = new TextureSourceReader(files, paths, platform);
Check(!typeof(TextureSourceReader).Assembly.GetReferencedAssemblies().Any(a => a.Name is "BEngine.TiledMap" or "BEngine.Rendering2D" or "BEngine.UIElements"), "Texture inputs require no scene, tile or UI package");
Throws<ArgumentNullException>(() => new TextureSourceReader(null!, paths, platform), "Null filesystem rejected");
Throws<ArgumentNullException>(() => new TextureSourceReader(files, null!, platform), "Null path service rejected");
Throws<ArgumentNullException>(() => new TextureSourceReader(files, paths, null!), "Null platform rejected");
Check(!reader.TryRead(" ", out var blank) && blank.Length == 0, "Blank source remains empty");
Check(!reader.TryRead("\0", out _), "Invalid physical source is declined");
Check(!reader.TryRead(Path.Combine(output,"absent.png"), out _), "Missing physical source is declined");
var rgba = new byte[] { 1,2,3,255, 4,5,6,255, 7,8,9,255, 10,11,12,255 };
var texture = Texture.FromRgba32(2,2,rgba); var png = texture.EncodeToPNG(); BObject.DestroyImmediate(texture);
var physical = Path.Combine(output,"image.png"); File.WriteAllBytes(physical,png);
Check(reader.TryRead(physical,out var absolute) && absolute.SequenceEqual(png), "Absolute source returns its original encoded bytes");
var empty = Path.Combine(output,"empty.png"); File.WriteAllBytes(empty,[]);
Check(reader.TryRead(empty,out var emptyBytes) && emptyBytes.Length == 0, "Empty physical files retain the previous read contract");
var previousDirectory = Environment.CurrentDirectory;
var dataPathProperty = typeof(Application).GetProperty(nameof(Application.dataPath))!;
var previousDataPath = Application.dataPath;
try
{
    var cwd = Path.Combine(output,"Working"); Directory.CreateDirectory(cwd); Environment.CurrentDirectory = cwd;
    File.WriteAllBytes("current.png",png);
    Check(reader.TryRead("current.png",out var current) && current.SequenceEqual(png), "Relative current-directory images remain available");
    var assets = Path.Combine(output,"Project/Assets"); Directory.CreateDirectory(assets);
    dataPathProperty.SetValue(null,assets); // Emulate the platform/editor host's path setup.
    File.WriteAllBytes(Path.Combine(assets,"asset.png"),png);
    Check(reader.TryRead("Assets/asset.png",out var asset) && asset.SequenceEqual(png), "Project Assets addresses use the host data path");
    Check(reader.TryRead("asset.png",out var data) && data.SequenceEqual(png), "Unprefixed data-directory images remain available");
    File.WriteAllBytes(Path.Combine(cwd,"asset.png"),[42]);
    Check(reader.TryRead("asset.png",out var priority) && priority.SequenceEqual(new byte[]{42}), "Current-directory precedence is preserved");
    var provider = new SourceProvider(png); Resources.RegisterResourceProvider(provider);
    try { Check(reader.TryRead("logical-image",out var resource) && resource.SequenceEqual(png) && !ReferenceEquals(resource,png), "Virtual image bytes retain the resource loader's defensive copy"); }
    finally { Resources.UnregisterResourceProvider(provider); }
}
finally { Environment.CurrentDirectory = previousDirectory; dataPathProperty.SetValue(null,previousDataPath); }
var decoded = TextureImageData.Decode(png); var spanDecoded = TextureImageData.Decode(png.AsSpan());
Check(decoded.Width == 2 && decoded.Height == 2 && decoded.DecodeRgba().SequenceEqual(rgba), "Encoded images decode to exact RGBA bytes");
Check(spanDecoded.DecodeRgba().SequenceEqual(rgba), "Borrowed encoded bytes follow the same decoder");
Check(decoded.HasOpaqueAlpha(), "All opaque base-level texels are proven opaque");
var mip = new byte[] { 1,2,3,254 };
var chain = new TextureImageData(2,2,TextureCompressionFormat.Rgba32,new byte[][]{rgba,mip});
var container = chain.Serialize();
Check(TextureImageData.Decode(container).MipLevels.Count == 2 && !TextureImageData.Decode(container).HasOpaqueAlpha(), "Transparent lower mips prevent opaque classification");
Check(!TextureImageData.Decode(container.AsSpan()).HasOpaqueAlpha(), "Container span decoding preserves lower-mip alpha");
mip[3] = 255;
Check(!chain.HasOpaqueAlpha(), "Public texture construction retains its defensive buffer copy");
chain.MipLevels[1][3] = 255;
Check(chain.HasOpaqueAlpha(), "Opacity is recomputed after exposed pixel buffers change");
chain.MipLevels[0][3] = 0;
Check(!chain.HasOpaqueAlpha(), "A transparent base texel remains visible to the proof");
var block = new TextureImageData(4,4,TextureCompressionFormat.Bc1,new byte[][]{new byte[8]});
Check(!block.HasOpaqueAlpha(), "Compressed block data retains conservative blending");
Throws<ArgumentNullException>(() => TextureImageData.Decode((byte[])null!), "Null encoded arrays are rejected");
Throws<InvalidDataException>(() => TextureImageData.Decode(Array.Empty<byte>()), "Empty image payloads are rejected");
Throws<InvalidDataException>(() => TextureImageData.Decode(new byte[]{1,2,3}), "Unsupported encoded images are rejected");
var damaged = container.ToArray(); damaged[^1] ^= 1;
Throws<InvalidDataException>(() => TextureImageData.Decode(damaged), "Container checksum validation remains mandatory");
var retained = TextureImageData.Decode(container); Array.Clear(container);
Check(retained.Width == 2 && retained.MipLevels[0][0] == 1, "Decoded container pixels never borrow mutable encoded input");
var warmBytes = Probe.MeasureAlpha(decoded);
Check(warmBytes == 0, "10000 warmed opacity queries allocate no memory");
var collected = Probe.VerifyCallbackRetirement(Path.GetFullPath(args[1]), reader, physical, png);
for (var pass = 0; pass < 20 && collected.IsAlive; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Yield(); }
Check(!collected.IsAlive, "An unindexed collectible import callback is released before owner unload");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Rendering2D" or "BEngine.TiledMap" or "BEngine.IMGUI.Editor" or "BEngine.UIElements"), "Standalone texture work loads no scene renderer, tile or GUI implementation");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new { Passed=true, Checks=checks.Count, WarmOpacityQueries=10000, WarmOpacityAllocatedBytes=warmBytes, Details=checks },new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("TEXTURE_SOURCE_CONTRACTS_OK|"+checks.Count);

sealed class SourceProvider(byte[] bytes) : IResourceProvider
{
    public bool TryLoad(string path,string folderName,out ResourceContent content)
    { content = new(path,bytes); return path == "logical-image"; }
    public IEnumerable<string> Enumerate(string path,string folderName) => [];
}
static class Probe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long MeasureAlpha(TextureImageData image)
    {
        for(var i=0;i<10000;i++) if(!image.HasOpaqueAlpha())throw new Exception("Wrong opacity");
        var start=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10000;i++) if(!image.HasOpaqueAlpha())throw new Exception("Wrong opacity");
        return GC.GetAllocatedBytesForCurrentThread()-start;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference VerifyCallbackRetirement(string driver,TextureSourceReader reader,string path,byte[] png)
    {
        var resolver=typeof(Texture).Assembly.GetType("BEngine.Rendering.TextureImportResolver")!.GetProperty("Resolve",BindingFlags.Static|BindingFlags.NonPublic)!;
        var context=new AssemblyLoadContext("Texture callback fixture",isCollectible:true);
        var assembly=context.LoadFromAssemblyPath(driver);
        var callback=assembly.GetType("Resolver")!.GetMethod("Empty")!.MakeGenericMethod(resolver.PropertyType.GenericTypeArguments[1]).CreateDelegate(resolver.PropertyType);
        resolver.SetValue(null,callback);
        if(!reader.TryRead(path,out var bytes)||!bytes.SequenceEqual(png))throw new Exception("Callback changed physical fallback");
        RuntimeAssemblyCleanup.UnregisterExtensions(assembly);
        if(resolver.GetValue(null) is not null)throw new Exception("Graphics retained an import callback");
        var weak=new WeakReference(context);context.Unload();return weak;
    }
}
