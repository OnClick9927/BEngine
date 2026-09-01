using BEngine.Editor;
using BEngine.Rendering;

namespace BEngine.ExampleTests.EditorProfiling;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyDisabledHotPath();
            VerifyFrameSamplesAndRendering();
            VerifyMethodSamplesAndCustomModules();
            VerifyFixedCapacityHistory();
            VerifyAllocationFreeFrameCopy();
            VerifyRecordingAndClear();
            VerifyThreadSafeSnapshots();
            Console.WriteLine(
                "EDITOR_PROFILER_OK|disabled-zero-allocation,phase-timing,render-aggregation," +
                "method-hierarchy,editor-runtime-domains,call-stacks,allocation-sites," +
                "custom-modules,counters,fixed-ring,allocation-free-copy,recording,clear,memory,gc," +
                "thread-safe-snapshot");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_PROFILER_FAILED|{exception}");
            return 1;
        }
        finally
        {
            EditorProfiler.Recording = false;
            EditorProfiler.HistoryCapacity = EditorProfiler.DefaultHistoryCapacity;
            EditorProfiler.Clear();
        }
    }

    private static void VerifyDisabledHotPath()
    {
        EditorProfiler.Recording = false;
        EditorProfiler.Clear();
        for (var index = 0; index < 16; index++)
        {
            using var frame = EditorProfiler.BeginFrame();
            using var sample = EditorProfiler.BeginSample(EditorProfilerArea.Update);
            using var method = EditorProfiler.BeginMethodSample();
            EditorProfiler.ReportRenderStatistics(default);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_024; index++)
        {
            using var frame = EditorProfiler.BeginFrame();
            using var sample = EditorProfiler.BeginSample(EditorProfilerArea.Update);
            using var method = EditorProfiler.BeginMethodSample();
            EditorProfiler.ReportRenderStatistics(default);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0,
            $"Disabled profiler allocated {allocated} bytes across 1,024 frame/sample calls.");
        Require(EditorProfiler.GetSnapshot().Frames.Length == 0,
            "Disabled profiler recorded frame history.");
    }

    private static void VerifyFrameSamplesAndRendering()
    {
        EditorProfiler.HistoryCapacity = 8;
        EditorProfiler.Clear();
        EditorProfiler.Recording = true;
        using (EditorProfiler.BeginFrame())
        {
            EditorProfiler.ReportSample(EditorProfilerArea.Update, 0.5);
            Measure(EditorProfilerArea.Update);
            Measure(EditorProfilerArea.Runtime);
            Measure(EditorProfilerArea.Render);
            Measure(EditorProfilerArea.IMGUI);
            Measure(EditorProfilerArea.Present);
            EditorProfiler.ReportRenderStatistics(new SceneRenderStatistics(
                1, 10, 3, 4, 60, 20, 2, 640, 360, true));
            EditorProfiler.ReportRenderStatistics(new SceneRenderStatistics(
                2, 6, 2, 3, 42, 14, 1, 1280, 720, false));
        }

        var snapshot = EditorProfiler.GetSnapshot();
        Require(snapshot.Recording && snapshot.Capacity == 8 && snapshot.Frames.Length == 1,
            "Profiler snapshot did not expose its recording state, capacity, or completed frame.");
        var frame = snapshot.Latest ?? throw new InvalidOperationException("Latest frame is missing.");
        Require(frame.FrameIndex == 1 && frame.FrameMilliseconds > 0,
            "Profiler frame identity or total duration was not captured.");
        Require(frame.UpdateMilliseconds >= 0.5 && frame.RuntimeMilliseconds > 0 &&
                frame.RenderMilliseconds > 0 && frame.ImGuiMilliseconds > 0 &&
                frame.PresentMilliseconds > 0,
            "One or more profiler phase durations were not captured.");
        Require(frame.ManagedAllocatedBytes >= 0 && frame.ManagedHeapBytes >= 0 &&
                frame.ManagedFragmentedBytes >= 0 && frame.WorkingSetBytes >= 0 &&
                frame.PrivateBytes >= 0 && frame.Gen0Collections >= 0 &&
                frame.Gen1Collections >= 0 && frame.Gen2Collections >= 0,
            "Profiler memory or GC counters contained a negative value.");
        var render = frame.RenderStatistics;
        Require(render.CameraCount == 3 && render.VisibleSubmissionCount == 16 &&
                render.BatchCount == 5 && render.DrawCallCount == 7 &&
                render.VertexCount == 102 && render.TriangleCount == 34 &&
                render.LineCount == 3 && render.TargetWidth == 1280 &&
                render.TargetHeight == 720 && !render.HasCompleteDrawStatistics,
            "Profiler did not aggregate multiple viewport render statistics.");
    }

    private static void VerifyFixedCapacityHistory()
    {
        EditorProfiler.HistoryCapacity = 3;
        EditorProfiler.Clear();
        for (var index = 0; index < 5; index++)
            using (EditorProfiler.BeginFrame())
                Measure(EditorProfilerArea.Update);

        var snapshot = EditorProfiler.GetSnapshot();
        Require(snapshot.Frames.Length == 3,
            "Profiler ring did not enforce its fixed capacity.");
        Require(snapshot.Frames.Select(frame => frame.FrameIndex).SequenceEqual([3L, 4L, 5L]),
            "Profiler ring did not return retained frames from oldest to newest.");

        var copy = snapshot.Frames;
        copy[0] = default;
        Require(EditorProfiler.GetSnapshot().Frames[0].FrameIndex == 3,
            "Profiler snapshot exposed mutable ring storage.");
    }

    private static void VerifyMethodSamplesAndCustomModules()
    {
        EditorProfiler.HistoryCapacity = 8;
        EditorProfiler.Clear();
        EditorProfiler.CaptureCallStacks = true;
        var changed = 0;
        void ModulesChanged() => changed++;
        EditorProfilerModuleRegistry.modulesChanged += ModulesChanged;
        var module = new EditorProfilerModuleDefinition(
            "tests.jobs",
            "Jobs",
            [new EditorProfilerCounterDescriptor(
                "active", "Active Jobs", EditorProfilerCounterUnit.Number, Color.white)]);
        using (EditorProfilerModuleRegistry.Register(module))
        {
            Require(changed == 1 && EditorProfilerModuleRegistry.GetModules().Any(item =>
                    item.Id == module.Id),
                "Profiler module registry did not publish an external module.");
            using (EditorProfiler.BeginFrame())
            {
                using var editor = EditorProfiler.BeginMethodSample(
                    typeof(Program), nameof(VerifyMethodSamplesAndCustomModules),
                    EditorProfilerDomain.Editor);
                using (EditorProfiler.BeginMethodSample(
                           typeof(Program), nameof(SimulatedRuntimeMethod),
                           EditorProfilerDomain.Runtime))
                    SimulatedRuntimeMethod();
                EditorProfiler.ReportCounter(module.Id, "active", 7);
            }

            var frame = EditorProfiler.GetSnapshot().Latest ??
                        throw new InvalidOperationException("Method profiler frame is missing.");
            Require(frame.MethodSamples.Length == 2 &&
                    frame.MethodSamples.Any(sample =>
                        sample.Domain == EditorProfilerDomain.Editor &&
                        sample.MethodName == nameof(VerifyMethodSamplesAndCustomModules)) &&
                    frame.MethodSamples.Any(sample =>
                        sample.Domain == EditorProfilerDomain.Runtime &&
                        sample.MethodName == nameof(SimulatedRuntimeMethod)),
                "Method profiler did not retain method names or Editor/Runtime domains.");
            var runtime = frame.MethodSamples.Single(sample =>
                sample.MethodName == nameof(SimulatedRuntimeMethod));
            Require(runtime.ParentId > 0 && runtime.TotalMilliseconds >= runtime.SelfMilliseconds &&
                    runtime.AllocatedBytes >= runtime.SelfAllocatedBytes &&
                    runtime.CallStack.Length > 0 && runtime.ThreadId > 0,
                "Method profiler did not retain hierarchy, timing, allocation, thread, or call stack data.");
            Require(frame.CounterSamples is
                    [{ ModuleId: "tests.jobs", CounterName: "active", Value: 7 }],
                "Profiler did not store an external module counter in the completed frame.");
        }
        EditorProfilerModuleRegistry.modulesChanged -= ModulesChanged;
        Require(changed == 2 && EditorProfilerModuleRegistry.GetModules().All(item =>
                item.Id != module.Id),
            "Disposing a profiler module registration did not remove that module.");
    }

    private static void SimulatedRuntimeMethod()
    {
        var allocation = new byte[128];
        allocation[0] = 1;
        GC.KeepAlive(allocation);
        Thread.SpinWait(1_024);
    }

    private static void VerifyRecordingAndClear()
    {
        var beforePause = EditorProfiler.GetSnapshot();
        EditorProfiler.Recording = false;
        using (EditorProfiler.BeginFrame()) Measure(EditorProfilerArea.Update);
        var paused = EditorProfiler.GetSnapshot();
        Require(!paused.Recording && paused.Frames.Length == beforePause.Frames.Length,
            "Pausing recording changed or appended profiler history.");
        Require(paused.Version > beforePause.Version,
            "Profiler version did not change when recording was paused.");

        var version = paused.Version;
        EditorProfiler.Clear();
        var cleared = EditorProfiler.GetSnapshot();
        Require(cleared.Frames.Length == 0 && cleared.Version > version,
            "Profiler Clear did not empty history or advance its version.");
        EditorProfiler.Recording = true;
    }

    private static void VerifyAllocationFreeFrameCopy()
    {
        var buffer = new EditorProfilerFrame[EditorProfiler.HistoryCapacity];
        _ = EditorProfiler.GetMetadata();
        _ = EditorProfiler.CopyFrames(buffer, out _, out _, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        long version = 0;
        bool recording = false;
        int capacity = 0;
        EditorProfilerMetadata metadata = default;
        for (var index = 0; index < 512; index++)
        {
            metadata = EditorProfiler.GetMetadata();
            count = EditorProfiler.CopyFrames(buffer, out version, out recording, out capacity);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0,
            $"Profiler metadata/history reads allocated {allocated} bytes.");
        Require(count == 3 && version == EditorProfiler.Version && recording && capacity == 3 &&
                metadata.Version == version && metadata.Recording == recording &&
                metadata.Capacity == capacity && metadata.FrameCount == count &&
                buffer[0].FrameIndex == 3 && buffer[2].FrameIndex == 5,
            "Caller-buffer profiler history copy returned inconsistent state or ordering.");
    }

    private static void VerifyThreadSafeSnapshots()
    {
        EditorProfiler.HistoryCapacity = 32;
        EditorProfiler.Clear();
        const int frames = 160;
        using var start = new ManualResetEventSlim(false);
        var writer = Task.Run(() =>
        {
            start.Wait();
            for (var index = 0; index < frames; index++)
            {
                using var frame = EditorProfiler.BeginFrame();
                using var sample = EditorProfiler.BeginSample(EditorProfilerArea.Runtime);
                Thread.SpinWait(32);
            }
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.Wait();
            while (!writer.IsCompleted)
            {
                var snapshot = EditorProfiler.GetSnapshot();
                Require(snapshot.Frames.Length <= snapshot.Capacity,
                    "A concurrent profiler snapshot exceeded the ring capacity.");
                for (var index = 1; index < snapshot.Frames.Length; index++)
                    Require(snapshot.Frames[index - 1].FrameIndex < snapshot.Frames[index].FrameIndex,
                        "A concurrent profiler snapshot was not ordered.");
            }
        })).ToArray();
        start.Set();
        Task.WaitAll([writer, .. readers]);

        var completed = EditorProfiler.GetSnapshot();
        Require(completed.Frames.Length == 32 && completed.Frames[^1].FrameIndex == frames,
            "Concurrent recording did not retain the newest fixed-capacity history.");
        Require(EditorProfiler.Version == completed.Version,
            "Profiler Version did not match its atomic snapshot version.");
    }

    private static void Measure(EditorProfilerArea area)
    {
        using var sample = EditorProfiler.BeginSample(area);
        Thread.SpinWait(2_048);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
