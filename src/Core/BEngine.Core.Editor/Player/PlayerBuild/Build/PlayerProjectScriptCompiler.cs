using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Editor.Build;

/// <summary>Builds Player scripts separately from the editor's loaded Debug assemblies.</summary>
internal static class PlayerProjectScriptCompiler
{
    internal static RuntimeManagedCodeReleaseInputSet Compile(PlayerBuildContext context,
        ProjectWorkspace workspace, RuntimeManagedCodeReleaseInputSet inputs,
        FileUtility files, CancellationToken cancellationToken)
    {
        bool __queryResult666;
        {
            var __querySource666 = inputs.Assemblies;
            ArgumentNullException.ThrowIfNull(__querySource666, "source");
            __queryResult666 = false;
            var __querySource666Type = __querySource666.GetType();
            if (__querySource666Type == typeof(BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput[]) || __querySource666Type == typeof(System.Collections.Generic.List<BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput>))
            {
                System.ReadOnlySpan<BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput> __querySource666Span = __querySource666Type == typeof(BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput[]) ? (BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput[])(object)__querySource666 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput>)(object)__querySource666);
                foreach (var __queryItem666 in __querySource666Span)
                {
                    if ((__queryItem666.Origin.StartsWith("project:", StringComparison.Ordinal) || __queryItem666.Origin.StartsWith("package:", StringComparison.Ordinal)))
                    {
                        __queryResult666 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem666 in __querySource666)
                {
                    if ((__queryItem666.Origin.StartsWith("project:", StringComparison.Ordinal) || __queryItem666.Origin.StartsWith("package:", StringComparison.Ordinal)))
                    {
                        __queryResult666 = true;
                        break;
                    }
                }
            }
        }
        
        if (!__queryResult666)
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
            with { InstrumentLoops = false, PublishSharedReferences = false, UseSharedCache = false };
        settings = settings with
        {
            DefineSymbols = PlatformTextBuildPolicy.For(context.Target.Platform, context.Request.WebFontBackend)
                .GetDefineSymbols(settings.DefineSymbols)
        };
        var hostReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        inputs = CompilePackages(context, workspace, inputs, settings, files, cancellationToken, hostReferences);
        var references = inputs.Assemblies.Where(static input => !input.Origin.StartsWith("project:", StringComparison.Ordinal))
            .ToDictionary(static input => input.Name, static input => input.AssemblyPath, StringComparer.OrdinalIgnoreCase);
        foreach (var reference in hostReferences) references[reference.Key] = reference.Value;
        references[typeof(BAsset).Assembly.GetName().Name!] = typeof(BAsset).Assembly.Location;
        // Match the editor's RuntimePackageLoader baseline: public Game registration APIs
        // expose IServiceCollection even when DI is supplied by the native Player host.
        references[typeof(IServiceCollection).Assembly.GetName().Name!] = typeof(IServiceCollection).Assembly.Location;
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
        // Explicit Core/host references are compilation inputs, but retain their
        // identity in the native host. Only released modules belong in the
        // downloadable dependency graph, as in the input collector's graph.
        var releaseNames = assemblies.Select(static input => input.Name)
            .Concat(result.CompiledAssemblies.Select(static artifact => artifact.Node.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in result.CompiledAssemblies)
        {
            var path = artifact.AssemblyPath;
            var symbols = Path.ChangeExtension(path, ".pdb");
            assemblies.Add(new RuntimeManagedCodeReleaseInput(artifact.Node.Name, artifact.BuildId, path,
                files.FileExists(symbols) ? symbols : null,
                Convert.ToHexStringLower(SHA256.HashData(files.ReadAllBytes(path))), files.GetFileLength(path),
                artifact.Node.References.Where(releaseNames.Contains).ToArray(), "project:" + artifact.Node.Name));
        }
        context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
            $"Compiled {result.CompiledAssemblies.Count} Player script assemblies ({configuration}, {context.Target.TargetId})", .17f));
        return new RuntimeManagedCodeReleaseInputSet(assemblies, inputs.PackageResources, inputs.PackageResourceRoots);
    }

    private static RuntimeManagedCodeReleaseInputSet CompilePackages(PlayerBuildContext context,
        ProjectWorkspace workspace, RuntimeManagedCodeReleaseInputSet inputs, ScriptBuildConfiguration settings,
        FileUtility files, CancellationToken cancellationToken, IDictionary<string, string> hostReferences)
    {
        var ids = inputs.Assemblies.Where(static input => input.Origin.StartsWith("package:", StringComparison.Ordinal))
            .Select(static input => input.Origin[8..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return inputs;
        var definitions = files.EnumerateFiles(workspace.PackageCachePath, "package.yaml", SearchOption.AllDirectories)
            .Select(static path => new BPackageDefinition(path, PackageDefinitionLoader.Load(path)))
            .Where(definition => ids.Contains(definition.Document.Id)).ToArray();
        if (definitions.Length != ids.Count)
            throw new InvalidDataException("Enabled Player package source definitions are missing or duplicated.");
        foreach (var definition in definitions)
        foreach (var name in definition.Document.Runtime?.HostReferences ?? [])
            hostReferences[name] = RuntimePackageLoader.ResolveAssemblyPath(workspace, name) ??
                throw new FileNotFoundException($"Host reference '{name}' is unavailable.");
        var compiled = PackageSourceCompiler.CompileEnabled(workspace, definitions, ProcessUtility.Current,
            (index,total,name) => context.Progress?.Report(new PlayerBuildProgress(PlayerBuildPhase.BuildingContent,
                $"Compiling Player packages ({settings.Configuration}): {name}", .16f + .01f * index / Math.Max(1,total))),
            configurationOverride: settings, cancellationToken: cancellationToken, includeEditorAssemblies: false);
        var assemblies = inputs.Assemblies.Select(input =>
        {
            if (!input.Origin.StartsWith("package:", StringComparison.Ordinal) ||
                !compiled.AssemblyPaths.TryGetValue(input.Name, out var path)) return input;
            var symbols = Path.ChangeExtension(path, ".pdb");
            var hash = Convert.ToHexStringLower(SHA256.HashData(files.ReadAllBytes(path)));
            return input with { BuildId = hash, AssemblyPath = path, SymbolsPath = files.FileExists(symbols) ? symbols : null,
                AssemblySha256 = hash, AssemblySize = files.GetFileLength(path) };
        }).ToArray();
        return new RuntimeManagedCodeReleaseInputSet(assemblies, inputs.PackageResources, inputs.PackageResourceRoots);
    }
}
