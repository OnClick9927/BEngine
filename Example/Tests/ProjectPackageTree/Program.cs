using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.ProjectPackageTree;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineProjectPackageTree_{Guid.NewGuid():N}");
        try
        {
            var projectRoot = Path.Combine(root, "Project");
            var assetsRoot = Path.Combine(projectRoot, "Assets");
            Directory.CreateDirectory(projectRoot);
            new ProjectDocument { Name = "Package Tree Test" }.Save(Path.Combine(projectRoot, "Project.yaml"));
            var workspace = ProjectWorkspace.Open(projectRoot);

            var enabledRoot = CreatePackage(root, "Enabled", "com.test.enabled", "Enabled Package");
            var disabledRoot = CreatePackage(root, "Disabled", "com.test.disabled", "Disabled Package");
            Directory.CreateDirectory(Path.Combine(enabledRoot, "Resources", "Shaders"));
            Directory.CreateDirectory(Path.Combine(enabledRoot, "Editor", "Icons"));
            Directory.CreateDirectory(Path.Combine(enabledRoot, "bin"));
            Directory.CreateDirectory(Path.Combine(enabledRoot, "obj"));
            File.WriteAllText(Path.Combine(enabledRoot, "Resources", "Shaders", "Default.shader"), "shader");
            File.WriteAllText(Path.Combine(enabledRoot, "Editor", "Icons", "Package.png"), "image");
            File.WriteAllText(Path.Combine(enabledRoot, "bin", "Ignored.dll"), "ignored");
            File.WriteAllText(Path.Combine(enabledRoot, "obj", "Ignored.cache"), "ignored");
            File.WriteAllText(Path.Combine(enabledRoot, "Ignored.meta"), "ignored");

            new PackageManifestDocument
            {
                Packages =
                [
                    new() { Id = "com.test.enabled", Version = "2.3.4", Enabled = true },
                    new() { Id = "com.test.disabled", Version = "1.0.0", Enabled = false },
                    new() { Id = "com.test.missing", Version = "9.0.0", Enabled = true }
                ]
            }.Save(workspace.PackageManifestPath);

            var catalog = new BPackageCatalog(
            [
                Path.Combine(enabledRoot, "package.yaml"),
                Path.Combine(disabledRoot, "package.yaml")
            ]);
            using var packages = new BPackageManager(workspace, catalog, loadAssemblies: false);
            var asset = new AssetRecord(Guid.NewGuid(), "Assets/Scripts", Path.Combine(assetsRoot, "Scripts"),
                Path.Combine(assetsRoot, "Scripts.meta"), string.Empty, "Folder", string.Empty, true);
            var beforeMeta = Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories).ToArray();

            var items = ProjectBrowserTreeBuilder.Build(assetsRoot, [asset], packages);

            Require(items[0].VirtualPath == "Assets", "Assets root must be first.");
            var packagesIndex = Array.FindIndex(items, item => item.VirtualPath == "Packages");
            Require(packagesIndex > 0 && items.Take(packagesIndex).All(item => !item.IsPackage),
                "Packages root must be rendered after every Assets item.");
            Require(items.Any(item => item.VirtualPath == "Packages/com.test.enabled" &&
                                      item.DisplayName == "Enabled Package" &&
                                      item.PackageVersion == "2.3.4"),
                "Enabled package metadata was not represented.");
            Require(items.All(item => !item.VirtualPath.StartsWith("Packages/com.test.disabled",
                    StringComparison.OrdinalIgnoreCase)),
                "Disabled package appeared in the Project tree.");
            Require(items.Any(item => item.VirtualPath ==
                                      "Packages/com.test.enabled/Resources/Shaders/Default.shader" &&
                                      item.AssetType == "Shader"),
                "Package resource hierarchy or resource type was lost.");
            Require(items.Any(item => item.VirtualPath ==
                                      "Packages/com.test.enabled/Editor/Icons/Package.png" &&
                                      item.ParentPath == "Packages/com.test.enabled/Editor/Icons"),
                "Editor hierarchy was not represented accurately.");
            Require(items.Any(item => item.VirtualPath == "Packages/com.test.missing" &&
                                      item.AssetType == "Missing Package"),
                "Missing enabled package did not receive a visible diagnostic row.");
            Require(items.All(item => !item.VirtualPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                                      !item.VirtualPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
                                      !item.VirtualPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)),
                "Generated or metadata files leaked into the package tree.");
            var afterMeta = Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories).ToArray();
            Require(beforeMeta.SequenceEqual(afterMeta, StringComparer.OrdinalIgnoreCase),
                "Building the package tree wrote metadata into package directories.");

            Console.WriteLine(
                "PROJECT_PACKAGE_TREE_OK|enabled-only,packages-last,deep-hierarchy,missing-package,readonly-scan");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PROJECT_PACKAGE_TREE_FAILED|{exception}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string CreatePackage(string root, string folderName, string id, string displayName)
    {
        var packageRoot = Path.Combine(root, "Installed", folderName);
        Directory.CreateDirectory(packageRoot);
        new PackageDefinitionDocument
        {
            Id = id,
            DisplayName = displayName,
            PackageVersion = "1.0.0",
            EnabledByDefault = false,
            Runtime = new PackageAssemblyDocument
            {
                Assembly = $"BEngine.Test.{folderName}",
                RootNamespace = $"BEngine.Test.{folderName}"
            }
        }.Save(Path.Combine(packageRoot, "package.yaml"));
        return packageRoot;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
