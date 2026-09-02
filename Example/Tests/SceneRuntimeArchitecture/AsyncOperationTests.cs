using BEngine.SceneManagement;

namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class AsyncOperationTests
{
    internal static void Run()
    {
        VerifyResourceRequestAsync().GetAwaiter().GetResult();
        VerifySceneActivationAsync().GetAwaiter().GetResult();
    }

    private static async Task VerifyResourceRequestAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineAsyncResources_{Guid.NewGuid():N}");
        var resources = Path.Combine(root, "Resources");
        Directory.CreateDirectory(resources);
        await File.WriteAllTextAsync(Path.Combine(resources, "message.txt"), "async resource");
        Resources.RegisterResourceRoot(root);
        try
        {
            var completedCount = 0;
            var request = Resources.LoadAsync<string>("message");
            request.completed += _ => completedCount++;
            await request.task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(request.status == AsyncOperationStatus.Succeeded && request.isDone &&
                    request.progress == 1 && request.asset == "async resource" && completedCount == 1,
                "ResourceRequest did not expose a coherent successful terminal state.");
            request.completed += _ => completedCount++;
            Require(completedCount == 2,
                "A completion handler registered after completion was not invoked exactly once.");

            var firstHandle = Resources.Acquire<string>("message");
            var secondHandle = Resources.Acquire<string>("message");
            Require(ReferenceEquals(firstHandle.asset, secondHandle.asset) &&
                    Resources.GetReferenceCount(firstHandle.asset) == 2,
                "Resource handles did not share a leased asset or count both references.");
            firstHandle.Dispose();
            Require(Resources.GetReferenceCount(secondHandle.asset) == 1,
                "Disposing a Resource handle released the wrong number of references.");
            secondHandle.Dispose();
            await Resources.UnloadUnusedAssets().task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(Resources.GetReferenceCount(secondHandle.asset) == 0,
                "UnloadUnusedAssets retained an unreferenced Resource lease.");

            var failed = Resources.LoadAsync("message", typeof(DateTime));
            try
            {
                await failed.task.WaitAsync(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("Unsupported async resource type did not fail.");
            }
            catch (NotSupportedException) { }
            Require(failed.status == AsyncOperationStatus.Failed && failed.exception is NotSupportedException,
                "ResourceRequest did not preserve its background exception.");
        }
        finally
        {
            Resources.UnregisterResourceRoot(root);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifySceneActivationAsync()
    {
        var services = new AsyncSceneServices();
        var manager = new RuntimeSceneManager(services);
        services.Manager = manager;
        var completedCount = 0;
        var operation = manager.LoadSceneAsync("Async.scene.yaml");
        operation.allowSceneActivation = false;
        operation.completed += _ => completedCount++;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (operation.progress < 0.9f)
            await Task.Delay(5, timeout.Token);
        Require(!operation.isDone && operation.preparedScene is not null && operation.scene is null,
            "SceneLoadOperation activated before allowSceneActivation was enabled.");

        operation.allowSceneActivation = true;
        await operation.task.WaitAsync(timeout.Token);
        Require(operation.status == AsyncOperationStatus.Succeeded && operation.progress == 1 &&
                operation.scene is { isLoaded: true } && ReferenceEquals(manager.ActiveScene, operation.scene) &&
                completedCount == 1,
            "SceneLoadOperation did not activate or publish its Scene exactly once.");
        manager.UnregisterScene(operation.scene!, disposeScene: true);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class AsyncSceneServices : IServiceProvider, ISceneLoader
    {
        internal RuntimeSceneManager? Manager { get; set; }

        public object? GetService(Type serviceType) => serviceType switch
        {
            _ when serviceType == typeof(ISceneLoader) => this,
            _ when serviceType == typeof(IRuntimeSceneManager) => Manager,
            _ => null
        };

        public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
        {
            Thread.Sleep(25);
            return new Scene(Path.GetFileNameWithoutExtension(sceneNameOrPath), services)
            {
                path = sceneNameOrPath
            };
        }
    }
}
