using System.Globalization;
using System.Security.Cryptography;
using BEngine.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
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
                Console.WriteLine("TEXTURE_SPRITE_IMPORT_RUNTIME_OK|texture-factory,pivot,sampling,png-only,atlas");
                return 0;
            }
            Require(AssetTypeRegistry.Resolve("Assets/Test.atlas.yaml") == nameof(BEngine.TextureAtlas),
                "The editor did not register the .atlas.yaml asset type.");
            Require(AssetTypeRegistry.ResolveAssetType("Assets/Test.atlas.yaml") == typeof(BEngine.TextureAtlas) &&
                    AssetTypeRegistry.ResolveImporterType("Assets/Test.atlas.yaml") ==
                    typeof(TextureAtlasImporter) &&
                    AssetTypeRegistry.ResolveAssetType("Assets/Test.png") == typeof(Texture) &&
                    !typeof(BAsset).IsAssignableFrom(typeof(Sprite)),
                "Texture Atlas, Texture, or Sprite object classification is incorrect.");
            var atlas = BuildAtlas(assets);
            ValidateSourceYamlContract(atlas, assets);
            ValidateImportedSpriteRuntime(assets);
            ValidateTextureRuntimeSampling(assets);
            ValidateAtlas(atlas, assets);
            ValidateDeterminism(atlas, assets);
            ValidateOutputCollision(assets);
            ValidateEmptySourcesCleanup(assets);
            ValidateSpriteBatch(atlas, repository);
            ValidatePackageAtlasOwnership(testRoot);
            ValidateRemovedSpriteReferenceStopsAtlasOwnership(assets);
            ValidateMovedSpriteRetainsAtlasOwnership(assets);
            ValidateRendererReferenceMigration(assets);
            ValidateDefaultExampleMigration(repository);
            ValidateTilePaletteImport(atlas);
            ValidateDuplicateNames(assets);
            Console.WriteLine("TEXTURE_ATLAS_OK|png-codec,power-of-two,maxrects,padding,extrude," +
                              "deterministic,library-artifact,guid-texture-subasset,atlas-importer," +
                              "empty-sources-cleanup,null-only-sources-cleanup," +
                              "source-overwrite-guard,uv,sprite-reference-reverse-lookup," +
                              "sprite-particle-batch,package-atlas-lazy-index,removed-reference," +
                              "moved-source-guid," +
                              "legacy-renderer-migration,solid-sprite," +
                              "default-example-sprite-sources,tile-palette-import,texture-created-sprite," +
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
        var red = CreateSprite(assets, "red", "Assets/red.png", 0, 1);
        var blue = CreateSprite(assets, "blue", "Assets/blue.png");

        var atlas = new BEngine.TextureAtlas
        {
            MaxSize = 64,
            Padding = 2,
            Extrude = 1,
            Sources = [red, blue]
        };
        var manifest = Path.Combine(assets, "Combined.atlas.yaml");
        atlas.Save(manifest);
        var result = TextureAtlasBuilder.Build(atlas, manifest);
        Require(result.SpriteCount == 2 && result.AtlasAssetPath == "Assets/Combined.atlas.yaml" &&
                result.TextureAssetPath == atlas.Texture && IsGeneratedTextureReference(atlas.Texture) &&
                !File.Exists(Path.Combine(assets, "Combined.png")),
            "Texture atlas build result used incorrect asset paths.");
        _ = GeneratedTexturePath(atlas, assets);
        return BEngine.TextureAtlas.Load(manifest);
    }

    private static void ValidateSourceYamlContract(BEngine.TextureAtlas atlas, string assets)
    {
        var manifest = Path.Combine(assets, "Combined.atlas.yaml");
        RequireMinimalSourceEntries(File.ReadAllText(manifest), expectedCount: 2);
        Require(!File.ReadAllText(manifest).Contains("version:", StringComparison.OrdinalIgnoreCase) &&
                !File.ReadAllText(manifest).Contains("spriteReferences:", StringComparison.OrdinalIgnoreCase),
            "TextureAtlas persisted a legacy version or SpriteReferences field.");

        var legacyPath = Path.Combine(assets, "LegacySources.atlas.yaml");
        File.WriteAllText(legacyPath, $$"""
            format: BEngine.TextureAtlas
            texture: ''
            width: 0
            height: 0
            maxSize: 64
            padding: 1
            extrude: 0
            sources:
            - path: Assets/red.png
              name: Legacy Red
              pivotX: 0.25
              pivotY: 0.75
            - texture: Assets/blue.png
              name: Legacy Blue
              pivotX: 0.75
              pivotY: 0.25
            sprites: []
            id: {{Guid.NewGuid():D}}
            name: Legacy Sources
            hideFlags: None
            version: 1
            """);
        BAsset.ClearLoadedAssets();
        var legacy = BEngine.TextureAtlas.Load(legacyPath);
        Require(legacy.Sources is
                [
                    { name: "Legacy Red", Texture: "Assets/red.png", OwnerGuid.Length: > 0,
                        LocalIdentifier: 21300000, PivotX: 0.25f, PivotY: 0.75f },
                    { name: "Legacy Blue", Texture: "Assets/blue.png", OwnerGuid.Length: > 0,
                        LocalIdentifier: 21300000, PivotX: 0.75f, PivotY: 0.25f }
                ],
            "TextureAtlas did not compatibly read legacy path/texture/name/pivot source entries.");
        legacy.Save(legacyPath);
        RequireMinimalSourceEntries(File.ReadAllText(legacyPath), expectedCount: 2);

        var referenceListPath = Path.Combine(assets, "LegacyReferences.atlas.yaml");
        File.WriteAllText(referenceListPath, $$"""
            format: BEngine.TextureAtlas
            version: 3
            texture: ''
            width: 0
            height: 0
            maxSize: 64
            padding: 1
            extrude: 0
            spriteReferences:
            - {{atlas.Sources[0].OwnerGuid}}
            sources: []
            sprites: []
            id: {{Guid.NewGuid():D}}
            name: Legacy References
            hideFlags: None
            """);
        var referenceList = BEngine.TextureAtlas.Load(referenceListPath);
        Require(referenceList.Sources is
                [{ OwnerGuid: var ownerGuid, LocalIdentifier: 21300000 }] &&
                ownerGuid.Equals(atlas.Sources[0].OwnerGuid, StringComparison.OrdinalIgnoreCase),
            "TextureAtlas did not compatibly read the legacy versioned SpriteReferences list.");
        referenceList.Save(referenceListPath);
        RequireMinimalSourceEntries(File.ReadAllText(referenceListPath), expectedCount: 1);
    }

    private static void RequireMinimalSourceEntries(string yaml, int expectedCount)
    {
        var start = yaml.IndexOf("sources:", StringComparison.OrdinalIgnoreCase);
        var end = start < 0 ? -1 : yaml.IndexOf("\nsprites:", start, StringComparison.OrdinalIgnoreCase);
        Require(start >= 0 && end > start, "TextureAtlas YAML does not contain a sources section.");
        var lines = yaml[start..end].Split('\n', StringSplitOptions.TrimEntries |
                                                  StringSplitOptions.RemoveEmptyEntries);
        Require(lines.Skip(1).All(line => line.StartsWith("- ownerGuid:", StringComparison.Ordinal) ||
                                          line.StartsWith("localIdentifier:", StringComparison.Ordinal)) &&
                lines.Count(line => line.StartsWith("- ownerGuid:", StringComparison.Ordinal)) == expectedCount &&
                lines.Count(line => line.StartsWith("localIdentifier:", StringComparison.Ordinal)) == expectedCount,
            "TextureAtlas sources persisted data other than ownerGuid/localIdentifier.");
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
            var importedTexture = BAsset.Load<Texture>("Assets/imported.png");
            var imported = importedTexture?.CreateSprite(new Vector2((Fix64)0.25, (Fix64)0.75));
            Require(imported is not null && imported.Texture == "Assets/imported.png" &&
                    imported.assetPath == "Assets/imported.png" &&
                    imported.OwnerGuid == importedTexture!.guid && imported.LocalIdentifier == 21300000 &&
                    Math.Abs((double)imported.pivot.x - 0.25) < 0.0001 &&
                    Math.Abs((double)imported.pivot.y - 0.75) < 0.0001,
                "Texture.CreateSprite did not preserve its Texture identity, local identifier, and pivot.");
            Require(BAsset.Load<Texture>("Assets/imported.png") is { width: 2, height: 2 },
                "A Sprite-mode image was no longer available as its underlying Texture.");
            Require(BAsset.Load<Texture>("Assets/texture-only.png") is not null,
                "The source Texture could not be loaded before creating a Sprite representation.");

            var directAtlas = new BEngine.TextureAtlas
            {
                name = "Imported Sprite Atlas",
                MaxSize = 32,
                Padding = 1,
                Extrude = 0,
                Sources = [imported!]
            };
            var directAtlasPath = Path.Combine(assets, "Direct.atlas.yaml");
            directAtlas.Save(directAtlasPath);
            var directResult = TextureAtlasBuilder.Build(directAtlas, directAtlasPath);
            directAtlas = BEngine.TextureAtlas.Load(directAtlasPath);
            Require(directResult.SpriteCount == 1 && directResult.TextureAssetPath == directAtlas.Texture &&
                    IsGeneratedTextureReference(directAtlas.Texture) &&
                    !File.Exists(Path.Combine(assets, "Direct.png")) &&
                    directAtlas.Sprites is
                    [{ Name: "imported", PivotX: 0.25f, PivotY: 0.75f } region] &&
                    region.Source.Equals($"{imported!.OwnerGuid}:{imported.LocalIdentifier}",
                        StringComparison.OrdinalIgnoreCase),
                "TextureAtlasBuilder did not pack a direct PNG Sprite reference with its importer pivot.");
            Require(directAtlas.LoadReferencedSprites() is [{ Texture: "Assets/imported.png" }],
                "A TextureAtlas did not load its direct image reference through Sprite import settings.");
            TextureAtlasResolver.Clear();
            var packed = TextureAtlasResolver.Resolve(imported!);
            Require(packed.Texture == directAtlas.Texture &&
                    packed.BatchIdentity.EndsWith("Assets/Direct.atlas.yaml", StringComparison.OrdinalIgnoreCase),
                "A directly imported Sprite did not resolve to its TextureAtlas region.");
            Require(directAtlas.Sources is [{ OwnerGuid.Length: > 0, LocalIdentifier: 21300000 }],
                "TextureAtlas did not persist a stable Sprite owner GUID and local identifier.");
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
                BAsset.Load<Texture>("Assets/sampling.png")?.CreateSprite(
                    new Vector2((Fix64)0.2, (Fix64)0.8)) is
                { Texture: "Assets/sampling.png", PivotX: 0.2f, PivotY: 0.8f },
            "Texture.CreateSprite did not create the expected Sprite view.");

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
        Require(atlas.Sprites.Count == 2 && IsGeneratedTextureReference(atlas.Texture),
            "Atlas manifest did not contain the generated texture and regions.");
        var spriteAsset = atlas.Sources.Single(source => source.name == "red");
        Require(spriteAsset.Texture == "Assets/red.png" && spriteAsset.OwnerGuid.Length > 0 &&
                spriteAsset.LocalIdentifier == 21300000,
            "The Atlas did not retain its Texture-created Sprite identity.");
        using (var editor = BEngine.Editor.Editor.CreateEditor(spriteAsset))
            Require(editor is SpriteEditor,
                "Selecting a Sprite did not create its dedicated Texture/Pivot Inspector.");
        var preview = AssetPreview.GetAssetPreview(spriteAsset);
        Require(preview is not null && preview.Value.Width == atlas.Width &&
                preview.Value.Height == atlas.Height &&
                AssetPreview.GetInfoString(spriteAsset).Contains("Packed in", StringComparison.Ordinal),
            "A packed Sprite did not resolve a non-empty Atlas-backed Inspector preview.");
        var red = atlas.Find("red") ?? throw new InvalidOperationException("Red region is missing.");
        var blue = atlas.Find("blue") ??
                   throw new InvalidOperationException("Blue region is missing.");
        Require(!Overlaps(red, blue), "Packed regions overlap.");
        Require(atlas.TryGetUv("red", out var uv) && uv.x >= 0 && uv.y >= 0 &&
                uv.xMax <= 1 && uv.yMax <= 1,
            "Atlas returned invalid normalized UV coordinates.");

        var png = File.ReadAllBytes(GeneratedTexturePath(atlas, assets));
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
        var texture = GeneratedTexturePath(atlas, assets);
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
        first.sprite = atlas.Sources.Single(source => source.name == "red");
        var second = scene.CreateGameObject("Blue").AddComponent<SpriteRenderer>();
        second.sprite = atlas.Sources.Single(source => source.name == "blue");
        second.transform.position = new Vector2(2, 0);

        TextureAtlasResolver.Clear();
        var indexBuilds = TextureAtlasResolver.IndexBuildCount;
        _ = first.ResolveSpriteUnchecked();
        _ = second.ResolveSpriteUnchecked();
        _ = first.ResolveSpriteUnchecked();
        Require(TextureAtlasResolver.IndexBuildCount == indexBuilds,
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
                device.Textures.Count(texture => texture.Label != "BEngine.Scene2D.MissingTexture") == 1,
            "Sprites and particles sharing one Material/Shader/Atlas were not rendered in one draw.");
    }

    private static void ValidateOutputCollision(string assets)
    {
        var source = Path.Combine(assets, "red.png");
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var atlas = new BEngine.TextureAtlas
        {
            MaxSize = 64,
            Sources = [CreateSpriteFromTexture("Assets/red.png", "red", new Vector2(0, 1))]
        };
        var manifest = Path.Combine(assets, "red.atlas.yaml");
        atlas.Save(manifest);
        var result = TextureAtlasBuilder.Build(atlas, manifest);
        var generated = GeneratedTexturePath(atlas, assets);
        Require(result.TextureAssetPath == atlas.Texture &&
                !generated.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))),
            "A same-name atlas build overwrote its source image instead of using Library/Artifacts.");
    }

    private static void ValidateEmptySourcesCleanup(string assets)
    {
        var manifest = Path.Combine(assets, "Cleanup.atlas.yaml");
        var atlas = new BEngine.TextureAtlas
        {
            MaxSize = 64,
            Sources = [CreateSpriteFromTexture("Assets/red.png", "cleanup", new Vector2(0, 1))]
        };
        atlas.Save(manifest);
        TextureAtlasBuilder.Build(atlas, manifest);
        var reference = atlas.Texture;
        var artifact = GeneratedTexturePath(atlas, assets);
        Require(File.Exists(artifact + ".meta"),
            "The generated Texture metadata did not exist before the empty-Sources cleanup check.");

        atlas.Sources = [null!];
        var result = TextureAtlasBuilder.Build(atlas, manifest);
        var cleared = BEngine.TextureAtlas.Load(manifest);
        Require(result.TextureAssetPath.Length == 0 && result.SpriteCount == 0 &&
                cleared.Texture.Length == 0 && cleared.Sprites.Count == 0 &&
                cleared.Width == 0 && cleared.Height == 0 &&
                !File.Exists(artifact) && !File.Exists(artifact + ".meta") &&
                BAsset.Load<Texture>(reference) is null,
            "Clearing the last TextureAtlas Source left a stale manifest or Library sub-asset.");
    }

    private static void ValidatePackageAtlasOwnership(string projectRoot)
    {
        var packageRoot = Path.Combine(projectRoot, "Packages", "com.bengine.atlas-test");
        Directory.CreateDirectory(packageRoot);
        var sourceReference = "Packages/com.bengine.atlas-test/package-source.png";
        var sourcePath = Path.Combine(packageRoot, "package-source.png");
        var packedTexture = "Packages/com.bengine.atlas-test/package-packed.png";
        File.WriteAllBytes(sourcePath, PngImageCodec.EncodeRgba(1, 1, [255, 255, 255, 255]));
        WriteTextureMeta(sourcePath, "Sprite", "0.5", "0.5");
        var sprite = CreateSpriteFromTexture(sourceReference, "package",
            new Vector2(Fix64.Half, Fix64.Half));
        var spriteIdentity = $"{sprite.OwnerGuid}:{sprite.LocalIdentifier}";
        new BEngine.TextureAtlas
        {
            name = "Package Atlas",
            Width = 1,
            Height = 1,
            Texture = packedTexture,
            Sources = [sprite],
            Sprites =
            [
                new TextureAtlasSprite
                {
                    Name = "package", Source = spriteIdentity, Width = 1, Height = 1
                }
            ]
        }.Save(Path.Combine(packageRoot, "package.atlas.yaml"));

        TextureAtlasResolver.Clear();
        var indexBuilds = TextureAtlasResolver.IndexBuildCount;
        var first = TextureAtlasResolver.Resolve(sprite);
        var second = TextureAtlasResolver.Resolve(sprite);
        Require(first.Texture == packedTexture && first.BatchIdentity.EndsWith(
                    "Packages/com.bengine.atlas-test/package.atlas.yaml", StringComparison.OrdinalIgnoreCase),
            "A Sprite referenced by a package Atlas did not inherit the packed texture/batch identity.");
        Require(second.Equals(first) && TextureAtlasResolver.IndexBuildCount == indexBuilds,
            "Resolving a package Sprite rebuilt the Assets/Packages Atlas index more than once.");
    }

    private static void ValidateRemovedSpriteReferenceStopsAtlasOwnership(string assets)
    {
        var manifest = Path.Combine(assets, "Combined.atlas.yaml");
        var atlas = BEngine.TextureAtlas.Load(manifest);
        var removedSprite = atlas.Sources.Single(source => source.name == "red");
        atlas.Sources = atlas.Sources.Where(source => source.name != "red").ToArray();
        atlas.Save(manifest);

        TextureAtlasResolver.Clear();
        var removed = TextureAtlasResolver.Resolve(removedSprite);
        var retained = TextureAtlasResolver.Resolve(atlas.Sources.Single(source => source.name == "blue"));
        Require(!string.IsNullOrWhiteSpace(removed.Texture) &&
                !removed.Texture.Equals(atlas.Texture, StringComparison.OrdinalIgnoreCase) &&
                retained.Texture == atlas.Texture &&
                removed.BatchIdentity != retained.BatchIdentity,
            "A stale built Atlas region still claimed a Sprite removed from Sources.");
    }

    private static void ValidateMovedSpriteRetainsAtlasOwnership(string assets)
    {
        var source = Path.Combine(assets, "blue.png");
        var destination = Path.Combine(assets, "blue-moved.png");
        File.Move(source, destination);
        File.Move(source + ".meta", destination + ".meta");
        BAsset.ClearLoadedAssets();
        TextureAtlasResolver.Clear();

        var moved = CreateSpriteFromTexture("Assets/blue-moved.png", "blue",
            new Vector2(Fix64.Half, Fix64.Half));
        var packed = TextureAtlasResolver.Resolve(moved);
        var atlas = BEngine.TextureAtlas.Load(Path.Combine(assets, "Combined.atlas.yaml"));
        Require(packed.Texture == atlas.Texture &&
                packed.BatchIdentity.EndsWith("Assets/Combined.atlas.yaml", StringComparison.OrdinalIgnoreCase),
            "Moving a Sprite source broke its GUID-based TextureAtlas ownership before a rebuild.");
    }

    private static void ValidateRendererReferenceMigration(string assets)
    {
        var legacySourcePath = Path.Combine(assets, "legacy-red.png");
        File.Copy(Path.Combine(assets, "red.png"), legacySourcePath);
        WriteTextureMeta(legacySourcePath, "Sprite", "0.25", "0.75");
        var legacySprite = CreateSpriteFromTexture(
            "Assets/legacy-red.png", "legacy-red", new Vector2((Fix64)0.25, (Fix64)0.75));
        var atlas = new BEngine.TextureAtlas
        {
            MaxSize = 64,
            Sources = [legacySprite]
        };
        var legacyPath = Path.Combine(assets, "Legacy.atlas.yaml");
        atlas.Save(legacyPath);
        var result = TextureAtlasBuilder.Build(atlas, legacyPath);
        Require(result.SpriteCount == 1 && BEngine.TextureAtlas.Load(legacyPath).Sources is
                [{ OwnerGuid.Length: > 0, LocalIdentifier: 21300000 }],
            "A Sprite-source texture atlas could not be loaded and rebuilt.");

        var scene = new Scene("Legacy SpriteRenderer Migration");
        try
        {
            var renderer = scene.CreateGameObject("Legacy").AddComponent<SpriteRenderer>();
            ComponentFieldSerializer.Deserialize(renderer,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["atlas"] = "Assets/Legacy.atlas.yaml",
                    ["sprite"] = "legacy-red",
                    ["useAtlasPivot"] = "false"
                });
            var migrated = ComponentFieldSerializer.Serialize(renderer);
            var stableReference =
                $"guid:{legacySprite.OwnerGuid}#subasset={legacySprite.LocalIdentifier}";
            Require(renderer.sprite is not null &&
                    renderer.sprite.OwnerGuid == legacySprite.OwnerGuid &&
                    renderer.sprite.LocalIdentifier == legacySprite.LocalIdentifier &&
                    !renderer.useSpritePivot &&
                    !migrated.ContainsKey("atlas") &&
                    migrated.GetValueOrDefault("sprite") == stableReference &&
                    migrated.ContainsKey("useSpritePivot") &&
                    !migrated.ContainsKey("useAtlasPivot"),
                "Legacy SpriteRenderer atlas and region fields were not migrated to one Sprite reference.");

            var solid = scene.CreateGameObject("Solid").AddComponent<SpriteRenderer>();
            ComponentFieldSerializer.Deserialize(solid,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["atlas"] = string.Empty,
                    ["sprite"] = string.Empty
                });
            var serializedSolid = ComponentFieldSerializer.Serialize(solid);
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
            .Select(path => BEngine.YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(path).Guid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var atlasMeta = BEngine.YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            Path.Combine(art, "Showcase.atlas.yaml.meta"));
        Require(atlas.Sources.Length == 5 &&
                atlas.Sources.All(source => sourceGuids.Contains(source.OwnerGuid) &&
                                            source.LocalIdentifier == 21300000) &&
                atlas.Sprites.Count == 5 && atlas.Sprites.All(region =>
                    atlas.Sources.Any(source =>
                        region.Source.Equals($"{source.OwnerGuid}:{source.LocalIdentifier}",
                            StringComparison.OrdinalIgnoreCase))) &&
                atlas.Texture.Equals($"guid:{atlasMeta.Guid}#subasset=2800000",
                    StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(Path.Combine(art, "Showcase.png")),
            "The default Showcase Atlas does not contain stable Sprite references and a Library Texture reference.");

        var scene = BEngine.YamlUtility.Load<SceneAssetData>(
            Path.Combine(repository, "Example", "Assets", "Scenes", "Main.scene.yaml"));
        var objectsByName = scene.GameObjects.ToDictionary(gameObject => gameObject.Name,
            StringComparer.Ordinal);
        string[] requiredObjects =
        [
            "Atlas Showcase", "Animation Card", "Physics Card", "Navigation Card", "Input Card",
            "Animation Sprite", "Physics 2D Sprite", "Navigation 2D Sprite",
            "Player (WASD or Arrow Keys)", "Main Camera", "Inset Camera"
        ];
        Require(scene.GameObjects.Count >= 25 && requiredObjects.All(objectsByName.ContainsKey),
            "The default Example is missing its complete showcase hierarchy.");
        var showcaseId = objectsByName["Atlas Showcase"].Id;
        Require(new[] { "Animation Card", "Physics Card", "Navigation Card", "Input Card" }
                .All(name => objectsByName[name].Parent == showcaseId),
            "The default Example showcase cards are not parented to Atlas Showcase.");
        var player = objectsByName["Player (WASD or Arrow Keys)"];
        var playerMover = player.Components.SingleOrDefault(component =>
            component.Type == "Game.PlayerMover");
        Require(playerMover is not null && playerMover.Fields.GetValueOrDefault("runInEditMode") == "false" &&
                playerMover.Fields.ContainsKey("moveBounds"),
            "The default Example Player is not configured as a Play-only input demonstration.");
        var renderers = scene.GameObjects.SelectMany(gameObject => gameObject.Components)
            .Where(component => component.Type == typeof(SpriteRenderer).FullName).ToArray();
        var particles = scene.GameObjects.SelectMany(gameObject => gameObject.Components)
            .Where(component => component.Type == typeof(ParticleSystem2D).FullName).ToArray();
        Require(renderers.Length > 0 &&
                renderers.All(renderer => !renderer.Fields.ContainsKey("atlas") &&
                                          !renderer.Fields.ContainsKey("useAtlasPivot")) &&
                renderers.Where(renderer => renderer.Fields.TryGetValue("sprite", out var reference) &&
                                             reference.Length > 0)
                    .All(renderer => renderer.Fields["sprite"].EndsWith(".png",
                                         StringComparison.OrdinalIgnoreCase) ||
                                     renderer.Fields["sprite"].StartsWith("guid:",
                                         StringComparison.OrdinalIgnoreCase) &&
                                     renderer.Fields["sprite"].EndsWith("#subasset=21300000",
                                         StringComparison.OrdinalIgnoreCase)) &&
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

    private static void ValidateDuplicateNames(string assets)
    {
        var first = CreateSpriteFromTexture("Assets/red.png", "same",
            new Vector2(Fix64.Half, Fix64.Half));
        var second = CreateSpriteFromTexture("Assets/blue-moved.png", "same",
            new Vector2(Fix64.Half, Fix64.Half));
        var atlas = new BEngine.TextureAtlas
        {
            Sources = [first, second]
        };
        Expect<InvalidDataException>(() => TextureAtlasBuilder.Build(atlas,
                Path.Combine(assets, "Duplicate.atlas.yaml")),
            "Duplicate atlas Sprite names were accepted.");
    }

    private static Sprite CreateSprite(
        string assets, string name, string texture, float pivotX = 0.5f, float pivotY = 0.5f)
    {
        var sourcePath = Path.Combine(assets, texture["Assets/".Length..].Replace('/', Path.DirectorySeparatorChar));
        WriteTextureMeta(sourcePath, "Sprite",
            pivotX.ToString(CultureInfo.InvariantCulture), pivotY.ToString(CultureInfo.InvariantCulture));
        return CreateSpriteFromTexture(texture, name,
            new Vector2((Fix64)(double)pivotX, (Fix64)(double)pivotY));
    }

    private static Sprite CreateSpriteFromTexture(string textureReference, string name, Vector2 pivot)
    {
        BAsset.Invalidate(textureReference);
        var texture = BAsset.Load<Texture>(textureReference) ??
                      throw new InvalidOperationException($"Texture '{textureReference}' could not be loaded.");
        var sprite = texture.CreateSprite(pivot);
        sprite.name = name;
        return sprite;
    }

    private static bool IsGeneratedTextureReference(string reference) =>
        reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
        reference.EndsWith("#subasset=2800000", StringComparison.OrdinalIgnoreCase);

    private static string GeneratedTexturePath(BEngine.TextureAtlas atlas, string assets)
    {
        BAsset.ClearLoadedAssets();
        var texture = BAsset.Load<Texture>(atlas.Texture) ??
                      throw new InvalidOperationException(
                          $"Generated Texture '{atlas.Texture}' could not be loaded.");
        var path = string.IsNullOrWhiteSpace(texture.artifactPath)
            ? texture.sourcePath
            : texture.artifactPath;
        var artifactRoot = Path.GetFullPath(Path.Combine(
            Directory.GetParent(Path.GetFullPath(assets))?.FullName ?? assets,
            "Library", "Artifacts"));
        var fullPath = Path.GetFullPath(path);
        Require(File.Exists(fullPath) &&
                fullPath.StartsWith(artifactRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
            "Generated atlas PNG was not stored under the project Library/Artifacts directory.");
        return fullPath;
    }

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
