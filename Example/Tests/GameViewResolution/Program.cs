using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using System.Reflection;
using System.Runtime.CompilerServices;

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
            VerifyFrameTiming();
            VerifyExpandableStatusPresentation();
            VerifyCustomResolutionLifecycle();
            Console.WriteLine(
                "GAME_VIEW_RESOLUTION_OK|presets,search-menu-data,free-aspect,fixed-letterbox," +
                "logical-screen-size,logical-render-size,render-statistics,status-toggle," +
                "frame-timing,editor-prefs,custom-add-delete");
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
            var statistics = renderer.LastRenderStatistics;
            Require(statistics is
                    {
                        CameraCount: 1,
                        VisibleSubmissionCount: 1,
                        BatchCount: 1,
                        DrawCallCount: 3,
                        VertexCount: 18,
                        TriangleCount: 6,
                        TargetWidth: 1920,
                        TargetHeight: 1080,
                        HasCompleteDrawStatistics: true
                    },
                $"Game rendering did not expose its actual draw statistics: {statistics}.");
        }
        finally
        {
            if (scene.isCreated) scene.Dispose();
        }
    }

    private static void VerifyFrameTiming()
    {
        var timing = new GameViewFrameTiming();
        timing.RecordSample(double.NaN);
        timing.RecordSample(-1);
        Require(!timing.snapshot.HasValue,
            "Invalid render intervals polluted the Game View frame timing.");
        timing.RecordSample(1d / 60d);
        var first = timing.snapshot;
        Require(first.HasValue && Math.Abs(first.FramesPerSecond - 60) < 0.01 &&
                Math.Abs(first.FrameTimeMilliseconds - 16.6667) < 0.01,
            "The Game View did not derive FPS and frame time from the render interval.");
        timing.RecordSample(1d / 30d);
        var smoothed = timing.snapshot;
        Require(smoothed.SampleCount == 2 && smoothed.FramesPerSecond is > 30 and < 60,
            "Game View frame timing was not smoothed across real render samples.");
    }

    private static void VerifyExpandableStatusPresentation()
    {
        var editorAssembly = typeof(EditorWindow).Assembly;
        var appType = editorAssembly.GetType("BEngine.Editor.GpuEditorApplication", true)!;
        var app = RuntimeHelpers.GetUninitializedObject(appType);
        var windowType = appType.GetNestedType("ImGuiGameWindow", BindingFlags.NonPublic) ??
                         throw new TypeLoadException("ImGuiGameWindow was not found.");
        var window = (EditorWindow)(Activator.CreateInstance(windowType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, [app], culture: null) ??
                                    throw new InvalidOperationException("Could not create the Game View."));
        windowType.GetField("_statusExpanded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, true);
        windowType.GetField("_renderStatistics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, new SceneRenderStatistics(2, 7, 3, 5, 42, 14, 0,
                1920, 1080, true));
        windowType.GetField("_hasRenderStatistics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, true);

        static IReadOnlyList<GpuCanvasCommand> Render(EditorWindow target, int width)
        {
            var output = new List<GpuCanvasCommand>();
            GUI.BeginFrame(new Event(EventType.Repaint), width, 320, output);
            try { target.OnGUIInternal(); }
            finally { GUI.EndFrame(); }
            return output;
        }

        var commands = Render(window, 480);
        var text = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(text.Contains("Status", StringComparer.Ordinal) &&
                text.Contains("Statistics", StringComparer.Ordinal) &&
                text.Contains("Cameras: 2   Visible: 7", StringComparer.Ordinal) &&
                text.Contains("Batches: 3   Draw calls: 5", StringComparer.Ordinal) &&
                text.Contains("Tris: 14   Verts: 42", StringComparer.Ordinal) &&
                text.Contains("Screen: 1,920 x 1,080", StringComparer.Ordinal),
            "The expandable Game View Status panel did not render its real graphics metrics.");

        windowType.GetField("_renderStatistics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, new SceneRenderStatistics(2, 7, 3, 0, 0, 0, 0,
                1920, 1080, false));
        var narrow = Render(window, 160);
        var narrowText = narrow.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(narrowText.Contains("Status", StringComparer.Ordinal) &&
                narrowText.Contains("Draw: unavailable", StringComparer.Ordinal) &&
                narrowText.Contains("Tris: unavailable", StringComparer.Ordinal),
            "A Game View without device counters displayed invented graphics statistics.");
        Require(narrow.All(command => command.Rect.X >= -0.01f &&
                                      command.Rect.Right <= 160.01f &&
                                      command.ClipRect.X >= -0.01f &&
                                      command.ClipRect.Right <= 160.01f),
            "The Status toolbar control or overlay escaped a narrow Game View.");
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

    private sealed class RecordingGraphicsDevice : IGraphicsDevice, IGraphicsDeviceStatistics
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
        public GraphicsDrawStatistics DrawStatistics { get; private set; }
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
        public void Draw(IGraphicsMesh mesh) => RecordDraw(mesh.VertexCount, mesh.Topology);
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) =>
            RecordDraw(vertexCount, mesh.Topology);
        public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0) =>
            RecordDraw(vertexCount, topology);
        public void Dispose() { }
        private void RecordDraw(int vertexCount, GraphicsPrimitiveTopology topology) =>
            DrawStatistics = new GraphicsDrawStatistics(
                DrawStatistics.DrawCallCount + 1,
                DrawStatistics.VertexCount + vertexCount,
                DrawStatistics.TriangleCount +
                (topology == GraphicsPrimitiveTopology.TriangleList ? vertexCount / 3 : 0),
                DrawStatistics.LineCount +
                (topology == GraphicsPrimitiveTopology.LineList ? vertexCount / 2 : 0));
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
