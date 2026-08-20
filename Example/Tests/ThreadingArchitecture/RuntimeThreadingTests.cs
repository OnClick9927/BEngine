using BEngine.Entities;
using BEngine.Rendering;

namespace BEngine.ExampleTests.ThreadingArchitecture;

internal static class RuntimeThreadingTests
{
    public static void Run()
    {
        TestAssert.Require(!EngineThreadContext.IsBound,
            "The test process inherited a stale BEngine main-thread binding.");

        var ownerThreadId = Environment.CurrentManagedThreadId;
        using (EngineThreadContext.BindCurrentThread("ThreadingArchitecture"))
        {
            TestAssert.Require(EngineThreadContext.IsBound && EngineThreadContext.IsMainThread &&
                               EngineThreadContext.MainThreadId == ownerThreadId,
                "BindCurrentThread did not capture the owning runtime thread.");

            using (EngineThreadContext.BindCurrentThread("Nested test scope"))
                EngineThreadContext.AssertMainThread("Nested runtime access");
            TestAssert.Require(EngineThreadContext.IsBound && EngineThreadContext.IsMainThread,
                "Disposing a nested binding released the outer runtime binding.");

            VerifyBackgroundBindingIsRejected();
            VerifyRuntimeBoundariesRejectWorkerAccess();
        }

        TestAssert.Require(!EngineThreadContext.IsBound && EngineThreadContext.MainThreadId == 0,
            "Disposing the outer binding did not release runtime thread ownership.");
        Task.Run(() => EngineThreadContext.AssertMainThread("Unbound data tool"))
            .GetAwaiter().GetResult();
    }

    private static void VerifyBackgroundBindingIsRejected()
    {
        var exception = CaptureWorkerException(() =>
        {
            using var ignored = EngineThreadContext.BindCurrentThread("Competing runtime");
        });
        RequireThreadFailure(exception, "A worker thread replaced the active runtime owner.");
    }

    private static void VerifyRuntimeBoundariesRejectWorkerAccess()
    {
        EngineThreadContext.AssertMainThread("Main-thread baseline");
        var scene = new Scene("Thread affinity scene");
        var mainObject = scene.CreateGameObject("Main-thread object");
        var mainObjectId = mainObject.GetInstanceID();
        var mainEntity = scene.world.EntityManager.CreateEntity();
        TestAssert.Require(mainObject.scene == scene && scene.world.EntityManager.Exists(mainEntity),
            "Runtime main-thread access failed before worker-affinity checks ran.");

        RequireThreadFailure(CaptureWorkerException(() => _ = new GameObject("Worker object")),
            "BObject construction was allowed on a worker thread.");
        RequireThreadFailure(CaptureWorkerException(() => _ = mainObject.name),
            "BObject state was readable from a worker thread.");
        RequireThreadFailure(CaptureWorkerException(() =>
            BObject.FindObjectFromInstanceID(mainObjectId)),
            "The global BObject registry was readable from a worker thread.");
        RequireThreadFailure(CaptureWorkerException(() => scene.CreateGameObject("Worker scene object")),
            "Scene mutation was allowed on a worker thread.");
        RequireThreadFailure(CaptureWorkerException(() => _ = new World("Worker world")),
            "World construction was allowed on a worker thread.");
        RequireThreadFailure(CaptureWorkerException(() => scene.world.EntityManager.CreateEntity()),
            "World/ECS mutation was allowed on a worker thread.");
        _ = EngineRenderer.ResolveGameCamera(scene);
        RequireThreadFailure(CaptureWorkerException(() => EngineRenderer.ResolveGameCamera(scene)),
            "A rendering scene query was allowed on a worker thread.");

        var runtime = new SceneRuntime(scene);
        runtime.Start();
        try
        {
            RequireThreadFailure(CaptureWorkerException(() => runtime.Tick(Fix64.Parse("0.016"))),
                "SceneRuntime.Tick was allowed on a worker thread.");
            runtime.Tick(Fix64.Parse("0.016"));
            TestAssert.Require(Time.frameCount > 0,
                "The main-thread SceneRuntime frame did not advance after a rejected worker call.");
        }
        finally
        {
            runtime.Stop();
            scene.world.Dispose();
        }
    }

    private static Exception? CaptureWorkerException(Action action) => Task.Run(() =>
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }).GetAwaiter().GetResult();

    private static void RequireThreadFailure(Exception? exception, string message)
    {
        if (exception is not InvalidOperationException threadException)
            throw new InvalidOperationException(
                $"{message} Received {exception?.GetType().Name ?? "no exception"}.", exception);
        TestAssert.Require(threadException.Message.Contains("thread", StringComparison.OrdinalIgnoreCase),
            $"{message} The error does not explain the thread-affinity violation: {threadException.Message}");
    }
}
