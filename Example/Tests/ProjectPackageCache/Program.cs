using BEngine.Editor.Documents;
using BEngine.Editor;
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
            var repository = Path.Combine(FindRepositoryRoot(), "Output", "Packages");
            Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", repository);
            var defaultWorkspace = ProjectWorkspaceFactory.Create(
                Path.Combine(temporaryRoot, "Default"), "Default");
            VerifyDefaultAotProject(defaultWorkspace);
            VerifyAotRecovery(defaultWorkspace);

            var selectedWorkspace = ProjectWorkspaceFactory.Create(
                Path.Combine(temporaryRoot, "Selected"), "Selected", ["com.bengine.navigation2d"]);
            var selectedManifest = BEngine.YamlUtility.Load<PackageManifestDocument>(selectedWorkspace.PackageManifestPath);
            var enabled = selectedManifest.Packages.Where(package => package.Enabled)
                .Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(enabled.IsSupersetOf([
                    "com.bengine.ui-elements", "com.bengine.navigation2d", "com.bengine.physics2d"
                ]),
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

            VerifyCommandLineBuildPreparation(defaultWorkspace);

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
                "PROJECT_PACKAGE_CACHE_OK|default-aot,aot-layout,aot-recovery,uielements-required," +
                "selection,dependencies,copy,delete,restore,cli-build,transaction");
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

    private static void VerifyCommandLineBuildPreparation(ProjectWorkspace workspace)
    {
        var packageRoot = Path.Combine(workspace.PackagesPath, "com.bengine.ui-elements");
        Directory.Delete(packageRoot, true);
        Require(File.Exists(workspace.PackageManifestPath) && !Directory.Exists(packageRoot),
            "The command-line build fixture was not reduced to a manifest-only package configuration.");

        BEngine.Editor.Program.PrepareProjectForCommandLineBuild(workspace);

        Require(File.Exists(Path.Combine(packageRoot, "package.yaml")),
            "Command-line build preparation did not synchronize UIElements from Output/Packages.");
        Require(Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories).Any(),
            "Command-line build preparation restored no UIElements source files.");
        Require(ScriptAssemblyStore.ResolveCurrentPath(workspace, "AOT") is { } assemblyPath &&
                File.Exists(assemblyPath),
            "Command-line build preparation did not compile the AOT project assembly.");
    }

    private static void VerifyDefaultAotProject(ProjectWorkspace workspace)
    {
        var manifest = BEngine.YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath);
        Require(manifest.Packages.Where(package => package.Enabled).Select(package => package.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["com.bengine.ui-elements"]),
            "A default project did not enable exactly the UIElements package required by AOT.");
        Require(File.Exists(Path.Combine(workspace.PackagesPath,
                "com.bengine.ui-elements", "package.yaml")),
            "A default project did not cache its required UIElements package.");

        var aotRoot = Path.Combine(workspace.AssetsPath, "Aot");
        var expectedRootAssets = new[]
        {
            "AOT.scene.yaml", "AOT.asmdef.yaml"
        };
        var aotUiRoot = Path.Combine(aotRoot, "UI");
        var expectedUiAssets = new[] { "AOT.uxml", "AOT.uss", "AotStartupView.cs", "BEngine.png" };
        Require(File.Exists(aotRoot + ".meta") && File.Exists(aotUiRoot + ".meta") &&
                expectedRootAssets.All(asset =>
                 File.Exists(Path.Combine(aotRoot, asset)) &&
                 File.Exists(Path.Combine(aotRoot, asset) + ".meta")) &&
                expectedUiAssets.All(asset =>
                 File.Exists(Path.Combine(aotUiRoot, asset)) &&
                 File.Exists(Path.Combine(aotUiRoot, asset) + ".meta")),
            "A default project did not create the complete AOT asset set and metadata.");
        var aotView = File.ReadAllText(Path.Combine(aotUiRoot, "AotStartupView.cs"));
        var aotUxml = File.ReadAllText(Path.Combine(aotUiRoot, "AOT.uxml"));
        Require(File.ReadAllText(Path.Combine(aotRoot, "AOT.asmdef.yaml"))
                .Contains("BEngine.UIElements", StringComparison.Ordinal) &&
                File.ReadAllText(Path.Combine(aotRoot, "AOT.scene.yaml")) is var aotScene &&
                aotScene.Contains("version: 2", StringComparison.Ordinal) &&
                aotScene.Contains("AOT.AotStartupView", StringComparison.Ordinal) &&
                aotView.Contains("IAotStartupFlow", StringComparison.Ordinal) &&
                aotView.Contains("CheckForUpdates()", StringComparison.Ordinal) &&
                aotView.Contains("ConfirmUpdate()", StringComparison.Ordinal) &&
                aotView.Contains("DeclineUpdate()", StringComparison.Ordinal) &&
                aotView.Contains("CanDeclineUpdate", StringComparison.Ordinal) &&
                aotView.Contains("Remote target version:", StringComparison.Ordinal) &&
                new[] { "CheckForUpdatesButton", "UpdateConfirmDialog", "ConfirmUpdateButton",
                    "CancelUpdateButton", "EnterGameButton" }.All(name =>
                    aotUxml.Contains($"name=\"{name}\"", StringComparison.Ordinal)),
            "The generated AOT assembly, scene, and startup controller are not wired together.");

        var settings = PlayerBuildSettingsStore.Load(workspace.RootPath);
        Require(PlayerBuildSettingsStore.GetEnabledScenes(settings) is
                    ["Assets/Aot/AOT.scene.yaml"] &&
                settings.SplashImage == AotProjectLayout.LogoAssetPath &&
                settings.HotResourceVersion == "v1" &&
                File.ReadAllText(Path.Combine(workspace.ProjectSettingsPath,
                        PlayerBuildSettingsStore.FileName))
                    .Contains("hotResourceVersion: v1", StringComparison.Ordinal),
            "A default project must build only AOT, use its logo, and persist Hot Resources v1.");
        Require(workspace.Project.StartupScene == "Assets/Scenes/Main.scene.yaml",
            "The source project startup scene must remain the hot-update game entry scene.");

        var sourceLogo = Path.Combine(FindRepositoryRoot(), "src", "Core", "Editor", "Icons", "BEngine.png");
        Require(File.ReadAllBytes(sourceLogo).SequenceEqual(
                File.ReadAllBytes(Path.Combine(aotUiRoot, "BEngine.png"))),
            "The generated AOT logo does not match the engine-provided BEngine logo.");
    }

    private static void VerifyAotRecovery(ProjectWorkspace workspace)
    {
        var scenePath = workspace.ResolveInside(AotProjectLayout.SceneAssetPath);
        var uiPath = workspace.ResolveInside(AotProjectLayout.UiDocumentAssetPath);
        File.Delete(scenePath);
        File.Delete(scenePath + ".meta");
        File.Delete(uiPath);
        File.Delete(uiPath + ".meta");
        var customUiPath = workspace.ResolveInside(AotProjectLayout.AssetRoot + "/CustomBootstrap.uxml");
        const string customUi = "<UXML><Label text=\"Custom AOT\" /></UXML>";
        File.WriteAllText(customUiPath, customUi);

        ProjectWorkspaceFactory.EnsureRequiredAotInvariants(workspace);

        Require(File.Exists(scenePath) && File.Exists(scenePath + ".meta") &&
                !File.Exists(uiPath) && !File.Exists(uiPath + ".meta") &&
                File.ReadAllText(scenePath).Contains("gameObjects: []", StringComparison.Ordinal) &&
                File.ReadAllText(customUiPath) == customUi,
            "AOT invariant repair did not restore the fixed scene or unexpectedly restored/changed custom UI.");
        Require(AotProjectLayout.IsProtectedAssetPath("assets/AOT") &&
                AotProjectLayout.IsProtectedAssetPath("Assets\\Aot\\AOT.scene.yaml") &&
                AotProjectLayout.IsProtectedAssetPath("Assets/Aot/../Aot/AOT.scene.yaml") &&
                !AotProjectLayout.IsProtectedAssetPath(AotProjectLayout.UiDocumentAssetPath),
            "The AOT protected-path policy is not normalized or is broader than the fixed identities.");
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
