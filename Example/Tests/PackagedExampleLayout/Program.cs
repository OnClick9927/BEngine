using System.Reflection;
using System.Security.Cryptography;
using BEngine.Editor;

namespace BEngine.ExampleTests.PackagedExampleLayout;

internal static class Program
{
    private static readonly Module[] Modules =
    [
        new("Core", "Core", true, ["CoreGettingStarted.bpackage"]),
        new("Animation", "Animation", false,
            ["AnimationGettingStarted.bpackage", "StateMachine.bpackage"]),
        new("Navigation2D", "Navigation2D", false,
            ["DynamicRebake.bpackage", "NavigationSurfaceAndAgent.bpackage"]),
        new("Physics2D", "Physics2D", false,
            ["RigidbodyAndQueries.bpackage", "TriggersAndQueries.bpackage"]),
        new("PropertyAttributes", "PropertyAttributes", false,
            ["AttributesGallery.bpackage", "InspectorAttributesAndDrawer.bpackage"]),
        new("TiledMap", "TiledMap", false,
            ["AtlasPalette.bpackage", "RuntimePainting.bpackage"]),
        new("UIElements", "UIElements", false,
            ["ControlsGallery.bpackage", "RuntimeHud.bpackage"])
    ];

    private static int Main()
    {
        try
        {
            var repositoryRoot = FindRepositoryRoot();
            foreach (var module in Modules) VerifyModule(repositoryRoot, module);

            Console.WriteLine(
                "PACKAGED_EXAMPLE_LAYOUT_OK|core,6-packages,editor-resources-only,expected-archives," +
                "yaml-manifest,release-mirror,package-manager-discovery");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PACKAGED_EXAMPLE_LAYOUT_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyModule(string repositoryRoot, Module module)
    {
        var sourceRoot = Path.Combine(repositoryRoot, "src", module.SourceDirectory);
        var sourceExamples = Path.Combine(sourceRoot, "EditorResources", "Examples");
        var legacySourceExamples = Path.Combine(sourceRoot, "Examples");
        var releasedRoot = module.IsCore
            ? Path.Combine(repositoryRoot, "Output", "BEgine")
            : Path.Combine(repositoryRoot, "Output", "Packages", module.SourceDirectory);
        var releasedExamples = Path.Combine(releasedRoot, "EditorResources", "Examples");
        var legacyReleasedExamples = Path.Combine(releasedRoot, "Examples");

        Require(Directory.Exists(sourceExamples),
            $"{module.Name} source EditorResources/Examples is missing.");
        Require(!Directory.Exists(legacySourceExamples),
            $"{module.Name} retained the legacy source Examples directory.");
        Require(Directory.Exists(releasedExamples),
            $"{module.Name} release EditorResources/Examples is missing.");
        Require(!Directory.Exists(legacyReleasedExamples),
            $"{module.Name} retained the legacy released Examples directory.");

        var sourceArchives = ReadArchives(sourceExamples, module.Name, "source");
        var releasedArchives = ReadArchives(releasedExamples, module.Name, "release");
        Require(sourceArchives.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(module.ExpectedArchives),
            $"{module.Name} source examples do not match the expected package examples: " +
            string.Join(", ", module.ExpectedArchives));
        Require(sourceArchives.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(releasedArchives.Keys),
            $"{module.Name} released example names do not match source examples.");

        foreach (var (fileName, source) in sourceArchives)
        {
            var released = releasedArchives[fileName];
            Require(source.Hash.Equals(released.Hash, StringComparison.Ordinal),
                $"{module.Name}/{fileName} differs between source and release output.");
        }

        VerifyPackageManagerDiscovery(sourceExamples, sourceArchives.Count, module.Name, "source");
        VerifyPackageManagerDiscovery(releasedExamples, releasedArchives.Count, module.Name, "release");
    }

    private static Dictionary<string, Archive> ReadArchives(string directory, string module, string location)
    {
        var unexpected = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetExtension(path).Equals(".bpackage", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Require(unexpected.Length == 0,
            $"{module} {location} examples contain non-.bpackage files: {string.Join(", ", unexpected)}");

        var result = new Dictionary<string, Archive>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*.bpackage", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var manifest = BPackageArchive.ReadManifest(path);
            Require(manifest.Format == "BEngine.BPackage" && manifest.Version == 1,
                $"{module} archive '{path}' has an invalid YAML manifest format/version.");
            Require(!string.IsNullOrWhiteSpace(manifest.Name) &&
                    !string.IsNullOrWhiteSpace(manifest.PackageVersion) && manifest.FileCount > 0,
                $"{module} archive '{path}' has an incomplete YAML manifest.");
            Require(manifest.Entries.All(entry =>
                    !Path.IsPathRooted(entry.RelativePath) &&
                    !entry.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Contains("..", StringComparer.Ordinal)),
                $"{module} archive '{path}' contains an unsafe manifest path.");

            var fileName = Path.GetFileName(path);
            Require(result.TryAdd(fileName, new Archive(manifest, ComputeHash(path))),
                $"{module} contains duplicate example archive name '{fileName}'.");
        }
        return result;
    }

    private static void VerifyPackageManagerDiscovery(
        string directory,
        int expectedCount,
        string module,
        string location)
    {
        var application = typeof(BPackageArchive).Assembly.GetType(
            "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
        var windowType = application.GetNestedType(
            "ImGuiPackageManagerWindow", BindingFlags.NonPublic) ??
            throw new TypeLoadException("GpuEditorApplication.ImGuiPackageManagerWindow was not found.");
        var window = Activator.CreateInstance(windowType, nonPublic: true) ??
                     throw new InvalidOperationException("Package Manager window could not be created.");
        var getExamples = windowType.GetMethod(
            "GetExamples", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(windowType.FullName, "GetExamples");
        var discovered = getExamples.Invoke(window, [directory]) as Array ??
                         throw new InvalidOperationException("Package Manager returned no example collection.");

        Require(discovered.Length == expectedCount,
            $"Package Manager discovered {discovered.Length}/{expectedCount} {module} {location} examples.");
        foreach (var item in discovered)
        {
            var itemType = item!.GetType();
            Require(itemType.GetProperty("Manifest")?.GetValue(item) is BPackageManifest,
                $"Package Manager could not read a {module} {location} example manifest.");
            Require(string.IsNullOrEmpty(itemType.GetProperty("Error")?.GetValue(item) as string),
                $"Package Manager reported an invalid {module} {location} example.");
        }
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Output")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Module(
        string Name,
        string SourceDirectory,
        bool IsCore,
        string[] ExpectedArchives);
    private sealed record Archive(BPackageManifest Manifest, string Hash);
}
