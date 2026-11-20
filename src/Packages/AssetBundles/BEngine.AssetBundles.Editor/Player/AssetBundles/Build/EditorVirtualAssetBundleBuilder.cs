using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Serialization;
using BEngine.Player.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;
using ProjectAssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;

namespace BEngine.Editor.Build;

public sealed class EditorVirtualAssetBundleBuilder
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;

    public EditorVirtualAssetBundleBuilder(FileUtility fileSystem, PathUtility pathService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
    }

    public static VirtualAssetBundleSnapshot Build(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        CancellationToken cancellationToken = default) =>
        EditorCapabilities.GetRequiredService<EditorVirtualAssetBundleBuilder>()
            .BuildSnapshot(workspace, assetDatabase, cancellationToken);

    public VirtualAssetBundleSnapshot BuildSnapshot(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetDatabase);
        var fileSystem = _fileSystem;
        var pathService = _pathService;
        using var releaseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new List<VirtualAssetBundleEntry>();
        var metadata = new Dictionary<string, ProjectAssetMetaDocument>(StringComparer.Ordinal);
        foreach (var record in assetDatabase.assets.Where(IsRuntimeAsset)
                     .OrderBy(record => record.AssetPath, StringComparer.Ordinal)
                     .ThenBy(record => record.LocalIdentifier))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // An imported owner and its generated subassets share the same .meta.
            // Keep this lookup local so the next PlayMode entry sees every edit.
            var metaPath = pathService.GetFullPath(record.MetaPath);
            if (!metadata.TryGetValue(metaPath, out var meta))
            {
                meta = fileSystem.FileExists(metaPath)
                    ? YamlUtility.Deserialize<ProjectAssetMetaDocument>(fileSystem.ReadAllText(metaPath))
                    : new ProjectAssetMetaDocument();
                metadata.Add(metaPath, meta);
            }
            var ownerGuid = record.ParentGuid ?? record.Guid;
            var localIdentifier = record.ParentGuid.HasValue ? record.LocalIdentifier : 0;
            var address = localIdentifier == 0
                ? AssetBundleValidation.NormalizeAddress(record.AssetPath)
                : $"guid:{ownerGuid:N}#subasset={localIdentifier}";
            var entry = localIdentifier == 0
                ? address
                : $"Assets/__BEngineSubAssets/{ownerGuid:N}/{localIdentifier}" +
                  pathService.GetExtension(record.ArtifactPath).ToLowerInvariant();
            entries.Add(VirtualAssetBundleEntry.FromFile(
                address,
                record.ArtifactPath,
                record.AssetType,
                record.Guid,
                "virtual-assets",
                entry,
                ownerGuid,
                localIdentifier,
                meta.Importer ?? string.Empty,
                meta.Settings,
                fileSystem,
                pathService));
            releaseHash.AppendData(Encoding.UTF8.GetBytes(
                $"{record.AssetPath}\0{record.ArtifactHash}\0{record.ArtifactSize}\0"));
        }
        var releaseInputs = RuntimeManagedCodeReleaseInputCollector.Collect(
            workspace,
            EditorInstanceContext.current?.scriptAssembliesPath,
            workspace.ScriptAssembliesPath,
            fileSystem,
            pathService);
        CapturePackageResources(
            releaseInputs.PackageResources, entries, releaseHash, fileSystem, pathService, cancellationToken);
        CaptureManagedCode(
            releaseInputs.Assemblies, entries, releaseHash, fileSystem, pathService, cancellationToken);
        if (entries.Count == 0)
            entries.Add(VirtualAssetBundleEntry.FromMemory(
                "Assets/__BEngine/Virtual/empty.bytes", [], "VirtualMarker"));
        var releaseId = "editor-" +
                        Convert.ToHexStringLower(releaseHash.GetHashAndReset())[..24];
        return new VirtualAssetBundleSnapshot(NormalizePackageName(workspace.Project.Name), releaseId, entries);
    }

    private static void CaptureManagedCode(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> inputs,
        ICollection<VirtualAssetBundleEntry> entries,
        IncrementalHash releaseHash,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        var manifest = new ManagedCodeReleaseManifest();
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddModule(
                input.Name,
                input.BuildId,
                input.AssemblyPath,
                input.Dependencies,
                manifest,
                entries,
                releaseHash,
                fileSystem,
                pathService);
        }
        if (manifest.Modules.Count == 0) return;
        manifest.ReleaseId = "editor-code-" + ComputeManifestId(manifest);
        entries.Add(VirtualAssetBundleEntry.FromMemory(
            ManagedCodeReleaseManifest.DefaultAddress,
            ManagedCodeReleaseManifestSerializer.Serialize(manifest),
            "ManagedCodeReleaseManifest",
            bundle: "virtual-hotupdate",
            importer: "BEngineHotUpdate"));
    }

    private static void CapturePackageResources(
        IEnumerable<RuntimePackageResourceInput> inputs,
        ICollection<VirtualAssetBundleEntry> entries,
        IncrementalHash releaseHash,
        FileUtility fileSystem,
        PathUtility pathService,
        CancellationToken cancellationToken)
    {
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(VirtualAssetBundleEntry.FromFile(
                input.Address,
                input.FilePath,
                "PackageResource",
                bundle: "virtual-packages",
                importer: "BEnginePackageResource",
                fileSystem: fileSystem,
                pathService: pathService));
            releaseHash.AppendData(Encoding.UTF8.GetBytes($"{input.Address}\0{input.Size}\0"));
            releaseHash.AppendData(Convert.FromHexString(input.Sha256));
        }
    }

    private static void AddModule(
        string name,
        string buildId,
        string path,
        IReadOnlyList<string> dependencies,
        ManagedCodeReleaseManifest manifest,
        ICollection<VirtualAssetBundleEntry> entries,
        IncrementalHash releaseHash,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var assemblyAddress = $"Assets/__BEngine/HotUpdate/{name}.dll";
        var assemblyPath = pathService.GetFullPath(path);
        var symbolsPath = ChangeExtension(assemblyPath, ".pdb", pathService);
        var symbolsAddress = fileSystem.FileExists(symbolsPath)
            ? $"Assets/__BEngine/HotUpdate/{name}.pdb"
            : null;
        var assemblyEntry = VirtualAssetBundleEntry.FromFile(
            assemblyAddress, assemblyPath, "ManagedAssembly", bundle: "virtual-hotupdate",
            importer: "BEngineHotUpdate", fileSystem: fileSystem, pathService: pathService);
        entries.Add(assemblyEntry);
        if (symbolsAddress is not null)
            entries.Add(VirtualAssetBundleEntry.FromFile(
                symbolsAddress, symbolsPath, "ManagedSymbols", bundle: "virtual-hotupdate",
                importer: "BEngineHotUpdate", fileSystem: fileSystem, pathService: pathService));
        manifest.Modules.Add(new ManagedCodeModuleManifest
        {
            Name = name,
            BuildId = buildId,
            AssemblyAddress = assemblyAddress,
            SymbolsAddress = symbolsAddress,
            Dependencies = dependencies.ToList()
        });
        releaseHash.AppendData(Encoding.UTF8.GetBytes($"{name}\0{buildId}\0"));
        releaseHash.AppendData(Convert.FromHexString(assemblyEntry.Sha256));
    }

    private static bool IsRuntimeAsset(ProjectAssetRecord record)
    {
        if (record.IsDirectory) return false;
        var assetLocation = record.AssetPath;
        if (!assetLocation.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
            !assetLocation.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !assetLocation.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase)) return false;
        var path = AssetBundleValidation.NormalizeAddress(assetLocation);
        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase)) return false;
        return !PathLexicalRules.ContainsSegment(path.AsSpan(), "Editor", StringComparison.OrdinalIgnoreCase, skipFirst: true);
    }

    private static string ChangeExtension(string path, string extension, PathUtility pathService)
    {
        var directory = pathService.GetDirectoryName(path);
        var fileName = pathService.GetFileNameWithoutExtension(path) + extension;
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : pathService.Combine(directory, fileName);
    }

    private static string ComputeManifestId(ManagedCodeReleaseManifest manifest)
    {
        var text = string.Join("\n", manifest.Modules.Select(module => $"{module.Name}:{module.BuildId}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..24];
    }

    private static string NormalizePackageName(string value)
    {
        var normalized = new string (Gather10701(value)).Trim('-', '.', '_');
        [global::BEngine.SkipLoopGuard]
        char[] Gather10701(global::System.Collections.Generic.IEnumerable<char> items10701)
        {
            ArgumentNullException.ThrowIfNull(items10701, "source");
            char Evaluate10701(char character) => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-';
            global::System.ReadOnlySpan<char> values;
            if (items10701.GetType() == typeof(char[]))
                values = (char[])items10701;
            else if (items10701.GetType() == typeof(global::System.Collections.Generic.List<char>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<char>)items10701);
            else if (items10701 is string text10701)
                values = text10701.AsSpan();
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items10701, Evaluate10701));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<char>();
            var result = new char[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate10701(values[index]);
            return result;
        }
        return (string.IsNullOrWhiteSpace(normalized)
            ? "editor"
            : normalized[..Math.Min(128, normalized.Length)]).ToLowerInvariant();
    }
}
