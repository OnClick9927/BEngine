# Application 游戏开发指南

Application 从 AOT 开始组织游戏工作流。`Launcher` 在一个场景运行域中只驱动一个 `GameDomain`；AOT 和热更新阶段使用不同的 GameDomain 实例。Core / Platform 负责环境准备、闪屏、窗口、渲染、输入和场景执行。运行时 API 统一使用 `BEngine.App` 前缀。

Framework 完全按单游戏循环设计，不启动工作线程，也不提供线程安全容器。异步统一使用引擎 `BTask` / `BTask<T>`，取消使用 `BCancellationToken`；等待通过引擎帧调度推进。服务调用、事件发布、取消和异步 continuation 均应在所属循环执行。

## 入口与域替换

新工程包含 `AOT.AotGameDomain` 和 `Game.ProjectGame`，两者均继承 `GameDomain`。AOT 入口先启动 AotGameDomain；内容及热更新代码准备好后，AOT 舞台停止其旧域，再进入业务场景，由 GameEntry 启动 ProjectGame。项目可在这些域内添加自己的服务和流程。

```csharp
using BEngine;
using BEngine.App;

namespace ApplicationGuide;

public sealed class GameEntry : MonoBehaviour
{
    public override void Awake()
    {
        if (Launcher.Current?.ActiveDomain is not ProjectGame)
            Launcher.Run(new ProjectGame(), replaceExisting: true);
    }
}
```

`ActiveDomain` 包括 Starting 阶段，可避免启动回调中同步换场景导致重复入口；`RunningDomain` 只表示已经启动且未请求停止的域。`Launcher.Current` / `GameDomain.Current` 根据当前场景上下文查询，不能当作全局静态游戏实例。工具和独立宿主应显式保存域引用。

在场景回调中可调用 `Launcher.Current!.Replace(new ProjectGame())`。Replace 先请求旧域停止；若正在执行旧域的初始化、帧更新或通知回调，会等该回调安全返回，完成清理后才启动新域。同一时刻不会同时运行两个域；不支持重叠的替换请求，也不能重新启动已经停止的实例。Launcher 使用 Core 的场景持久化跟随切场景；停止 Play Mode、最后场景停止、退出应用或销毁 Launcher 时结束当前域。

## GameDomain 与服务集合

```text
Start → ConfigureServices（可选普通 DI）→ Configure（显式组合服务）
      → 服务按注册顺序 Initialize → Startup
Tick / FixedTick / LateTick → 服务按注册顺序 → 域对应回调
Quit → 取消域 Lifetime → OnQuit
     → 服务反序 OnDomainStopping → 服务反序 Dispose → 释放 DI
```

GameDomain 不内置 Events、State、MVC 或 Pref 等固定服务，也不固定它们的执行顺序。每个域通过 `Services.Add(instance, key, ownsInstance)` 注册任意数量的 `IGameService`；初始化前注册集合冻结。初始化失败也会释放已接管的服务，避免留下部分初始化的资源。`IGameService` 是统一接口；可继承 `GameService` 使用初始化、帧更新、停止、释放钩子及局部 Lifetime。

同一类型可以注册多个独立实例。`Services.Get<T>(key)` 按 key 获取，`GetAll<T>()` 返回全部匹配实例；没有 key 且匹配多个实例时会明确报歧义，不能依靠最后注册者覆盖。key 应在预期查询类型内保持唯一。同一对象不能重复注册。

下面显式注册服务、选择顺序，并组合 State / MVC / Pref / Events。所有服务由域清理，项目不再次调用其 Initialize / Dispose。

```csharp
using BEngine.App;
using BEngine.App.Events;
using BEngine.App.Mvc;
using BEngine.App.Pref;
using BEngine.App.State;
using BEngine.App.Value;
using Microsoft.Extensions.DependencyInjection;

namespace ApplicationGuide;

public sealed class ProjectGame : GameDomain
{
    private IEventBus _events = null!;
    private IPrefService _prefs = null!;
    private MvcService _mvc = null!;
    private GameStateService _states = null!;

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton(new GameOptions("My Game"));

    protected override void Configure()
    {
        Services.Add(new ValueService(Services));
        _events = Services.Add(new EventBus());
        _prefs = Services.Add(new PrefService("my-studio/my-game"));
        _mvc = Services.Add(new MvcService(Services));
        _states = Services.Add(new GameStateService(Services));
        _mvc.RegisterModel(new ProgressModel(_prefs));
        _mvc.RegisterCtrl(provider => new LobbyCtrl(
            provider.GetRequiredService<ProgressModel>(), _events));
        _mvc.RegisterView(provider => new LobbyView(provider.GetRequiredService<LobbyCtrl>()));
        _states.Register(_ => new LobbyState(_events, _mvc.GetModel<ProgressModel>()));
    }

    protected override void Startup()
    {
        _states.Switch<LobbyState>();
        _events.Publish(new LobbyOpened(Services.Get<IValueService>().GetRequired<GameOptions>().Title));
    }
}

public sealed record GameOptions(string Title);
public readonly record struct LobbyOpened(string Title);
public readonly record struct CoinsChanged(int Value);
public sealed class ProgressData { public int Coins { get; set; } }
public sealed class ProgressModel(IPrefService prefs) : ModelBase
{
    public ProgressData Data { get; private set; } = new();
    protected override void OnInitialize() => Data = prefs.Load<ProgressData>("progress");
}
public sealed class LobbyCtrl(ProgressModel model, IEventBus events) : CtrlBase
{
    public void AddCoin() => events.Publish(new CoinsChanged(model.Data.Coins + 1));
}
public sealed class LobbyView(LobbyCtrl controller) : ViewBase
{
    public void OnAddCoinClicked() => controller.AddCoin();
}
public sealed class LobbyState(IEventBus events, ProgressModel model) : GameStateBase
{
    protected override void OnEnter(IGameState? previous)
        => Track(events.Subscribe<CoinsChanged>(message => model.Data.Coins = message.Value));
}
```

State 的注册和切换使用显式实例或 factory。进入状态会创建本次激活的 Lifetime，离开时清理 `Track` 的订阅；`TrackLifetime` 属于状态实例。帧回调内切换在回调返回后处理。MVC 按 Model → Controller → View 初始化，反序清理；需要帧更新的元素显式实现 `IMvcUpdate` / `IMvcFixedUpdate` / `IMvcLateUpdate`。上例将 MVC 注册在 State 前，因此状态 factory 可以读取已初始化的 Model。多个 State / MVC 服务可以分别管理不同子流程。

`ValueService` 是标准 DI / keyed DI 与域服务集合的查询门面，不创建第二套 DI 容器。构造注入或显式 factory 优先；持有的 ValueService 实例可调用 `Inject(target)` 进行可选的反射成员注入，需要调用方为裁剪保留成员。传入 ValueService 的 provider 是借用的，不由它释放。

普通 DI 的 `ConfigureServices` 用于不需要域生命周期的依赖。需要统一调度的服务只交给域集合拥有，避免同一实例同时被 DI scope 和域集合 Dispose。`ownsInstance: false` 表示外部借用实例，调用方仍负责最终释放；它仍接收初始化和停止通知。一个服务实例不能重新初始化并复用到另一个域。

## 自定义服务与同类型多实例

```csharp
using System;
using BEngine;
using BEngine.App;

namespace ApplicationGuide;

public sealed class CounterService : GameService
{
    public int Frames { get; private set; }
    protected override void OnUpdate(Fix64 deltaTime) => Frames++;
}
public sealed class MultiServiceDomain : GameDomain
{
    protected override void Configure()
    {
        Services.Add(new CounterService(), "main");
        Services.Add(new CounterService(), "preview");
    }
    protected override void Startup()
    {
        var main = Services.Get<CounterService>("main");
        var all = Services.GetAll<CounterService>();
    }
}
public static class HeadlessExample
{
    public static void RunOneFrame(GameDomain domain, IServiceProvider? hostServices = null)
    {
        using (domain)
        {
            domain.Start(hostServices);
            var dt = Fix64.Parse("0.02");
            domain.FixedTick(dt);
            domain.Tick(dt);
            domain.LateTick(dt);
        }
    }
}
```

Headless 宿主负责推进域的三种帧回调；使用 BTask 时，还需推进 Core 的 `BTaskScheduler`。Player / Editor 已由引擎统一推进，不要再重复 Tick 调度器。调用 Quit 会先请求停止并取消 Lifetime，当前域回调返回后才释放服务；不要在服务、State 或 MVC 回调中直接 Dispose 自己，统一向所属域请求退出。

## Events / BTask / Lifetime

事件中心属于服务实例。typed event 按消息类型区分，topic 额外区分命名通道。`Publish` 只执行同步订阅；`PublishAsync` 按订阅顺序等待同步和 BTask handler，不会后台启动任务。等待者收到消息、被取消或 event bus 释放后都会退订。

```csharp
using BEngine;
using BEngine.App;
using BEngine.App.Events;

namespace ApplicationGuide;

public static class EventExample
{
    public static async BTask<int> PublishAndWaitAsync(IEventBus events, LifetimeScope lifetime)
    {
        lifetime.Track(events.SubscribeAsync<int>((value, token) =>
        {
            token.ThrowIfCancellationRequested();
            return BTask.CompletedTask;
        }, topic: "score"));
        var next = events.WaitAsync<int>(lifetime.Token, topic: "score");
        await BTask.Yield();
        await events.PublishAsync(100, lifetime.Token, topic: "score");
        return await next;
    }
}
```

`LifetimeScope.Token` 是 BCancellationToken。`Track` 的资源在取消后反序释放；已经结束的 Lifetime 会立即释放晚到的资源。将事件 token 交给域的 `Track`、状态的 `Track`、UI 面板的 `Track` 或自定义 GameService 的 `Track`，即可跟随正确的生命周期。挂起操作应显式使用该 token，并在恢复后检查取消；服务销毁不会中断已经进入的用户回调。

## 红点 / UI / Undo / GameObject 池

四种服务同样是显式组合，互不依赖全局单例。下例的 Install 在域 Configure 中调用，Use 在域启动后执行；UI root 和创建 GameObject 的 factory 由工程提供。

```csharp
using System;
using BEngine;
using BEngine.App;
using BEngine.App.Pooling;
using BEngine.App.RedDot;
using BEngine.App.UI;
using BEngine.App.Undo;
using BEngine.UIElements;

namespace ApplicationGuide;

public static class WorkflowExamples
{
    public static void Install(GameDomain domain, VisualElement uiRoot, Func<GameObject> createBullet)
    {
        domain.Services.Add(new RedDotService());
        var ui = domain.Services.Add(new UIService(uiRoot));
        ui.Register("notice", () => new NoticePanel(), layer: 10);
        domain.Services.Add(new UndoService(capacity: 64));
        var pools = domain.Services.Add(new GameObjectPoolService());
        pools.Register("bullet", createBullet, maxInactive: 32, maxActive: 256);
    }
    public static int Use(GameDomain domain)
    {
        var dots = domain.Services.Get<RedDotService>();
        domain.Track(dots.Subscribe("mail", change => { }));
        dots.SetCount("mail/unread", 3);
        dots.Flush();
        var ui = domain.Services.Get<UIService>();
        var panel = ui.Show("notice");
        ui.Hide(panel);
        ui.Show(panel);
        ui.Close(panel);
        var score = 0;
        var undo = domain.Services.Get<UndoService>();
        undo.Execute(new DelegateUndoCommand("Add score", () => score = 1, () => score = 0));
        undo.Undo();
        undo.Redo();
        var pools = domain.Services.Get<GameObjectPoolService>();
        pools.Prewarm("bullet", 4);
        using var rental = pools.Rent("bullet");
        return score;
    }
}
public sealed class NoticePanel : UIPanel { }
```

- **RedDotService**：按 `/` 路径组成树，Count 为本节点 LocalCount 与子树总和。数据立即更新，通知在 Flush / LateUpdate 合并；Subscribe 返回可退订 token。`BindActive` 可绑定一个 GameObject 的显示状态。独立服务可分别管理账号、活动等红点树。
- **UIService**：包装 UIElements 的 root / UIDocument，负责层级、显隐、打开与关闭。`SingleInstance` 默认重用已打开或隐藏的面板；`MultipleInstances` 每次创建新面板。UIPanel 的 Load / Show / Hide / Close 钩子和 Lifetime 管理面板资源。服务借用 root/document，释放时只移除自己创建的层和面板，不销毁借用宿主；资源加载、BXML 克隆和绘制由工程及 UIElements 负责。
- **UndoService**：有界 undo/redo 历史、记录已完成操作和批量命令。命令的 Execute / Undo 必须各自成功，或失败时保持原状态。批量回滚或补偿失败会将历史标记为 Faulted，应修复业务状态后 Clear；捕获的对象由历史持有到其被淘汰或清空。
- **GameObjectPoolService**：复用 Pooling 包的 ObjectPool，提供命名池、Get/Return、generation 检查的 Rent、Prewarm、Trim、统计和释放。默认归还会恢复局部 Transform；onSpawn/onDespawn 负责工程组件状态。工厂交出新对象所有权，借用的父 Transform 必须存活到池结束；服务不会擅自把场景对象设为持久化。离开所属场景前应结束相应域或 Unregister 该池。

多个 UIService 可以管理不同 UI root，多个 GameObjectPoolService 可以隔离战斗与预览对象。Get/Rent 取得的对象只能交回所属服务；使用 rental 时不要再手动 Return 同一对象。

## Pref 与持久化

PrefService 显式指定稳定的 namespace，例如 `new PrefService("my-studio/my-game")`。`Load<T>(key)` 惰性加载并缓存，`GetContext<T>(key)` 提供 Value / Save / Delete。正常完成启动的域停止时，PrefService 默认在 OnDomainStopping 保存已加载内容；设置 `SaveOnDomainStop = false` 可关闭。Dispose 本身只释放内存，启动失败不自动保存。

浏览器可能直接结束页面，关卡完成或购买确认等时点应主动 Save。默认存储复用 Core PlayerPrefs：Web 使用当前 origin 的 localStorage，更换协议、域名、端口或清理网站数据会影响可见存档。服务不再创建一套 Web 沙盒文件。

可向 PrefService 构造器提供 `IPrefLoader` / `IPrefConverter` 替换存储和序列化；二者是借用实例，生命周期由调用方管理。默认转换器为 YAML。泛型入口仅标注根 DTO 的裁剪保留要求；嵌套 DTO 需显式保留完整类型图或使用生成式 converter。持久化 key 包含 namespace、数据类型和业务 key；发布后重命名类型应自行迁移旧数据。

## 边界与参考

服务接口组织参考 [IFramework-Unity](https://github.com/OnClick9927/IFramework-Unity/tree/Master/Assets/IFramework/)，实际实现使用 BEngine 的 GameDomain、Fix64、BTask、BCancellationToken、场景生命周期、标准 DI 和 PlayerPrefs。Application 只组合工作流；AssetBundles、Networking、Pooling、UIElements 继续各自提供底层功能，不重复实现到 Core。

参考项目的 [MIT 声明](ThirdParty/IFramework-License.txt) 随文档保留。包与平台、构建的职责见 [Application 开发框架](Framework.md)。
