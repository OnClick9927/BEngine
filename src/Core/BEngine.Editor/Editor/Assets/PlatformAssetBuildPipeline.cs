using System.Security.Cryptography;
using BEngine.Build;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>Target defaults are supplied by platform assemblies; explicit asset overrides win.</summary>
public sealed record PlatformAssetImportContext(string SourcePath, string AssetType, string Importer,
    BuildTargetPlatform Platform, long SourceBytes, IReadOnlyDictionary<string, string> AuthoredSettings,
    IDictionary<string, string> Settings);

public static class PlatformAssetBuildPipeline
{
    private static readonly Dictionary<BuildTargetPlatform, Action<PlatformAssetImportContext>> Policies = [];
    private static readonly Dictionary<string, List<RuntimeCompiler>> RuntimeCompilers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static void Register(BuildTargetPlatform platform, Action<PlatformAssetImportContext> policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        lock (Gate) Policies[platform] = policy;
    }

    /// <summary>Registers a pure runtime payload compiler without changing authoring or imported artifacts.</summary>
    public static IDisposable RegisterRuntimeCompiler(string extension, string assetType, string format,
        int maximumInputBytes, Func<ReadOnlyMemory<byte>, CancellationToken, byte[]> compile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumInputBytes);
        ArgumentNullException.ThrowIfNull(compile);
        if (!extension.StartsWith('.')) extension = "." + extension;
        var registration = new RuntimeCompiler(extension, assetType, format, maximumInputBytes, compile);
        lock (Gate)
        {
            if (!RuntimeCompilers.TryGetValue(extension, out var compilers)) RuntimeCompilers.Add(extension, compilers = []);
            foreach (var previous in compilers.Where(value => value.Owner == registration.Owner && value.AssetType == assetType).ToArray()) previous.Dispose();
            if (!RuntimeCompilers.TryGetValue(extension, out compilers)) RuntimeCompilers.Add(extension, compilers = []);
            compilers.Add(registration);
        }
        return registration;
    }

    internal static bool TryCompileRuntimeArtifact(string sourcePath, string assetType, string artifactPath,
        string artifactHash, byte[]? preparedContent, IReadOnlyDictionary<string, string> authored,
        FileUtility files, CancellationToken cancellation, out byte[] content,
        out IReadOnlyDictionary<string, string> effectiveSettings)
    {
        content = []; effectiveSettings = authored;
        var extension = Path.GetExtension(sourcePath);
        RuntimeCompiler? compiler;
        lock (Gate) compiler = RuntimeCompilers.TryGetValue(extension, out var compilers)
            ? compilers.LastOrDefault(value => value.AssetType == assetType) : null;
        if (compiler is null && assetType == "Scene" && sourcePath.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            var sceneLength = preparedContent?.LongLength ?? files.GetFileLength(artifactPath);
            if (sceneLength <= 0 || sceneLength > BinaryDataSerializer.MaximumDocumentBytes)
                throw new InvalidDataException($"Runtime Scene compiler input exceeds its size limit: {sourcePath}");
            var sceneInput = preparedContent ?? files.ReadAllBytes(artifactPath);
            if (sceneInput.LongLength != sceneLength || preparedContent is null &&
                !Convert.ToHexString(SHA256.HashData(sceneInput)).Equals(artifactHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Scene artifact changed after the build snapshot: {sourcePath}");
            content = SceneRuntimeDataSerializer.CompileRuntimeBytes(sceneInput, cancellation);
            var sceneSettings = new Dictionary<string, string>(authored, StringComparer.Ordinal)
            { ["builtSceneFormat"] = BinaryDataSerializer.IsBinaryData(content) ? "binary-v1" : "yaml-v2" };
            effectiveSettings = sceneSettings;
            return true;
        }
        if (compiler is null || compiler.AssetType != assetType) return false;
        cancellation.ThrowIfCancellationRequested();
        var length = preparedContent?.LongLength ?? files.GetFileLength(artifactPath);
        if (length <= 0 || length > compiler.MaximumInputBytes)
            throw new InvalidDataException($"Runtime compiler input exceeds its size limit: {sourcePath}");
        var input = preparedContent ?? files.ReadAllBytes(artifactPath);
        if (input.LongLength != length || preparedContent is null &&
            !Convert.ToHexString(SHA256.HashData(input)).Equals(artifactHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset artifact changed after the build snapshot: {sourcePath}");
        content = compiler.Compile(input, cancellation) ?? throw new InvalidDataException("Runtime compiler returned no payload.");
        cancellation.ThrowIfCancellationRequested();
        lock (Gate)
            if (!RuntimeCompilers.TryGetValue(extension, out var current) ||
                !ReferenceEquals(current.LastOrDefault(value => value.AssetType == assetType), compiler))
                throw new InvalidOperationException("The runtime asset compiler changed during the build.");
        var settings = new Dictionary<string, string>(authored, StringComparer.Ordinal) { ["builtRuntimeFormat"] = compiler.Format };
        effectiveSettings = settings;
        return true;
    }

    private sealed class RuntimeCompiler : IDisposable
    {
        internal readonly string Extension, AssetType, Format;
        internal readonly int MaximumInputBytes;
        internal readonly Func<ReadOnlyMemory<byte>, CancellationToken, byte[]> Compile;
        internal readonly System.Reflection.Assembly Owner;
        private readonly System.Runtime.Loader.AssemblyLoadContext? _context;
        internal RuntimeCompiler(string extension, string assetType, string format, int maximumInputBytes,
            Func<ReadOnlyMemory<byte>, CancellationToken, byte[]> compile)
        {
            Extension = extension; AssetType = assetType; Format = format; MaximumInputBytes = maximumInputBytes; Compile = compile;
            Owner = compile.Method.Module.Assembly;
            _context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(Owner);
            if (_context?.IsCollectible == true) _context.Unloading += Unloading;
        }
        private void Unloading(System.Runtime.Loader.AssemblyLoadContext context) => Dispose();
        public void Dispose()
        {
            if (_context?.IsCollectible == true) _context.Unloading -= Unloading;
            lock (Gate)
                if (RuntimeCompilers.TryGetValue(Extension, out var compilers))
                {
                    compilers.Remove(this);
                    if (compilers.Count == 0) RuntimeCompilers.Remove(Extension);
                }
        }
    }

    /// <summary>Builds an isolated target artifact without changing source metadata or editor artifacts.</summary>
    public static bool TryBuildArtifact(string sourcePath, string sourceHash, string artifactPath,
        string assetType, string importerName, IReadOnlyDictionary<string, string> authored,
        BuildTargetPlatform platform, FileUtility files, PathUtility paths, CancellationToken cancellation,
        out byte[] content, out IReadOnlyDictionary<string, string> effectiveSettings)
    {
        content = []; effectiveSettings = authored;
        if (!Enum.IsDefined(platform)) throw new ArgumentOutOfRangeException(nameof(platform));
        var settings = new Dictionary<string, string>(authored, StringComparer.Ordinal);
        Action<PlatformAssetImportContext>? policy;
        lock (Gate) Policies.TryGetValue(platform, out policy);
        policy?.Invoke(new(sourcePath, assetType, importerName, platform, files.GetFileLength(sourcePath), authored, settings));
        var prefix = $"platform.{platform}.";
        if (authored.TryGetValue(prefix + "overridden", out var enabled) && bool.TryParse(enabled, out var overridden) && overridden)
            foreach (var (key, value) in authored)
                if (key.StartsWith(prefix, StringComparison.Ordinal) && key != prefix + "overridden")
                    settings[key[prefix.Length..] == "format" ? "compressionFormat" : key[prefix.Length..]] = value;
        // Plain data can keep YAML authoring/editor artifacts while shipping a compact runtime
        // graph. No game assemblies or CLR type names are needed during this build conversion.
        if (settings.TryGetValue("runtimeDataFormat", out var dataFormat) && dataFormat == "binary")
        {
            if (assetType != "YamlAsset" || importerName != nameof(DefaultImporter))
                throw new InvalidDataException("runtimeDataFormat: binary requires a plain YamlAsset with DefaultImporter.");
            cancellation.ThrowIfCancellationRequested();
            if (files.GetFileLength(sourcePath) is <= 0 or > BinaryDataSerializer.MaximumDocumentBytes)
                throw new InvalidDataException("Compiled YAML input must contain 1 byte to 64 MiB.");
            var source = files.ReadAllBytes(sourcePath);
            if (!Convert.ToHexString(SHA256.HashData(source)).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Data source changed after the build snapshot: {sourcePath}");
            content = BinaryDataSerializer.CompileYaml(source, cancellation);
            settings["builtDataFormat"] = "binary-v1";
            settings["builtForPlatform"] = platform.ToString();
            effectiveSettings = settings;
            return true;
        }
        if (settings.Count == authored.Count && settings.All(pair => authored.TryGetValue(pair.Key, out var value) && value == pair.Value))
            return false;
        cancellation.ThrowIfCancellationRequested();
        using (var source = files.OpenRead(sourcePath))
            if (!Convert.ToHexString(SHA256.HashData(source)).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Asset source changed after the build snapshot: {sourcePath}");
        settings["builtForPlatform"] = platform.ToString();
        var importer = AssetImporter.CreateForImport(sourcePath, sourcePath, settings);
        if (importer.GetType().Name != importerName)
            throw new InvalidDataException($"Platform importer '{importerName}' is not registered for '{sourcePath}'.");
        // AssetImportContext owns and removes the uncommitted temporary file.
        using var context = new AssetImportContext(sourcePath, sourcePath, artifactPath, settings,
            cancellation, files, paths);
        importer.Import(context);
        cancellation.ThrowIfCancellationRequested();
        content = files.ReadAllBytes(context.artifactPath);
        effectiveSettings = settings;
        return true;
    }
}
