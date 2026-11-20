using System.Security.Cryptography;
using BEngine.ProjectSystem.Editor;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

/// <summary>Adapts the engine's compiled Shader artifacts to content-archive inputs.</summary>
internal static class PlayerShaderBuildPipeline
{
    internal static RuntimeManagedCodeReleaseInputSet AddArtifacts(PlayerBuildContext context,
        RuntimeManagedCodeReleaseInputSet inputs, IReadOnlyList<AssetRecord> assets, CancellationToken cancellationToken)
    {
        var backends = PlatformGraphicsBuildPolicy.GetOfflineBackends(
            context.GraphicsBackends, context.Request.CompileShaders);
        if (backends.Count == 0) return inputs;
        var resources = inputs.PackageResources.ToList();
        // Staged files are consumed by the enclosing content build.
        var directory = Path.Combine(context.StagingDirectory, ".shader-artifacts");
        Directory.CreateDirectory(directory);
        var sourceResources = inputs.PackageResources
            .Select(static input => new ShaderBuildResourceInput(input.Address, input.FilePath)).ToArray();
        foreach (var backend in backends)
        foreach (var source in ShaderBuildPipeline.CollectSources(assets, sourceResources, backend, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = source.GetResourcePath(backend);
            var bytes = ShaderBuildPipeline.Compile(source, backend, cancellationToken);
            if (context.StrippingReport is { } report) { report.OfflineShaderCount++; report.OfflineShaderBytes += bytes.Length; }
            var file = Path.Combine(directory, Path.GetFileName(path));
            File.WriteAllBytes(file, bytes);
            resources.Add(new("shader-artifacts", "Assets/__BEngine/Compiled/Resources/" + path, file,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length));
        }
        return new(inputs.Assemblies, resources, inputs.PackageResourceRoots);
    }
}
