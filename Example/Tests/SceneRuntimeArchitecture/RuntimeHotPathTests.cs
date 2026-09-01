using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class RuntimeHotPathTests
{
    private const int TickWarmupFrames = 32;
    private const int TickMeasuredFrames = 512;
    private const int SpriteCount = 256;
    private const int RenderWarmupFrames = 4;
    private const int RenderMeasuredFrames = 4;
    private const long TickAllocationBudget = 1_024;
    private const long RenderAllocationBudgetPerFrame = 70_000;

    internal static void Run()
    {
        VerifyDeepHierarchyActiveState();
        VerifyDelayedDestroyQueue();
        VerifyRendererRejectsReentry();
        var tickBytes = MeasureRuntimeTickAllocations();
        var renderBytes = MeasureSolidQuadRenderAllocations();

        Require(tickBytes <= TickAllocationBudget,
            $"Steady-state SceneRuntime.Tick allocated {tickBytes} bytes for {TickMeasuredFrames} frames; " +
            $"budget is {TickAllocationBudget} bytes.");
        Require(renderBytes <= RenderAllocationBudgetPerFrame * RenderMeasuredFrames,
            $"Solid-quad rendering allocated {renderBytes / RenderMeasuredFrames} bytes per frame; " +
            $"budget is {RenderAllocationBudgetPerFrame} bytes.");

        Console.WriteLine($"RUNTIME_HOT_PATHS|tickFrames={TickMeasuredFrames},tickBytes={tickBytes}," +
                          $"sprites={SpriteCount},renderFrames={RenderMeasuredFrames}," +
                          $"renderBytes={renderBytes},renderBytesPerFrame={renderBytes / RenderMeasuredFrames}");
    }

    private static void VerifyDeepHierarchyActiveState()
    {
        using var scene = new Scene("Deep active hierarchy");
        var root = scene.CreateGameObject("Root");
        var parent = root;
        GameObject? middle = null;
        var leaf = root;
        for (var depth = 1; depth < 256; depth++)
        {
            leaf = scene.CreateGameObject($"Node {depth}");
            leaf.transform.SetParent(parent.transform, false);
            parent = leaf;
            if (depth == 128) middle = leaf;
        }

        root.SetActive(false);
        Require(!leaf.activeInHierarchy,
            "An inactive root did not propagate its cached hierarchy state to a deep descendant.");
        middle!.SetActive(false);
        root.SetActive(true);
        Require(!leaf.activeInHierarchy,
            "An inactive middle node did not keep a deep descendant inactive after its root was enabled.");
        middle.SetActive(true);
        Require(leaf.activeInHierarchy,
            "Re-enabling a middle node did not restore a deep descendant's cached hierarchy state.");
    }

    private static long MeasureRuntimeTickAllocations()
    {
        const string registration = "scene-runtime-hot-path-allocation";
        const string sceneProbeRegistration = "scene-runtime-hot-path-scene-probe";
        var system = new AllocationProbeSystem();
        var sceneProbe = new SceneProbeSystem();
        sceneProbe.Calls.EnsureCapacity((TickWarmupFrames + TickMeasuredFrames) * 2 + 2);
        RuntimeSystemRegistry.Register(registration, () => system);
        RuntimeSystemRegistry.Register(sceneProbeRegistration, () => sceneProbe);
        using var scene = new Scene("Runtime tick allocation");
        var runtime = new SceneRuntime(scene);
        try
        {
            runtime.Start();
            for (var frame = 0; frame < TickWarmupFrames; frame++)
                runtime.Tick(Time.fixedDeltaTime);

            CollectGarbage();
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < TickMeasuredFrames; frame++)
                runtime.Tick(Time.fixedDeltaTime);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

            Require(system.UpdateCount == TickWarmupFrames + TickMeasuredFrames &&
                    system.FixedUpdateCount == TickWarmupFrames + TickMeasuredFrames,
                "The allocation probe did not exercise both per-frame runtime-system callbacks.");
            return allocated;
        }
        finally
        {
            runtime.Stop();
            RuntimeSystemRegistry.Unregister(sceneProbeRegistration);
            RuntimeSystemRegistry.Unregister(registration);
        }
    }

    private static void VerifyDelayedDestroyQueue()
    {
        using var scene = new Scene("Delayed destroy queue");
        var target = scene.CreateGameObject("Delayed target");
        var runtime = new SceneRuntime(scene);
        try
        {
            runtime.Start();
            BObject.Destroy(target, Time.fixedDeltaTime * 2);
            runtime.Tick(Time.fixedDeltaTime);
            Require(ReferenceEquals(scene.Find("Delayed target"), target),
                "Delayed destruction removed its target before the requested time.");
            runtime.Tick(Time.fixedDeltaTime);
            Require(scene.Find("Delayed target") is null,
                "Delayed destruction did not remove its target when the requested time elapsed.");
        }
        finally
        {
            runtime.Stop();
        }
    }

    private static long MeasureSolidQuadRenderAllocations()
    {
        var resourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Core");
        Resources.RegisterResourceRoot(resourceRoot);
        using var scene = new Scene("Solid quad allocation");
        try
        {
            for (var index = 0; index < SpriteCount; index++)
                scene.CreateGameObject($"Sprite {index}").AddComponent<SpriteRenderer>();

            using var device = new AllocationGraphicsDevice();
            using var renderer = new PortableSceneRenderer(device);
            for (var frame = 0; frame < RenderWarmupFrames; frame++)
                renderer.Render(scene, RenderCamera.Default, 640, 360);

            CollectGarbage();
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < RenderMeasuredFrames; frame++)
                renderer.Render(scene, RenderCamera.Default, 640, 360);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

            Require(device.DrawCallCount >= RenderWarmupFrames + RenderMeasuredFrames,
                "The allocation renderer did not submit the solid-quad batch.");
            Require(renderer.LastRenderStatistics.VisibleSubmissionCount == SpriteCount &&
                    renderer.LastRenderStatistics.BatchCount == 1 &&
                    device.LastVertexCount == SpriteCount * 6,
                "The optimized renderer dropped submissions or produced an incomplete solid-quad batch.");

            using var sparseScene = new Scene("Sparse solid quad allocation");
            sparseScene.CreateGameObject("Only sprite").AddComponent<SpriteRenderer>();
            renderer.Render(sparseScene, RenderCamera.Default, 640, 360);
            Require(renderer.LastRenderStatistics.VisibleSubmissionCount == 1 &&
                    renderer.LastRenderStatistics.BatchCount == 1 && device.LastVertexCount == 6,
                "The renderer retained submissions from the preceding dense scene.");

            using var emptyScene = new Scene("Empty solid quad allocation");
            var drawCallsBeforeEmptyScene = device.DrawCallCount;
            renderer.Render(emptyScene, RenderCamera.Default, 640, 360);
            Require(renderer.LastRenderStatistics.VisibleSubmissionCount == 0 &&
                    renderer.LastRenderStatistics.BatchCount == 0 &&
                    device.DrawCallCount == drawCallsBeforeEmptyScene,
                "The renderer submitted stale geometry for an empty scene.");
            return allocated;
        }
        finally
        {
            Resources.UnregisterResourceRoot(resourceRoot);
        }
    }

    private static void VerifyRendererRejectsReentry()
    {
        var resourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Core");
        Resources.RegisterResourceRoot(resourceRoot);
        using var scene = new Scene("Renderer reentry");
        scene.CreateGameObject("Sprite").AddComponent<SpriteRenderer>();
        using var device = new AllocationGraphicsDevice();
        using var renderer = new PortableSceneRenderer(device);
        try
        {
            var rejected = false;
            try
            {
                renderer.RenderViewport([scene], scene, RenderCamera.Default,
                    new GraphicsRect(0, 0, 320, 180),
                    objectFilter: _ =>
                    {
                        renderer.Render(scene, RenderCamera.Default, 320, 180);
                        return true;
                    });
            }
            catch (InvalidOperationException exception)
            {
                rejected = exception.Message.Contains("reentrant", StringComparison.OrdinalIgnoreCase);
            }

            Require(rejected, "PortableSceneRenderer allowed a reentrant render to corrupt shared buffers.");
            renderer.Render(scene, RenderCamera.Default, 320, 180);
            Require(renderer.LastRenderStatistics.VisibleSubmissionCount == 1,
                "PortableSceneRenderer did not release its render guard after a failed reentrant render.");
        }
        finally
        {
            Resources.UnregisterResourceRoot(resourceRoot);
        }
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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

    private sealed class AllocationProbeSystem : ISceneRuntimeSystem
    {
        internal int FixedUpdateCount { get; private set; }
        internal int UpdateCount { get; private set; }
        public void FixedUpdate(Scene scene, Fix64 fixedDeltaTime) => FixedUpdateCount++;
        public void Update(Scene scene, Fix64 deltaTime) => UpdateCount++;
    }

    private sealed class AllocationGraphicsDevice : IGraphicsDevice
    {
        internal int DrawCallCount { get; private set; }
        internal int LastVertexCount { get; private set; }
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL,
            "Allocation probe",
            "1.0",
            GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.AlphaBlending,
            [GraphicsShaderLanguage.Glsl]);

        public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
            new AllocationGraphicsProgram(this, description.Label);
        public IGraphicsMesh CreateMesh(GraphicsMeshDescription description) =>
            new AllocationGraphicsMesh(this, description);
        public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData = default) => throw new NotSupportedException();
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
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
        {
            DrawCallCount++;
            LastVertexCount = vertexCount;
        }
        public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
        {
            DrawCallCount++;
            LastVertexCount = vertexCount;
        }
        public void Dispose() { }
    }

    private sealed class AllocationGraphicsProgram(IGraphicsDevice device, string label) : IGraphicsProgram
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

    private sealed class AllocationGraphicsMesh : IGraphicsMesh
    {
        internal AllocationGraphicsMesh(IGraphicsDevice device, GraphicsMeshDescription description)
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
