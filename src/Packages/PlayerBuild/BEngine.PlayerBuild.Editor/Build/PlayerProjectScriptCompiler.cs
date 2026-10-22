using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

/// <summary>Builds Player scripts separately from the editor's loaded Debug assemblies.</summary>
internal static class PlayerProjectScriptCompiler
{
    internal static RuntimeManagedCodeReleaseInputSet Compile(PlayerBuildContext context,
        ProjectWorkspace workspace, RuntimeManagedCodeReleaseInputSet inputs,
        FileUtility files, CancellationToken cancellationToken)
    {
        if (!inputs.Assemblies.Any(static input => input.Origin.StartsWith("project:", StringComparison.Ordinal) ||
                                                  input.Origin.StartsWith("package:", StringComparison.Ordinal)))
            return inputs;
        var requested = context.Request.Configuration;
        if (!requested.Equals("Debug", StringComparison.OrdinalIgnoreCase) &&
            !requested.Equals("Release", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentOutOfRangeException(nameof(context), "Player script configuration must be Debug or Release.");
        var configuration = context.Request.DevelopmentBuild || requested.Equals("Debug", StringComparison.OrdinalIgnoreCase)
            ? "Debug" : "Release";
        var platform = context.Target.Platform switch
        {
            BuildTargetPlatform.Windows => RuntimePlatform.WindowsPlayer,
            BuildTargetPlatform.Linux => RuntimePlatform.LinuxPlayer,
            BuildTargetPlatform.MacOS => RuntimePlatform.OSXPlayer,
            BuildTargetPlatform.Android => RuntimePlatform.AndroidPlayer,
            BuildTargetPlatform.IOS => RuntimePlatform.IPhonePlayer,
            BuildTargetPlatform.Web => RuntimePlatform.WebGLPlayer,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
        var architecture = context.Target.Architecture switch
        {
            BuildArchitecture.X64 => Architecture.X64,
            BuildArchitecture.Arm64 => Architecture.Arm64,
            BuildArchitecture.Arm => Architecture.Arm,
            BuildArchitecture.Wasm32 => Architecture.Wasm,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
        var settings = ProjectScriptCompiler.CreateBuildConfiguration(workspace, false, configuration, platform, architecture)
            with { IsPlayerBuild = true };
        inputs = CompilePackages(context, workspace, inputs, settings, files, cancellationToken);
        var references = inputs.Assemblies.Where(static input => !input.Origin.StartsWith("project:", StringComparison.Ordinal))
            .ToDictionary(static input => input.Name, static input => input.AssemblyPath, StringComparer.OrdinalIgnoreCase);
        references[typeof(BAsset).Assembly.GetName().Name!] = typeof(BAsset).Assembly.Location;
        var graph = new ProjectAssemblyDatabase(workspace).BuildGraph(platform,
            settings.DefineSymbols.ToHashSet(StringComparer.Ordinal),
            references.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), includeEditorAssemblies: false);
        // A legacy release may contain only precompiled assemblies, with no source graph.
        if (graph.RuntimeAssemblies.Count == 0) return inputs;
        var result = ProjectScriptCompiler.CompileGraph(workspace, graph.RuntimeAssemblies, references,
            new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase), settings,
            publishRuntimeManifest: false, raiseCompilationEvents: false, publishOutputs: false,
            cancellationToken: cancellationToken);
        var assemblies = inputs.Assemblies.Where(static input => !input.Origin.StartsWith("project:", StringComparison.Ordinal)).ToList();
        foreach (var artifact in result.CompiledAssemblies)
        {
            var path = artifact.AssemblyPath;
            var symbols = Path.ChangeExtension(path, ".pdb");
            assemblies.Add(new RuntimeManagedCodeReleaseInput(artifact.Node.Name, artifact.BuildId, path,
                files.FileExists(symbols) ? symbols : null,
                Convert.ToHexString(SHA256.HashData(files.ReadAllBytes(path))).ToLowerInvariant(), files.GetFileLength(path),
                artifact.Node.References, "project:" + artifact.Node.Name));
        }
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            $"Compiled {result.CompiledAssemblies.Count} Player script assemblies ({configuration}, {context.Target.TargetId})", .17f));
        return new RuntimeManagedCodeReleaseInputSet(assemblies, inputs.PackageResources, inputs.PackageResourceRoots);
    }

    private static RuntimeManagedCodeReleaseInputSet CompilePackages(PlayerBuildContext context,
        ProjectWorkspace workspace, RuntimeManagedCodeReleaseInputSet inputs, ScriptBuildConfiguration settings,
        FileUtility files, CancellationToken cancellationToken)
    {
        var ids = inputs.Assemblies.Where(static input => input.Origin.StartsWith("package:", StringComparison.Ordinal))
            .Select(static input => input.Origin[8..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return inputs;
        var definitions = files.EnumerateFiles(workspace.PackageCachePath, "package.yaml", SearchOption.AllDirectories)
            .Select(static path => new BPackageDefinition(path, PackageDefinitionLoader.Load(path)))
            .Where(definition => ids.Contains(definition.Document.Id)).ToArray();
        if (definitions.Length != ids.Count)
            throw new InvalidDataException("Enabled Player package source definitions are missing or duplicated.");
        var compiled = PackageSourceCompiler.CompileEnabled(workspace, definitions, ProcessUtility.Current,
            (index,total,name) => context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                $"Compiling Player packages ({settings.Configuration}): {name}", .16f + .01f * index / Math.Max(1,total))),
            playerConfiguration: settings, cancellationToken: cancellationToken);
        var assemblies = inputs.Assemblies.Select(input =>
        {
            if (!input.Origin.StartsWith("package:", StringComparison.Ordinal) ||
                !compiled.AssemblyPaths.TryGetValue(input.Name, out var path)) return input;
            var symbols = Path.ChangeExtension(path, ".pdb");
            var hash = Convert.ToHexString(SHA256.HashData(files.ReadAllBytes(path))).ToLowerInvariant();
            return input with { BuildId = hash, AssemblyPath = path, SymbolsPath = files.FileExists(symbols) ? symbols : null,
                AssemblySha256 = hash, AssemblySize = files.GetFileLength(path) };
        }).ToArray();
        return new RuntimeManagedCodeReleaseInputSet(assemblies, inputs.PackageResources, inputs.PackageResourceRoots);
    }
}
