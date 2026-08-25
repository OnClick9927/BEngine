using BEngine.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.GameViewResolution;

internal static class Program
{
    private static int Main()
    {
        var previousDataPath = Environment.GetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH");
        var previousResolution = Screen.currentResolution;
        var previousMode = Screen.fullScreenMode;
        var testDirectory = Path.Combine(Path.GetTempPath(), $"BEngine-GameView-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", testDirectory);
        try
        {
            VerifyBuiltInsAndFreeAspect();
            VerifyFixedViewportAndScreenSemantics();
            VerifyLogicalRenderSizeIsIndependentFromPreviewViewport();
            VerifyCustomResolutionLifecycle();
            Console.WriteLine(
                "GAME_VIEW_RESOLUTION_OK|presets,search-menu-data,free-aspect,fixed-letterbox," +
                "logical-screen-size,logical-render-size,editor-prefs,custom-add-delete");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Screen.SetResolution(previousResolution.width, previousResolution.height,
                previousMode, previousResolution.refreshRate);
            Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", previousDataPath);
            try { Directory.Delete(testDirectory, true); } catch { }
        }
    }

    private static void VerifyBuiltInsAndFreeAspect()
    {
        var settings = new GameViewResolutionSettings();
        Require(settings.selected.IsFreeAspect && settings.selected.SizeLabel == "Free Aspect",
            "Game View did not default to Free Aspect.");
        Require(settings.builtInOptions.Any(option =>
                    option.Width == 1920 && option.Height == 1080) &&
                settings.builtInOptions.Any(option =>
                    option.Width == 1080 && option.Height == 1920) &&
                settings.builtInOptions.All(option => option.IsBuiltIn),
            "Common landscape and portrait presets were not available to the searchable menu.");

        var available = new GraphicsRect(11, 17, 913, 527);
        Require(settings.FitViewport(available) == available,
            "Free Aspect changed the available Game View viewport.");
        settings.ApplyScreenSize(available);
        Require(Screen.width == available.Width && Screen.height == available.Height,
            "Free Aspect did not expose the current panel pixel size through Screen.");
    }

    private static void VerifyFixedViewportAndScreenSemantics()
    {
        var settings = new GameViewResolutionSettings();
        var fullHd = settings.builtInOptions.Single(option =>
            option.Id == "standalone-1920x1080");
        settings.Select(fullHd);
        Require(Screen.width == 1920 && Screen.height == 1080,
            "Selecting a fixed Game View preset did not immediately update Screen width and height.");
        Require(EditorPrefs.GetString(GameViewResolutionSettings.SelectedPreferenceKey) == fullHd.Id,
            "The selected Game View preset was not stored in EditorPrefs.");

        var available = new GraphicsRect(10, 20, 1000, 700);
        var fitted = settings.FitViewport(available);
        Require(fitted == new GraphicsRect(10, 88, 1000, 563),
            $"The fixed 16:9 viewport was not centered with letterboxing: {fitted}.");
        var target = settings.ResolveTargetSize(fitted);
        Require(target == (1920, 1080),
            "The fitted preview pixels replaced the fixed logical render resolution.");
        Require(available == new GraphicsRect(10, 20, 1000, 700),
            "Fitting a Game View resolution mutated the host editor viewport.");

        var restored = new GameViewResolutionSettings();
        Require(restored.selected.Id == fullHd.Id,
            "A new Game View did not restore its selected preset from EditorPrefs.");
    }

    private static void VerifyCustomResolutionLifecycle()
    {
        var settings = new GameViewResolutionSettings();
        var custom = settings.AddCustom("Tablet/Portrait", 1200, 1600);
        Require(custom.Name == "Tablet-Portrait" && !custom.IsBuiltIn &&
                settings.selected.Id == custom.Id && Screen.width == 1200 && Screen.height == 1600,
            "Adding a custom Game View resolution did not normalize, select, and apply it.");
        Require(EditorPrefs.GetString(GameViewResolutionSettings.CustomPreferenceKey)
                    .Contains(custom.Id, StringComparison.Ordinal),
            "Custom Game View resolutions were not serialized into EditorPrefs.");

        var restored = new GameViewResolutionSettings();
        Require(restored.customOptions.Any(option => option.Id == custom.Id) &&
                restored.selected.Id == custom.Id,
            "A custom Game View resolution did not survive settings reconstruction.");
        Require(restored.RemoveCustom(custom.Id) && restored.selected.IsFreeAspect,
            "Deleting the selected custom resolution did not return to Free Aspect.");
        Require(new GameViewResolutionSettings().customOptions.All(option => option.Id != custom.Id),
            "A deleted custom Game View resolution remained in EditorPrefs.");

        var rejected = false;
        try { settings.AddCustom("Invalid", 0, 720); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected, "A zero-width custom Game View resolution was accepted.");
        Require(File.Exists(EditorDataPaths.editorPrefsPath),
            "Game View state was not written to the editor preferences document.");
    }

    private static void VerifyLogicalRenderSizeIsIndependentFromPreviewViewport()
    {
        Resources.RegisterResourceRoot(Path.Combine(FindRepositoryRoot(), "src", "Core"));
        var scene = new Scene("Game View logical resolution");
        try
        {
            var camera = scene.CreateGameObject("Camera").AddComponent<Camera2D>();
            scene.CreateGameObject("Sprite").AddComponent<SpriteRenderer>();
            var device = new RecordingGraphicsDevice();
            using var renderer = new PortableSceneRenderer(device);
            var preview = new GraphicsRect(32, 48, 800, 450);
            renderer.RenderCameras([scene], scene, [camera], preview,
                1920, 1080, drawUi: false);

            Require(device.Viewports.All(viewport => viewport == preview),
                "A fixed Game View resolution resized the actual GPU/editor viewport.");
            Require(device.FloatUniforms.Contains(("uViewportWidth", 1920f)) &&
                    device.FloatUniforms.Contains(("uViewportHeight", 1080f)),
                "Camera rendering did not use the selected logical Game View dimensions.");
        }
        finally
        {
            if (scene.isCreated) scene.Dispose();
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingGraphicsDevice : IGraphicsDevice
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL, "Game View Test", "1.0",
            GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.AlphaBlending |
            GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        public List<GraphicsRect> Viewports { get; } = [];
        public List<(string Name, float Value)> FloatUniforms { get; } = [];
        public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
            new RecordingProgram(this, description.Label);
        public IGraphicsMesh CreateMesh(GraphicsMeshDescription description) =>
            new RecordingMesh(this, description);
        public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData = default) => throw new NotSupportedException();
        public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description) =>
            throw new NotSupportedException();
        public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget) =>
            throw new NotSupportedException();
        public void SetViewport(GraphicsRect viewport) => Viewports.Add(viewport);
        public void SetScissor(GraphicsRect? scissor) { }
        public void Clear(GraphicsClearFlags flags, System.Numerics.Vector4 color) { }
        public void SetDepthState(GraphicsDepthState state) { }
        public void SetBlendMode(GraphicsBlendMode mode) { }
        public void SetRasterizerState(GraphicsRasterizerState state) { }
        public void BindTexture(int slot, IGraphicsTexture2D texture) { }
        public void Draw(IGraphicsMesh mesh) { }
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) { }
        public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0) { }
        public void Dispose() { }
    }

    private sealed class RecordingProgram(RecordingGraphicsDevice device, string label) : IGraphicsProgram
    {
        public IGraphicsDevice Device => device;
        public string Label => label;
        public void Bind() { }
        public void SetMatrix4x4(string name, System.Numerics.Matrix4x4 value) { }
        public void SetVector4(string name, System.Numerics.Vector4 value) { }
        public void SetFloat(string name, float value) => device.FloatUniforms.Add((name, value));
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
        public void Update(ReadOnlySpan<float> vertices) =>
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
        public void Dispose() { }
    }
}
