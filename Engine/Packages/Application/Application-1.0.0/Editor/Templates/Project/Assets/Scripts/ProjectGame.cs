using BEngine.Player;
using BEngine;
using BEngine.App;
using BEngine.App.Events;
using BEngine.App.Mvc;
using BEngine.App.Pref;
using BEngine.App.State;
using BEngine.App.Value;
using Microsoft.Extensions.DependencyInjection;

namespace Game;

public sealed class ProjectGame : GameDomain
{
    private IValueService _values = null!;
    private IEventBus _events = null!;
    private IPrefService _prefs = null!;
    private MvcService _mvc = null!;
    private GameStateService _states = null!;
    protected override void ConfigureServices(IServiceCollection services)
    {
        // Values resolve these standard DI registrations, including scoped/keyed registrations.
        services.AddSingleton(new ProjectInfo("New Game"));
    }

    protected override void Configure()
    {
        // Domain composition is explicit. Add custom services or multiple instances of any type here.
        _values = Services.Add(new ValueService(Services));
        _events = Services.Add(new EventBus());
        _prefs = Services.Add(new PrefService("Game.ProjectGame"));
        _mvc = Services.Add(new MvcService(Services));
        _states = Services.Add(new GameStateService(Services));
        // Explicit factories keep composition visible and work with trimmed/AOT players.
        _mvc.RegisterModel(new PlayerModel());
        _mvc.RegisterCtrl<PlayerCtrl>(services => new PlayerCtrl(
            services.GetRequiredService<PlayerModel>(), services.GetRequiredService<IEventBus>()));
        _states.Register(new MainState());
    }

    protected override void Startup()
    {
        var profile = _prefs.Load<PlayerProfile>("profile");
        profile.LaunchCount++;
        Track(_events.Subscribe<GameStarted>(message =>
            Debug.Log($"{_values.GetRequired<ProjectInfo>().Name}: launch {message.LaunchCount}")));
        _states.Switch<MainState>();
        _events.Publish(new GameStarted(profile.LaunchCount));
        // PrefService saves on domain shutdown. The domain disposes its owned services in reverse order.
    }
}

public sealed record ProjectInfo(string Name);
public readonly record struct GameStarted(int LaunchCount);
public sealed class PlayerProfile { public int LaunchCount { get; set; } }
public sealed class PlayerModel : ModelBase { public int LaunchCount { get; set; } }

public sealed class PlayerCtrl(PlayerModel model, IEventBus events) : CtrlBase
{
    protected override void OnInitialize() =>
        Track(events.Subscribe<GameStarted>(message => model.LaunchCount = message.LaunchCount));
}

public sealed class MainState : GameStateBase
{
    protected override void OnEnter(IGameState? previous) { }
    protected override void OnUpdate(Fix64 deltaTime) { }
}
