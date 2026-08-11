using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using BEngine.Serialization;

namespace BEngine.ProjectSystem;

internal readonly record struct ScriptBuildConfiguration(string TargetFramework, bool UseWindowsForms);

public static class ProjectScriptCompiler
{
    internal static ScriptBuildConfiguration RuntimeBuildConfiguration { get; } = new("net9.0", false);

    public static Assembly? CompileAndLoad(ProjectWorkspace workspace)
    {
        var references = RuntimePackageReferences(new BPackageManager(workspace));
        return CompileAndLoadAssembly(workspace, workspace.ScriptsPath, "GameScripts",
            "ScriptCompilation.log", references, RuntimeBuildConfiguration);
    }

    internal static Assembly? CompileAndLoadAssembly(
        ProjectWorkspace workspace,
        string sourceDirectory,
        string assemblyName,
        string logFileName,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration)
    {
        var sources = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0) return null;

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName);
        if (loaded is not null) return loaded;

        var buildDirectory = Path.Combine(workspace.TempPath, "ScriptBuild", assemblyName);
        var outputDirectory = Path.Combine(workspace.LibraryPath, "ScriptAssemblies");
        Directory.CreateDirectory(buildDirectory);
        Directory.CreateDirectory(outputDirectory);
        var projectPath = Path.Combine(buildDirectory, $"{assemblyName}.csproj");
        WriteBuildProject(projectPath, sources, assemblyName, references, configuration);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workspace.RootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add("-nr:false");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputDirectory);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        File.WriteAllText(Path.Combine(workspace.LogsPath, logFileName), stdout.Result + stderr.Result);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{assemblyName} failed to compile. See Logs/{logFileName}.");
        }

        var assemblyPath = Path.Combine(outputDirectory, $"{assemblyName}.dll");
        return File.Exists(assemblyPath)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath)
            : throw new FileNotFoundException($"The {assemblyName} assembly was not produced.", assemblyPath);
    }

    internal static void WriteBuildProject(
        string path,
        IReadOnlyList<string> sources,
        string assemblyName,
        IReadOnlyDictionary<string, string> references,
        ScriptBuildConfiguration configuration)
    {
        var itemGroup = new XElement("ItemGroup");
        foreach (var source in sources)
        {
            itemGroup.Add(new XElement("Compile", new XAttribute("Include", source)));
        }
        foreach (var reference in references)
        {
            itemGroup.Add(new XElement("Reference", new XAttribute("Include", reference.Key),
                new XElement("HintPath", reference.Value),
                new XElement("Private", "false")));
        }

        var propertyGroup = new XElement("PropertyGroup",
            new XElement("TargetFramework", configuration.TargetFramework),
            new XElement("LangVersion", "13.0"),
            new XElement("Nullable", "enable"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("AssemblyName", assemblyName));
        if (configuration.UseWindowsForms) propertyGroup.Add(new XElement("UseWindowsForms", "true"));

        new XDocument(
            new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                propertyGroup,
                itemGroup))
            .Save(path);
    }

    internal static Dictionary<string, string> RuntimePackageReferences(
        BPackageManager packages,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages.definitions)
        {
            if (!packages.IsEnabled(package.Document.Id)) continue;
            AddPackageAssemblyReference(
                package.Document.Id, "runtime", package.Document.Runtime, references,
                preferredAssemblyPaths);
        }
        return references;
    }

    internal static void AddPackageAssemblyReference(
        string packageId,
        string kind,
        BEngine.Serialization.Documents.PackageAssemblyDocument? assembly,
        IDictionary<string, string> references,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths)
    {
        if (assembly is null) return;
        references[assembly.Assembly] = ResolveAssemblyPath(
            packageId, kind, assembly.Assembly, preferredAssemblyPaths);
    }

    internal static string ResolveAssemblyPath(
        string packageId,
        string kind,
        string assemblyName,
        IReadOnlyDictionary<string, string>? preferredAssemblyPaths)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
            string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (ReadableLocation(loaded) is { } loadedPath) return loadedPath;

        if (preferredAssemblyPaths is not null &&
            preferredAssemblyPaths.TryGetValue(assemblyName, out var preferredPath) &&
            File.Exists(preferredPath))
            return Path.GetFullPath(preferredPath);

        var applicationPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
        if (File.Exists(applicationPath)) return Path.GetFullPath(applicationPath);

        Exception? loadFailure = null;
        try
        {
            var resolved = Assembly.Load(new AssemblyName(assemblyName));
            if (ReadableLocation(resolved) is { } resolvedPath) return resolvedPath;
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            loadFailure = exception;
        }

        throw new FileNotFoundException(
            $"Enabled package '{packageId}' declares {kind} assembly '{assemblyName}', but no readable DLL " +
            $"was found. Checked loaded assemblies, '{applicationPath}', and Assembly.Load('{assemblyName}').",
            loadFailure);
    }

    private static string? ReadableLocation(Assembly? assembly)
    {
        if (assembly is null || assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location)) return null;
        var path = Path.GetFullPath(assembly.Location);
        return File.Exists(path) ? path : null;
    }

    internal static IReadOnlyDictionary<string, string> PreferredAssemblyPaths(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The preferred assembly was not found.", fullPath);
        var name = AssemblyName.GetAssemblyName(fullPath).Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BadImageFormatException($"The preferred assembly '{fullPath}' has no assembly name.");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = fullPath };
    }
}
