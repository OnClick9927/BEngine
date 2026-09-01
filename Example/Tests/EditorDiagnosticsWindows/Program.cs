using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Diagnostics;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.ExampleTests.EditorDiagnosticsWindows;

internal static class Program
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Assembly EditorAssembly = typeof(EditorWindow).Assembly;
    private static readonly Type FrameDebuggerWindowType = RequireType("BEngine.Editor.FrameDebuggerWindow");
    private static readonly Type ProfilerWindowType = RequireType("BEngine.Editor.ProfilerWindow");
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame", HiddenStatic) ??
                                                    throw new MissingMethodException(typeof(GUI).FullName,
                                                        "BeginFrame");
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame", HiddenStatic) ??
                                                  throw new MissingMethodException(typeof(GUI).FullName,
                                                      "EndFrame");

    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            VerifyWindowMetadata();
            VerifyFrameDebuggerRendering();
            VerifyGameFrameDebuggerIntegration();
            VerifyProfilerRendering();
            VerifyMultiWindowLifecycles();
            Console.WriteLine(
                "EDITOR_DIAGNOSTICS_WINDOWS_OK|menu-metadata,tab-metadata,empty-state,data-state," +
                "narrow-layout,wide-layout,frame-preview,typed-details,preview-lifecycle," +
                "step-selection,multi-window-step-sync,frame-debugger-lifecycle," +
                "profiler-recording-lifecycle");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_DIAGNOSTICS_WINDOWS_FAILED|{Unwrap(exception)}");
            return 1;
        }
        finally
        {
            ResetWindowCount(FrameDebuggerWindowType);
            ResetWindowCount(ProfilerWindowType);
            FrameDebuggerService.Shared.Disable();
            FrameDebuggerService.Shared.Clear();
            EditorProfiler.Recording = false;
            EditorProfiler.HistoryCapacity = EditorProfiler.DefaultHistoryCapacity;
            EditorProfiler.Clear();
        }
    }

    private static void VerifyWindowMetadata()
    {
        VerifyMetadata(FrameDebuggerWindowType, "Analysis/Frame Debugger",
            "Window/Analysis/Frame Debugger");
        VerifyMetadata(ProfilerWindowType, "Analysis/Profiler", "Window/Analysis/Profiler");
    }

    private static void VerifyMetadata(Type windowType, string tabPath, string menuPath)
    {
        Require(typeof(EditorWindow).IsAssignableFrom(windowType),
            $"{windowType.Name} is not an EditorWindow.");
        var tab = windowType.GetCustomAttribute<EditorWindowTabAttribute>();
        Require(tab?.menuPath == tabPath,
            $"{windowType.Name} did not register the Add new tab path '{tabPath}'.");
        var menuItems = windowType.GetMethods(BindingFlags.Static | BindingFlags.Public |
                                              BindingFlags.NonPublic)
            .SelectMany(method => method.GetCustomAttributes<MenuItemAttribute>())
            .Where(attribute => !attribute.isValidateFunction)
            .ToArray();
        Require(menuItems.Any(attribute => attribute.itemName == menuPath),
            $"{windowType.Name} did not register the menu path '{menuPath}'.");
    }

    private static void VerifyFrameDebuggerRendering()
    {
        ResetWindowCount(FrameDebuggerWindowType);
        var service = FrameDebuggerService.Shared;
        service.Disable();
        service.Clear();
        var window = CreateWindow(FrameDebuggerWindowType);
        Invoke(window, FrameDebuggerWindowType, "OnEnable");
        try
        {
            VerifyStateAtBothSizes(window, FrameDebuggerWindowType,
                "Enable Frame Debugger", "Frame Debugger empty state");

            var source = new GameObject("Diagnostics Camera");
            PublishFrameDebuggerSnapshot(CreateFrameDebuggerSnapshot(source.GetInstanceID()),
                CreatePreviewImage());
            VerifyStateAtBothSizes(window, FrameDebuggerWindowType,
                "Diagnostics Game", "Frame Debugger captured state");
            var commands = Render(window, FrameDebuggerWindowType,
                new Event(EventType.Repaint), 900, 600);
            var previewCommand = commands.FirstOrDefault(command =>
                command.Type == GpuCanvasCommandType.Image &&
                command.Content.Contains("__bengine_frame_debugger_preview_",
                    StringComparison.Ordinal));
            Require(!string.IsNullOrEmpty(previewCommand.Content),
                "Frame Debugger did not render the selected step as an in-window image.");
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                            command.Content.Contains("Diagnostics Camera",
                                                StringComparison.Ordinal)),
                "Frame Debugger did not render the source BObject through ObjectField.");
            var detailText = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).ToArray();
            Require(detailText.Contains("Diagnostics.Target") &&
                    detailText.Contains(nameof(GraphicsTextureFormat.Depth24Stencil8)) &&
                    detailText.Contains(nameof(GraphicsBufferUsage.Dynamic)) &&
                    detailText.Contains("vertexMain"),
                "Frame Debugger did not render detailed target, mesh, and shader metadata.");
            Require(TryResolveEditorTexture(previewCommand.Content!, out var previewTexture) &&
                    previewTexture.Width == 2 && previewTexture.Height == 2,
                "Frame Debugger preview was not available to the GPU canvas resolver.");

            service.SetStepLimit(1);
            _ = Render(window, FrameDebuggerWindowType, new Event(EventType.Repaint), 900, 600);
            Require((int)(GetField(window, "_selectedEventIndex") ?? -1) == 0,
                "Frame Debugger selection did not follow the selected preview step.");
            service.RequestCapture();
            _ = Render(window, FrameDebuggerWindowType, new Event(EventType.Repaint), 900, 600);
            Require((int)(GetField(window, "_selectedEventIndex") ?? -1) == 1,
                "Starting a completed-frame capture left details on a stale intermediate event.");

            Invoke(window, FrameDebuggerWindowType, "OnDisable");
            Require(!TryResolveEditorTexture(previewCommand.Content!, out _),
                "Closing Frame Debugger retained its in-memory preview texture.");
            return;
        }
        finally
        {
            if ((bool)(GetField(window, "_countedAsOpen") ?? false))
                Invoke(window, FrameDebuggerWindowType, "OnDisable");
            service.Clear();
        }
    }

    private static void VerifyProfilerRendering()
    {
        ResetWindowCount(ProfilerWindowType);
        EditorProfiler.Recording = false;
        EditorProfiler.Clear();
        var window = CreateWindow(ProfilerWindowType);
        Invoke(window, ProfilerWindowType, "OnEnable");
        var moduleTree = GetField(window, "_moduleTree");
        Require(moduleTree?.GetType().BaseType?.IsGenericType == true &&
                moduleTree.GetType().BaseType!.GetGenericTypeDefinition().FullName ==
                "UnityEditor.IMGUI.Controls.TreeView`1",
            "Profiler modules are not rendered by IMGUI TreeView.");
        using var customModule = EditorProfilerModuleRegistry.Register(
            new EditorProfilerModuleDefinition(
                "tests.jobs",
                "Jobs",
                [new EditorProfilerCounterDescriptor(
                    "active", "Active Jobs", EditorProfilerCounterUnit.Number, Color.white)]));
        try
        {
            EditorProfiler.Clear();
            VerifyStateAtBothSizes(window, ProfilerWindowType,
                "Recording profiler data", "Profiler empty state");

            using (EditorProfiler.BeginFrame())
            {
                EditorProfiler.ReportSample(EditorProfilerArea.Update, 0.75);
                EditorProfiler.ReportSample(EditorProfilerArea.Runtime, 0.25);
                EditorProfiler.ReportSample(EditorProfilerArea.Render, 1.50);
                EditorProfiler.ReportSample(EditorProfilerArea.IMGUI, 0.60);
                EditorProfiler.ReportSample(EditorProfilerArea.Present, 0.20);
                EditorProfiler.ReportRenderStatistics(new SceneRenderStatistics(
                    2, 12, 4, 5, 72, 24, 2, 1280, 720, true));
                EditorProfiler.ReportCounter("tests.jobs", "active", 5);
            }
            using (EditorProfiler.BeginFrame())
            {
                using var editorMethod = EditorProfiler.BeginMethodSample(
                    typeof(Program), "ProfiledEditorWork", EditorProfilerDomain.Editor);
                EditorProfiler.ReportSample(EditorProfilerArea.Update, 0.55);
                EditorProfiler.ReportSample(EditorProfilerArea.Runtime, 0.35);
                EditorProfiler.ReportSample(EditorProfilerArea.Render, 1.25);
                EditorProfiler.ReportSample(EditorProfilerArea.IMGUI, 0.45);
                EditorProfiler.ReportSample(EditorProfilerArea.Present, 0.15);
                EditorProfiler.ReportRenderStatistics(new SceneRenderStatistics(
                    1, 9, 3, 4, 60, 20, 1, 1280, 720, true));
                using (EditorProfiler.BeginMethodSample(
                           typeof(Program), "ProfiledRuntimeWork", EditorProfilerDomain.Runtime))
                    Thread.SpinWait(512);
                EditorProfiler.ReportCounter("tests.jobs", "active", 7);
            }
            VerifyStateAtBothSizes(window, ProfilerWindowType,
                "CPU Usage", "Profiler sampled state");

            var cpuCommands = Render(window, ProfilerWindowType,
                new Event(EventType.Repaint), 900, 600);
            var cpuText = cpuCommands.Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).ToArray();
            string[] expectedCpuText =
            [
                "CPU Usage", "Rendering", "Memory", "Frame: 2 / 2", "Overview", "Time ms",
                "EditorLoop", "Editor", "Runtime"
            ];
            var missingCpuText = expectedCpuText.Where(expected => !cpuText.Any(text =>
                text.StartsWith(expected, StringComparison.Ordinal))).ToArray();
            Require(missingCpuText.Length == 0,
                $"Profiler did not render Unity-style modules and CPU hierarchy columns: " +
                $"{string.Join(", ", missingCpuText)}.");
            Require(cpuText.Any(text => text.Contains("ProfiledEditorWork", StringComparison.Ordinal)) &&
                    cpuText.Any(text => text.Contains("ProfiledRuntimeWork", StringComparison.Ordinal)),
                "Profiler CPU hierarchy did not render captured class and method names.");

            var selectedSample = EditorProfiler.GetSnapshot().Latest!.Value.MethodSamples
                .Single(sample => sample.MethodName == "ProfiledRuntimeWork");
            SetField(window, "_selectedMethodSample", selectedSample);
            var callStackText = Render(window, ProfilerWindowType,
                    new Event(EventType.Repaint), 900, 600)
                .Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).ToArray();
            Require(callStackText.Any(text => text.Contains("Runtime |", StringComparison.Ordinal)) &&
                    callStackText.Any(text => text.Contains(nameof(VerifyProfilerRendering),
                        StringComparison.Ordinal)),
                "Profiler CPU details did not render the method domain and managed call stack.");

            _ = Render(window, ProfilerWindowType,
                new Event(EventType.MouseDown) { button = 0, mousePosition = new Vector2(10, 150) },
                900, 600);
            _ = Render(window, ProfilerWindowType,
                new Event(EventType.MouseUp) { button = 0, mousePosition = new Vector2(10, 150) },
                900, 600);
            var renderingText = Render(window, ProfilerWindowType,
                    new Event(EventType.Repaint), 900, 600)
                .Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).ToArray();
            Require(renderingText.Contains("Saved by Batching") &&
                    renderingText.Contains("Visible Submissions") &&
                    renderingText.Contains("Render Target"),
                "Profiler Rendering module did not expose detailed frame counters.");

            _ = Render(window, ProfilerWindowType,
                new Event(EventType.MouseDown) { button = 0, mousePosition = new Vector2(10, 220) },
                900, 600);
            _ = Render(window, ProfilerWindowType,
                new Event(EventType.MouseUp) { button = 0, mousePosition = new Vector2(10, 220) },
                900, 600);
            var memoryText = Render(window, ProfilerWindowType,
                    new Event(EventType.Repaint), 900, 600)
                .Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).ToArray();
            Require(memoryText.Contains("Allocated This Frame") &&
                    memoryText.Contains("Managed Heap") &&
                    memoryText.Contains("Process Working Set"),
                $"Profiler Memory module did not expose detailed memory counters. " +
                $"Selected module: {GetField(window, "_module")}.");

            var moduleTreeState = GetField(window, "_moduleTreeState")!;
            moduleTreeState.GetType().GetProperty("scrollPos")!.SetValue(
                moduleTreeState, new Vector2(0, 120));
            var customText = Render(window, ProfilerWindowType,
                    new Event(EventType.Repaint), 900, 600)
                .Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).ToArray();
            Require(customText.Any(text => text.StartsWith("Jobs", StringComparison.Ordinal)),
                "Profiler did not add a registered external module to the module sidebar.");
            SetField(window, "_externalModuleId", "tests.jobs");
            customText = Render(window, ProfilerWindowType,
                    new Event(EventType.Repaint), 900, 600)
                .Where(command => command.Type == GpuCanvasCommandType.Text)
                .Select(command => command.Content).ToArray();
            Require(customText.Contains("Active Jobs") && customText.Contains("7.00"),
                "Profiler external module details did not render its captured counter.");

            var toggleVisibility = ProfilerWindowType.GetMethod("ToggleModuleVisibility",
                HiddenInstance) ?? throw new MissingMethodException(ProfilerWindowType.FullName,
                "ToggleModuleVisibility");
            toggleVisibility.Invoke(window, ["builtin.rendering"]);
            var visibleModules = (HashSet<string>)GetField(window, "_visibleModuleIds")!;
            Require(!visibleModules.Contains("builtin.rendering"),
                "Profiler Modules did not allow a built-in analysis aspect to be hidden.");
            toggleVisibility.Invoke(window, ["builtin.rendering"]);
        }
        finally
        {
            Invoke(window, ProfilerWindowType, "OnDisable");
        }
    }

    private static void VerifyGameFrameDebuggerIntegration()
    {
        var gameWindowType = RequireType("BEngine.Editor.GpuEditorApplication+ImGuiGameWindow");
        var gameWindow = CreateWindow(gameWindowType);
        var source = new GameObject("Game Debug Camera");
        var snapshot = CreateFrameDebuggerSnapshot(source.GetInstanceID());
        PublishFrameDebuggerSnapshot(snapshot, CreatePreviewImage());
        var service = FrameDebuggerService.Shared;
        SetField(service, "_target", gameWindow);
        SetField(service, "_targetName", "Game");
        SetField(service, "_stepLimit", 1);
        SetField(service, "_preview", new FrameDebugPreviewSnapshot(
            snapshot.CaptureId, 1, GraphicsColorReadbackStatus.Ready,
            CreatePreviewImage(), string.Empty));
        try
        {
            var commands = Render(gameWindow, gameWindowType,
                new Event(EventType.Repaint), 640, 360);
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                            command.Content.Contains("FrameDebugger.png",
                                                StringComparison.Ordinal)),
                "Game toolbar did not render the Frame Debugger icon button.");
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                            command.Content.Contains(
                                                "__bengine_frame_debugger_preview_",
                                                StringComparison.Ordinal)),
                "Game view did not display the selected Frame Debugger step output.");
        }
        finally
        {
            Invoke(gameWindow, gameWindowType, "OnDisable");
            service.Disable();
            service.Clear();
        }
    }

    private static void VerifyStateAtBothSizes(
        object window,
        Type windowType,
        string expectedText,
        string stateName)
    {
        VerifyDraw(window, windowType, 280, 220, expectedText, $"{stateName} narrow layout");
        VerifyDraw(window, windowType, 900, 600, expectedText, $"{stateName} wide layout");
    }

    private static void VerifyDraw(
        object window,
        Type windowType,
        int width,
        int height,
        string expectedText,
        string stateName)
    {
        _ = Render(window, windowType, new Event(EventType.Layout), width, height);
        var commands = Render(window, windowType, new Event(EventType.Repaint), width, height);
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text),
            $"{stateName} produced no text commands.");
        Require(commands.Any(IsRectVisual),
            $"{stateName} produced no rectangular visual commands.");
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                        command.Content.Contains(expectedText,
                                            StringComparison.Ordinal)),
            $"{stateName} did not render '{expectedText}'.");
    }

    private static void VerifyMultiWindowLifecycles()
    {
        VerifyFrameDebuggerMultiWindowLifecycle();
        VerifyProfilerMultiWindowLifecycle();
    }

    private static void VerifyFrameDebuggerMultiWindowLifecycle()
    {
        ResetWindowCount(FrameDebuggerWindowType);
        var service = FrameDebuggerService.Shared;
        service.Disable();
        service.Clear();
        var first = CreateWindow(FrameDebuggerWindowType);
        var second = CreateWindow(FrameDebuggerWindowType);
        Invoke(first, FrameDebuggerWindowType, "OnEnable");
        Invoke(second, FrameDebuggerWindowType, "OnEnable");
        var source = new GameObject("Shared Frame Debugger Camera");
        PublishFrameDebuggerSnapshot(CreateFrameDebuggerSnapshot(source.GetInstanceID()),
            CreatePreviewImage());
        service.SetStepLimit(1);
        _ = Render(first, FrameDebuggerWindowType, new Event(EventType.Repaint), 900, 600);
        _ = Render(second, FrameDebuggerWindowType, new Event(EventType.Repaint), 900, 600);
        Require((int)(GetField(first, "_selectedEventIndex") ?? -1) == 0 &&
                (int)(GetField(second, "_selectedEventIndex") ?? -1) == 0,
            "Frame Debugger windows did not observe the same selected preview step.");
        service.RequestCapture();
        _ = Render(first, FrameDebuggerWindowType, new Event(EventType.Repaint), 900, 600);
        _ = Render(second, FrameDebuggerWindowType, new Event(EventType.Repaint), 900, 600);
        Require((int)(GetField(first, "_selectedEventIndex") ?? -1) == 1 &&
                (int)(GetField(second, "_selectedEventIndex") ?? -1) == 1,
            "Frame Debugger windows did not return to the completed-frame event together.");
        Invoke(first, FrameDebuggerWindowType, "OnDisable");
        Require(service.Enabled,
            "Closing one of two Frame Debugger windows disabled the shared service.");
        Invoke(second, FrameDebuggerWindowType, "OnDisable");
        Require(!service.Enabled,
            "Closing the final Frame Debugger window did not disable the shared service.");
    }

    private static void VerifyProfilerMultiWindowLifecycle()
    {
        ResetWindowCount(ProfilerWindowType);
        EditorProfiler.Recording = false;
        EditorProfiler.Clear();
        var first = CreateWindow(ProfilerWindowType);
        var second = CreateWindow(ProfilerWindowType);
        Invoke(first, ProfilerWindowType, "OnEnable");
        Invoke(second, ProfilerWindowType, "OnEnable");
        Require(EditorProfiler.Recording,
            "Opening Profiler windows did not start recording.");
        Invoke(first, ProfilerWindowType, "OnDisable");
        Require(EditorProfiler.Recording,
            "Closing one of two Profiler windows stopped recording.");
        Invoke(second, ProfilerWindowType, "OnDisable");
        Require(!EditorProfiler.Recording,
            "Closing the final Profiler window did not stop recording.");
    }

    private static FrameDebugCaptureSnapshot CreateFrameDebuggerSnapshot(int sourceInstanceId)
    {
        var renderState = new FrameDebugRenderState(
            new GraphicsRect(0, 0, 1280, 720),
            new GraphicsRect(32, 24, 512, 320),
            GraphicsDepthState.Disabled,
            GraphicsBlendMode.AlphaBlend,
            GraphicsRasterizerState.CullBackFaces,
            "Diagnostics.SpriteProgram",
            "Diagnostics.Target",
            [new FrameDebugTextureBinding(0, "Diagnostics.Atlas")],
            Program: new FrameDebugProgramState("Diagnostics.SpriteProgram", Array.AsReadOnly(
            [
                new FrameDebugShaderStage(GraphicsShaderStage.Vertex,
                    GraphicsShaderLanguage.Glsl, "vertexMain"),
                new FrameDebugShaderStage(GraphicsShaderStage.Fragment,
                    GraphicsShaderLanguage.Glsl, "fragmentMain")
            ])),
            RenderTarget: new FrameDebugRenderTargetState("Diagnostics.Target", 1280, 720,
                GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureFormat.Depth24Stencil8,
                ColorSampled: true, DepthSampled: false));
        var mesh = new FrameDebugMeshState("Quad Batch", 6, GraphicsBufferUsage.Dynamic, 24,
            Array.AsReadOnly(
            [
                new GraphicsVertexAttribute(0, 2, 0),
                new GraphicsVertexAttribute(1, 4, 8)
            ]));
        var marker = new FrameDebugMarker(
            "Camera Main", "Sprite Batch", Guid.NewGuid(), "Sprite/Default",
            "Diagnostics.Atlas", "Diagnostics Camera", sourceInstanceId);
        FrameDebugEvent[] events =
        [
            new(0, FrameDebugEventKind.Clear, "Clear Game Color", renderState, string.Empty,
                GraphicsPrimitiveTopology.TriangleList, 0, 0, 0, 0,
                GraphicsClearFlags.Color, new NVector4(0.05f, 0.08f, 0.12f, 1), true,
                string.Empty, marker),
            new(1, FrameDebugEventKind.Draw, "Sprite Batch", renderState, "Quad Batch",
                GraphicsPrimitiveTopology.TriangleList, 0, 6, 2, 0,
                default, default, true, string.Empty, marker, mesh)
        ];
        return new FrameDebugCaptureSnapshot(7, "Diagnostics Game", GraphicsBackend.Vulkan,
            DateTimeOffset.UtcNow, events);
    }

    private static void PublishFrameDebuggerSnapshot(FrameDebugCaptureSnapshot snapshot,
        GraphicsColorReadbackImage previewImage)
    {
        var service = FrameDebuggerService.Shared;
        service.Disable();
        service.Clear();
        SetField(service, "_snapshot", snapshot);
        SetField(service, "_preview", new FrameDebugPreviewSnapshot(
            snapshot.CaptureId, snapshot.Events.Count, GraphicsColorReadbackStatus.Ready,
            previewImage, string.Empty));
        SetField(service, "_enabled", true);
        SetField(service, "_stepLimit", -1);
        var version = (long)(GetField(service, "_version") ?? 0L);
        SetField(service, "_version", version + 1);
    }

    private static GraphicsColorReadbackImage CreatePreviewImage()
    {
        return new GraphicsColorReadbackImage(
            2, 2,
            new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255
            });
    }

    private static bool TryResolveEditorTexture(string source, out GpuCanvasTextureData texture)
    {
        texture = default;
        var resolverType = RequireType("BEngine.Editor.EditorGpuCanvasResourceResolver");
        var resolver = resolverType.GetProperty("Shared", BindingFlags.Public | BindingFlags.Static)
                           ?.GetValue(null) ??
                       throw new MissingMemberException(resolverType.FullName, "Shared");
        var resolve = resolverType.GetMethod("TryResolveTexture", BindingFlags.Public |
                                                                   BindingFlags.Instance) ??
                      throw new MissingMethodException(resolverType.FullName, "TryResolveTexture");
        object?[] arguments = [source, null];
        if (resolve.Invoke(resolver, arguments) is not true) return false;
        texture = (GpuCanvasTextureData)arguments[1]!;
        return true;
    }

    private static List<GpuCanvasCommand> Render(
        object window,
        Type windowType,
        Event evt,
        int width,
        int height)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, width, height, commands]);
        try
        {
            Invoke(window, windowType, "OnGUI");
        }
        finally
        {
            EndFrame.Invoke(null, null);
        }
        return commands;
    }

    private static bool IsRectVisual(GpuCanvasCommand command) => command.Type is
        GpuCanvasCommandType.SolidRect or GpuCanvasCommandType.GradientRect or
        GpuCanvasCommandType.Image;

    private static object CreateWindow(Type type) =>
        Activator.CreateInstance(type, nonPublic: true) ??
        throw new InvalidOperationException($"Could not create {type.FullName}.");

    private static void Invoke(object target, Type declaringType, string methodName)
    {
        var method = declaringType.GetMethod(methodName, HiddenInstance | BindingFlags.DeclaredOnly) ??
                     throw new MissingMethodException(declaringType.FullName, methodName);
        method.Invoke(target, null);
    }

    private static void ResetWindowCount(Type type)
    {
        var field = type.GetFields(HiddenStatic).SingleOrDefault(candidate =>
            candidate.FieldType == typeof(int) &&
            candidate.Name.Contains("openWindowCount", StringComparison.OrdinalIgnoreCase));
        field?.SetValue(null, 0);
    }

    private static object? GetField(object target, string name) =>
        target.GetType().GetField(name, HiddenInstance)?.GetValue(target) ??
        throw new MissingFieldException(target.GetType().FullName, name);

    private static void SetField(object target, string name, object? value) =>
        (target.GetType().GetField(name, HiddenInstance) ??
         throw new MissingFieldException(target.GetType().FullName, name)).SetValue(target, value);

    private static Type RequireType(string fullName) =>
        EditorAssembly.GetType(fullName, throwOnError: true) ??
        throw new TypeLoadException(fullName);

    private static Exception Unwrap(Exception exception) => exception is TargetInvocationException invocation &&
                                                            invocation.InnerException is { } inner
        ? Unwrap(inner)
        : exception;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
