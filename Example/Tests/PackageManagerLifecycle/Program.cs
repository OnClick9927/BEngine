using BEngine.ProjectSystem;
using BEngine.Documents;
using BEngine.Editor.Documents;
using System.Runtime.Loader;

namespace BEngine.ExampleTests.PackageManagerLifecycle;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEnginePackageLifecycle_{Guid.NewGuid():N}");
        try
        {
            var example = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../..", "Example"));
            Directory.CreateDirectory(Path.Combine(root, "Packages"));
            File.Copy(Path.Combine(example, "Project.yaml"), Path.Combine(root, "Project.yaml"), true);
            File.Copy(Path.Combine(example, "Packages", "manifest.yaml"),
                Path.Combine(root, "Packages", "manifest.yaml"), true);
            var workspace = ProjectWorkspace.Open(root);
            var legacyManifest = Document.Load<PackageManifestDocument>(workspace.PackageManifestPath);
            legacyManifest.Packages.Add(new PackageReferenceDocument
            {
                Id = "com.bengine.codex",
                Version = "1.0.0",
                Enabled = true
            });
            legacyManifest.Save(workspace.PackageManifestPath);
            var legacyCache = Path.Combine(workspace.PackagesPath, "com.bengine.codex");
            Directory.CreateDirectory(legacyCache);
            File.WriteAllText(Path.Combine(legacyCache, "legacy.txt"), "legacy Codex package cache");
            var legacyLibraryCache = Path.Combine(workspace.LibraryPath, "Packages", "com.bengine.codex");
            Directory.CreateDirectory(legacyLibraryCache);
            File.WriteAllText(Path.Combine(legacyLibraryCache, "legacy.txt"), "legacy Library package cache");
            using (var manager = new BPackageManager(workspace))
            {
                Assert(manager.packages.Count == 0,
                    "The example project must start without extension packages.");
                PackageManagerViewSmoke.Verify(workspace, manager);
                manager.SetEnabled("com.bengine.animation", true);
                Assert(manager.IsEnabled("com.bengine.animation"), "Animation was not enabled explicitly.");
                Assert(manager.IsLoaded("com.bengine.animation"), "Animation assembly was not loaded.");
                manager.SetEnabled("com.bengine.navigation2d", true);
                Assert(manager.IsEnabled("com.bengine.navigation2d") &&
                       manager.IsEnabled("com.bengine.physics2d"),
                    "Enabling Navigation did not enable its transitive package dependencies.");
                Assert(!manager.packages.Any(package => package.Id.Equals(
                           "com.bengine.codex", StringComparison.OrdinalIgnoreCase)),
                    "The legacy Codex package remained in the in-memory manifest.");
                Assert(!manager.definitions.Any(definition => definition.Document.Id.Equals(
                           "com.bengine.codex", StringComparison.OrdinalIgnoreCase)),
                    "Codex is still discoverable as an extension package.");
                Assert(!Directory.Exists(legacyCache),
                    "The legacy Codex package cache was not removed.");
                Assert(!Directory.Exists(legacyLibraryCache),
                    "The legacy Library/Packages Codex cache was not removed.");
                var normalizedManifest = Document.Load<PackageManifestDocument>(workspace.PackageManifestPath);
                Assert(!normalizedManifest.Packages.Any(package => package.Id.Equals(
                           "com.bengine.codex", StringComparison.OrdinalIgnoreCase)),
                    "The legacy Codex package remained in the persisted manifest.");
                try
                {
                    manager.SetEnabled("com.bengine.codex", true);
                    throw new InvalidOperationException("The retired Codex package was enabled again.");
                }
                catch (KeyNotFoundException)
                {
                }

                manager.SetEnabled("com.bengine.animation", false);
                Assert(!manager.IsEnabled("com.bengine.animation"), "Animation manifest state stayed enabled.");
                Assert(!manager.IsLoaded("com.bengine.animation"), "Animation load context stayed registered.");
                Assert(AssemblyLoadContext.All.All(context => context.Name != "BEngine.Package:com.bengine.animation"),
                    "Animation collectible AssemblyLoadContext was not unloaded.");
                manager.SetEnabled("com.bengine.ui-elements", true);

                var export = Path.Combine(root, "Build");
                manager.ExportEnabledRuntimePackages(export);
                Assert(!Directory.EnumerateFiles(Path.Combine(export, "Packages"), "BEngine.Animation.dll",
                        SearchOption.AllDirectories).Any(),
                    "Disabled Animation runtime assembly was exported.");
                Assert(!Directory.EnumerateFiles(Path.Combine(export, "Packages"), "*", SearchOption.AllDirectories)
                        .Any(path => path.Contains($"{Path.DirectorySeparatorChar}Animation{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase)),
                    "Disabled Animation package content was exported.");
                Assert(Directory.EnumerateFiles(Path.Combine(export, "Packages"), "BEngine.Physics2D.dll",
                        SearchOption.AllDirectories).Any(),
                    "Enabled Physics2D runtime assembly was not exported.");
                Assert(!Directory.EnumerateFiles(Path.Combine(export, "Packages"), "*.Editor.dll",
                        SearchOption.AllDirectories).Any(),
                    "Editor package assemblies must not be exported to a Player build.");
                Assert(!Directory.EnumerateDirectories(Path.Combine(export, "Packages"), "EditorResources",
                        SearchOption.AllDirectories).Any(),
                    "EditorResources must not be exported to a Player build.");
                Assert(!Directory.EnumerateFiles(Path.Combine(export, "Packages"), "package.yaml",
                        SearchOption.AllDirectories).Any(path => File.ReadAllText(path).Contains(
                            "com.bengine.codex", StringComparison.OrdinalIgnoreCase)),
                    "The integrated Codex editor feature was exported as a Player package.");
                Assert(!Directory.EnumerateFiles(Path.Combine(export, "Packages"), "*.cs",
                        SearchOption.AllDirectories).Any(),
                    "Player package export must not contain package source files.");
                var uiElementsExport = Path.GetDirectoryName(Directory.EnumerateFiles(
                    Path.Combine(export, "Packages"), "BEngine.UIElements.dll", SearchOption.AllDirectories).Single())!;
                Assert(File.Exists(Path.Combine(uiElementsExport, "BEngine.UIElements.dll")) &&
                       File.Exists(Path.Combine(uiElementsExport, "Silk.NET.OpenGL.dll")),
                    "Player export did not include the project-built UIElements runtime and its companion dependency.");

                manager.SetEnabled("com.bengine.animation", true);
                Assert(manager.IsEnabled("com.bengine.animation") && manager.IsLoaded("com.bengine.animation"),
                    "Animation was not reloaded after enabling it.");
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Console.WriteLine(
                "PACKAGE_MANAGER_LIFECYCLE_OK|package-import,detail-tabs,example-import-reimport," +
                "legacy-codex-migration,runtime-and-editor-disable,unload,export-filter,reload");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PACKAGE_MANAGER_LIFECYCLE_FAILED|{exception}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
