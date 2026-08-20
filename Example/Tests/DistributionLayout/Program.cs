namespace BEngine.ExampleTests.DistributionLayout;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var root = FindRepositoryRoot();
            var output = Path.Combine(root, "Output");
            var engine = Path.Combine(output, "BEgine");
            var packages = Path.Combine(output, "Packages");
            var example = Path.Combine(root, "Example");

            Require(Directory.Exists(packages), "Output/Packages is missing.");
            Require(!Directory.Exists(Path.Combine(engine, "Packages")),
                "The core engine still contains a Packages directory.");
            Require(File.Exists(Path.Combine(example, "Project.yaml")),
                "The root Example project is missing.");
            Require(!Directory.Exists(Path.Combine(output, "Example")),
                "The legacy Output/Example directory still exists.");
            Require(!Directory.Exists(Path.Combine(output, "Doc")),
                "Output retained the legacy root Doc directory.");
            Require(!Directory.Exists(Path.Combine(output, "Skills")),
                "Output retained the legacy root Skills directory.");

            var sourceDefinitions = Directory.EnumerateFiles(Path.Combine(root, "src"), "package.yaml",
                    SearchOption.AllDirectories)
                .ToDictionary(ReadPackageId, StringComparer.OrdinalIgnoreCase);
            var exportedDefinitions = Directory.EnumerateFiles(packages, "package.yaml",
                    SearchOption.AllDirectories)
                .ToDictionary(ReadPackageId, StringComparer.OrdinalIgnoreCase);
            Require(sourceDefinitions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(exportedDefinitions.Keys),
                "Output/Packages does not match the source package catalog.");

            foreach (var (packageId, path) in sourceDefinitions)
            {
                Require(IsDisabledByDefault(path), $"Source package '{packageId}' is enabled by default.");
                Require(IsDisabledByDefault(exportedDefinitions[packageId]),
                    $"Exported package '{packageId}' is enabled by default.");
                var packageRoot = Path.GetDirectoryName(exportedDefinitions[packageId])!;
                Require(File.Exists(Path.Combine(packageRoot, "EditorResources", "Readme.md")),
                    $"Exported package '{packageId}' has no EditorResources/Readme.md.");
                Require(!File.Exists(Path.Combine(packageRoot, "Readme.md")),
                    $"Exported package '{packageId}' retained a root Readme.md.");
                Require(Directory.Exists(Path.Combine(packageRoot, "Resources")),
                    $"Exported package '{packageId}' has no Resources directory.");
                Require(Directory.Exists(Path.Combine(packageRoot, "EditorResources")),
                    $"Exported package '{packageId}' has no EditorResources directory.");
                Require(File.Exists(Path.Combine(packageRoot, "EditorResources", "Doc", "index.html")),
                    $"Exported package '{packageId}' has no EditorResources/Doc/index.html.");
                var examples = Path.Combine(packageRoot, "EditorResources", "Examples");
                Require(Directory.Exists(examples),
                    $"Exported package '{packageId}' has no EditorResources/Examples directory.");
                var exampleFiles = Directory.EnumerateFiles(examples, "*", SearchOption.AllDirectories).ToArray();
                Require(exampleFiles.Length >= 2 && exampleFiles.All(path =>
                        Path.GetExtension(path).Equals(".bpackage", StringComparison.OrdinalIgnoreCase)),
                    $"Exported package '{packageId}' must contain at least two .bpackage archives in " +
                    "EditorResources/Examples.");
                Require(!Directory.Exists(Path.Combine(packageRoot, "Examples")),
                    $"Exported package '{packageId}' retained the legacy top-level Examples directory.");
            }

            var coreExamples = Path.Combine(engine, "EditorResources", "Examples");
            Require(Directory.Exists(coreExamples) &&
                    Directory.EnumerateFiles(coreExamples, "*.bpackage", SearchOption.AllDirectories).Count() >= 2,
                "The core engine must contain at least two .bpackage examples in " +
                "Output/BEgine/EditorResources/Examples.");
            Require(File.Exists(Path.Combine(engine, "EditorResources", "Readme.md")),
                "The core engine has no Output/BEgine/EditorResources/Readme.md.");
            Require(File.Exists(Path.Combine(engine, "EditorResources", "Doc", "index.html")),
                "The core engine has no Output/BEgine/EditorResources/Doc/index.html.");
            Require(!Directory.Exists(Path.Combine(engine, "Examples")),
                "The core engine retained the legacy Output/BEgine/Examples directory.");

            Require(!File.Exists(Path.Combine(engine, "AngleSharp.dll")) &&
                    !File.Exists(Path.Combine(engine, "AngleSharp.Css.dll")),
                "UIElements dependencies leaked into the core engine directory.");
            Require(!Directory.EnumerateFiles(engine, "package.yaml", SearchOption.AllDirectories).Any(),
                "A package definition leaked into the core engine directory.");

            Console.WriteLine(
                $"DISTRIBUTION_LAYOUT_OK|packages={exportedDefinitions.Count},core-isolated,example-root,defaults-off");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DISTRIBUTION_LAYOUT_FAILED|{exception}");
            return 1;
        }
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

    private static string ReadPackageId(string path)
    {
        var line = File.ReadLines(path).FirstOrDefault(value => value.StartsWith("id:",
            StringComparison.OrdinalIgnoreCase));
        return line?[3..].Trim() is { Length: > 0 } id
            ? id
            : throw new InvalidDataException($"Package id is missing from '{path}'.");
    }

    private static bool IsDisabledByDefault(string path) => File.ReadLines(path).Any(line =>
        line.Trim().Equals("enabledByDefault: false", StringComparison.OrdinalIgnoreCase));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
