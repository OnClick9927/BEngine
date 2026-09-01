using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal sealed class AssetBundleTestWorkspace : IDisposable
{
    internal const string PackageName = "com.bengine.tests.hotupdate";
    internal const string SharedAddress = "Assets/Data/shared.txt";
    internal const string BinaryAddress = "Assets/Data/payload.bin";
    internal const string ResourceAddress = "Assets/Resources/config.txt";
    internal const string SceneAddress = "Assets/Scenes/main.scene.yaml";
    internal const string SpriteAddress = "Assets/Sprites/bundled.png";
    internal const string AtlasAddress = "Assets/Atlases/bundled.atlas.yaml";
    internal const long AtlasTextureLocalIdentifier = 2800000;

    private readonly string _sharedPath;
    private readonly string _spritePath;
    private readonly string _atlasTexturePath;
    private readonly string _previousDataPath;

    internal AssetBundleTestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), $"BEngineAssetBundleHotUpdate_{Guid.NewGuid():N}");
        Workspace = ProjectWorkspaceFactory.Create(Path.Combine(Root, "Project"), "Asset Bundle Hot Update");
        var dataDirectory = Path.Combine(Workspace.AssetsPath, "Data");
        var sceneDirectory = Path.Combine(Workspace.AssetsPath, "Scenes");
        var resourcesDirectory = Path.Combine(Workspace.AssetsPath, "Resources");
        var spriteDirectory = Path.Combine(Workspace.AssetsPath, "Sprites");
        var atlasDirectory = Path.Combine(Workspace.AssetsPath, "Atlases");
        var editorDirectory = Path.Combine(Workspace.AssetsPath, "Editor");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(sceneDirectory);
        Directory.CreateDirectory(resourcesDirectory);
        Directory.CreateDirectory(spriteDirectory);
        Directory.CreateDirectory(atlasDirectory);
        Directory.CreateDirectory(editorDirectory);
        _previousDataPath = Application.dataPath;
        Application.dataPath = Workspace.AssetsPath;
        _sharedPath = Path.Combine(dataDirectory, "shared.txt");
        File.WriteAllText(_sharedPath, "shared-v1");
        File.WriteAllBytes(Path.Combine(dataDirectory, "payload.bin"), [0, 1, 2, 3, 4, 255]);
        File.WriteAllText(Path.Combine(resourcesDirectory, "config.txt"), "resource-v1");
        SpriteBytes = CreateSpritePng();
        _spritePath = Path.Combine(spriteDirectory, "bundled.png");
        File.WriteAllBytes(_spritePath, SpriteBytes);
        File.WriteAllText(Path.Combine(editorDirectory, "ignored.txt"), "editor-only");
        AssetDatabase = new ProjectAssetDatabase(Workspace);
        _ = AssetDatabase.Refresh();
        var spriteMeta = BEngine.YamlUtility.Load<AssetMetaDocument>(_spritePath + ".meta");
        spriteMeta.Settings["textureType"] = "Sprite";
        spriteMeta.Settings["spritePivotX"] = "0.25";
        spriteMeta.Settings["spritePivotY"] = "0.75";
        BEngine.YamlUtility.Save(spriteMeta, _spritePath + ".meta");
        WriteScene("Bundled Scene", "Bundled Root");
        _ = AssetDatabase.Refresh();
        SpriteArtifactBytes = File.ReadAllBytes(
            AssetDatabase.GetRecord(SpriteAddress)?.ArtifactPath ??
            throw new InvalidOperationException("The imported Sprite Artifact is missing."));

        var sourceTexture = BAsset.Load<Texture>(_spritePath) ??
                            throw new InvalidOperationException("The atlas source Texture could not be loaded.");
        var atlas = new TextureAtlas
        {
            MaxSize = 64,
            Sources = [sourceTexture.CreateSprite(new Vector2((Fix64)0.25, (Fix64)0.75))]
        };
        var atlasPath = Path.Combine(atlasDirectory, "bundled.atlas.yaml");
        atlas.Save(atlasPath);
        var atlasBuild = TextureAtlasBuilder.Build(atlas, atlasPath);
        _ = AssetDatabase.Refresh();
        var atlasRecord = AssetDatabase.GetRecord(AtlasAddress) ??
                          throw new InvalidOperationException("The TextureAtlas record is missing.");
        var atlasTextureRecord = AssetDatabase.assets.SingleOrDefault(record =>
            record.ParentGuid == atlasRecord.Guid &&
            record.LocalIdentifier == AtlasTextureLocalIdentifier) ??
                                 throw new InvalidOperationException(
                                     "The generated TextureAtlas Texture record is missing.");
        var atlasTextureMeta = BEngine.YamlUtility.Load<AssetMetaDocument>(
            atlasTextureRecord.SourcePath + ".meta");
        atlasTextureMeta.Settings[nameof(TextureImporter.sRGBTexture)] = bool.FalseString;
        atlasTextureMeta.Settings[nameof(TextureImporter.alphaIsTransparency)] = bool.FalseString;
        atlasTextureMeta.Settings[nameof(TextureImporter.isReadable)] = bool.TrueString;
        atlasTextureMeta.Settings[nameof(TextureImporter.compressionFormat)] =
            TextureCompressionFormat.Bc7.ToString();
        atlasTextureMeta.Settings[nameof(TextureImporter.filterMode)] = TextureFilterMode.Point.ToString();
        atlasTextureMeta.Settings[nameof(TextureImporter.wrapMode)] = TextureWrapMode.Mirror.ToString();
        atlasTextureMeta.Settings[nameof(TextureImporter.generateMipMaps)] = bool.TrueString;
        atlasTextureMeta.Settings[nameof(TextureImporter.maxTextureSize)] = "1024";
        atlasTextureMeta.Settings[nameof(TextureImporter.pixelsPerUnit)] = "64";
        BEngine.YamlUtility.Save(atlasTextureMeta, atlasTextureRecord.SourcePath + ".meta");
        _ = AssetDatabase.Refresh();
        atlasTextureRecord = AssetDatabase.assets.Single(record =>
            record.ParentGuid == atlasRecord.Guid &&
            record.LocalIdentifier == AtlasTextureLocalIdentifier);
        _atlasTexturePath = atlasTextureRecord.ArtifactPath;
        AtlasOwnerGuid = atlasRecord.Guid;
        AtlasTextureReference = atlasBuild.TextureAssetPath;
        AtlasTextureAddress = atlasBuild.TextureAssetPath;
        AtlasWidth = atlasBuild.Width;
        AtlasHeight = atlasBuild.Height;
        AtlasTextureBytes = File.ReadAllBytes(atlasTextureRecord.ArtifactPath);
    }

    internal string Root { get; }
    internal ProjectWorkspace Workspace { get; }
    internal ProjectAssetDatabase AssetDatabase { get; }
    internal byte[] SpriteBytes { get; }
    internal byte[] SpriteArtifactBytes { get; }
    internal Guid AtlasOwnerGuid { get; }
    internal string AtlasTextureReference { get; }
    internal string AtlasTextureAddress { get; }
    internal int AtlasWidth { get; }
    internal int AtlasHeight { get; }
    internal byte[] AtlasTextureBytes { get; }

    internal AssetBundleBuildResult Build(
        string version,
        string sharedText,
        string outputName,
        bool reverseInput = false)
    {
        File.WriteAllText(_sharedPath, sharedText);
        File.SetLastWriteTimeUtc(_sharedPath,
            reverseInput ? new DateTime(2031, 5, 6, 7, 8, 10, DateTimeKind.Utc) :
                new DateTime(2021, 1, 2, 3, 4, 6, DateTimeKind.Utc));
        _ = AssetDatabase.Refresh();

        AssetBundleBuildDefinition shared = new()
        {
            Name = "shared",
            AssetPaths = reverseInput
                ? ["Assets/Editor/ignored.txt", AtlasAddress, SpriteAddress, ResourceAddress, BinaryAddress, SharedAddress]
                : ["Assets/Data", "Assets/Resources", "Assets/Sprites", "Assets/Atlases", "Assets/Editor"]
        };
        AssetBundleBuildDefinition main = new()
        {
            Name = "main",
            AssetPaths = [SceneAddress],
            Dependencies = ["shared"]
        };
        var definitions = reverseInput
            ? new[] { main, shared }
            : new[] { shared, main };
        return AssetBundleBuilder.Build(
            Workspace,
            AssetDatabase,
            definitions,
            new AssetBundleBuildOptions
            {
                PackageName = PackageName,
                Version = version,
                OutputDirectory = Path.Combine(Root, "Builds", outputName)
            });
    }

    internal void WriteScene(string sceneName, string rootName, bool includeBundledSprite = true)
    {
        var path = Path.Combine(Workspace.AssetsPath, "Scenes", "main.scene.yaml");
        using var scene = new Scene(sceneName);
        var root = scene.CreateGameObject(rootName);
        var renderer = includeBundledSprite ? root.AddComponent<SpriteRenderer>() : null;
        var document = SceneAssetSerialization.Capture(scene);
        if (includeBundledSprite)
            document.GameObjects.SelectMany(gameObject => gameObject.Components)
                .Single(component => component.Id == renderer!.Id)
                .Fields[nameof(SpriteRenderer.sprite)] = SpriteAddress;
        BEngine.YamlUtility.Save(document, path);
    }

    internal void DeleteSpriteSource()
    {
        File.Delete(_spritePath);
        File.Delete(_spritePath + ".meta");
        BAsset.ClearLoadedAssets();
    }

    internal void WriteStaleSpriteSource() =>
        File.WriteAllBytes(_spritePath, "stale-project-image"u8.ToArray());

    internal void DeleteAtlasTextureArtifact()
    {
        File.Delete(_atlasTexturePath);
        File.Delete(_atlasTexturePath + ".meta");
        BAsset.ClearLoadedAssets();
    }

    private static byte[] CreateSpritePng()
    {
        using var bitmap = new System.Drawing.Bitmap(2, 2,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, 25, 210, 120));
        bitmap.SetPixel(1, 0, System.Drawing.Color.FromArgb(255, 200, 40, 90));
        bitmap.SetPixel(0, 1, System.Drawing.Color.FromArgb(255, 60, 80, 230));
        bitmap.SetPixel(1, 1, System.Drawing.Color.FromArgb(255, 245, 220, 30));
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    public void Dispose()
    {
        Application.dataPath = _previousDataPath;
        BAsset.ClearLoadedAssets();
        if (Environment.GetEnvironmentVariable("BENGINE_KEEP_TEST_TEMP") == "1")
        {
            Console.WriteLine($"ASSET_BUNDLE_HOT_UPDATE_TEMP|{Root}");
            return;
        }
        if (!Directory.Exists(Root)) return;
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
