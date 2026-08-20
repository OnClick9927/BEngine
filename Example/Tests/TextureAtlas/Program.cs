using System.Security.Cryptography;
using BEngine.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.TiledMap;

namespace BEngine.ExampleTests.TextureAtlas;

internal static class Program
{
    private static int Main()
    {
        var repository = FindRepositoryRoot();
        var testRoot = Path.Combine(repository, "Temp", "TextureAtlasTests");
        try
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            var assets = Directory.CreateDirectory(Path.Combine(testRoot, "Assets")).FullName;
            Application.dataPath = assets;
            Require(AssetTypeRegistry.Resolve("Assets/Test.atlas.yaml") == nameof(BEngine.TextureAtlas),
                "The editor did not register the .atlas.yaml asset type.");
            var atlas = BuildAtlas(assets);
            ValidateAtlas(atlas, assets);
            ValidateDeterminism(atlas, assets);
            ValidateOutputCollision(assets);
            ValidateSpriteBatch(atlas, repository);
            ValidateTilePaletteImport(atlas);
            ValidateDuplicateNames();
            Console.WriteLine("TEXTURE_ATLAS_OK|png-codec,power-of-two,maxrects,padding,extrude," +
                              "deterministic,source-overwrite-guard,uv,sprite-particle-batch," +
                              "tile-palette-import");
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

        var atlas = new BEngine.TextureAtlas
        {
            MaxSize = 64,
            Padding = 2,
            Extrude = 1,
            Sources =
            [
                new TextureAtlasSource { Name = "red", Path = "Assets/red.png", PivotX = 0, PivotY = 1 },
                new TextureAtlasSource { Name = "blue", Path = "Assets/blue.png" }
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

    private static void ValidateAtlas(BEngine.TextureAtlas atlas, string assets)
    {
        Require(IsPowerOfTwo(atlas.Width) && IsPowerOfTwo(atlas.Height) &&
                atlas.Width <= atlas.MaxSize && atlas.Height <= atlas.MaxSize,
            "Atlas dimensions are not bounded powers of two.");
        Require(atlas.Sprites.Count == 2 && atlas.Texture == "Assets/Combined.png",
            "Atlas manifest did not contain the generated texture and regions.");
        var red = atlas.Find("red") ?? throw new InvalidOperationException("Red region is missing.");
        var blue = atlas.Find("Assets/blue.png") ?? throw new InvalidOperationException("Blue region is missing.");
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
        first.atlas = "Assets/Combined.atlas.yaml";
        first.sprite = "red";
        var second = scene.CreateGameObject("Blue").AddComponent<SpriteRenderer>();
        second.atlas = "Assets/Combined.atlas.yaml";
        second.sprite = "blue";
        second.transform.position = new Vector2(2, 0);

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
            Sources = [new TextureAtlasSource { Name = "red", Path = "Assets/red.png" }]
        };
        var manifest = Path.Combine(assets, "red.atlas.yaml");
        Expect<InvalidDataException>(() => TextureAtlasBuilder.Build(atlas, manifest),
            "Atlas output was allowed to overwrite a source image.");
        Require(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))),
            "Source image changed after an atlas output collision.");
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
