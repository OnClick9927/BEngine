using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.TiledMap;

namespace BEngine.ExampleTests.TiledMap;

internal static class Program
{
    private static int Main()
    {
        try
        {
            CellEditingAndOrdering();
            SerializationRoundTrip();
            CoordinateConversion();
            PaletteRoundTripAndValidation();
            RendererBoundaries();
            GpuBatchRendering();
            Console.WriteLine(
                "TILEDMAP_OK|sparse-cells,box-fill,flood-fill,swap,deterministic-order,scene-fields," +
                "coordinates,palette-yaml,world-layer-boundary,gpu-batch,culling,scene-object-filter");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"TILEDMAP_FAILED|{exception}");
            return 1;
        }
    }

    private static void CellEditingAndOrdering()
    {
        var map = NewMap();
        map.SetTile(new TileCoordinate(2, 1), 3, TileTransformFlags.FlipX);
        Require(map.GetTile(new TileCoordinate(2, 1)) == 3, "SetTile/GetTile failed.");
        Require(map.GetCell(new TileCoordinate(2, 1))?.Transform == TileTransformFlags.FlipX,
            "Tile transform flags were not retained.");
        Require(map.BoxFill(new TileCoordinate(-1, -1), new TileCoordinate(1, 0), 1) == 6,
            "BoxFill changed an unexpected number of cells.");
        Require(map.FloodFill(new TileCoordinate(0, 0), 2) == 6,
            "FloodFill did not stay within the connected occupied region.");
        Require(map.SwapTile(2, 4) == 6, "SwapTile did not replace all matching cells.");
        var bottomLeft = map.GetTiles(TilemapSortOrder.BottomLeft);
        Require(bottomLeft.First().Position == new TileCoordinate(-1, -1),
            "Bottom-left sort order is not deterministic.");
        var topRight = map.GetTiles(TilemapSortOrder.TopRight);
        Require(topRight.First().Position == new TileCoordinate(2, 1),
            "Top-right sort order is not deterministic.");
        Require(map.ClearTile(new TileCoordinate(2, 1)) && !map.HasTile(new TileCoordinate(2, 1)),
            "ClearTile failed.");
        map.ClearAllTiles();
        Require(map.cellCount == 0 && map.GetBounds().IsEmpty, "ClearAllTiles did not reset bounds.");
    }

    private static void SerializationRoundTrip()
    {
        var source = NewMap();
        source.SetTile(new TileCoordinate(-4, 7), 11,
            TileTransformFlags.FlipY | TileTransformFlags.Rotate90);
        source.SetTile(new TileCoordinate(3, -2), 5, TileTransformFlags.Rotate180);
        var fields = ComponentFieldSerializer.Serialize(source);
        Require(fields.TryGetValue("_serializedCells", out var data) &&
                data.StartsWith("v1|", StringComparison.Ordinal),
            "Tilemap did not expose versioned cell data to scene serialization.");

        var restored = NewMap();
        ComponentFieldSerializer.Deserialize(restored, fields);
        Require(restored.cellCount == 2, "Tilemap cell count did not survive component serialization.");
        Require(restored.GetCell(new TileCoordinate(-4, 7))?.Transform ==
                (TileTransformFlags.FlipY | TileTransformFlags.Rotate90),
            "Tile flags did not survive component serialization.");
        Require(ComponentFieldSerializer.Serialize(restored)["_serializedCells"] == data,
            "Tile serialization is not deterministic after a round trip.");
    }

    private static void CoordinateConversion()
    {
        var map = NewMap();
        map.cellSize = new Vector2(2, 3);
        map.cellGap = new Vector2(1, 1);
        map.tileAnchor = new Vector2(Fix64.Half, Fix64.Half);
        map.transform.position = new Vector2(10, -5);
        var coordinate = new TileCoordinate(2, -1);
        var world = map.CellToWorld(coordinate);
        Require(map.WorldToCell(world) == coordinate, "Cell/world coordinate conversion is not reversible.");
    }

    private static void PaletteRoundTripAndValidation()
    {
        var palette = new TilePalette { Name = "Test", Atlas = "Assets/Tiles.png" };
        palette.SliceAtlas(2, 2);
        palette.Tiles[1].HasCollider = true;
        Require(palette.Find(2)?.UvX == 0.5f && palette.Find(2)?.HasCollider == true,
            "Atlas slicing generated incorrect tile metadata.");
        var yaml = YamlUtility.Serialize(palette);
        var restored = YamlUtility.Deserialize<TilePalette>(yaml);
        restored.Validate();
        Require(restored.Tiles.Count == 4 && restored.Find(4)?.UvY == 0.5f,
            "Palette YAML round trip failed.");

        restored.Tiles.Add(new TileDefinition { Id = 4 });
        Expect<InvalidDataException>(restored.Validate, "Duplicate palette IDs were accepted.");
    }

    private static void RendererBoundaries()
    {
        var owner = new GameObject("Tilemap Renderer");
        var renderer = owner.AddComponent<TilemapRenderer>();
        Require(owner.GetComponent<Tilemap>() is not null,
            "TilemapRenderer did not require a Tilemap component.");
        renderer.sortingLayer = SortingLayer.Default;
        Expect<ArgumentOutOfRangeException>(() => renderer.sortingLayer = SortingLayer.Ui,
            "TilemapRenderer accepted a UI sorting layer.");
    }

    private static void GpuBatchRendering()
    {
        var repository = FindRepositoryRoot();
        Resources.RegisterResourceRoot(Path.Combine(repository, "src", "Core"));
        Resources.RegisterResourceRoot(Path.Combine(repository, "src", "Packages", "TiledMap"));
        using var device = new RecordingGraphicsDevice();
        using var renderer = new PortableSceneRenderer(device);
        var scene = new Scene("Tiled Map Rendering");
        var owner = scene.CreateGameObject("Tilemap");
        var map = owner.AddComponent<Tilemap>();
        var palette = new TilePalette();
        palette.SliceAtlas(1, 1);
        map.SetPaletteOverride(palette);
        map.SetTile(new TileCoordinate(-1, 0), 1);
        map.SetTile(new TileCoordinate(0, 0), 1);
        map.SetTile(new TileCoordinate(1, 0), 1);
        map.SetTile(new TileCoordinate(1000, 1000), 1);
        owner.AddComponent<TilemapRenderer>();

        renderer.Render(scene, RenderCamera.Default, 320, 180);

        var mesh = device.Meshes.Single(item => item.Label == "BEngine.TiledMap.DynamicMesh");
        var draws = device.Draws.Where(draw => ReferenceEquals(draw.Mesh, mesh)).ToArray();
        Require(mesh.UpdateCount == 1 && mesh.VertexCount == 18,
            $"Expected one upload with 18 visible tile vertices, got {mesh.UpdateCount}/{mesh.VertexCount}.");
        Require(draws.Length == 1 && draws[0].VertexCount == 18,
            "Tiles with one Material/Shader/Atlas key did not render as one batch.");
        Require(device.Textures.Any(texture => texture.Label == "BEngine.TiledMap.Missing"),
            "Missing Atlas fallback texture was not created.");

        using var filteredDevice = new RecordingGraphicsDevice();
        using var filteredRenderer = new PortableSceneRenderer(filteredDevice);
        var filterCalls = 0;
        filteredRenderer.RenderViewport([scene], scene, RenderCamera.Default,
            new GraphicsRect(0, 0, 320, 180), drawUi: false,
            objectFilter: gameObject =>
            {
                filterCalls++;
                return !ReferenceEquals(gameObject, owner);
            });
        var filteredMesh = filteredDevice.Meshes.Single(item => item.Label == "BEngine.TiledMap.DynamicMesh");
        Require(filterCalls > 0 && filteredMesh.UpdateCount == 0 &&
                filteredDevice.Draws.All(draw => !ReferenceEquals(draw.Mesh, filteredMesh)),
            "The Scene object filter did not suppress a Tilemap contributor owned by a hidden GameObject.");
    }

    private static Tilemap NewMap()
    {
        var owner = new GameObject("Tilemap");
        var map = owner.AddComponent<Tilemap>();
        var palette = new TilePalette();
        palette.SliceAtlas(4, 4);
        map.SetPaletteOverride(palette);
        return map;
    }

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
            GraphicsDeviceFeatures.Rasterization |
            GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers |
            GraphicsDeviceFeatures.SampledTextures |
            GraphicsDeviceFeatures.AlphaBlending |
            GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        public List<RecordingMesh> Meshes { get; } = [];
        public List<RecordingTexture> Textures { get; } = [];
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
        public void BindTexture(int slot, IGraphicsTexture2D texture) { }
        public void Draw(IGraphicsMesh mesh) => Draw(mesh, mesh.VertexCount);
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) =>
            Draws.Add(new DrawRecord((RecordingMesh)mesh, vertexCount, firstVertex));
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

    private sealed record DrawRecord(RecordingMesh Mesh, int VertexCount, int FirstVertex);
}
