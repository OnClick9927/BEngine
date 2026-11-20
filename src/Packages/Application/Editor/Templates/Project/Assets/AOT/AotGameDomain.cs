using BEngine.Player;
using BEngine;
using BEngine.App;
using BEngine.App.Events;

namespace AOT;

/// <summary>The startup domain. Its lifetime ends before the hot-update domain starts.</summary>
public sealed class AotGameDomain : GameDomain
{
    protected override void Configure() => Services.Add(new EventBus());
    protected override void Startup() => Debug.Log("AOT_GAME_DOMAIN_STARTED");
    protected override void OnQuit() => Debug.Log("AOT_GAME_DOMAIN_STOPPED");
}
