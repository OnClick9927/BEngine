using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;
using Mono.Cecil;
using System.Text.Json;

namespace BEngine.Editor;

/// <summary>Links both Web runtime stages once against their complete asset graph.</summary>
internal static class WebCanonicalScriptBuild
{
    internal static void ValidateFontBackend(PlayerBuildContext context, IEnumerable<string> assetFiles,
        FileUtility files, PathUtility paths, CancellationToken cancellationToken)
    {
        if (context.Target.Platform != BuildTargetPlatform.Web || context.Request.WebFontBackend != WebFontBackend.FreeType)
            return;
        var assets = 0;
        var fonts = 0;
        var faces = 0;
        foreach (var path in assetFiles.Where(path => paths.GetExtension(path).Equals(".sdfont", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.GetFileLength(path) > 192L * 1024 * 1024)
                throw new InvalidDataException($"Font asset '{path}' exceeds the 192 MB asset limit.");
            var result = WebFontCapabilityValidator.ValidateAsset(files.ReadAllBytes(path), path);
            assets++;
            fonts += result.EmbeddedFonts;
            faces += result.Faces;
        }
        Debug.Log($"Web FreeType font capability check: {assets} admitted .sdfont assets, " +
                  $"{fonts} embedded font sources, {faces} SFNT faces.");
    }

    internal static bool IsEnabled(PlayerBuildContext context) =>
        context.Target.Platform == BuildTargetPlatform.Web &&
        context.Request.WebScriptCompilation == WebScriptCompilationMode.AheadOfTime &&
        context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full;

    internal static async Task<RuntimeManagedCodeReleaseInputSet> CreateAsync(
        PlayerBuildContext context,
        ProjectWorkspace workspace,
        RuntimeManagedCodeReleaseInputSet originals,
        IEnumerable<string> assetFiles,
        CancellationToken cancellationToken)
    {
        // The AOT scene and downloaded game share these images. Linking only the
        // startup graph would remove gameplay components and change package identity
        // between the separately invoked content and Player builds.
        var candidates = new RuntimeManagedCodeReleaseInputSet(originals.Assemblies
                .Where(static input => !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name))
                .OrderBy(static input => input.Name, StringComparer.Ordinal).ToArray(),
            originals.PackageResources, originals.PackageResourceRoots);
        var modules = candidates.Assemblies.Select(static input => input.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var report = new PlayerStrippingReport
        {
            Target = context.Target.TargetId,
            Mode = "Full Web canonical scripts",
            ContentEncryptionKeyFingerprint = context.ContentEncryptionKey?.Fingerprint
        };
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            "Linking shared Web startup and gameplay scripts", .23f));
        var priorDescriptor = context.ReflectionRootDescriptorPath;
        var linked = await PlayerManagedCodeTrimmer.TrimAsync(context, workspace, candidates,
            assetFiles.Concat(originals.PackageResources.Select(static resource => resource.FilePath))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal),
            PlayerStrippingSettings.Load(workspace), report, modules, cancellationToken,
            preserveModuleInitializers: true, webCanonicalPlatform: true).ConfigureAwait(false);
        // AssemblyRef rows may outlive removed methods. Inspect the surviving
        // type/forwarder contracts after linking rather than rejecting dead Skia
        // code before the canonical graph can discard it.
        WebFontBackendSettings.ValidateRuntimeInputs(linked, true, context.Request.WebFontBackend);
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        var directory = paths.Combine(workspace.RootPath, "Temp", "BuildReports");
        files.CreateDirectory(directory);
        var reportPath = paths.Combine(directory, $"web-canonical-{Guid.NewGuid():N}.yaml");
        files.WriteAllText(reportPath, YamlUtility.Serialize(report));
        files.WriteAllText(Path.ChangeExtension(reportPath, ".roots.xml"),
            files.ReadAllText(context.ReflectionRootDescriptorPath!));
        var identities = linked.Assemblies.Select(static input =>
        {
            using var image = AssemblyDefinition.ReadAssembly(input.AssemblyPath);
            return new { input.Name, Sha256 = input.AssemblySha256, Mvid = image.MainModule.Mvid,
                input.AssemblyPath, input.BuildId, input.Dependencies };
        }).ToArray();
        files.WriteAllText(Path.ChangeExtension(reportPath, ".images.json"),
            JsonSerializer.Serialize(identities, new JsonSerializerOptions { WriteIndented = true }));
        // Keep the startup descriptor when present. The host publisher derives
        // gameplay contracts from the final canonical images, including serializers.
        context.ReflectionRootDescriptorPath = priorDescriptor ?? context.ReflectionRootDescriptorPath;
        Debug.Log($"Web canonical scripts: {candidates.Assemblies.Count} inputs, " +
                  $"{linked.Assemblies.Count} reachable images, " +
                  $"{candidates.Assemblies.Sum(static input => input.AssemblySize)} -> " +
                  $"{linked.Assemblies.Sum(static input => input.AssemblySize)} bytes.");
        // Static-host images remain reference inputs; they are never rewritten or
        // duplicated in the canonical downloadable/native image set.
        return MergeHostReferences(originals, linked);
    }

    internal static RuntimeManagedCodeReleaseInputSet MergeHostReferences(
        RuntimeManagedCodeReleaseInputSet originals, RuntimeManagedCodeReleaseInputSet linked)
    {
        var hosts = originals.Assemblies.Where(static input =>
            DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name)).ToArray();
        var names = hosts.Concat(linked.Assemblies).Select(static input => input.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var input in linked.Assemblies)
        foreach (var dependency in input.Dependencies)
            if (!names.Contains(dependency))
                throw new InvalidDataException(
                    $"Canonical Web script '{input.Name}' depends on missing module '{dependency}'.");
        // Host snapshots describe ABI contracts, not independently loaded DLLs.
        // Their remaining framework/native references are resolved by the SDK's
        // static Host closure. Publish only edges to actual release modules; do
        // not silently repair a missing dependency of a linked dynamic image.
        return new RuntimeManagedCodeReleaseInputSet(hosts.Select(input => input with
                { Dependencies = input.Dependencies.Where(names.Contains).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal).ToArray() })
                .Concat(linked.Assemblies).OrderBy(static input => input.Name, StringComparer.Ordinal).ToArray(),
            originals.PackageResources, originals.PackageResourceRoots);
    }
}
