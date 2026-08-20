using System.Reflection;
using BEngine.Launcher;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.LauncherHub;

internal static class Program
{
    [STAThread]
    private static void Main()
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
            Console.WriteLine(
                "LAUNCHER_HUB_OK|projects,history-migration,packages-tab,output-catalog,target-path,ioc,default-none");
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
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
        var workspace = projectService.Create(root, "Core Only");
        var manifest = BEngine.Documents.Document.Load<BEngine.Editor.Documents.PackageManifestDocument>(
            workspace.PackageManifestPath);
        Require(manifest.Packages.Count == 0,
            "A project created by the Hub must contain no extension packages.");
        try
        {
            projectService.ResolveTargetPath(root, "../Outside");
            throw new InvalidOperationException("An invalid project name escaped the selected location.");
        }
        catch (ArgumentException)
        {
        }
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
