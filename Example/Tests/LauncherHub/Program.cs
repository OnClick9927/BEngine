using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BEngine.Launcher;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.LauncherHub;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"BEngine-LauncherHub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            VerifyHistoryMigrationAndMultipleProjects(temporaryRoot);
            VerifyAvailablePackageDiscovery(temporaryRoot);
            VerifyProjectTargetPath(temporaryRoot);
            VerifyDependencyInjectionComposition();
            VerifyHubTabsAndCoreOnlyCreation();
            VerifyHubSourceBoundary();
            await VerifyEditorStartupMonitoring(temporaryRoot).ConfigureAwait(false);
            Console.WriteLine(
                "LAUNCHER_HUB_OK|projects,history-migration,packages-tab,output-catalog,target-path,ioc," +
                "default-aot,source-boundary,no-reverse-core-dependency,startup-ready," +
                "startup-reported-failure,startup-unexpected-exit,startup-timeout");
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    private static async Task VerifyEditorStartupMonitoring(string root)
    {
        using var currentProcess = Process.GetCurrentProcess();

        var readyPath = Path.Combine(root, "EditorStartupReady.json");
        WriteStartupStatus(readyPath, "ready");
        var ready = await EditorStartupMonitor.WaitAsync(currentProcess, readyPath,
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5)).ConfigureAwait(false);
        Require(ready.Kind == EditorStartupOutcomeKind.Ready && ready.IsReady,
            "The Hub did not accept the Editor's first-frame-ready report.");

        var failureLog = Path.Combine(root, "SyntheticEditorStartup.log");
        var failedPath = Path.Combine(root, "EditorStartupFailed.json");
        WriteStartupStatus(failedPath, "failed", "Synthetic managed startup failure.", failureLog);
        var failed = await EditorStartupMonitor.WaitAsync(currentProcess, failedPath,
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5)).ConfigureAwait(false);
        Require(failed.Kind == EditorStartupOutcomeKind.Failed && !failed.IsReady &&
                failed.Message == "Synthetic managed startup failure." &&
                failed.LogPath == failureLog,
            "The Hub discarded the Editor's reported startup failure details.");

        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var startInfo = new ProcessStartInfo(commandInterpreter)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("exit 37");
        using var exitedProcess = Process.Start(startInfo) ??
                                  throw new InvalidOperationException("Could not start the exit-code probe.");
        var exited = await EditorStartupMonitor.WaitAsync(exitedProcess,
            Path.Combine(root, "NoStartupStatusForExitedProcess.json"),
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5)).ConfigureAwait(false);
        Require(exited.Kind == EditorStartupOutcomeKind.UnexpectedExit && !exited.IsReady &&
                exited.ExitCode == 37 && exited.Message.Contains("0x00000025", StringComparison.Ordinal),
            "The Hub did not turn an early non-zero Editor exit into an actionable startup failure.");

        var timedOut = await EditorStartupMonitor.WaitAsync(currentProcess,
            Path.Combine(root, "NoStartupStatusForTimeout.json"),
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5)).ConfigureAwait(false);
        Require(timedOut.Kind == EditorStartupOutcomeKind.TimedOut && !timedOut.IsReady &&
                timedOut.Message.Contains("may still be running", StringComparison.OrdinalIgnoreCase),
            "The Hub did not distinguish a blocked startup from a process exit.");
    }

    private static void WriteStartupStatus(string path, string state, string? message = null,
        string? logPath = null)
    {
        var status = new EditorStartupMonitor.EditorStartupStatus(state, message, logPath,
            Environment.ProcessId, DateTimeOffset.UtcNow);
        File.WriteAllText(path, JsonSerializer.Serialize(status));
    }

    private static void VerifyHistoryMigrationAndMultipleProjects(string root)
    {
        var first = Path.Combine(root, "First");
        var second = Path.Combine(root, "Second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var settingsPath = Path.Combine(root, "LauncherSettings.yaml");
        File.WriteAllText(settingsPath, $"""
            format: BEngine.LauncherSettings
            version: 1
            lastProjectDirectory: {first}
            """);

        var store = new LauncherHistoryStore(settingsPath);
        var settings = store.Load();
        Require(settings.Version == 2 && settings.Projects.Count == 1,
            "Legacy launcher settings were not migrated to the project list.");
        store.Remember(settings, first, "First project");
        store.Remember(settings, second, "Second project");
        store.Remember(settings, first, "First project renamed");

        var reloaded = store.Load();
        Require(reloaded.Projects.Count == 2, "Multiple project history was not retained or deduplicated.");
        Require(reloaded.Projects[0].Name == "First project renamed",
            "The most recently opened project was not promoted to the top.");
        store.Remove(reloaded, first);
        Require(Directory.Exists(first), "Removing history must not delete the project directory.");
        Require(store.Load().Projects is [{ Path: var remaining }] &&
                remaining.Equals(second, StringComparison.OrdinalIgnoreCase),
            "Removing one history item damaged the remaining project list.");

        var repository = Path.Combine(root, "MovedRepository");
        var movedProject = Path.Combine(repository, "Example");
        Directory.CreateDirectory(movedProject);
        File.WriteAllText(Path.Combine(movedProject, "Project.yaml"), "format: moved-test");
        var movedSettings = Path.Combine(root, "MovedLauncherSettings.yaml");
        File.WriteAllText(movedSettings, $"""
            format: BEngine.LauncherSettings
            version: 1
            lastProjectDirectory: {Path.Combine(repository, "Output", "Example")}
            """);
        var migrated = new LauncherHistoryStore(movedSettings).Load();
        Require(migrated.Projects is [{ Path: var migratedPath }] &&
                migratedPath.Equals(movedProject, StringComparison.OrdinalIgnoreCase),
            "The moved Output/Example project history was not migrated to the root Example directory.");
    }

    private static void VerifyAvailablePackageDiscovery(string root)
    {
        var packagesRoot = Path.Combine(root, "Packages");
        WritePackage(packagesRoot, "Animation", "com.bengine.animation", "Animation", runtime: true);
        Directory.CreateDirectory(Path.Combine(packagesRoot, "Codex"));
        File.WriteAllText(Path.Combine(packagesRoot, "Codex", "Readme.md"),
            "Codex is integrated into BEngine.Editor and is not a package.");

        var packageCatalog = new LauncherPackageCatalog();
        var packages = packageCatalog.Discover(packagesRoot);
        Require(packages.Count == 1, "The launcher treated a non-package Codex directory as a package.");
        Require(packages[0].DisplayName == "Animation" && packages[0].HasRuntime,
            "Runtime package metadata was not presented correctly.");
        Require(packageCatalog.ResolvePackagesRoot().EndsWith(
                Path.Combine("Output", "Packages"), StringComparison.OrdinalIgnoreCase),
            "The default package catalog must resolve directly to Output/Packages.");
    }

    private static void VerifyHubTabsAndCoreOnlyCreation()
    {
        using var form = new ProjectLauncherForm(Path.Combine(Path.GetTempPath(), "Missing-BEngine.Editor.exe"));
        var projects = Field<Button>(form, "_projectsNavigation");
        var packages = Field<Button>(form, "_packagesNavigation");
        Require(projects.Text == "Projects" && packages.Text == "Packages",
            "The Hub must expose Projects and Packages navigation tabs.");

        typeof(ProjectLauncherForm).GetMethod("ShowCreatePage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, null);
        Require(typeof(ProjectLauncherForm).GetField("_createPackageList",
                    BindingFlags.Instance | BindingFlags.NonPublic) is null,
            "The new-project page must not offer extension package selection.");
    }

    private static void VerifyDependencyInjectionComposition()
    {
        using var services = new ServiceCollection()
            .AddBEngineLauncher(Path.Combine(Path.GetTempPath(), "Missing-BEngine.Editor.exe"))
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        using var form = services.GetRequiredService<ProjectLauncherForm>();
        Require(form is not null, "The Launcher IoC composition root did not create its main window.");
    }

    private static void VerifyProjectTargetPath(string root)
    {
        var projectService = new LauncherProjectService();
        var target = projectService.ResolveTargetPath(root, "Clean Project");
        Require(target.Equals(Path.Combine(root, "Clean Project"), StringComparison.OrdinalIgnoreCase),
            "The Hub did not combine Location and Project name into a dedicated project folder.");
        var workspace = projectService.Create(root, "AOT Project");
        var manifest = BEngine.YamlUtility.Load<BEngine.Editor.Documents.PackageManifestDocument>(
            workspace.PackageManifestPath);
        Require(manifest.Packages.Where(package => package.Enabled).Select(package => package.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["com.bengine.ui-elements"]),
            "A project created by the Hub must enable exactly the UIElements package required by AOT.");
        try
        {
            projectService.ResolveTargetPath(root, "../Outside");
            throw new InvalidOperationException("An invalid project name escaped the selected location.");
        }
        catch (ArgumentException)
        {
        }
    }

    private static void VerifyHubSourceBoundary()
    {
        var repository = FindRepositoryRoot();
        var hubRoot = Path.Combine(repository, "src", "Hub");
        var launcherRoot = Path.Combine(hubRoot, "BEngine.Launcher");
        Require(File.Exists(Path.Combine(launcherRoot, "BEngine.Launcher.csproj")),
            "The Launcher project was not moved under src/Hub.");
        Require(!Directory.Exists(Path.Combine(repository, "src", "Core", "BEngine.Launcher")),
            "The old src/Core/BEngine.Launcher directory still exists.");

        var project = System.Xml.Linq.XDocument.Load(
            Path.Combine(launcherRoot, "BEngine.Launcher.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        Require(references.Length > 0 && references.All(reference =>
                reference.Replace('\\', '/').StartsWith("../../Core/", StringComparison.Ordinal)),
            "Hub may depend on Core only through explicit project references.");

        var coreProjects = Directory.EnumerateFiles(Path.Combine(repository, "src", "Core"), "*.csproj",
                SearchOption.AllDirectories)
            .SelectMany(path => System.Xml.Linq.XDocument.Load(path).Descendants("ProjectReference"))
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty);
        Require(coreProjects.All(reference =>
                !reference.Replace('\\', '/').Contains("/Hub/", StringComparison.OrdinalIgnoreCase)),
            "A Core assembly has a forbidden reverse dependency on Hub.");

        var nonProjectFiles = Directory.EnumerateFiles(hubRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                           !path.EndsWith(".csproj.user", StringComparison.OrdinalIgnoreCase));
        Require(nonProjectFiles.All(path => !File.ReadAllText(path).Contains("src/Core",
                StringComparison.OrdinalIgnoreCase)),
            "Hub contains a filesystem dependency on the Core source tree.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
    }

    private static T Field<T>(object instance, string name) where T : class =>
        typeof(ProjectLauncherForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance) as T ?? throw new InvalidOperationException($"Missing Hub field '{name}'.");

    private static void WritePackage(string root, string folder, string id, string name, bool runtime)
    {
        var directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        var assembly = runtime
            ? $"""
              runtime:
                assembly: BEngine.{name}
                rootNamespace: BEngine.{name}
                dependencies: []
              """
            : $"""
              editor:
                assembly: BEngine.{name}.Editor
                rootNamespace: BEngine.{name}.Editor
                dependencies: []
              """;
        File.WriteAllText(Path.Combine(directory, "package.yaml"), $"""
            format: BEngine.Package
            version: 2
            id: {id}
            packageVersion: 1.0.0
            displayName: {name}
            description: {name} package
            enabledByDefault: false
            {assembly}
            """);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
