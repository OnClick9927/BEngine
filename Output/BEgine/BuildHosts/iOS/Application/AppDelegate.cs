using System.Diagnostics;
using BEngine.Player;
using Foundation;
using UIKit;

namespace BEngine.Player.Hosts.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    private readonly Stopwatch _clock = new();
    private PlayerRuntimeSession? _session;
    private NSTimer? _timer;
    private TimeSpan _lastFrame;

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        var payload = Path.Combine(NSBundle.MainBundle.ResourcePath!, "BEnginePayload");
        var projectPath = BEnginePlayer.ResolveDefaultProjectPath(payload);
        _session = BEnginePlayer.CreateRuntimeSession(
            projectPath, declareAotInterpreter: true,
            persistentDataPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEnginePlayer"));
        _session.Start();
        _clock.Start();
        _lastFrame = _clock.Elapsed;
        _timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(16), _ => Tick());
        return true;
    }

    public override void OnActivated(UIApplication application)
    {
        _session?.SetPaused(false);
        _session?.SetFocused(true);
        _lastFrame = _clock.Elapsed;
    }

    public override void DidEnterBackground(UIApplication application)
    {
        _session?.SetFocused(false);
        _session?.SetPaused(true);
    }

    public override void ReceiveMemoryWarning(UIApplication application) => _session?.RaiseLowMemory();

    public override void WillTerminate(UIApplication application)
    {
        _timer?.Invalidate();
        _timer?.Dispose();
        _timer = null;
        _session?.Dispose();
        _session = null;
    }

    private void Tick()
    {
        if (_session is null) return;
        var now = _clock.Elapsed;
        _session.Tick(now - _lastFrame);
        _lastFrame = now;
    }
}
