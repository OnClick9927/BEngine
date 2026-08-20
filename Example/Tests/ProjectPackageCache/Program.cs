using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.ProjectPackageCache;

internal static class Program
{
    private static int Main()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"BEnginePackageCache_{Guid.NewGuid():N}");
        var previousRepository = Environment.GetEnvironmentVariable("BENGINE_PACKAGES_PATH");
        try
        {
            var brokenRepository = Path.Combine(temporaryRoot, "BrokenRepository");
            Directory.CreateDirectory(brokenRepository);
            File.WriteAllText(Path.Combine(brokenRepository, "package.yaml"), "not: a-package-definition");
            Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", brokenRepository);
            var emptyWorkspace = ProjectWorkspaceFactory.Create(
                Path.Combine(temporaryRoot, "Empty"), "Empty");
            var emptyManifest = Document.Load<PackageManifestDocument>(emptyWorkspace.PackageManifestPath);
            Require(emptyManifest.Packages.Count == 0,
                "A default project manifest contains extension packages.");
            Require(!Directory.EnumerateDirectories(emptyWorkspace.PackagesPath).Any(),
                "A default project copied extension packages into Packages.");

            var repository = Path.Combine(FindRepositoryRoot(), "Output", "Packages");
            Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", repository);
            var selectedWorkspace = ProjectWorkspaceFactory.Create(
                Path.Combine(temporaryRoot, "Selected"), "Selected", ["com.bengine.navigation2d"]);
            var selectedManifest = Document.Load<PackageManifestDocument>(selectedWorkspace.PackageManifestPath);
            var enabled = selectedManifest.Packages.Where(package => package.Enabled)
                .Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(enabled.IsSupersetOf(["com.bengine.navigation2d", "com.bengine.physics2d"]),
                "Selected package dependencies were not enabled.");
            foreach (var packageId in enabled)
            {
                var packageRoot = Path.Combine(selectedWorkspace.PackagesPath, packageId);
                Require(File.Exists(Path.Combine(packageRoot, "package.yaml")),
                    $"Enabled package '{packageId}' was not copied into project Packages.");
                Require(Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories).Any(),
                    $"Enabled package '{packageId}' contains no source files.");
                Require(!Directory.EnumerateFiles(packageRoot, "*.dll", SearchOption.AllDirectories).Any() &&
                        !Directory.EnumerateFiles(packageRoot, "*.pdb", SearchOption.AllDirectories).Any(),
                    $"Enabled package '{packageId}' contains precompiled package artifacts.");
            }

            using (var manager = new BPackageManager(selectedWorkspace))
            {
                manager.SetEnabled("com.bengine.navigation2d", false);
                Require(!Directory.Exists(Path.Combine(selectedWorkspace.PackagesPath,
                        "com.bengine.navigation2d")),
                    "Disabled package content remained in project Packages.");
                manager.SetEnabled("com.bengine.navigation2d", true);
                Require(File.Exists(Path.Combine(selectedWorkspace.PackagesPath,
                        "com.bengine.navigation2d", "package.yaml")),
                    "Re-enabled package was not restored from Output/Packages.");
            }

            Require(BPackageRepository.rootPath.Equals(repository, StringComparison.OrdinalIgnoreCase),
                "The package repository did not resolve to Output/Packages.");

            var failedRoot = Path.Combine(temporaryRoot, "Failed");
            try
            {
                ProjectWorkspaceFactory.Create(failedRoot, "Failed", ["com.bengine.missing"]);
                throw new InvalidOperationException("Creating a project with a missing package unexpectedly succeeded.");
            }
            catch (KeyNotFoundException)
            {
            }
            Require(!Directory.Exists(failedRoot),
                "A failed project creation left a partially initialized project directory.");
            Require(!Directory.EnumerateDirectories(temporaryRoot, ".bengine-project-*", SearchOption.TopDirectoryOnly)
                    .Any(),
                "A failed project creation left a staging directory behind.");
            Console.WriteLine(
                "PROJECT_PACKAGE_CACHE_OK|default-none,repository-independent,selection,dependencies,copy,delete,restore,transaction");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PROJECT_PACKAGE_CACHE_FAILED|{exception}");
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", previousRepository);
            if (Directory.Exists(temporaryRoot))
            {
                try { Directory.Delete(temporaryRoot, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
