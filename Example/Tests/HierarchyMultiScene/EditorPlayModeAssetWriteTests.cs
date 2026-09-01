using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class EditorPlayModeAssetWriteTests
{
    public static void Run(SceneFixture fixture)
    {
        var assetsBeforeFixture = AssetTreeSnapshot.Capture(fixture.Workspace.AssetsPath);
        var seedPath = Path.Combine(fixture.Workspace.AssetsPath, "Scenes", "PlayWriteBarrierSeed.txt");
        var movedSeedPath = Path.Combine(fixture.Workspace.AssetsPath, "Scenes", "PlayWriteBarrierMoved.txt");
        var createdAssetPath = Path.Combine(fixture.Workspace.AssetsPath, "Scenes", "PlayWriteBarrier.asset.yaml");
        var createdFolderPath = Path.Combine(fixture.Workspace.AssetsPath, "PlayWriteBarrierFolder");
        var prefabPath = Path.Combine(fixture.Workspace.AssetsPath, "Scenes", "PlayWriteBarrier.prefab.yaml");
        var atlasSourcePath = Path.Combine(fixture.Workspace.AssetsPath, "Scenes", "PlayWriteBarrierSource.png");
        var atlasPath = Path.Combine(fixture.Workspace.AssetsPath, "Scenes", "PlayWriteBarrier.atlas.yaml");
        var atlasOutputPath = Path.Combine(fixture.Workspace.AssetsPath, "Scenes", "PlayWriteBarrier.png");
        File.WriteAllText(seedPath, "Play Mode project write barrier seed");
        File.WriteAllBytes(atlasSourcePath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAErSURBVFhH7ZbNSsNAEMdz9izqO/guIvVUta/ipSDizdL6IDkZyGaS/Ui1SaxtwI+K0DcZd8Kcag4B0z3tDwYyf4bJ7EwybODxeP6DUOpU6ucbADhkyS1JZiaf31sUYIYsuUXIfFq8rjHJ9BVLbqEOUAEiVZcsucUX4AtIpLkvl7UtQJ+x1B+0ZESmH6zN6KR/LFUT++KVygu0zxH5rXGZeUxSPY4ic8Spu5FIffv1s8ViuUZq864tqhVKU2AMGvNF1fhtceVbjfX7BgHMOafuRhzPj2nBkNGMdw2kGdpF9JS/VDSCaeO3xunrVM0HYVgecOr+2Os30AWaMbWZTsqSW3wBvgBQ+az5C0COWHILZPqu/tigEGrAklvsXfDELqqLMAz7XzIejxuC4BeQX1VtPm/zcgAAAABJRU5ErkJggg=="));
        File.WriteAllText(atlasSourcePath + ".meta", """
            format: BEngine.AssetMeta
            version: 1
            guid: e612a4689d7441aa86b0544b36533bc3
            importer: TextureImporter
            assetType: Texture
            sourceHash: ''
            settings:
              textureType: Sprite
              spritePivotX: '0.5'
              spritePivotY: '0.5'
            """);
        BAsset.Invalidate(atlasSourcePath);
        var sourceTexture = BAsset.Load<Texture>(atlasSourcePath) ??
                            throw new InvalidOperationException("The play-mode Atlas source did not load.");
        var sourceSprite = sourceTexture.CreateSprite(new Vector2(Fix64.Half, Fix64.Half));
        sourceSprite.name = "PlayWriteBarrier";
        var atlas = new TextureAtlas
        {
            MaxSize = 64,
            Sources = [sourceSprite]
        };
        atlas.Save(atlasPath);

        try
        {
            using var harness = new EditorApplicationHarness(fixture);
            var assetsBeforePlay = AssetTreeSnapshot.Capture(fixture.Workspace.AssetsPath);
            try
            {
                harness.EnterPlay();
                var runtimeRoot = harness.ActiveScene.Find("First Root") ??
                                  throw new InvalidOperationException(
                                      "The project write-barrier fixture lost its runtime root.");

                var createAssetBlocked = IsPlayModeWriteRejected(() =>
                    AssetDatabase.CreateAsset(
                        new BEngine.TextAsset("runtime asset", "PlayWriteBarrier.txt"),
                        "Assets/Scenes/PlayWriteBarrier.asset.yaml"));
                var createFolderBlocked = IsPlayModeWriteRejected(() =>
                    AssetDatabase.CreateFolder("Assets", "PlayWriteBarrierFolder"));
                var deleteBlocked = IsPlayModeWriteRejected(() =>
                    AssetDatabase.DeleteAsset("Assets/Scenes/PlayWriteBarrierSeed.txt"));
                var moveBlocked = IsPlayModeWriteRejected(() =>
                    AssetDatabase.MoveAsset(
                        "Assets/Scenes/PlayWriteBarrierSeed.txt",
                        "Assets/Scenes/PlayWriteBarrierMoved.txt"));
                var refreshBlocked = IsPlayModeWriteRejected(() => AssetDatabase.Refresh());
                var saveAssetsBlocked = IsPlayModeWriteRejected(AssetDatabase.SaveAssets);
                var prefabBlocked = IsPlayModeWriteRejected(() =>
                    PrefabUtility.SaveAsPrefabAsset(runtimeRoot, "Assets/Scenes/PlayWriteBarrier.prefab.yaml"));
                var atlasBuildBlocked = IsPlayModeWriteRejected(() =>
                    TextureAtlasBuilder.Build(atlas, atlasPath));

                assetsBeforePlay.RequireCurrent(fixture.Workspace.AssetsPath,
                    "Play Mode project and Prefab write attempts");
                TestAssert.Require(createAssetBlocked && createFolderBlocked && deleteBlocked && moveBlocked &&
                                   refreshBlocked && saveAssetsBlocked && prefabBlocked && atlasBuildBlocked &&
                                   !File.Exists(atlasOutputPath) && atlas.Width == 0 && atlas.Height == 0,
                    "A project or Prefab write API did not explicitly reject its Play Mode operation.");
            }
            finally
            {
                if (harness.IsPlaying) harness.ExitPlay();
            }

            assetsBeforePlay.RequireCurrent(fixture.Workspace.AssetsPath,
                "Stopping Play Mode after project write attempts");
        }
        finally
        {
            DeleteFileAndMeta(seedPath);
            DeleteFileAndMeta(movedSeedPath);
            DeleteFileAndMeta(createdAssetPath);
            DeleteFileAndMeta(prefabPath);
            DeleteFileAndMeta(atlasSourcePath);
            DeleteFileAndMeta(atlasPath);
            DeleteFileAndMeta(atlasOutputPath);
            DeleteDirectoryAndMeta(createdFolderPath);
            assetsBeforeFixture.RequireCurrent(fixture.Workspace.AssetsPath,
                "Project write-barrier fixture cleanup");
        }
    }

    private static bool IsPlayModeWriteRejected(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message.Contains("Play Mode", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void DeleteFileAndMeta(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
    }

    private static void DeleteDirectoryAndMeta(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
    }

    private sealed record AssetTreeSnapshot(string[] Directories, IReadOnlyDictionary<string, byte[]> Files)
    {
        internal static AssetTreeSnapshot Capture(string root)
        {
            var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Select(path => Relative(root, path))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => Relative(root, path), StringComparer.Ordinal)
                .ToDictionary(path => Relative(root, path), File.ReadAllBytes, StringComparer.Ordinal);
            return new AssetTreeSnapshot(directories, files);
        }

        internal void RequireCurrent(string root, string operation)
        {
            var current = Capture(root);
            TestAssert.Require(Directories.SequenceEqual(current.Directories, StringComparer.Ordinal),
                $"{operation} changed the Assets directory layout. " +
                $"Expected [{string.Join(", ", Directories)}], actual [{string.Join(", ", current.Directories)}].");
            TestAssert.Require(Files.Keys.SequenceEqual(current.Files.Keys, StringComparer.Ordinal),
                $"{operation} changed the Assets file layout. Added " +
                $"[{string.Join(", ", current.Files.Keys.Except(Files.Keys, StringComparer.Ordinal))}], removed " +
                $"[{string.Join(", ", Files.Keys.Except(current.Files.Keys, StringComparer.Ordinal))}].");
            foreach (var (path, expectedBytes) in Files)
            {
                TestAssert.Require(expectedBytes.SequenceEqual(current.Files[path]),
                    $"{operation} changed the bytes of Assets/{path}.");
            }
        }

        private static string Relative(string root, string path) =>
            Path.GetRelativePath(root, path).Replace('\\', '/');
    }
}
