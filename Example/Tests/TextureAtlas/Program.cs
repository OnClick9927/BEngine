using System.Globalization;
using System.Security.Cryptography;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.TiledMap;

namespace BEngine.ExampleTests.TextureAtlas;

internal static class Program
{
    private static int Main(string[] args)
    {
        var repository = FindRepositoryRoot();
        var testRoot = Path.Combine(repository, "Temp", "TextureAtlasTests");
        try
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            var assets = Directory.CreateDirectory(Path.Combine(testRoot, "Assets")).FullName;
            Application.dataPath = assets;
            if (args.Contains("--runtime-sprite-import-only", StringComparer.Ordinal))
            {
                _ = BuildAtlas(assets);
                ValidateImportedSpriteRuntime(assets);
                ValidateTextureRuntimeSampling(assets);
                Console.WriteLine("TEXTURE_SPRITE_IMPORT_RUNTIME_OK|meta-mode,pivot,sampling,png-only,atlas,legacy");
                return 0;
            }
            Require(AssetTypeRegistry.Resolve("Assets/Test.atlas.yaml") == nameof(BEngine.TextureAtlas),
                "The editor did not register the .atlas.yaml asset type.");
            Require(AssetTypeRegistry.ResolveAssetType("Assets/Test.atlas.yaml") == typeof(BEngine.TextureAtlas) &&
                    AssetTypeRegistry.ResolveAssetType("Assets/Test.sprite.yaml") == typeof(Sprite),
                "Texture Atlas or Sprite did not register as a typed BAsset.");
            var atlas = BuildAtlas(assets);
            ValidateImportedSpriteRuntime(assets);
            ValidateTextureRuntimeSampling(assets);
            ValidateAtlas(atlas, assets);
            ValidateDeterminism(atlas, assets);
            ValidateOutputCollision(assets);
            ValidateSpriteBatch(atlas, repository);
            ValidatePackageAtlasOwnership(testRoot);
            ValidateRemovedSpriteReferenceStopsAtlasOwnership(assets);
            ValidateMovedSpriteRetainsAtlasOwnership(assets);
            ValidateLegacyAtlasAndRendererMigration(assets);
            ValidateDefaultExampleMigration(repository);
            ValidateTilePaletteImport(atlas);
            ValidateDuplicateNames();
            Console.WriteLine("TEXTURE_ATLAS_OK|png-codec,power-of-two,maxrects,padding,extrude," +
                              "deterministic,source-overwrite-guard,uv,sprite-reference-reverse-lookup," +
                              "sprite-particle-batch,package-atlas-lazy-index,removed-reference," +
                              "moved-source-guid," +
                              "legacy-atlas,legacy-renderer-migration,solid-sprite," +
                              "default-example-v3,tile-palette-import,texture-imported-sprite," +
                              "texture-runtime-sampling,png-only-import");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"TEXTURE_ATLAS_FAILED|{exception}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    private static BEngine.TextureAtlas BuildAtlas(string assets)
    {
        var redPixels = SolidPixels(3, 2, 240, 35, 45, 255);
        var bluePixels = SolidPixels(2, 4, 25, 90, 230, 255);
        File.WriteAllBytes(Path.Combine(assets, "red.png"), PngImageCodec.EncodeRgba(3, 2, redPixels));
        File.WriteAllBytes(Path.Combine(assets, "blue.png"), PngImageCodec.EncodeRgba(2, 4, bluePixels));
        Require(PngImageCodec.TryDecode(File.ReadAllBytes(Path.Combine(assets, "red.png")),
                    out var width, out var height, out var decoded) &&
                width == 3 && height == 2 && decoded.SequenceEqual(redPixels),
            "PNG codec did not round-trip RGBA data.");
        CreateSprite(assets, "red", "Assets/red.png", 0, 1);
        CreateSprite(assets, "blue", "Assets/blue.png");

        var atlas = new BEngine.TextureAtlas
        {
            MaxSize = 64,
            Padding = 2,
            Extrude = 1,
            SpriteReferences =
            [
                "Assets/red.sprite.yaml",
                "Assets/blue.sprite.yaml"
            ]
        };
        var manifest = Path.Combine(assets, "Combined.atlas.yaml");
        atlas.Save(manifest);
        var result = TextureAtlasBuilder.Build(atlas, manifest);
        Require(result.SpriteCount == 2 && result.AtlasAssetPath == "Assets/Combined.atlas.yaml" &&
                result.TextureAssetPath == "Assets/Combined.png",
            "Texture atlas build result used incorrect asset paths.");
        return BEngine.TextureAtlas.Load(manifest);
    }

    private static void ValidateImportedSpriteRuntime(string assets)
    {
        var importedPath = Path.Combine(assets, "imported.png");
        var textureOnlyPath = Path.Combine(assets, "texture-only.png");
        File.WriteAllBytes(importedPath, PngImageCodec.EncodeRgba(2, 2,
            SolidPixels(2, 2, 25, 210, 120, 255)));
        File.WriteAllBytes(textureOnlyPath, PngImageCodec.EncodeRgba(1, 1,
            SolidPixels(1, 1, 180, 90, 30, 255)));
        WriteTextureMeta(importedPath, "Sprite", "0.25", "0.75");
        WriteTextureMeta(textureOnlyPath, "Texture", "0.1", "0.9");

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            BAsset.ClearLoadedAssets();
            var imported = BAsset.Load<Sprite>("Assets/imported.png");
            Require(imported is not null && imported.Texture == "Assets/imported.png" &&
                    imported.assetPath == "Assets/imported.png" &&
                    Math.Abs((double)imported.pivot.x - 0.25) < 0.0001 &&
                    Math.Abs((double)imported.pivot.y - 0.75) < 0.0001,
                "A texture imported as Sprite did not load with invariant-culture pivot settings.");
            Require(BAsset.Load<Texture>("Assets/imported.png") is { width: 2, height: 2 },
                "A Sprite-mode image was no longer available as its underlying Texture.");
            Require(BAsset.Load<Sprite>("Assets/texture-only.png") is null &&
                    BAsset.Load<Sprite>("Assets/red.png") is null,
                "A regular image without Sprite import mode was exposed as a Sprite.");

            var directAtlas = new BEngine.TextureAtlas
            {
                name = "Imported Sprite Atlas",
                MaxSize = 32,
                Padding = 1,
                Extrude = 0,
                SpriteReferences = ["Assets/imported.png"]
            };
            var directAtlasPath = Path.Combine(assets, "Direct.atlas.yaml");
            directAtlas.Save(directAtlasPath);
            var directResult = TextureAtlasBuilder.Build(directAtlas, directAtlasPath);
            directAtlas = BEngine.TextureAtlas.Load(directAtlasPath);
            Require(directResult.SpriteCount == 1 && directResult.TextureAssetPath == "Assets/Direct.png" &&
                    directAtlas.Sprites is
                    [{ Name: "imported", PivotX: 0.25f, PivotY: 0.75f } region] &&
                    region.Source.Equals(imported!.guid, StringComparison.OrdinalIgnoreCase),
                "TextureAtlasBuilder did not pack a direct PNG Sprite reference with its importer pivot.");
            Require(directAtlas.LoadReferencedSprites() is [{ Texture: "Assets/imported.png" }],
                "A TextureAtlas did not load its direct image reference through Sprite import settings.");
            Expect<InvalidDataException>(() => new BEngine.TextureAtlas
                {
                    SpriteReferences = ["Assets/texture-only.png"]
                }.LoadReferencedSprites(),
                "A TextureAtlas accepted a direct image that was not imported as Sprite.");
            var rejectedAtlasPath = Path.Combine(assets, "Rejected.atlas.yaml");
            var rejectedAtlas = new BEngine.TextureAtlas
            {
                MaxSize = 32,
                SpriteReferences = ["Assets/texture-only.png"]
            };
            rejectedAtlas.Save(rejectedAtlasPath);
            Expect<InvalidDataException>(() => TextureAtlasBuilder.Build(rejectedAtlas, rejectedAtlasPath),
                "TextureAtlasBuilder accepted a PNG whose TextureImporter type was not Sprite.");

            TextureAtlasResolver.Clear();
            var packed = TextureAtlasResolver.Resolve(imported!);
            Require(packed.Texture == "Assets/Direct.png" &&
                    packed.BatchIdentity.EndsWith("Assets/Direct.atlas.yaml", StringComparison.OrdinalIgnoreCase),
                "A directly imported Sprite did not resolve to its TextureAtlas region.");
            Require(TextureAtlasResolver.LoadSpriteReference("Assets/imported.png") is
                    { Texture: "Assets/imported.png" } &&
                    TextureAtlasResolver.LoadSpriteReference("Assets/texture-only.png") is null,
                "Sprite reference deserialization did not enforce texture import mode.");
            Require(BAsset.Load<Sprite>("Assets/red.sprite.yaml") is { Texture: "Assets/red.png" },
                "Legacy .sprite.yaml loading compatibility was lost.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            BAsset.ClearLoadedAssets();
            TextureAtlasResolver.Clear();
        }
    }

    private static void WriteTextureMeta(
        string texturePath,
        string textureType,
        string pivotX,
        string pivotY,
        TextureFilterMode filterMode = TextureFilterMode.Bilinear,
        TextureWrapMode wrapMode = TextureWrapMode.Clamp) =>
        File.WriteAllText(texturePath + ".meta", $$"""
            format: BEngine.AssetMeta
            version: 1
            guid: {{Guid.NewGuid():N}}
            importer: TextureImporter
            assetType: Texture
            sourceHash: ''
            settings:
              textureType: {{textureType}}
              filterMode: {{filterMode}}
              wrapMode: {{wrapMode}}
              spritePivotX: '{{pivotX}}'
              spritePivotY: '{{pivotY}}'
            """);

    private static void ValidateTextureRuntimeSampling(string assets)
    {
        var texturePath = Path.Combine(assets, "sampling.png");
        File.WriteAllBytes(texturePath, PngImageCodec.EncodeRgba(2, 1,
            SolidPixels(2, 1, 70, 120, 230, 255)));
        WriteTextureMeta(texturePath, "Sprite", "0.2", "0.8",
            TextureFilterMode.Point, TextureWrapMode.Mirror);

        BAsset.ClearLoadedAssets();
        Require(BAsset.Load<Texture>("Assets/sampling.png") is
                {
                    width: 2, height: 1,
                    filterMode: TextureFilterMode.Point,
                    wrapMode: TextureWrapMode.Mirror
                } &&
                BAsset.Load<Sprite>("Assets/sampling.png") is
                {
                    Texture: "Assets/sampling.png",
                    PivotX: 0.2f,
                    PivotY: 0.8f
                },
            "Texture and Sprite views did not consume the same TextureImporter metadata.");

        using var device = new RecordingGraphicsDevice();
        using var cache = new SceneTextureCache(device);
        var pointMirror = (RecordingTexture)cache.Resolve("Assets/sampling.png");
        Require(pointMirror.Description is
                {
                    MinFilter: GraphicsTextureFilter.Nearest,
                    MagFilter: GraphicsTextureFilter.Nearest,
                    AddressMode: GraphicsTextureAddressMode.MirroredRepeat
                } && ReferenceEquals(pointMirror, cache.Resolve("Assets/sampling.png")),
            "Point/Mirror TextureImporter settings were not applied to the runtime GPU texture.");

        WriteTextureMeta(texturePath, "Sprite", "0.2", "0.8",
            TextureFilterMode.Bilinear, TextureWrapMode.Repeat);
        File.SetLastWriteTimeUtc(texturePath + ".meta", DateTime.UtcNow.AddSeconds(2));
        var bilinearRepeat = (RecordingTexture)cache.Resolve("Assets/sampling.png");
        Require(!ReferenceEquals(pointMirror, bilinearRepeat) && bilinearRepeat.Description is
                {
                    MinFilter: GraphicsTextureFilter.Linear,
                    MagFilter: GraphicsTextureFilter.Linear,
                    AddressMode: GraphicsTextureAddressMode.Repeat
                },
            "Changing only TextureImporter metadata did not rebuild the runtime GPU sampler.");

        var unsupportedPath = Path.Combine(assets, "unsupported.jpg");
        File.WriteAllBytes(unsupportedPath, PngImageCodec.EncodeRgba(1, 1,
            SolidPixels(1, 1, 255, 255, 255, 255)));
        WriteTextureMeta(unsupportedPath, "Sprite", "0.5", "0.5",
            TextureFilterMode.Point, TextureWrapMode.Clamp);
        BAsset.ClearLoadedAssets();
        Require(BAsset.Load<Texture>("Assets/unsupported.jpg") is null &&
                BAsset.Load<Sprite>("Assets/unsupported.jpg") is null &&
                ((RecordingTexture)cache.Resolve("Assets/unsupported.jpg")).Label ==
                "BEngine.Scene2D.MissingTexture" &&
                AssetTypeRegistry.Resolve("Assets/unsupported.jpg") is null &&
                AssetTypeRegistry.ResolveAssetType("Assets/unsupported.jpg") is null &&
                AssetTypeRegistry.ResolveImporterType("Assets/unsupported.jpg") is null,
            "An unsupported JPG was still advertised or loaded as a Texture/Sprite.");
    }

    private static void ValidateAtlas(BEngine.TextureAtlas atlas, string assets)
    {
        Require(IsPowerOfTwo(atlas.Width) && IsPowerOfTwo(atlas.Height) &&
                atlas.Width <= atlas.MaxSize && atlas.Height <= atlas.MaxSize,
            "Atlas dimensions are not bounded powers of two.");
        Require(atlas.Sprites.Count == 2 && atlas.Texture == "Assets/Combined.png",
            "Atlas manifest did not contain the generated texture and regions.");
        var spritePath = Path.Combine(assets, "red.sprite.yaml");
        var loadedAsset = AssetTypeRegistry.Load(new AssetLoadContext(
            Guid.NewGuid(), "Assets/red.sprite.yaml", spritePath, nameof(Sprite))) as Sprite;
        Require(loadedAsset is not null && loadedAsset.Texture == "Assets/red.png" &&
                loadedAsset.assetPath == "Assets/red.sprite.yaml",
            "The typed Sprite asset loader did not restore its stable texture and asset paths.");
        var spriteAsset = loadedAsset ?? throw new InvalidOperationException("The Sprite asset loader returned null.");
        using (var editor = BEngine.Editor.Editor.CreateEditor(spriteAsset))
            Require(editor is SpriteEditor,
                "Selecting a Sprite did not create its dedicated Texture/Pivot Inspector.");
        var preview = AssetPreview.GetAssetPreview(spriteAsset);
        Require(preview is not null && preview.Value.Width == atlas.Width &&
                preview.Value.Height == atlas.Height &&
                AssetPreview.GetInfoString(spriteAsset).Contains("Packed in", StringComparison.Ordinal),
            "A packed Sprite did not resolve a non-empty Atlas-backed Inspector preview.");
        var red = atlas.Find("red") ?? throw new InvalidOperationException("Red region is missing.");
        var blue = atlas.Find("Assets/blue.sprite.yaml") ??
                   throw new InvalidOperationException("Blue region is missing.");
        Require(!Overlaps(red, blue), "Packed regions overlap.");
        Require(atlas.TryGetUv("red", out var uv) && uv.x >= 0 && uv.y >= 0 &&
                uv.xMax <= 1 && uv.yMax <= 1,
            "Atlas returned invalid normalized UV coordinates.");

        var png = File.ReadAllBytes(Path.Combine(assets, "Combined.png"));
        Require(PngImageCodec.TryDecode(png, out var width, out var height, out var pixels) &&
                width == atlas.Width && height == atlas.Height,
            "Generated atlas PNG could not be decoded.");
        Require(Pixel(pixels, width, red.X, red.Y).SequenceEqual(new byte[] { 240, 35, 45, 255 }),
            "Source pixels were not copied to the atlas.");
        Require(Pixel(pixels, width, red.X - 1, red.Y).SequenceEqual(new byte[] { 240, 35, 45, 255 }),
            "Atlas edge extrusion did not fill the padding pixel.");
    }

    private static void ValidateDeterminism(BEngine.TextureAtlas atlas, string assets)
    {
        var manifest = Path.Combine(assets, "Combined.atlas.yaml");
        var texture = Path.Combine(assets, "Combined.png");
        var firstManifest = SHA256.HashData(File.ReadAllBytes(manifest));
        var firstTexture = SHA256.HashData(File.ReadAllBytes(texture));
        TextureAtlasBuilder.Build(BEngine.TextureAtlas.Load(manifest), manifest);
        Require(firstManifest.SequenceEqual(SHA256.HashData(File.ReadAllBytes(manifest))) &&
                firstTexture.SequenceEqual(SHA256.HashData(File.ReadAllBytes(texture))),
            "Repeated atlas builds are not byte-for-byte deterministic.");
        Require(atlas.Sprites.Select(sprite => sprite.Name).SequenceEqual(["blue", "red"]),
            "Atlas sprite metadata order is not deterministic.");
    }

    private static void ValidateSpriteBatch(BEngine.TextureAtlas atlas, string repository)
    {
        Resources.RegisterResourceRoot(Path.Combine(repository, "src", "Core"));
        using var device = new RecordingGraphicsDevice();
        using var renderer = new PortableSceneRenderer(device);
        var scene = new Scene("Texture Atlas Batch");
        var first = scene.CreateGameObject("Red").AddComponent<SpriteRenderer>();
        first.sprite = Sprite.Load("Assets/red.sprite.yaml");
        var second = scene.CreateGameObject("Blue").AddComponent<SpriteRenderer>();
        second.sprite = Sprite.Load("Assets/blue.sprite.yaml");
        second.transform.position = new Vector2(2, 0);

        TextureAtlasResolver.Clear();
        var indexBuilds = TextureAtlasResolver.IndexBuildCount;
        _ = first.ResolveSpriteUnchecked();
        _ = second.ResolveSpriteUnchecked();
        _ = first.ResolveSpriteUnchecked();
        Require(TextureAtlasResolver.IndexBuildCount == indexBuilds + 1,
            "Resolving multiple SpriteRenderers repeatedly rebuilt the Atlas filesystem index.");

        var particles = scene.CreateGameObject("Particles").AddComponent<ParticleSystem2D>();
        particles.atlas = "Assets/Combined.atlas.yaml";
        particles.sprite = "red";
        particles.material = first.material;
        particles.Emit(1);
        renderer.Render(scene, RenderCamera.Default, 320, 180);

        var mesh = device.Meshes.Single(item => item.Label == "BEngine.Scene2D.TexturedTriangles");
        Require(mesh.UpdateCount == 1 && mesh.VertexCount == 18,
            $"Expected one 18-vertex atlas batch, got {mesh.UpdateCount}/{mesh.VertexCount}.");
        Require(device.Draws.Count(draw => ReferenceEquals(draw.Mesh, mesh)) == 1 &&
                device.BoundTextures.Count == 1 &&
                device.Textures.Count(texture => texture.Label == "BEngine.Scene2D.Combined.png") == 1,
            "Sprites and particles sharing one Material/Shader/Atlas were not rendered in one draw.");
    }

    private static void ValidateOutputCollision(string assets)
    {
        var source = Path.Combine(assets, "red.png");
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var atlas = new BEngine.TextureAtlas
        {
            MaxSize = 64,
            SpriteReferences = ["Assets/red.sprite.yaml"]
        };
        var manifest = Path.Combine(assets, "red.atlas.yaml");
        Expect<InvalidDataException>(() => TextureAtlasBuilder.Build(atlas, manifest),
            "Atlas output was allowed to overwrite a source image.");
        Require(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))),
            "Source image changed after an atlas output collision.");
    }

    private static void ValidatePackageAtlasOwnership(string projectRoot)
    {
        var packageRoot = Path.Combine(projectRoot, "Packages", "com.bengine.atlas-test");
        Directory.CreateDirectory(packageRoot);
        var spriteReference = "Packages/com.bengine.atlas-test/package.sprite.yaml";
        var packedTexture = "Packages/com.bengine.atlas-test/package-packed.png";
        new Sprite { name = "package", Texture = "Packages/com.bengine.atlas-test/package-source.png" }
            .Save(Path.Combine(packageRoot, "package.sprite.yaml"));
        new BEngine.TextureAtlas
        {
            name = "Package Atlas",
            Width = 1,
            Height = 1,
            Texture = packedTexture,
            SpriteReferences = [spriteReference],
            Sprites =
            [
                new TextureAtlasSprite
                {
                    Name = "package", Source = spriteReference, Width = 1, Height = 1
                }
            ]
        }.Save(Path.Combine(packageRoot, "package.atlas.yaml"));

        TextureAtlasResolver.Clear();
        var indexBuilds = TextureAtlasResolver.IndexBuildCount;
        var sprite = Sprite.Load(spriteReference);
        var first = TextureAtlasResolver.Resolve(sprite);
        var second = TextureAtlasResolver.Resolve(sprite);
        Require(first.Texture == packedTexture && first.BatchIdentity.EndsWith(
                    "Packages/com.bengine.atlas-test/package.atlas.yaml", StringComparison.OrdinalIgnoreCase),
            "A Sprite referenced by a package Atlas did not inherit the packed texture/batch identity.");
        Require(second.Equals(first) && TextureAtlasResolver.IndexBuildCount == indexBuilds + 1,
            "Resolving a package Sprite rebuilt the Assets/Packages Atlas index more than once.");
    }

    private static void ValidateRemovedSpriteReferenceStopsAtlasOwnership(string assets)
    {
        var manifest = Path.Combine(assets, "Combined.atlas.yaml");
        var atlas = BEngine.TextureAtlas.Load(manifest);
        var redGuid = Document.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            Path.Combine(assets, "red.sprite.yaml.meta")).Guid;
        Require(atlas.SpriteReferences.Remove(redGuid),
            "The removal test could not find its Sprite reference.");
        atlas.Save(manifest);

        TextureAtlasResolver.Clear();
        var removed = TextureAtlasResolver.Resolve(Sprite.Load("Assets/red.sprite.yaml"));
        var retained = TextureAtlasResolver.Resolve(Sprite.Load("Assets/blue.sprite.yaml"));
        Require(removed.Texture == "Assets/red.png" &&
                retained.Texture == "Assets/Combined.png" &&
                removed.BatchIdentity != retained.BatchIdentity,
            "A stale built Atlas region still claimed a Sprite removed from SpriteReferences.");
    }

    private static void ValidateMovedSpriteRetainsAtlasOwnership(string assets)
    {
        var source = Path.Combine(assets, "blue.sprite.yaml");
        var destination = Path.Combine(assets, "blue-moved.sprite.yaml");
        File.Move(source, destination);
        File.Move(source + ".meta", destination + ".meta");
        BAsset.ClearLoadedAssets();
        TextureAtlasResolver.Clear();

        var moved = Sprite.Load("Assets/blue-moved.sprite.yaml");
        var packed = TextureAtlasResolver.Resolve(moved);
        Require(packed.Texture == "Assets/Combined.png" &&
                packed.BatchIdentity.EndsWith("Assets/Combined.atlas.yaml", StringComparison.OrdinalIgnoreCase),
            "Moving a Sprite source broke its GUID-based TextureAtlas ownership before a rebuild.");
    }

    private static void ValidateLegacyAtlasAndRendererMigration(string assets)
    {
        var legacy = new BEngine.TextureAtlas
        {
            Version = 1,
            MaxSize = 64,
            Sources = [new TextureAtlasSource
            {
                Name = "legacy-red", Path = "Assets/red.png", PivotX = 0.25f, PivotY = 0.75f
            }]
        };
        var legacyPath = Path.Combine(assets, "Legacy.atlas.yaml");
        legacy.Save(legacyPath);
        var result = TextureAtlasBuilder.Build(legacy, legacyPath);
        Require(result.SpriteCount == 1 && BEngine.TextureAtlas.Load(legacyPath).Version == 1,
            "A version 1 source-based texture atlas could not be loaded and rebuilt.");

        var scene = new Scene("Legacy SpriteRenderer Migration");
        try
        {
            var renderer = scene.CreateGameObject("Legacy").AddComponent<SpriteRenderer>();
            BEngine.Documents.ComponentFieldSerializer.Deserialize(renderer,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["atlas"] = "Assets/Legacy.atlas.yaml",
                    ["sprite"] = "legacy-red",
                    ["useAtlasPivot"] = "false"
                });
            var migrated = BEngine.Documents.ComponentFieldSerializer.Serialize(renderer);
            Require(renderer.sprite is not null &&
                    renderer.sprite.assetPath == "Assets/Legacy.atlas.yaml#legacy-red" &&
                    !renderer.useSpritePivot &&
                    !migrated.ContainsKey("atlas") &&
                    migrated.GetValueOrDefault("sprite") == "Assets/Legacy.atlas.yaml#legacy-red" &&
                    migrated.ContainsKey("useSpritePivot") &&
                    !migrated.ContainsKey("useAtlasPivot"),
                "Legacy SpriteRenderer atlas and region fields were not migrated to one Sprite reference.");

            var solid = scene.CreateGameObject("Solid").AddComponent<SpriteRenderer>();
            BEngine.Documents.ComponentFieldSerializer.Deserialize(solid,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["atlas"] = string.Empty,
                    ["sprite"] = string.Empty
                });
            var serializedSolid = BEngine.Documents.ComponentFieldSerializer.Serialize(solid);
            Require(solid.sprite is null &&
                    !solid.ResolveSpriteUnchecked().IsTextured &&
                    !serializedSolid.ContainsKey("sprite"),
                "An empty legacy SpriteRenderer reference no longer renders and serializes as a solid quad.");
        }
        finally { scene.Dispose(); }
    }

    private static void ValidateDefaultExampleMigration(string repository)
    {
        var art = Path.Combine(repository, "Example", "Assets", "Art");
        var atlas = BEngine.TextureAtlas.Load(Path.Combine(art, "Showcase.atlas.yaml"));
        var sourceGuids = Directory.EnumerateFiles(Path.Combine(art, "Sources"), "*.png.meta")
            .Select(path => Document.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(path).Guid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var atlasMeta = Document.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            Path.Combine(art, "Showcase.atlas.yaml.meta"));
        var textureMeta = Document.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            Path.Combine(art, "Showcase.png.meta"));
        Require(atlas.Version == 3 && atlas.Sources.Count == 0 && atlas.SpriteReferences.Count == 5 &&
                atlas.SpriteReferences.All(reference => Guid.TryParse(reference, out _)) &&
                atlas.SpriteReferences.All(sourceGuids.Contains) &&
                atlas.Sprites.Count == 5 && atlas.Sprites.All(region =>
                    Guid.TryParse(region.Source, out _)) &&
                textureMeta.ParentGuid.Equals(atlasMeta.Guid, StringComparison.OrdinalIgnoreCase) &&
                textureMeta.LocalIdentifier == 2800000,
            "The default Showcase Atlas was not migrated to GUID Sprite references and a PNG sub-asset.");

        var scene = Document.Load<SceneDocument>(
            Path.Combine(repository, "Example", "Assets", "Scenes", "Main.scene.yaml"));
        var renderers = scene.GameObjects.SelectMany(gameObject => gameObject.Components)
            .Where(component => component.Type == typeof(SpriteRenderer).FullName).ToArray();
        var particles = scene.GameObjects.SelectMany(gameObject => gameObject.Components)
            .Where(component => component.Type == typeof(ParticleSystem2D).FullName).ToArray();
        Require(renderers.Length > 0 &&
                renderers.All(renderer => !renderer.Fields.ContainsKey("atlas") &&
                                          !renderer.Fields.ContainsKey("useAtlasPivot")) &&
                renderers.Where(renderer => renderer.Fields.TryGetValue("sprite", out var reference) &&
                                             reference.Length > 0)
                    .All(renderer => renderer.Fields["sprite"]
                        .EndsWith(".png", StringComparison.OrdinalIgnoreCase)) &&
                renderers.Any(renderer => !renderer.Fields.ContainsKey("sprite")) &&
                particles.Any(particle => particle.Fields.ContainsKey("atlas")),
            "The default Showcase Scene does not reference textures imported as Sprite.");
    }

    private static void ValidateTilePaletteImport(BEngine.TextureAtlas atlas)
    {
        var palette = new TilePalette();
        palette.ImportAtlas(atlas);
        Require(palette.Atlas == atlas.Texture && palette.Tiles.Count == 2 &&
                palette.Tiles.Select(tile => tile.Name).SequenceEqual(["blue", "red"]) &&
                palette.Tiles.All(tile => tile.UvWidth > 0 && tile.UvHeight > 0),
            "Tile Palette did not import atlas regions and normalized UV coordinates.");
    }

    private static void ValidateDuplicateNames()
    {
        var atlas = new BEngine.TextureAtlas
        {
            Sources =
            [
                new TextureAtlasSource { Name = "same", Path = "Assets/a.png" },
                new TextureAtlasSource { Name = "same", Path = "Assets/b.png" }
            ]
        };
        Expect<InvalidDataException>(atlas.Validate, "Duplicate atlas source names were accepted.");
    }

    private static void CreateSprite(
        string assets, string name, string texture, float pivotX = 0.5f, float pivotY = 0.5f) =>
        new Sprite
        {
            name = name,
            Texture = texture,
            PivotX = pivotX,
            PivotY = pivotY
        }.Save(Path.Combine(assets, $"{name}.sprite.yaml"));

    private static byte[] SolidPixels(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var result = new byte[width * height * 4];
        for (var offset = 0; offset < result.Length; offset += 4)
        {
            result[offset] = red;
            result[offset + 1] = green;
            result[offset + 2] = blue;
            result[offset + 3] = alpha;
        }
        return result;
    }

    private static ReadOnlySpan<byte> Pixel(byte[] pixels, int width, int x, int y) =>
        pixels.AsSpan((y * width + x) * 4, 4);
    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
    private static bool Overlaps(TextureAtlasSprite left, TextureAtlasSprite right) =>
        right.X + right.Width > left.X && right.X < left.X + left.Width &&
        right.Y + right.Height > left.Y && right.Y < left.Y + left.Height;

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private sealed class RecordingGraphicsDevice : IGraphicsDevice
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL, "Recording GPU", "1.0",
            GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.SampledTextures |
            GraphicsDeviceFeatures.AlphaBlending | GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        public List<RecordingMesh> Meshes { get; } = [];
        public List<RecordingTexture> Textures { get; } = [];
        public List<RecordingTexture> BoundTextures { get; } = [];
        public List<DrawRecord> Draws { get; } = [];

        public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
            new RecordingProgram(this, description.Label);
        public IGraphicsMesh CreateMesh(GraphicsMeshDescription description)
        {
            var mesh = new RecordingMesh(this, description);
            Meshes.Add(mesh);
            return mesh;
        }
        public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData = default)
        {
            var texture = new RecordingTexture(this, label, description);
            Textures.Add(texture);
            return texture;
        }
        public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description) =>
            throw new NotSupportedException();
        public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget) => throw new NotSupportedException();
        public void SetViewport(GraphicsRect viewport) { }
        public void SetScissor(GraphicsRect? scissor) { }
        public void Clear(GraphicsClearFlags flags, System.Numerics.Vector4 color) { }
        public void SetDepthState(GraphicsDepthState state) { }
        public void SetBlendMode(GraphicsBlendMode mode) { }
        public void SetRasterizerState(GraphicsRasterizerState state) { }
        public void BindTexture(int slot, IGraphicsTexture2D texture) => BoundTextures.Add((RecordingTexture)texture);
        public void Draw(IGraphicsMesh mesh) => Draw(mesh, mesh.VertexCount);
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) =>
            Draws.Add(new DrawRecord((RecordingMesh)mesh, vertexCount));
        public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0) { }
        public void Dispose() { }
    }

    private sealed class RecordingProgram(IGraphicsDevice device, string label) : IGraphicsProgram
    {
        public IGraphicsDevice Device => device;
        public string Label => label;
        public void Bind() { }
        public void SetMatrix4x4(string name, System.Numerics.Matrix4x4 value) { }
        public void SetVector4(string name, System.Numerics.Vector4 value) { }
        public void SetFloat(string name, float value) { }
        public void SetInt(string name, int value) { }
        public void Dispose() { }
    }

    private sealed class RecordingMesh : IGraphicsMesh
    {
        public RecordingMesh(IGraphicsDevice device, GraphicsMeshDescription description)
        {
            Device = device;
            Label = description.Label;
            Layout = description.Layout;
            Topology = description.Topology;
            Usage = description.Usage;
        }
        public IGraphicsDevice Device { get; }
        public string Label { get; }
        public GraphicsVertexLayout Layout { get; }
        public GraphicsPrimitiveTopology Topology { get; }
        public GraphicsBufferUsage Usage { get; }
        public int VertexCount { get; private set; }
        public int UpdateCount { get; private set; }
        public void Update(ReadOnlySpan<float> vertices)
        {
            UpdateCount++;
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
        }
        public void Dispose() { }
    }

    private sealed class RecordingTexture(IGraphicsDevice device, string label,
        GraphicsTextureDescription description) : IGraphicsTexture2D
    {
        public IGraphicsDevice Device => device;
        public string Label => label;
        public GraphicsTextureDescription Description => description;
        public void Update(ReadOnlySpan<byte> pixels) { }
        public void Dispose() { }
    }

    private sealed record DrawRecord(RecordingMesh Mesh, int VertexCount);
}
