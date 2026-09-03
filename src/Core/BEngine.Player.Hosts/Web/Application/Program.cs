using System.Diagnostics;
using BEngine.Player;

AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
var projectPath = BEnginePlayer.ResolveDefaultProjectPath("/");
using var session = BEnginePlayer.CreateRuntimeSession(
    projectPath, declareAotInterpreter: true);
session.Start();
var clock = Stopwatch.StartNew();
var previous = clock.Elapsed;
while (BEngine.Application.isPlaying)
{
    await Task.Delay(16);
    var now = clock.Elapsed;
    session.Tick(now - previous);
    previous = now;
}
