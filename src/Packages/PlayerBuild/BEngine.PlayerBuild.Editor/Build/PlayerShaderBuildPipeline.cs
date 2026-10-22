using System.Security.Cryptography;
using BEngine.ProjectSystem.Editor;
using BEngine.ProjectSystem;
using BEngine.Rendering.Rhi;
using ShaderStages = Veldrid.ShaderStages;
using Veldrid.SPIRV;

namespace BEngine.Editor;

/// <summary>Package editors contribute generated shader variants without package code in Core.</summary>
public interface IPlayerShaderSourceProvider
{
    IEnumerable<GraphicsShaderSource> CollectSources(IReadOnlyList<AssetRecord> assets, IReadOnlyList<RuntimePackageResourceInput> resources, GraphicsBackend backend);
}

internal static class PlayerShaderBuildPipeline
{
    internal static byte[] Compile(GraphicsShaderSource source)
    {
        if (source.Language != GraphicsShaderLanguage.Glsl || source.EntryPoint != "main" ||
            source.Code.Length > 1024 * 1024 || source.Stage is not (GraphicsShaderStage.Vertex or GraphicsShaderStage.Fragment))
            throw new InvalidDataException("Offline Vulkan compilation requires GLSL vertex/fragment shaders, entry 'main', at most 1 MiB.");
        return SpirvCompilation.CompileGlslToSpirv(source.Code, "bengine.glsl",
            source.Stage == GraphicsShaderStage.Vertex ? ShaderStages.Vertex : ShaderStages.Fragment,
            GlslCompileOptions.Default).SpirvBytes;
    }

    internal static bool TryReadSource(string path, out GraphicsShaderSource source)
    {
        source = default;
        if (!path.EndsWith(".vulkan.vert.glsl", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".vulkan.frag.glsl", StringComparison.OrdinalIgnoreCase)) return false;
        source = new(path.EndsWith(".vert.glsl", StringComparison.OrdinalIgnoreCase) ? GraphicsShaderStage.Vertex :
            GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, File.ReadAllText(path));
        return !System.Text.RegularExpressions.Regex.IsMatch(source.Code, "__[A-Z_]+__");
    }

    internal static RuntimeManagedCodeReleaseInputSet AddArtifacts(PlayerBuildContext context,
        RuntimeManagedCodeReleaseInputSet inputs, IReadOnlyList<AssetRecord> assets, CancellationToken cancellationToken)
    {
        if (!context.Request.CompileShaders || !context.GraphicsBackends.Contains(GraphicsBackend.Vulkan)) return inputs;
        var resources = inputs.PackageResources.ToList();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        // Staged files are consumed by the enclosing content build.
        var directory = Path.Combine(context.StagingDirectory, ".shader-artifacts");
        Directory.CreateDirectory(directory);
        foreach (var resource in inputs.PackageResources)
            if (TryReadSource(resource.FilePath, out var source)) Add(source);
        foreach (var asset in assets)
            if (TryReadSource(asset.SourcePath, out var source)) Add(source);
        foreach (var providerType in TypeCache.GetTypesDerivedFrom<IPlayerShaderSourceProvider>())
        {
            if (providerType.IsAbstract || providerType.GetConstructor(Type.EmptyTypes) is null) continue;
            var provider = (IPlayerShaderSourceProvider)Activator.CreateInstance(providerType)!;
            foreach (var source in provider.CollectSources(assets, inputs.PackageResources, GraphicsBackend.Vulkan)) Add(source);
        }
        return new(inputs.Assemblies, resources, inputs.PackageResourceRoots);

        void Add(GraphicsShaderSource source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = GraphicsShaderArtifacts.GetResourcePath(source, GraphicsBackend.Vulkan);
            if (!emitted.Add(path)) return;
            var bytes = Compile(source);
            if (context.StrippingReport is { } report) { report.OfflineShaderCount++; report.OfflineShaderBytes += bytes.Length; }
            var file = Path.Combine(directory, Path.GetFileName(path));
            File.WriteAllBytes(file, bytes);
            resources.Add(new("shader-artifacts", "Assets/__BEngine/Compiled/Resources/" + path, file,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length));
        }
    }
}
