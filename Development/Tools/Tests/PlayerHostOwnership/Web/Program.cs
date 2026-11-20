using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Player;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

if (OperatingSystem.IsBrowser()) await Probe.Run();

[SupportedOSPlatform("browser")]
static partial class Probe
{
    [JSImport("globalThis.hostProbeResult")] private static partial void Report(string result);
    public static async Task Run()
    {
        var checks = new List<string>();
        void Check(bool condition, string detail)
        {
            if (!condition) throw new Exception(detail);
            checks.Add(detail);
        }
        try
        {
            WebPlatform.Initialize();
            var lateCalls = 0;
            void Later(IServiceCollection services) { lateCalls++; }
            void First(IServiceCollection services) { RuntimeServiceContributions.Register(Later);RuntimeServiceContributions.Configure(services); }
            RuntimeServiceContributions.Register(First);
            RuntimeServiceContributions.Configure(new ServiceCollection());
            Check(lateCalls == 1, "Actual WASM completes lazy prerequisite registration without reentry");
            RuntimeServiceContributions.UnregisterAssembly(typeof(Workflow).Assembly);
            var workflow = new Workflow();
            PlayerHostRegistry.RegisterContentWorkflow(workflow);
            Check(PlayerHostRegistry.Current is PlayerHost, "Actual WASM registers the Core host");
            Check(BEnginePlayer.ResolveDefaultProjectPath() == "/host-probe", "Actual WASM resolves a contributed workspace");
            var session = BEnginePlayer.CreateRuntimeSession("/host-probe");
            try
            {
                session.Start();
                for (int i = 0; i < 100 && !session.CanRender; i++)
                {
                    session.Tick(TimeSpan.FromMilliseconds(16));
                    await Task.Delay(1);
                }
                Check(session.CanRender && session.ActiveScene?.name == "Independent browser scene", "Actual WASM executes content without AB or Graphics");
                Check(session.Services.GetService(typeof(Workflow)) == workflow, "Actual WASM shares host services with scene content");
                session.Stop();
            }
            finally { session.Dispose(); }
            Check(workflow.Disposed, "Actual WASM releases prepared content");
            PlayerHostRegistry.Unregister(typeof(PlayerHost).Assembly);
            Check(PlayerHostRegistry.Current is PlayerHost, "Core assembly retirement cannot clear a different contributor");
            PlayerHostRegistry.Unregister(typeof(Workflow).Assembly);
            var unregistered = false;
            try { _ = PlayerHostRegistry.Current; } catch (InvalidOperationException) { unregistered = true; }
            Check(unregistered, "Actual WASM retires the contributor-owned registration");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.AssetBundles" or "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.Application"), "Actual WASM needs no feature or framework package");
            Report(JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks }));
        }
        catch (Exception exception)
        {
            Report(JsonSerializer.Serialize(new { Passed = false, Checks = checks, Error = exception.ToString() }));
        }
    }
}

sealed class Workflow : IPlayerContentWorkflow
{
    public bool Disposed;
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(this);
        services.AddSingleton<IPlayerStartupResolver>(new Resolver());
    }
    public RuntimeWorkspace OpenWorkspace(string path, IServiceProvider services) => RuntimeWorkspace.CreateRuntime(path,
        new RuntimeProjectData(), new RuntimeSettingsData(), services.GetRequiredService<FileUtility>(), services.GetRequiredService<PathUtility>());
    public IDisposable? ActivateStartupResources(PlayerStartupDescriptor startup, IServiceProvider services) => null;
    public IPlayerStartupLog StartDiagnostics(PlayerStartupDescriptor startup, string? persistentDataPath) => throw new NotSupportedException();
    public IPlayerStartupLog StartFailureDiagnostics(string root) => throw new NotSupportedException();
    public PlayerRuntimeDiagnostics Diagnostics { get; } = new();
    public BValueTask<IPlayerRuntimeContent> PrepareAsync(RuntimeWorkspace workspace, string? persistentDataPath,
        IReadOnlyList<ServiceDescriptor> hostServices, IServiceProvider startupServices, BCancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var services = new ServiceCollection();
        foreach (var descriptor in hostServices) ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        services.AddSingleton<IRuntimeSceneManager, RuntimeSceneManager>();
        var provider = services.BuildServiceProvider();
        var scenes = provider.GetRequiredService<IRuntimeSceneManager>();
        scenes.RegisterScene(new Scene("Independent browser scene", provider), true);
        var stage = new PlayerSceneStage(provider, new SceneRuntimeFactory(), scenes, "browser", serviceOwner: provider);
        return new(new Content(stage, () => Disposed = true));
    }
}
sealed class Content(PlayerSceneStage stage, Action disposed) : IPlayerRuntimeContent
{
    public PlayerSceneStage InitialStage => stage;
    public BValueTask DisposeAsync() { disposed(); return stage.DisposeAsync(); }
}
sealed class Resolver : IPlayerStartupResolver
{
    public string BaseDirectory => "/host-probe";
    public string? PersistentDataPath => null;
    public PlayerStartupDescriptor Resolve(IReadOnlyList<string> arguments) => new(BaseDirectory, BaseDirectory, null);
    public string ResolveDefaultProjectPath(string? directory = null) => BaseDirectory;
}
