using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.Tooling;

namespace BEngine.Editor;

/// <summary>Dispatches a declared package command without opening a workbench.
/// Owns the temporary activation, command instances and process-environment restoration.
/// Invocation is exclusive; tools own any project sessions they open themselves.</summary>
public interface IEditorCommandLineRunner
{
    int Run(IReadOnlyList<string> arguments, BPackageCatalog? catalog = null);
}

internal sealed class EditorPackageCommandLine(IProjectPackageManagerFactory packages) : IEditorCommandLineRunner
{
    private static int _running;

    public int Run(IReadOnlyList<string> arguments, BPackageCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var copy = arguments.ToArray();
        if (copy.Length == 0 || string.IsNullOrWhiteSpace(copy[0]) || !copy[0].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("A package command option is required.", nameof(arguments));
        if (copy.Any(static value => value is null))
            throw new ArgumentException("Command arguments cannot contain null.", nameof(arguments));
        if (EditorInstanceContext.current is not null)
            throw new InvalidOperationException("Package command dispatch cannot replace an active editor or project tool.");
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("A package command is already running.");

        Dictionary<string, string?>? previousEnvironment = null;
        Dictionary<string, bool>? previousPackages = null;
        string? commandRoot = null;
        List<Exception>? failures = null;
        var result = 0;
        try
        {
            catalog ??= BPackageCatalog.DiscoverAvailable();
            var owners = catalog.packages.Where(definition =>
                definition.Document.CommandLineOptions.Contains(copy[0], StringComparer.OrdinalIgnoreCase)).ToArray();
            if (owners.Length != 1)
                throw new ArgumentException($"Expected one installed package providing '{copy[0]}', found {owners.Length}.");
            var project = ResolveProject(copy);
            previousEnvironment = new[] { "TEMP", "TMP", "TMPDIR", ToolEnvironment.ProjectDirectoryVariable }
                .ToDictionary(name => name, Environment.GetEnvironmentVariable);
            previousPackages = RuntimePackageState.Capture();
            var temporaryRoot = project is null ? ToolEnvironment.TempDirectory :
                EditorHostServices.Paths.Combine(project, "Temp");
            commandRoot = EditorHostServices.Paths.Combine(temporaryRoot, "PackageTools", Guid.NewGuid().ToString("N"));
            if (project is not null) ToolEnvironment.ConfigureProject(project);
            result = RunTool(commandRoot, owners[0], catalog, Array.AsReadOnly(copy));
        }
        catch (Exception failure) { (failures ??= []).Add(failure); }
        finally
        {
            if (previousPackages is not null) Cleanup(() => RuntimePackageState.Restore(previousPackages));
            if (previousEnvironment is not null)
                foreach (var (name, value) in previousEnvironment)
                    Cleanup(() => Environment.SetEnvironmentVariable(name, value));
            if (commandRoot is not null)
            {
                try
                {
                    if (EditorHostServices.FileSystem.DirectoryExists(commandRoot))
                        EditorHostServices.FileSystem.DeleteDirectory(commandRoot, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { System.Diagnostics.Trace.WriteLine(exception); }
                catch (Exception failure) { (failures ??= []).Add(failure); }
            }
            Volatile.Write(ref _running, 0);
        }
        ThrowFailures(failures);
        return result;

        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
    }

    private int RunTool(string commandRoot, BPackageDefinition package, BPackageCatalog catalog,
        IReadOnlyList<string> arguments)
    {
        var workspace = new ProjectWorkspace(commandRoot, new ProjectData { Name = "Package Tools" });
        var manifest = new PackageManifestDocument();
        foreach (var id in catalog.GetDependencyClosure(package.Document.Id).Append(package.Document.Id))
            manifest.Packages.Add(new PackageReferenceDocument
            {
                Id = id, Version = catalog.GetRequired(id).Document.PackageVersion, Enabled = true
            });
        YamlUtility.Save(manifest, workspace.PackageManifestPath);
        BPackageManager? manager = null;
        List<Exception>? failures = null;
        var result = 0;
        try
        {
            manager = packages.Create(workspace, catalog);
            result = RunOwnedCommands(manager, package, arguments);
        }
        catch (Exception failure) { (failures ??= []).Add(failure); }
        finally
        {
            try { manager?.Dispose(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        ThrowFailures(failures);
        return result;
    }

    // Leave collectible command objects and reflection temporaries off the stack
    // before the outer scope retires package assemblies and their image leases.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunOwnedCommands(BPackageManager manager, BPackageDefinition package,
        IReadOnlyList<string> arguments)
    {
        var tools = new List<IEditorCommandLineTool>();
        List<Exception>? failures = null;
        var result = 0;
        try
        {
            var owned = manager.LoadedAssemblies.ToHashSet();
            IEditorCommandLineTool? selected = null;
            foreach (var type in TypeCache.GetTypesDerivedFrom<IEditorCommandLineTool>()
                         .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                if (!owned.Contains(type.Assembly) || type.IsAbstract || type.ContainsGenericParameters) continue;
                var tool = (IEditorCommandLineTool)Activator.CreateInstance(type)!;
                tools.Add(tool);
                if (!string.Equals(tool.Option, arguments[0], StringComparison.OrdinalIgnoreCase)) continue;
                if (selected is not null)
                    throw new InvalidOperationException($"Package '{package.Document.Id}' registered multiple tools for '{arguments[0]}'.");
                selected = tool;
            }
            result = selected?.Run(arguments) ?? throw new InvalidOperationException(
                $"Package '{package.Document.Id}' did not register command '{arguments[0]}'.");
        }
        catch (Exception failure) { (failures ??= []).Add(failure); }
        finally
        {
            for (var i = tools.Count - 1; i >= 0; i--)
                if (tools[i] is IDisposable disposable) Cleanup(disposable.Dispose);
            tools.Clear();
        }
        ThrowFailures(failures);
        return result;

        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
    }

    private static void ThrowFailures(List<Exception>? failures)
    {
        if (failures is not { Count: > 0 }) return;
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("Package command execution or cleanup failed.", failures);
    }

    private static string? ResolveProject(string[] arguments)
    {
        var index = Array.FindIndex(arguments, argument => argument.Equals("--project", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Option '--project' requires a project path.");
        var path = EditorHostServices.Paths.GetFullPath(arguments[index + 1]);
        var root = EditorHostServices.FileSystem.FileExists(path)
            ? EditorHostServices.Paths.GetDirectoryName(path)! : path;
        if (!EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(root, ProjectWorkspace.ProjectFileName)))
            throw new FileNotFoundException("The BEngine project was not found.", path);
        return root;
    }
}
