using System.Diagnostics;
using Android.App;
using Android.Content.PM;
using Android.OS;
using BEngine.Player;

namespace BEngine.Player.Hosts.Android;

[Activity(
    Label = "BEngine",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : Activity
{
    private readonly Stopwatch _clock = new();
    private Handler? _handler;
    private PlayerRuntimeSession? _session;
    private TimeSpan _lastFrame;
    private bool _resumed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        base.OnCreate(savedInstanceState);
        var root = Path.Combine(FilesDir!.AbsolutePath, "BEnginePlayer");
        ExtractAssetDirectory("BEnginePayload", root);
        var projectPath = BEnginePlayer.ResolveDefaultProjectPath(root);
        _session = BEnginePlayer.CreateRuntimeSession(
            projectPath, declareAotInterpreter: true,
            persistentDataPath: Path.Combine(FilesDir.AbsolutePath, "BEnginePersistentData"));
        _session.Start();
        _handler = new Handler(Looper.MainLooper!);
        _clock.Start();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _resumed = true;
        _session?.SetPaused(false);
        _lastFrame = _clock.Elapsed;
        _handler?.Post(Tick);
    }

    protected override void OnPause()
    {
        _resumed = false;
        _handler?.RemoveCallbacks(Tick);
        _session?.SetPaused(true);
        base.OnPause();
    }

    public override void OnLowMemory()
    {
        _session?.RaiseLowMemory();
        base.OnLowMemory();
    }

    protected override void OnDestroy()
    {
        _resumed = false;
        _handler?.RemoveCallbacksAndMessages(null);
        _session?.Dispose();
        _session = null;
        _handler?.Dispose();
        _handler = null;
        base.OnDestroy();
    }

    private void Tick()
    {
        if (!_resumed || _session is null) return;
        var now = _clock.Elapsed;
        _session.Tick(now - _lastFrame);
        _lastFrame = now;
        _handler!.PostDelayed(Tick, 16);
    }

    private void ExtractAssetDirectory(string assetPath, string destination)
    {
        var children = Assets!.List(assetPath) ?? [];
        if (children.Length == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = Assets.Open(assetPath);
            using var output = File.Create(destination);
            input.CopyTo(output);
            return;
        }
        Directory.CreateDirectory(destination);
        foreach (var child in children)
            ExtractAssetDirectory($"{assetPath}/{child}", Path.Combine(destination, child));
    }
}
