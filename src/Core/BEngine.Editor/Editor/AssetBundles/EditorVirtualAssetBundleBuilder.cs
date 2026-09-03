using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Documents;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;
using ProjectAssetMetaDocument = BEngine.ProjectSystem.Editor.AssetMetaDocument;

namespace BEngine.Editor;

public static class EditorVirtualAssetBundleBuilder
{
    public static VirtualAssetBundleSnapshot Build(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetDatabase);
        using var releaseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new List<VirtualAssetBundleEntry>();
        foreach (var record in assetDatabase.assets.Where(IsRuntimeAsset)
                     .OrderBy(record => record.AssetPath, StringComparer.Ordinal)
                     .ThenBy(record => record.LocalIdentifier))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var meta = File.Exists(record.MetaPath)
                ? YamlUtility.Load<ProjectAssetMetaDocument>(record.MetaPath)
                : new ProjectAssetMetaDocument();
            var ownerGuid = record.ParentGuid ?? record.Guid;
            var localIdentifier = record.ParentGuid.HasValue ? record.LocalIdentifier : 0;
            var address = localIdentifier == 0
                ? record.AssetPath.Replace('\\', '/')
                : $"guid:{ownerGuid:N}#subasset={localIdentifier}";
            var entry = localIdentifier == 0
                ? address
                : $"Assets/__BEngineSubAssets/{ownerGuid:N}/{localIdentifier}" +
                  Path.GetExtension(record.ArtifactPath).ToLowerInvariant();
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
                meta.Settings));
            releaseHash.AppendData(Encoding.UTF8.GetBytes(
                $"{record.AssetPath}\0{record.ArtifactHash}\0{record.ArtifactSize}\0"));
        }
        var releaseInputs = RuntimeManagedCodeReleaseInputCollector.Collect(
            workspace, EditorInstanceContext.current?.scriptAssembliesPath);
        CapturePackageResources(releaseInputs.PackageResources, entries, releaseHash, cancellationToken);
        CaptureManagedCode(releaseInputs.Assemblies, entries, releaseHash, cancellationToken);
        if (entries.Count == 0)
            entries.Add(VirtualAssetBundleEntry.FromMemory(
                "Assets/__BEngine/Virtual/empty.bytes", [], "VirtualMarker"));
        var releaseId = "editor-" +
                        Convert.ToHexString(releaseHash.GetHashAndReset()).ToLowerInvariant()[..24];
        return new VirtualAssetBundleSnapshot(NormalizePackageName(workspace.Project.Name), releaseId, entries);
    }

    private static void CaptureManagedCode(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> inputs,
        ICollection<VirtualAssetBundleEntry> entries,
        IncrementalHash releaseHash,
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
                releaseHash);
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
                importer: "BEnginePackageResource"));
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
        IncrementalHash releaseHash)
    {
        var assemblyAddress = $"Assets/__BEngine/HotUpdate/{name}.dll";
        var symbolsPath = Path.ChangeExtension(path, ".pdb");
        var symbolsAddress = File.Exists(symbolsPath)
            ? $"Assets/__BEngine/HotUpdate/{name}.pdb"
            : null;
        entries.Add(VirtualAssetBundleEntry.FromFile(
            assemblyAddress, path, "ManagedAssembly", bundle: "virtual-hotupdate",
            importer: "BEngineHotUpdate"));
        if (symbolsAddress is not null)
            entries.Add(VirtualAssetBundleEntry.FromFile(
                symbolsAddress, symbolsPath, "ManagedSymbols", bundle: "virtual-hotupdate",
                importer: "BEngineHotUpdate"));
        manifest.Modules.Add(new ManagedCodeModuleManifest
        {
            Name = name,
            BuildId = buildId,
            AssemblyAddress = assemblyAddress,
            SymbolsAddress = symbolsAddress,
            Dependencies = dependencies.ToList()
        });
        releaseHash.AppendData(Encoding.UTF8.GetBytes($"{name}\0{buildId}\0"));
        releaseHash.AppendData(Convert.FromHexString(ComputeFileHash(path)));
    }

    private static bool IsRuntimeAsset(ProjectAssetRecord record)
    {
        if (record.IsDirectory) return false;
        var path = record.AssetPath.Replace('\\', '/');
        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase)) return false;
        return !path.Split('/').Skip(1).Any(segment =>
            segment.Equals("Editor", StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeManifestId(ManagedCodeReleaseManifest manifest)
    {
        var text = string.Join("\n", manifest.Modules.Select(module => $"{module.Name}:{module.BuildId}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..24];
    }

    private static string NormalizePackageName(string value)
    {
        var normalized = new string(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-').ToArray())
            .Trim('-', '.', '_');
        return (string.IsNullOrWhiteSpace(normalized)
            ? "editor"
            : normalized[..Math.Min(128, normalized.Length)]).ToLowerInvariant();
    }
}
