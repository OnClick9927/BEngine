using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.GpuTextAtlasCapacity;

internal static class Program
{
    private const int TextCount = 448;
    private const int TextWidth = 256;
    private const int TextHeight = 32;
    private const int NaturalTextWidth = 24;
    private const int ConstrainedTextWidth = 31;
    private const int ConstrainedNaturalTextWidth = 512;
    private const int NarrowResizeWidth = 64;
    private const int ResizeTextCount = 192;
    private const byte FinalTextMarker = 254;
    private const byte LongTextMarker = 253;
    private const byte MenuTextMarker = 252;
    private const byte ChurnTextMarker = 251;
    private const byte ResizeTextMarker = 250;
    private const string LongText =
        "This project asset name is deliberately much longer than its available row width";
    private const string ResizeProbeText =
        "Narrow window text must stay visible throughout continuous resize frames";
    private const string ResizeStressPrefix = "Narrow resize atlas label ";

    private static int Main()
    {
        try
        {
            Resources.RegisterResourceRoot(Path.Combine(FindRepositoryRoot(), "src", "Core"));
            using var device = new RecordingGraphicsDevice();
            var resources = new RecordingTextResources();
            using var renderer = new GpuCanvasRenderer(device, resourceResolver: resources);
            var clip = new GpuCanvasRect(0, 0, TextWidth, TextHeight);
            var commands = Enumerable.Range(0, TextCount)
                .Select(index => new GpuCanvasCommand(
                    GpuCanvasCommandType.Text,
                    new GpuCanvasRect(0, 0, TextWidth, TextHeight),
                    clip,
                    new GpuCanvasColor(255, 255, 255),
                    Text(index),
                    13))
                .ToArray();

            renderer.Render(commands, TextWidth, TextHeight);

            var texturedMesh = device.Meshes.Single(mesh => mesh.Label.EndsWith("TextureDynamic"));
            var atlas = device.Textures.Single(texture => texture.Label.EndsWith("Atlas"));
            var standaloneTexts = device.Textures
                .Where(texture => texture.Label.EndsWith("Text"))
                .ToArray();
            var lastDraw = device.Draws[^1];

            Require(renderer.LastRenderStats.CommandCount == TextCount &&
                    renderer.LastRenderStats.VisibleCommandCount == TextCount,
                $"Expected all {TextCount} unique texts to remain visible, got {renderer.LastRenderStats}.");
            Require(resources.ResolveCount == TextCount,
                $"Expected {TextCount} unique text rasterizations, got {resources.ResolveCount}.");
            Require(texturedMesh.VertexCount == TextCount * 6,
                $"Expected {TextCount * 6} textured vertices, got {texturedMesh.VertexCount}.");
            Require(atlas.UpdateCount == 1 && atlas.Pixels.Any(value => value != 0),
                "The filled text atlas was not uploaded with renderable pixels.");
            Require(standaloneTexts.Length == 0,
                $"Compact short text unexpectedly created {standaloneTexts.Length} standalone textures.");
            Require(device.Draws.Count == 1 && ReferenceEquals(lastDraw.Texture, atlas) &&
                    lastDraw.FirstVertex == 0 && lastDraw.VertexCount == TextCount * 6,
                "The compact text quads were not submitted as one renderable atlas batch.");
            Require(QuadWidth(texturedMesh, 0) == NaturalTextWidth &&
                    QuadWidth(texturedMesh, TextCount - 1) == NaturalTextWidth,
                "Short text destination quads retained the full 256-pixel control width.");
            Require(ContainsAlpha(atlas.Pixels, FinalTextMarker),
                "The final unique short text did not reach the uploaded atlas pixels.");

            var resolveCount = resources.ResolveCount;
            var previousDrawCount = device.Draws.Count;
            renderer.Render([commands[^1]], TextWidth, TextHeight);
            var cachedDraw = device.Draws[^1];
            Require(resources.ResolveCount == resolveCount,
                "The final text was rasterized again instead of being reused from the atlas cache.");
            Require(device.Draws.Count == previousDrawCount + 1 &&
                    ReferenceEquals(cachedDraw.Texture, atlas) && cachedDraw.VertexCount == 6,
                "The cached final text was not submitted on the following frame.");

            var longCommand = new GpuCanvasCommand(
                GpuCanvasCommandType.Text,
                new GpuCanvasRect(7, 0, ConstrainedTextWidth, TextHeight),
                new GpuCanvasRect(0, 0, TextWidth, TextHeight),
                new GpuCanvasColor(255, 255, 255),
                LongText,
                13);
            renderer.Render([longCommand], TextWidth, TextHeight);
            Require(resources.ResolvedWidths[LongText] == ConstrainedNaturalTextWidth,
                $"Long text was not rasterized once at its {ConstrainedNaturalTextWidth}px natural width.");
            Require(Math.Abs(QuadWidth(texturedMesh, 0) - ConstrainedTextWidth) < 0.001f,
                $"Long text geometry was not cropped to its {ConstrainedTextWidth}px control width.");
            Require(device.Draws[^1].Scissor is { X: 0, Width: TextWidth },
                "Long text replaced the stable outer clip with a control-width scissor.");
            Require(device.Textures.All(texture => !texture.Label.EndsWith("Text")) &&
                    ContainsAlpha(atlas.Pixels, LongTextMarker),
                "Clamped long text did not remain renderable in the shared atlas.");

            VerifyNarrowResizeTextStability(renderer, device, resources, texturedMesh, atlas, clip);

            var atlasUploadsBeforeChurn = atlas.UpdateCount;
            var churnCommands = Enumerable.Range(0, 760)
                .Select(index => new GpuCanvasCommand(
                    GpuCanvasCommandType.Text,
                    new GpuCanvasRect(0, 0, 180, TextHeight),
                    clip,
                    new GpuCanvasColor(255, 255, 255),
                    $"Project zoom cache {index:D4}",
                    13))
                .ToArray();
            renderer.Render(churnCommands, TextWidth, TextHeight);
            renderer.Render([
                new GpuCanvasCommand(
                    GpuCanvasCommandType.Text,
                    new GpuCanvasRect(0, 0, 140, TextHeight),
                    clip,
                    new GpuCanvasColor(255, 255, 255),
                    "Open Folder",
                    13)
            ], TextWidth, TextHeight);
            var menuDraw = device.Draws[^1];
            var menuTexture = menuDraw.Texture ??
                              throw new InvalidOperationException("Context-menu text draw had no texture.");
            Require(atlas.UpdateCount == atlasUploadsBeforeChurn && ContainsAlpha(atlas.Pixels, FinalTextMarker),
                "A saturated text atlas was reset while opening a context menu.");
            Require(!ReferenceEquals(menuTexture, atlas) &&
                    menuTexture.Label.EndsWith("Text") && ContainsAlpha(menuTexture.Pixels, MenuTextMarker),
                "Context-menu text disappeared instead of using the stable standalone fallback.");
            var menuResolveCount = resources.ResolveCount;
            renderer.Render([
                new GpuCanvasCommand(
                    GpuCanvasCommandType.Text,
                    new GpuCanvasRect(0, 0, 140, TextHeight),
                    clip,
                    new GpuCanvasColor(255, 255, 255),
                    "Open Folder",
                    13)
            ], TextWidth, TextHeight);
            Require(resources.ResolveCount == menuResolveCount &&
                    ReferenceEquals(device.Draws[^1].Texture, menuTexture) && !menuTexture.IsDisposed,
                "Stable standalone context-menu text was recreated or evicted on the next frame.");

            Console.WriteLine(
                $"GPU_TEXT_ATLAS_CAPACITY_OK|texts={TextCount}|shortWidth={NaturalTextWidth}|" +
                $"clampedWidth={ConstrainedTextWidth}|naturalWidth={ConstrainedNaturalTextWidth}|" +
                "subpixel-resize-stable|stable-atlas-saturation|standalone-lru|menu-text-visible");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GPU_TEXT_ATLAS_CAPACITY_FAILED|{exception}");
            return 1;
        }
    }

    private static string Text(int index) => $"Atlas capacity text {index:D4}";

    private static void VerifyNarrowResizeTextStability(
        GpuCanvasRenderer renderer,
        RecordingGraphicsDevice device,
        RecordingTextResources resources,
        RecordingMesh texturedMesh,
        RecordingTexture atlas,
        GpuCanvasRect clip)
    {
        var standaloneTextureCount = device.Textures.Count(texture => texture.Label.EndsWith("Text"));
        var resolveCountBeforeProbe = resources.ResolveCount;
        var widths = Enumerable.Range(1, NarrowResizeWidth)
            .SelectMany(width => new[] { Math.Max(0.25f, width - 0.49f), width - 0.01f, (float)width })
            .ToArray();
        var probeX = 5.25f;
        var atlasUploadsAfterFirstProbe = -1;
        var firstUvWidth = 0f;
        var lastUvWidth = 0f;
        foreach (var width in widths)
        {
            var command = new GpuCanvasCommand(
                GpuCanvasCommandType.Text,
                new GpuCanvasRect(probeX, 0, width, TextHeight),
                clip,
                new GpuCanvasColor(255, 255, 255),
                ResizeProbeText,
                13);
            var drawCount = device.Draws.Count;
            renderer.Render([command], TextWidth, TextHeight);
            var frameDraws = device.Draws.Skip(drawCount).ToArray();
            Require(renderer.LastRenderStats is { CommandCount: 1, VisibleCommandCount: 1, VertexCount: 6 },
                $"Narrow resize text disappeared at {width:0.00}px: {renderer.LastRenderStats}.");
            Require(frameDraws.Length == 1 && ReferenceEquals(frameDraws[0].Texture, atlas) &&
                    frameDraws[0].VertexCount == 6 && frameDraws[0].Scissor is { X: 0, Width: TextWidth },
                $"Narrow resize text did not retain its stable atlas batch and outer clip at {width:0.00}px.");
            Require(Math.Abs(QuadWidth(texturedMesh, 0) - width) < 0.001f,
                $"Narrow text geometry was not cropped to its {width:0.00}px control width.");
            var uvWidth = QuadUvWidth(texturedMesh, 0);
            Require(uvWidth > 0,
                $"Narrow resize text emitted an empty UV range at {width:0.00}px.");
            if (firstUvWidth == 0) firstUvWidth = uvWidth;
            lastUvWidth = uvWidth;
            Require(device.Textures.Count(texture => texture.Label.EndsWith("Text")) == standaloneTextureCount,
                $"The shared text atlas overflowed into a standalone texture at {width:0.00}px.");
            Require(ContainsAlpha(atlas.Pixels, ResizeTextMarker),
                $"Narrow resize text pixels disappeared from the atlas at {width:0.00}px.");
            if (atlasUploadsAfterFirstProbe < 0) atlasUploadsAfterFirstProbe = atlas.UpdateCount;
            else Require(atlas.UpdateCount == atlasUploadsAfterFirstProbe,
                $"Subpixel or 1px resize variation re-uploaded the natural-width text texture at " +
                $"{width:0.00}px.");
        }
        Require(resources.ResolveCount == resolveCountBeforeProbe + 1 &&
                resources.ResolveCounts.GetValueOrDefault(ResizeProbeText) == 1 &&
                resources.ResolvedWidths[ResizeProbeText] == ConstrainedNaturalTextWidth,
            $"The same constrained text was rasterized {resources.ResolveCounts.GetValueOrDefault(ResizeProbeText)} " +
            $"times while moving through 1..{NarrowResizeWidth}px controls.");
        Require(lastUvWidth > firstUvWidth * 32,
            "Cropped text UVs did not expand with the visible control width.");

        var fractionalClip = new GpuCanvasRect(5.25f, 0.4f, 40.2f, 31.2f);
        var fractionalDrawCount = device.Draws.Count;
        renderer.Render([
            new GpuCanvasCommand(
                GpuCanvasCommandType.Text,
                new GpuCanvasRect(0, 0, NarrowResizeWidth, TextHeight),
                fractionalClip,
                new GpuCanvasColor(255, 255, 255),
                ResizeProbeText,
                13)
        ], TextWidth, TextHeight);
        var fractionalDraw = device.Draws.Skip(fractionalDrawCount).Single();
        Require(fractionalDraw.Scissor is { X: 5, Y: 0, Width: 41, Height: 32 },
            $"Fractional text clip was truncated instead of using floor/ceil bounds: {fractionalDraw.Scissor}.");
        Require(resources.ResolveCounts.GetValueOrDefault(ResizeProbeText) == 1,
            "A fractional outer clip caused stable text to be rasterized again.");

        var stressCommands = Enumerable.Range(0, ResizeTextCount)
            .Select(index => new GpuCanvasCommand(
                GpuCanvasCommandType.Text,
                new GpuCanvasRect(0, 0, 1, TextHeight),
                clip,
                new GpuCanvasColor(255, 255, 255),
                $"{ResizeStressPrefix}{index:D3}",
                13))
            .ToArray();
        var stableAtlasUploads = -1;
        var stableTextureCount = -1;
        var stableStandaloneCount = -1;
        foreach (var width in widths)
        {
            for (var index = 0; index < stressCommands.Length; index++)
                stressCommands[index] = stressCommands[index] with
                {
                    Rect = stressCommands[index].Rect with { Width = width }
                };
            var drawCount = device.Draws.Count;
            renderer.Render(stressCommands, TextWidth, TextHeight);
            var frameDraws = device.Draws.Skip(drawCount).ToArray();
            Require(renderer.LastRenderStats.CommandCount == ResizeTextCount &&
                    renderer.LastRenderStats.VisibleCommandCount == ResizeTextCount &&
                    renderer.LastRenderStats.VertexCount == ResizeTextCount * 6,
                $"One or more of {ResizeTextCount} narrow labels disappeared at {width:0.00}px: " +
                $"{renderer.LastRenderStats}.");
            Require(texturedMesh.VertexCount == ResizeTextCount * 6 &&
                    frameDraws.Sum(draw => draw.VertexCount) == ResizeTextCount * 6 &&
                    frameDraws.All(draw => draw.Texture is not null),
                $"Narrow labels lost textured vertices at {width:0.00}px.");
            Require(ContainsAlpha(atlas.Pixels, FinalTextMarker),
                $"A saturated atlas was reset during resize at {width:0.00}px.");
            if (stableTextureCount < 0)
            {
                stableAtlasUploads = atlas.UpdateCount;
                stableTextureCount = device.Textures.Count;
                stableStandaloneCount = device.Textures.Count(texture => texture.Label.EndsWith("Text"));
                Require(stableStandaloneCount > standaloneTextureCount,
                    "The resize stress did not saturate the atlas and exercise stable standalone text fallback.");
            }
            else
            {
                Require(atlas.UpdateCount == stableAtlasUploads,
                    $"The saturated atlas was reset or re-uploaded at {width:0.00}px.");
                Require(device.Textures.Count == stableTextureCount &&
                        device.Textures.Count(texture => texture.Label.EndsWith("Text")) == stableStandaloneCount,
                    $"Control-width changes created additional text textures at {width:0.00}px.");
            }
        }
        Require(Enumerable.Range(0, ResizeTextCount).All(index =>
                    resources.ResolveCounts.GetValueOrDefault($"{ResizeStressPrefix}{index:D3}") == 1),
            "At least one narrow stress label was rasterized more than once while only its control width changed.");
    }

    private static byte Marker(int index) =>
        index == TextCount - 1 ? FinalTextMarker : (byte)(index % 200 + 1);

    private static bool ContainsAlpha(byte[] pixels, byte marker)
    {
        for (var offset = 3; offset < pixels.Length; offset += 4)
            if (pixels[offset] == marker) return true;
        return false;
    }

    private static float QuadWidth(RecordingMesh mesh, int quadIndex)
    {
        var stride = mesh.Layout.StrideBytes / sizeof(float);
        var firstVertex = quadIndex * 6;
        var minimum = float.MaxValue;
        var maximum = float.MinValue;
        for (var vertex = 0; vertex < 6; vertex++)
        {
            var x = mesh.Vertices[(firstVertex + vertex) * stride];
            minimum = Math.Min(minimum, x);
            maximum = Math.Max(maximum, x);
        }
        return maximum - minimum;
    }

    private static float QuadUvWidth(RecordingMesh mesh, int quadIndex)
    {
        var stride = mesh.Layout.StrideBytes / sizeof(float);
        var firstVertex = quadIndex * 6;
        var minimum = float.MaxValue;
        var maximum = float.MinValue;
        for (var vertex = 0; vertex < 6; vertex++)
        {
            var u = mesh.Vertices[(firstVertex + vertex) * stride + 6];
            minimum = Math.Min(minimum, u);
            maximum = Math.Max(maximum, u);
        }
        return maximum - minimum;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private sealed class RecordingTextResources : IGpuCanvasResourceResolver, IGpuCanvasTextResolver
    {
        public int ResolveCount { get; private set; }
        public Dictionary<string, int> ResolvedWidths { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ResolveCounts { get; } = new(StringComparer.Ordinal);

        public bool TryResolveTexture(string source, out GpuCanvasTextureData texture)
        {
            texture = default;
            return false;
        }

        public bool TryMeasureText(string text, float fontSize, string fontFamily, out int width)
        {
            width = text == LongText ? ConstrainedNaturalTextWidth :
                text == ResizeProbeText || text.StartsWith(ResizeStressPrefix, StringComparison.Ordinal)
                    ? ConstrainedNaturalTextWidth :
                text.StartsWith("Project zoom cache ", StringComparison.Ordinal) ? 180 :
                text == "Open Folder" ? 120 : NaturalTextWidth;
            return true;
        }

        public bool TryResolveText(string text, int width, int height, float fontSize, string fontFamily,
            out GpuCanvasTextureData texture)
        {
            ResolveCount++;
            ResolvedWidths[text] = width;
            ResolveCounts[text] = ResolveCounts.GetValueOrDefault(text) + 1;
            var marker = text == LongText ? LongTextMarker :
                text == ResizeProbeText || text.StartsWith(ResizeStressPrefix, StringComparison.Ordinal)
                    ? ResizeTextMarker :
                text.StartsWith("Project zoom cache ", StringComparison.Ordinal) ? ChurnTextMarker :
                text == "Open Folder" ? MenuTextMarker : Marker(int.Parse(text.AsSpan(text.Length - 4)));
            texture = new GpuCanvasTextureData(width, height, GraphicsTextureFormat.R8Unorm,
                Enumerable.Repeat(marker, checked(width * height)).ToArray());
            return true;
        }
    }

    private sealed class RecordingGraphicsDevice : IGraphicsDevice
    {
        private RecordingTexture? _boundTexture;
        private GraphicsRect? _scissor;

        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL,
            "Atlas Capacity Recording GPU",
            "1.0",
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
            var texture = new RecordingTexture(this, label, description, initialData);
            Textures.Add(texture);
            return texture;
        }

        public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description) =>
            throw new NotSupportedException();
        public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget) => throw new NotSupportedException();
        public void SetViewport(GraphicsRect viewport) { }
        public void SetScissor(GraphicsRect? scissor) => _scissor = scissor;
        public void Clear(GraphicsClearFlags flags, System.Numerics.Vector4 color) { }
        public void SetDepthState(GraphicsDepthState state) { }
        public void SetBlendMode(GraphicsBlendMode mode) { }
        public void SetRasterizerState(GraphicsRasterizerState state) { }
        public void BindTexture(int slot, IGraphicsTexture2D texture) => _boundTexture = (RecordingTexture)texture;
        public void Draw(IGraphicsMesh mesh) => Draw(mesh, mesh.VertexCount);
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) =>
            Draws.Add(new DrawRecord((RecordingMesh)mesh, _boundTexture, vertexCount, firstVertex, _scissor));
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

    private sealed class RecordingMesh(IGraphicsDevice device, GraphicsMeshDescription description) : IGraphicsMesh
    {
        public IGraphicsDevice Device => device;
        public string Label => description.Label;
        public GraphicsVertexLayout Layout => description.Layout;
        public GraphicsPrimitiveTopology Topology => description.Topology;
        public GraphicsBufferUsage Usage => description.Usage;
        public int VertexCount { get; private set; }
        public float[] Vertices { get; private set; } = [];

        public void Update(ReadOnlySpan<float> vertices)
        {
            Vertices = vertices.ToArray();
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
        }

        public void Dispose() { }
    }

    private sealed class RecordingTexture : IGraphicsTexture2D
    {
        public RecordingTexture(IGraphicsDevice device, string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> pixels)
        {
            Device = device;
            Label = label;
            Description = description;
            Pixels = pixels.ToArray();
        }

        public IGraphicsDevice Device { get; }
        public string Label { get; }
        public GraphicsTextureDescription Description { get; }
        public byte[] Pixels { get; private set; }
        public int UpdateCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Update(ReadOnlySpan<byte> pixels)
        {
            UpdateCount++;
            Pixels = pixels.ToArray();
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed record DrawRecord(
        RecordingMesh Mesh,
        RecordingTexture? Texture,
        int VertexCount,
        int FirstVertex,
        GraphicsRect? Scissor);
}
