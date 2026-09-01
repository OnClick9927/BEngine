using BEngine;
using BEngine.Editor.Diagnostics;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using BEngine.Rendering.Rhi.Vulkan;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.ExampleTests.FrameDebugger;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyResourceWrappingAndRetirement();
            VerifyColorReadbackContract();
            VerifyCaptureStateAndOrder();
            VerifyPortableSceneRendererMarkers();
            VerifyStepLimitAndTargetIsolation();
            VerifyQueuedRenderTargetPredicate();
            VerifyStaleCaptureLeaseIsolation();
            VerifyPlayModePauseOwnership();
            VerifyDisabledCaptureDoesNotAllocateEvents();
            Console.WriteLine("FRAME_DEBUGGER_OK|resources,readback-contract,capture,state," +
                              "texture-state,shader-properties,pipeline-metadata,renderer-markers,preview-readback," +
                              "target-isolation,hidden-target-replay,stale-lease-isolation,pause-ownership," +
                              "zero-idle-allocation");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyColorReadbackContract()
    {
        var region = new GraphicsRect(10, 12, 20, 16);
        Require(GraphicsColorReadbackValidation.ValidateRegion(region, 64, 48, 4096) == 1280,
            "Readback validation returned the wrong RGBA8 byte count.");
        RequireThrows<ArgumentOutOfRangeException>(
            () => GraphicsColorReadbackValidation.ValidateRegion(
                new GraphicsRect(50, 12, 20, 16), 64, 48, 4096),
            "Readback validation accepted a region outside the color surface.");
        RequireThrows<ArgumentOutOfRangeException>(
            () => GraphicsColorReadbackValidation.ValidateRegion(region, 64, 48, 1279),
            "Readback validation accepted a request above the byte limit.");

        Require(OpenGlGraphicsDevice.ToFramebufferReadRegion(region, 48) ==
                new GraphicsRect(10, 20, 20, 16),
            "OpenGL did not convert the top-left region to its bottom-left framebuffer origin.");
        Require(VulkanGraphicsDevice.ToFramebufferReadRegion(region) == region,
            "Vulkan changed a top-left readback region.");

        var rows = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        OpenGlGraphicsDevice.FlipRowsInPlace(rows, 1, 2);
        Require(rows.SequenceEqual(new byte[] { 5, 6, 7, 8, 1, 2, 3, 4 }),
            "OpenGL readback rows were not normalized to top-left order.");

        var source = new byte[] { 10, 20, 30, 40 };
        var image = new GraphicsColorReadbackImage(1, 1, source);
        source[0] = 255;
        Require(image.Pixels.Span[0] == 10,
            "Readback image retained a caller-owned mutable pixel array.");

        using var unavailable = GraphicsColorReadbackRequest.Unavailable(region, "not supported");
        Require(unavailable.Status == GraphicsColorReadbackStatus.Unavailable &&
                unavailable.Error == "not supported" &&
                !unavailable.TryGetResult(out _),
            "Unavailable readback did not preserve its explicit status and reason.");
    }

    private static void VerifyResourceWrappingAndRetirement()
    {
        var inner = new RecordingGraphicsDevice();
        using var device = new FrameDebugGraphicsDevice(inner);
        var program = device.CreateProgram(ProgramDescription("Wrapped.Program"));
        var mesh = device.CreateMesh(MeshDescription("Wrapped.Mesh"));
        var texture = device.CreateTexture2D("Wrapped.Texture", TextureDescription());
        var target = device.CreateRenderTarget("Wrapped.Target",
            new GraphicsRenderTargetDescription(32, 16, GraphicsTextureFormat.Rgba8Unorm, null,
                SampleColor: true));

        Require(ReferenceEquals(program.Device, device), "Wrapped program reported the native device.");
        Require(ReferenceEquals(mesh.Device, device), "Wrapped mesh reported the native device.");
        Require(ReferenceEquals(texture.Device, device), "Wrapped texture reported the native device.");
        Require(ReferenceEquals(target.Device, device), "Wrapped target reported the native device.");
        Require(target.ColorTexture is { } color && ReferenceEquals(color.Device, device),
            "Render-target attachment was not wrapped.");

        program.Bind();
        mesh.Update([0, 0, 0, 0, 0, 0]);
        texture.Update([255, 255, 255, 255]);
        device.BindTexture(0, texture);
        using (device.PushRenderTarget(target)) device.Draw(mesh);
        Require(inner.LastBoundTexture?.Label == texture.Label,
            "Texture wrapper was not unwrapped before native binding.");
        Require(inner.LastMesh?.Label == mesh.Label,
            "Mesh wrapper was not unwrapped before native drawing.");
        Require(inner.LastRenderTarget?.Label == target.Label,
            "Render target wrapper was not unwrapped before native binding.");
        Require(device.DrawStatistics.DrawCallCount == 1,
            "Graphics statistics were not forwarded through the decorator.");

        device.RetireResource(mesh);
        Require(inner.RetiredResources.Count == 1 &&
                inner.RetiredResources[0] is RecordingMesh { Label: "Wrapped.Mesh" },
            "Resource retirement did not forward the unwrapped native resource.");
        RequireThrows<ObjectDisposedException>(() => _ = mesh.VertexCount,
            "A retired resource wrapper remained usable.");

        using var foreignDevice = new FrameDebugGraphicsDevice(new RecordingGraphicsDevice());
        using var foreignMesh = foreignDevice.CreateMesh(MeshDescription("Foreign.Mesh"));
        RequireThrows<ArgumentException>(() => device.RetireResource(foreignMesh),
            "Resource retirement accepted a wrapper owned by another graphics device.");

        target.Dispose();
        texture.Dispose();
        program.Dispose();
    }

    private static void VerifyCaptureStateAndOrder()
    {
        var inner = new RecordingGraphicsDevice();
        using var device = new FrameDebugGraphicsDevice(inner);
        var service = new FrameDebuggerService();
        var target = new object();
        var program = device.CreateProgram(ProgramDescription("Capture.Program"));
        var mesh = device.CreateMesh(MeshDescription("Capture.Mesh", 6));
        var texture = device.CreateTexture2D("Capture.Texture", TextureDescription());
        using var renderTarget = device.CreateRenderTarget("Capture.Target",
            new GraphicsRenderTargetDescription(320, 180, GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureFormat.Depth24Stencil8, SampleColor: true, SampleDepth: true,
                Filter: GraphicsTextureFilter.Linear));

        device.SetViewport(new GraphicsRect(10, 20, 640, 360));
        device.SetScissor(new GraphicsRect(3, 4, 120, 80));
        device.SetDepthState(GraphicsDepthState.Disabled);
        device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        device.SetRasterizerState(GraphicsRasterizerState.CullBackFaces);
        program.Bind();
        program.SetInt("uTexture", 2);
        program.SetFloat("uOpacity", 0.75f);
        program.SetVector4("uTint", new NVector4(0.2f, 0.4f, 0.6f, 1));
        program.SetMatrix4x4("uModel", NMatrix4x4.CreateTranslation(2, 3, 4));
        device.BindTexture(2, texture);
        service.Enable(target, "Game A");
        service.RequestCapture();
        using (service.BeginRender(target, device, "Game A"))
        using (device.PushRenderTarget(renderTarget))
        using (device.PushMarker(new FrameDebugMarker(
                   "Camera Main", "Sprite Batch", Guid.NewGuid(), "Sprites", "Atlas/Main",
                   "Player", 42)))
        {
            device.Clear(GraphicsClearFlags.Color | GraphicsClearFlags.Depth,
                new NVector4(0.1f, 0.2f, 0.3f, 1));
            device.Draw(mesh, 3, 1);
            device.Draw(4, GraphicsPrimitiveTopology.LineList, 2);
        }

        var snapshot = service.Snapshot ?? throw new InvalidOperationException("Capture was not published.");
        Require(snapshot.TargetName == "Game A" && snapshot.Backend == GraphicsBackend.OpenGL,
            "Capture target metadata is incorrect.");
        Require(snapshot.Events.Count == 3, "Clear/draw event order was not fully captured.");
        Require(snapshot.Events[0] is
            {
                Index: 0,
                Kind: FrameDebugEventKind.Clear,
                ClearFlags: GraphicsClearFlags.Color | GraphicsClearFlags.Depth,
                Executed: true
            }, "Clear event is incorrect.");
        var draw = snapshot.Events[1];
        Require(draw.Index == 1 && draw.Kind == FrameDebugEventKind.Draw &&
                draw.MeshLabel == "Capture.Mesh" && draw.FirstVertex == 1 &&
                draw.VertexCount == 3 && draw.TriangleCount == 1,
            "Mesh draw event is incorrect.");
        Require(draw.Name == "Sprite Batch" && draw.Marker.SourceInstanceId == 42 &&
                draw.Marker.Atlas == "Atlas/Main",
            "Semantic marker was not copied into the event.");
        Require(draw.State.Viewport == new GraphicsRect(10, 20, 640, 360) &&
                draw.State.Scissor == new GraphicsRect(3, 4, 120, 80) &&
                draw.State.DepthState == GraphicsDepthState.Disabled &&
                draw.State.BlendMode == GraphicsBlendMode.AlphaBlend &&
                draw.State.RasterizerState == GraphicsRasterizerState.CullBackFaces &&
                draw.State.ProgramLabel == "Capture.Program",
            "Draw state snapshot is incomplete.");
        Require(draw.State.RenderTarget is
            {
                Label: "Capture.Target",
                Width: 320,
                Height: 180,
                ColorFormat: GraphicsTextureFormat.Rgba8Unorm,
                DepthFormat: GraphicsTextureFormat.Depth24Stencil8,
                ColorSampled: true,
                DepthSampled: true
            }, "Render-target attachments were not captured.");
        Require(draw.State.Program is { Label: "Capture.Program", Stages.Count: 2 } &&
                draw.State.Program.Stages[0] == new FrameDebugShaderStage(
                    GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, "main") &&
                draw.State.Program.Stages[1] == new FrameDebugShaderStage(
                    GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, "main"),
            "Shader-stage metadata was not captured.");
        Require(draw.Mesh is
            {
                Label: "Capture.Mesh",
                AvailableVertexCount: 6,
                Usage: GraphicsBufferUsage.Dynamic,
                StrideBytes: 24,
                Attributes.Count: 2
            } && draw.Mesh.Attributes[0] == new GraphicsVertexAttribute(0, 2, 0) &&
                 draw.Mesh.Attributes[1] == new GraphicsVertexAttribute(1, 4, 8),
            "Mesh buffer and vertex-layout metadata was not captured.");
        Require(draw.State.Textures.Count == 1 &&
                draw.State.Textures[0] == new FrameDebugTextureBinding(
                    2,
                    "Capture.Texture",
                    1,
                    1,
                    GraphicsTextureFormat.Rgba8Unorm,
                    GraphicsTextureUsage.Sampled,
                    GraphicsTextureFilter.Linear,
                    GraphicsTextureFilter.Linear,
                    GraphicsTextureAddressMode.ClampToEdge),
            "Texture bindings were not captured.");
        Require(snapshot.Events is not FrameDebugEvent[],
            "Capture events exposed their mutable backing array.");
        Require(draw.State.Textures is not FrameDebugTextureBinding[],
            "Texture bindings exposed their mutable backing array.");
        Require(draw.State.Ints is { Count: 1 } &&
                draw.State.Ints[0] == new FrameDebugIntProperty("uTexture", 2) &&
                draw.State.Floats is { Count: 1 } &&
                draw.State.Floats[0] == new FrameDebugFloatProperty("uOpacity", 0.75f) &&
                draw.State.Vectors is { Count: 1 } &&
                draw.State.Vectors[0] == new FrameDebugVectorProperty(
                    "uTint", new NVector4(0.2f, 0.4f, 0.6f, 1)) &&
                draw.State.Matrices is { Count: 1 } &&
                draw.State.Matrices[0] == new FrameDebugMatrixProperty(
                    "uModel", NMatrix4x4.CreateTranslation(2, 3, 4)),
            "Shader properties were not captured with the draw state.");
        Require(draw.State.Ints is not FrameDebugIntProperty[] &&
                draw.State.Floats is not FrameDebugFloatProperty[] &&
                draw.State.Vectors is not FrameDebugVectorProperty[] &&
                draw.State.Matrices is not FrameDebugMatrixProperty[],
            "Shader properties exposed mutable backing arrays.");
        Require(draw.State.Program?.Stages is not FrameDebugShaderStage[] &&
                draw.Mesh?.Attributes is not GraphicsVertexAttribute[],
            "Pipeline metadata exposed mutable backing arrays.");
        RequireThrows<NotSupportedException>(
            () => ((IList<FrameDebugEvent>)snapshot.Events)[0] = snapshot.Events[0],
            "Capture events could be modified through IList.");
        Require(snapshot.Events[2].Topology == GraphicsPrimitiveTopology.LineList &&
                snapshot.Events[2].LineCount == 2,
            "Procedural line draw event is incorrect.");
    }

    private static void VerifyPortableSceneRendererMarkers()
    {
        Resources.RegisterResourceRoot(Path.Combine(FindRepositoryRoot(), "src", "Core"));
        using var scene = new Scene("Frame Debugger scene");
        var camera = scene.CreateGameObject("Debug Camera").AddComponent<Camera2D>();
        scene.CreateGameObject("Debug Sprite").AddComponent<SpriteRenderer>();

        using var device = new FrameDebugGraphicsDevice(new RecordingGraphicsDevice());
        using var renderer = new PortableSceneRenderer(device);
        var service = new FrameDebuggerService();
        var target = new object();
        service.Enable(target, "Marker Game");
        service.RequestCapture();
        using (service.BeginRender(target, device, "Marker Game"))
            renderer.RenderCameras([scene], scene, [camera],
                new GraphicsRect(0, 0, 320, 180), drawUi: false);

        var events = service.Snapshot?.Events ??
                     throw new InvalidOperationException("Portable renderer capture was not published.");
        Require(events.Any(item => item.Marker.Group == "Camera: Debug Camera" &&
                                   item.Marker.SourceName == "Debug Camera (Camera2D)" &&
                                   item.Marker.SourceInstanceId == camera.GetInstanceID()),
            "PortableSceneRenderer did not annotate camera operations.");
        Require(events.Any(item => !string.IsNullOrWhiteSpace(item.Marker.BatchName) &&
                                   !string.IsNullOrWhiteSpace(item.Marker.Shader)),
            "PortableSceneRenderer did not annotate sprite batches.");
    }

    private static void VerifyStepLimitAndTargetIsolation()
    {
        var inner = new RecordingGraphicsDevice();
        using var device = new FrameDebugGraphicsDevice(inner);
        var service = new FrameDebuggerService();
        var selectedTarget = new object();
        var otherTarget = new object();
        var mesh = device.CreateMesh(MeshDescription("Step.Mesh", 6));
        var previewArea = new FrameDebugPreviewArea(new GraphicsRect(0, 0, 4, 2), 4, 2);

        service.Enable(selectedTarget, "Selected Game");
        service.RequestCapture();
        using (service.BeginRender(otherTarget, device, "Other Game")) device.Draw(mesh);
        Require(service.Snapshot is null && service.CapturePending,
            "A non-selected target consumed the capture request.");
        inner.ResetPixelWrites();
        using (service.BeginRender(selectedTarget, device, "Selected Game", previewArea))
        {
            device.Clear(GraphicsClearFlags.Color, default);
            device.Draw(mesh);
            device.Draw(mesh);
        }
        var snapshot = service.Snapshot;
        Require(snapshot?.Events.Count == 3, "Selected target did not publish its capture.");
        var initialPreview = service.Preview;
        Require(initialPreview is { Status: GraphicsColorReadbackStatus.Ready, EventCount: 3 } &&
                initialPreview.Image?.Pixels.Span[0] == 3 && inner.ReadbackRequests == 1,
            "Initial capture did not read the completed frame exactly once.");

        inner.ResetPixelWrites();
        service.SetStepLimit(2);
        using (service.BeginRender(selectedTarget, device, "Selected Game", previewArea))
        {
            device.Clear(GraphicsClearFlags.Color, default);
            device.Draw(mesh);
            device.Draw(mesh);
        }
        Require(inner.PixelWrites == 3, "Preview stepping changed the completed Game frame.");
        Require(inner.ReadbackPixelWrites[^1] == 2 && inner.ReadbackRequests == 2,
            "The selected step was not read back immediately after its event.");
        Require(service.Preview is { Status: GraphicsColorReadbackStatus.Ready, EventCount: 2 } stepPreview &&
                stepPreview.Image?.Pixels.Span[0] == 2,
            "The selected step preview was not published.");
        Require(ReferenceEquals(snapshot, service.Snapshot), "A replay replaced the immutable capture snapshot.");

        inner.ResetPixelWrites();
        service.SetStepLimit(0);
        using (service.BeginRender(selectedTarget, device, "Selected Game", previewArea))
        {
            device.Clear(GraphicsClearFlags.Color, default);
            device.Draw(mesh);
        }
        Require(inner.PixelWrites == 2, "Zero-step preview changed the completed Game frame.");
        Require(inner.ReadbackPixelWrites[^1] == 0,
            "Zero-step preview was not captured before the first event.");

        inner.ResetPixelWrites();
        service.SetStepLimit(-1);
        using (service.BeginRender(selectedTarget, device, "Selected Game", previewArea))
        {
            device.Clear(GraphicsClearFlags.Color, default);
            device.Draw(mesh);
        }
        Require(inner.PixelWrites == 2 && inner.ReadbackPixelWrites[^1] == 2,
            "Completed-frame preview was not captured after all events.");

        inner.DeferNextReadback = true;
        service.SetStepLimit(1);
        using (service.BeginRender(selectedTarget, device, "Selected Game", previewArea))
        {
            device.Clear(GraphicsClearFlags.Color, default);
            device.Draw(mesh);
        }
        var staleRequest = inner.LastReadbackRequest ??
                           throw new InvalidOperationException("Deferred readback was not requested.");
        Require(service.Preview?.Status == GraphicsColorReadbackStatus.Pending,
            "Deferred preview did not remain pending.");
        service.SetStepLimit(2);
        Require(staleRequest.Status == GraphicsColorReadbackStatus.Disposed,
            "Changing steps did not dispose the stale preview ticket.");
        staleRequest.Complete();
        using (service.BeginRender(selectedTarget, device, "Selected Game", previewArea))
        {
            device.Clear(GraphicsClearFlags.Color, default);
            device.Draw(mesh);
        }
        Require(service.Preview is { Status: GraphicsColorReadbackStatus.Ready, EventCount: 2 },
            "A stale preview ticket replaced the current step output.");

        var automatic = new FrameDebuggerService();
        automatic.Enable();
        automatic.RequestCapture();
        using (automatic.BeginRender(selectedTarget, device, "Auto Game")) device.Draw(mesh);
        Require(automatic.TargetName == "Auto Game" && automatic.Snapshot?.Events.Count == 1,
            "Parameterless enable did not select the first rendered target.");

        automatic.ReleaseTarget(selectedTarget);
        Require(automatic.CapturePending && automatic.TargetName.Length == 0 &&
                automatic.Snapshot is null,
            "Closing the selected target did not queue capture for the next Game view.");
        using (automatic.BeginRender(otherTarget, device, "Replacement Game")) device.Draw(mesh);
        Require(automatic.TargetName == "Replacement Game" &&
                automatic.Snapshot?.TargetName == "Replacement Game",
            "Frame Debugger did not rebind after its selected Game view closed.");
    }

    private static void VerifyDisabledCaptureDoesNotAllocateEvents()
    {
        var inner = new QuietGraphicsDevice();
        using var device = new FrameDebugGraphicsDevice(inner);
        var service = new FrameDebuggerService();
        var target = new object();
        var mesh = device.CreateMesh(MeshDescription("Quiet.Mesh", 6));
        for (var index = 0; index < 32; index++)
        {
            using var scope = service.BeginRender(target, device, "Quiet Game");
            device.Draw(mesh);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 512; index++)
        {
            using var scope = service.BeginRender(target, device, "Quiet Game");
            device.Draw(mesh);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0,
            $"Disabled frame debugging allocated {allocated:N0} bytes while drawing.");
        Require(service.Snapshot is null, "Disabled frame debugging produced an event snapshot.");

        service.Enable(target, "Quiet Game");
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 512; index++)
        {
            using var scope = service.BeginRender(target, device, "Quiet Game");
            device.Draw(mesh);
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0,
            $"An enabled but idle Frame Debugger allocated {allocated:N0} bytes while drawing.");
        service.Disable();
    }

    private static void VerifyQueuedRenderTargetPredicate()
    {
        var service = new FrameDebuggerService();
        var selectedTarget = new object();
        var otherTarget = new object();
        using var device = new FrameDebugGraphicsDevice(new RecordingGraphicsDevice());
        using var mesh = device.CreateMesh(MeshDescription("Hidden.Game.Mesh", 6));
        var previewArea = new FrameDebugPreviewArea(new GraphicsRect(0, 0, 4, 2), 4, 2);

        Require(!service.RequiresRender(selectedTarget),
            "A disabled Frame Debugger requested a hidden Game render.");
        service.RequestCapture();
        Require(service.RequiresRender(selectedTarget) && service.RequiresRender(otherTarget),
            "An unbound capture request could not be claimed by the first hidden Game view.");
        using (service.BeginRender(selectedTarget, device, "Hidden Game", previewArea)) device.Draw(mesh);
        Require(!service.RequiresRender(selectedTarget),
            "A completed capture kept requesting hidden Game renders.");

        service.SetStepLimit(1);
        Require(service.RequiresRender(selectedTarget) && !service.RequiresRender(otherTarget),
            "A selected-step replay was not restricted to the captured hidden Game target.");
        var replay = service.BeginRender(selectedTarget, device, "Hidden Game", previewArea);
        Require(!service.RequiresRender(selectedTarget),
            "An in-flight selected-step replay requested a duplicate hidden Game render.");
        device.Draw(mesh);
        replay.Dispose();
        Require(!service.RequiresRender(selectedTarget) &&
                service.Preview is { Status: GraphicsColorReadbackStatus.Ready, EventCount: 1 },
            "The hidden Game replay did not publish the selected-step preview.");
        service.Disable();
    }

    private static void VerifyStaleCaptureLeaseIsolation()
    {
        var service = new FrameDebuggerService();
        var oldTarget = new object();
        var replacementTarget = new object();
        using var oldDevice = new FrameDebugGraphicsDevice(new RecordingGraphicsDevice());
        using var replacementDevice = new FrameDebugGraphicsDevice(new RecordingGraphicsDevice());
        using var oldMesh = oldDevice.CreateMesh(MeshDescription("Old.Mesh"));
        using var replacementMesh = replacementDevice.CreateMesh(MeshDescription("Replacement.Mesh"));

        service.Enable(oldTarget, "Old Game");
        service.RequestCapture();
        var stale = service.BeginRender(oldTarget, oldDevice, "Old Game");
        oldDevice.Draw(oldMesh);
        service.ReleaseTarget(oldTarget);
        var current = service.BeginRender(replacementTarget, replacementDevice, "Replacement Game");
        replacementDevice.Draw(replacementMesh);
        stale.Dispose();
        Require(service.Snapshot is null && service.CapturePending,
            "A released target's in-flight capture published a stale snapshot.");
        current.Dispose();
        Require(service.Snapshot is { TargetName: "Replacement Game" } replacement &&
                replacement.Events.Any(item => item.MeshLabel == "Replacement.Mesh"),
            "The replacement target did not publish after a stale lease completed.");

        service.RequestCapture();
        var cleared = service.BeginRender(replacementTarget, replacementDevice, "Replacement Game");
        replacementDevice.Draw(replacementMesh);
        service.Clear();
        cleared.Dispose();
        Require(service.Snapshot is null && !service.CapturePending,
            "Clear allowed an in-flight capture to republish a snapshot.");

        service.RequestCapture();
        var disabled = service.BeginRender(replacementTarget, replacementDevice, "Replacement Game");
        replacementDevice.Draw(replacementMesh);
        service.Disable();
        service.Enable(oldTarget, "New Session");
        service.RequestCapture();
        var newSession = service.BeginRender(oldTarget, oldDevice, "New Session");
        oldDevice.Draw(oldMesh);
        disabled.Dispose();
        Require(service.Snapshot is null && service.CapturePending,
            "A disabled session's in-flight capture leaked into the new session.");
        newSession.Dispose();
        Require(service.Snapshot is { TargetName: "New Session" },
            "The new Frame Debugger session was blocked by an old capture lease.");
    }

    private static void VerifyPlayModePauseOwnership()
    {
        var playMode = new RecordingPlayMode { IsPlaying = true };
        var service = new FrameDebuggerService(playMode);
        service.Enable();
        Require(playMode.IsPaused && service.PausedPlayMode,
            "Enabling the Frame Debugger did not pause active Play Mode.");
        service.Disable();
        Require(!playMode.IsPaused && !service.PausedPlayMode,
            "Disabling the Frame Debugger did not restore its own pause.");

        playMode.IsPaused = true;
        service.Enable();
        Require(!service.PausedPlayMode,
            "The Frame Debugger claimed ownership of an existing user pause.");
        service.Disable();
        Require(playMode.IsPaused,
            "The Frame Debugger resumed a pause that it did not create.");

        playMode.IsPaused = false;
        service.RequestCapture();
        Require(playMode.IsPaused && service.PausedPlayMode,
            "RequestCapture did not pause an active unpaused Play Mode.");
        playMode.IsPaused = false;
        service.SynchronizePlayModeState();
        Require(!service.Enabled && !service.CapturePending && service.Snapshot is null &&
                !service.PausedPlayMode,
            "Resuming Play Mode did not immediately clear the Frame Debugger session.");
    }

    private static GraphicsShaderProgramDescription ProgramDescription(string label) => new(
        label,
        new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
            "void main(){}"),
        new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
            "void main(){}"));

    private static GraphicsMeshDescription MeshDescription(string label, int vertexCount = 1)
    {
        var layout = new GraphicsVertexLayout(6 * sizeof(float),
            [new GraphicsVertexAttribute(0, 2, 0), new GraphicsVertexAttribute(1, 4, 2 * sizeof(float))]);
        return new GraphicsMeshDescription(label, new float[vertexCount * 6], layout,
            GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic);
    }

    private static GraphicsTextureDescription TextureDescription() => new(
        1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled);

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

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private class RecordingGraphicsDevice : IGraphicsDevice, IGraphicsDeviceStatistics,
        IGraphicsResourceRetirement, IGraphicsColorReadback
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL, "Frame Debug Test", "1.0",
            GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.SampledTextures |
            GraphicsDeviceFeatures.AlphaBlending | GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        public GraphicsDrawStatistics DrawStatistics { get; private set; }
        public IGraphicsTexture2D? LastBoundTexture { get; private set; }
        public IGraphicsMesh? LastMesh { get; private set; }
        public IGraphicsRenderTarget? LastRenderTarget { get; private set; }
        public List<IDisposable> RetiredResources { get; } = [];
        public int PixelWrites { get; private set; }
        public int ReadbackRequests { get; private set; }
        public List<int> ReadbackPixelWrites { get; } = [];
        public bool DeferNextReadback { get; set; }
        public RecordingReadbackRequest? LastReadbackRequest { get; private set; }
        public GraphicsColorReadbackCapabilities ColorReadbackCapabilities { get; } = new(true, 1024 * 1024);

        public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
            new RecordingProgram(this, description.Label);
        public IGraphicsMesh CreateMesh(GraphicsMeshDescription description) =>
            new RecordingMesh(this, description);
        public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData = default) => new RecordingTexture(this, label, description);
        public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description) =>
            new RecordingRenderTarget(this, label, description);
        public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget)
        {
            LastRenderTarget = renderTarget;
            return EmptyScope.Instance;
        }
        public void SetViewport(GraphicsRect viewport) { }
        public void SetScissor(GraphicsRect? scissor) { }
        public void Clear(GraphicsClearFlags flags, NVector4 color) => PixelWrites++;
        public void SetDepthState(GraphicsDepthState state) { }
        public void SetBlendMode(GraphicsBlendMode mode) { }
        public void SetRasterizerState(GraphicsRasterizerState state) { }
        public void BindTexture(int slot, IGraphicsTexture2D texture) => LastBoundTexture = texture;
        public void Draw(IGraphicsMesh mesh) => Draw(mesh, mesh.VertexCount);
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0)
        {
            LastMesh = mesh;
            PixelWrites++;
            DrawStatistics = DrawStatistics.AddForTest(vertexCount, mesh.Topology);
        }
        public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0)
        {
            PixelWrites++;
            DrawStatistics = DrawStatistics.AddForTest(vertexCount, topology);
        }
        public void RetireResource(IDisposable resource) => RetiredResources.Add(resource);
        public GraphicsColorReadbackRequest RequestColorReadback(
            GraphicsRect region,
            int surfaceWidth,
            int surfaceHeight)
        {
            ReadbackRequests++;
            ReadbackPixelWrites.Add(PixelWrites);
            var request = new RecordingReadbackRequest(region, PixelWrites, !DeferNextReadback);
            DeferNextReadback = false;
            LastReadbackRequest = request;
            return request;
        }
        public void ResetPixelWrites() => PixelWrites = 0;
        public void Dispose() { }
    }

    private sealed class RecordingReadbackRequest : GraphicsColorReadbackRequest
    {
        private readonly int _pixelWrites;
        private GraphicsColorReadbackImage? _image;
        private bool _disposed;

        internal RecordingReadbackRequest(GraphicsRect region, int pixelWrites, bool complete)
        {
            Region = region;
            _pixelWrites = pixelWrites;
            if (complete) Complete();
        }

        public override GraphicsRect Region { get; }
        public override GraphicsColorReadbackStatus Status => _disposed
            ? GraphicsColorReadbackStatus.Disposed
            : _image is null ? GraphicsColorReadbackStatus.Pending : GraphicsColorReadbackStatus.Ready;
        public override string Error => string.Empty;

        public void Complete()
        {
            if (_disposed || _image is not null) return;
            var pixels = new byte[checked(Region.Width * Region.Height * 4)];
            pixels.AsSpan().Fill((byte)Math.Clamp(_pixelWrites, 0, byte.MaxValue));
            _image = new GraphicsColorReadbackImage(Region.Width, Region.Height, pixels);
        }

        public override bool TryGetResult(out GraphicsColorReadbackImage? image)
        {
            image = _disposed ? null : _image;
            return image is not null;
        }

        public override void Dispose()
        {
            _disposed = true;
            _image = null;
        }
    }

    private sealed class QuietGraphicsDevice : RecordingGraphicsDevice
    {
    }

    private abstract class RecordingResource(IGraphicsDevice device, string label) : IGraphicsResource
    {
        private bool _disposed;
        public IGraphicsDevice Device { get; } = device;
        public string Label
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return label;
            }
        }
        public void Dispose() => _disposed = true;
    }

    private sealed class RecordingProgram(IGraphicsDevice device, string label)
        : RecordingResource(device, label), IGraphicsProgram
    {
        public void Bind() { }
        public void SetMatrix4x4(string name, NMatrix4x4 value) { }
        public void SetVector4(string name, NVector4 value) { }
        public void SetFloat(string name, float value) { }
        public void SetInt(string name, int value) { }
    }

    private sealed class RecordingMesh : RecordingResource, IGraphicsMesh
    {
        private int _vertexCount;
        public RecordingMesh(IGraphicsDevice device, GraphicsMeshDescription description)
            : base(device, description.Label)
        {
            Layout = description.Layout;
            Topology = description.Topology;
            Usage = description.Usage;
            _vertexCount = description.VertexCount;
        }
        public GraphicsVertexLayout Layout { get; }
        public GraphicsPrimitiveTopology Topology { get; }
        public GraphicsBufferUsage Usage { get; }
        public int VertexCount => _vertexCount;
        public void Update(ReadOnlySpan<float> vertices) =>
            _vertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
    }

    private sealed class RecordingTexture(
        IGraphicsDevice device,
        string label,
        GraphicsTextureDescription description)
        : RecordingResource(device, label), IGraphicsTexture2D
    {
        public GraphicsTextureDescription Description { get; } = description;
        public void Update(ReadOnlySpan<byte> pixels) { }
    }

    private sealed class RecordingRenderTarget : RecordingResource, IGraphicsRenderTarget
    {
        public RecordingRenderTarget(
            IGraphicsDevice device,
            string label,
            GraphicsRenderTargetDescription description)
            : base(device, label)
        {
            Width = description.Width;
            Height = description.Height;
            if (description.ColorFormat is { } color)
                ColorTexture = new RecordingTexture(device, $"{label}.Color",
                    new GraphicsTextureDescription(Width, Height, color,
                        GraphicsTextureUsage.RenderTarget | GraphicsTextureUsage.Sampled));
            if (description.DepthFormat is { } depth)
                DepthTexture = new RecordingTexture(device, $"{label}.Depth",
                    new GraphicsTextureDescription(Width, Height, depth,
                        GraphicsTextureUsage.RenderTarget | GraphicsTextureUsage.Sampled));
        }
        public int Width { get; }
        public int Height { get; }
        public IGraphicsTexture2D? ColorTexture { get; }
        public IGraphicsTexture2D? DepthTexture { get; }
    }

    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }

    private sealed class RecordingPlayMode : IFrameDebuggerPlayMode
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
    }
}

internal static class GraphicsStatisticsTestExtensions
{
    internal static GraphicsDrawStatistics AddForTest(
        this GraphicsDrawStatistics statistics,
        int vertices,
        GraphicsPrimitiveTopology topology) => new(
        statistics.DrawCallCount + 1,
        statistics.VertexCount + vertices,
        statistics.TriangleCount + (topology == GraphicsPrimitiveTopology.TriangleList ? vertices / 3 : 0),
        statistics.LineCount + (topology == GraphicsPrimitiveTopology.LineList ? vertices / 2 : 0));
}
