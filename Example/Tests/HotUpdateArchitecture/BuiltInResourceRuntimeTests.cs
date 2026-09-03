using System.Text;
using BEngine;
using BEngine.Content;
using BEngine.HotUpdate;
using BEngine.Player;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.HotUpdateArchitecture;

internal static class BuiltInResourceRuntimeTests
{
    internal static async Task RunAsync(string repositoryRoot)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(), $"bengine-built-in-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        PlayerBuiltInResourceProvider? provider = null;
        var fallback = new FallbackResourceProvider();
        try
        {
            const string sceneAddress = "Assets/Aot/__BuiltInRuntimeTest.scene.yaml";
            const string directAddress = "Assets/Aot/__BuiltInRuntimeTest.uxml";
            const string packageAddress =
                "Assets/Packages/com.bengine.tests/Resources/Config/built-in-value.txt";
            const string assemblyAddress = "Assets/__BEngine/HotUpdate/AOT.dll";
            var scene = new Scene("BuiltIn Archive Scene");
            var sceneBytes = Encoding.UTF8.GetBytes(SceneAssetSerialization.Serialize(scene));
            scene.Dispose();
            var manifest = new ManagedCodeReleaseManifest
            {
                ReleaseId = "built-in-runtime-test",
                Modules =
                [
                    new ManagedCodeModuleManifest
                    {
                        Name = "AOT",
                        BuildId = "built-in-runtime-test-aot",
                        AssemblyAddress = assemblyAddress
                    }
                ]
            };
            var archivePath = Path.Combine(temporaryRoot, BuiltInResourceArchive.FileName);
            BuiltInResourceArchive.Write(archivePath,
            [
                BuiltInResourceArchiveWriteEntry.FromMemory(
                    sceneAddress, sceneBytes, "SceneAsset"),
                BuiltInResourceArchiveWriteEntry.FromMemory(
                    directAddress, Encoding.UTF8.GetBytes("archive-direct"), "VisualTreeAsset"),
                BuiltInResourceArchiveWriteEntry.FromMemory(
                    packageAddress, Encoding.UTF8.GetBytes("archive-package"), "TextAsset"),
                BuiltInResourceArchiveWriteEntry.FromMemory(
                    ManagedCodeReleaseManifest.DefaultAddress,
                    ManagedCodeReleaseManifestSerializer.Serialize(manifest),
                    "ManagedCodeRelease"),
                BuiltInResourceArchiveWriteEntry.FromMemory(
                    assemblyAddress, [1, 2, 3, 4], "ManagedAssembly")
            ]);

            provider = new PlayerBuiltInResourceProvider(archivePath);
            Require(provider.TryLoad(directAddress, "Resources", out var direct) &&
                    Encoding.UTF8.GetString(direct.Bytes) == "archive-direct",
                "The built-in provider could not resolve an exact Assets address.");
            Require(provider.TryLoad("Config/built-in-value", "Resources", out var package) &&
                    Encoding.UTF8.GetString(package.Bytes) == "archive-package",
                "The built-in provider could not resolve an extensionless package Resources path.");

            Resources.RegisterResourceProvider(fallback);
            provider.Activate();
            Require(Resources.Load<string>(directAddress) == "archive-direct",
                "The active built-in provider did not take priority over an older provider.");
            provider.Deactivate();
            Require(Resources.Load<string>(directAddress) == FallbackResourceProvider.Value,
                "The built-in provider remained registered after the AOT transition.");
            provider.Activate();

            var workspace = ProjectWorkspace.Open(Path.Combine(repositoryRoot, "Example"));
            using var services = new ServiceCollection().BuildServiceProvider();
            var loader = new PlayerProjectSceneLoader(workspace, builtInResources: provider);
            using var loadedScene = loader.LoadScene(sceneAddress, services);
            Require(loadedScene.name == "BuiltIn Archive Scene" && loadedScene.path == sceneAddress,
                "The Player scene loader did not deserialize the AOT scene directly from the archive.");

            var release = await PlayerHotUpdateReleaseLoader.LoadAsync(
                    workspace, assetBundles: null, PlayerManagedCodeStage.Aot, provider)
                .ConfigureAwait(false);
            Require(release is not null && release.ReleaseId == manifest.ReleaseId &&
                    release.Modules.Count == 1 && release.Modules[0].Name == "AOT" &&
                    release.Modules[0].GetAssemblyImage().SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "The Player AOT release loader did not restore managed code from the archive.");
            var forbiddenFallback = await PlayerHotUpdateReleaseLoader.LoadAsync(
                    workspace, assetBundles: null, PlayerManagedCodeStage.HotUpdate, provider)
                .ConfigureAwait(false);
            Require(forbiddenFallback is null,
                "The HotUpdate stage incorrectly fell back to built-in or workspace managed code.");
        }
        finally
        {
            provider?.Dispose();
            Resources.UnregisterResourceProvider(fallback);
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FallbackResourceProvider : IResourceProvider
    {
        internal const string Value = "fallback-resource";

        public bool TryLoad(string path, string folderName, out ResourceContent content)
        {
            if (path.Equals("Assets/Aot/__BuiltInRuntimeTest.uxml", StringComparison.OrdinalIgnoreCase))
            {
                content = new ResourceContent(path, Encoding.UTF8.GetBytes(Value));
                return true;
            }
            content = null!;
            return false;
        }

        public IEnumerable<string> Enumerate(string path, string folderName) => [];
    }
}
