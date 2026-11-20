using System.Reflection;
using System.Runtime.Loader;
using BEngine;
using BEngine.DependencyInjection;

static class ExtensionChecks
{
    internal static long Run(Action<bool, string> check)
    {
        void Throws<T>(Action action, string name) where T : Exception
        {
            try { action(); }
            catch (T) { check(true, name); return; }
            throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
        }
        check(!Time.isFrameActive && !FrameWorkBudget.isActive, "Frame ownership starts inactive");
        check(FrameWorkBudget.GetCooperativeMaximumStep(3) == 3, "Normal work keeps its configured step limit");
        foreach (var invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            Throws<ArgumentOutOfRangeException>(() => FrameWorkBudget.GetCooperativeMaximumStep(invalid), "Invalid step limit rejected: " + invalid);
        using (FrameWorkBudget.EnterLoading(maximumStepMilliseconds: 1.5))
        {
            check(FrameWorkBudget.GetCooperativeMaximumStep(3) == 1.5, "Loading adapter observes the shared step bound");
            using (FrameWorkBudget.EnterLoading(maximumStepMilliseconds: .75))
                check(FrameWorkBudget.GetCooperativeMaximumStep(3) == .75, "Nested loading applies the strictest bound");
            check(FrameWorkBudget.GetCooperativeMaximumStep(3) == 1.5, "Nested loading disposal restores prior bound");
        }
        check(!FrameWorkBudget.isLoading && FrameWorkBudget.GetCooperativeMaximumStep(3) == 3, "Loading disposal restores normal limits");
        bool budgetActive = false;
        void ObserveFrame(ApplicationTick tick) => budgetActive = FrameWorkBudget.isActive;
        BTaskScheduler.Main.FrameAdvanced += ObserveFrame;
        try { BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(16)); }
        finally { BTaskScheduler.Main.FrameAdvanced -= ObserveFrame; }
        check(budgetActive && !FrameWorkBudget.isActive, "Optional work sees budget ownership only during scheduler frames");

        using var scene = new Scene("extension-contracts");
        var gameObject = scene.CreateGameObject();
        var behaviour = gameObject.AddComponent<MessageProbe>();
        var runtime = new SceneRuntime(scene);
        runtime.Start();
        runtime.Tick((Fix64)(1d / 60));
        check(behaviour.FrameActive && !Time.isFrameActive, "Runtime extension sees frame state without controlling its lifetime");
        check(RuntimeTypeCache.CanInvokeMessage(typeof(MessageProbe), "Zero") &&
            !RuntimeTypeCache.CanInvokeMessage(typeof(MessageProbe), "One"), "Zero argument lookup rejects argument messages");
        check(RuntimeTypeCache.CanInvokeMessage(typeof(MessageProbe), "One", typeof(string)) &&
            !RuntimeTypeCache.CanInvokeMessage(typeof(MessageProbe), "One", typeof(int)), "Message lookup preserves argument assignability");
        check(RuntimeTypeCache.TryInvokeMessage(behaviour, "Zero") && behaviour.ZeroCalls == 1 &&
            RuntimeTypeCache.TryInvokeMessage(behaviour, "One", "value") && behaviour.Argument == "value", "Public cached messages retain execution semantics");
        check(!RuntimeTypeCache.CanInvokeMessage(typeof(MessageProbe), "Missing"), "Missing message is detected before scheduling");
        using (var components = gameObject.GetComponentsEnumerator())
        {
            check(components.MoveNext() && ReferenceEquals(components.Current, gameObject.transform) &&
                components.MoveNext() && ReferenceEquals(components.Current, behaviour) && !components.MoveNext(), "Component enumeration retains attachment order");
        }
        var invalidated = gameObject.GetComponentsEnumerator();
        gameObject.AddComponent<OtherProbe>();
        Throws<InvalidOperationException>(() => invalidated.MoveNext(), "Component topology changes invalidate the live enumerator");
        invalidated.Dispose();
        for (var i = 0; i < 100000; i++) Enumerate(gameObject);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var i = 0; i < 100000; i++) count += Enumerate(gameObject);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (count != 300000 || allocated != 0) throw new InvalidOperationException($"Component enumeration count={count}, allocated={allocated}");
        check(true, "100000 live component enumerations allocate zero bytes");
        runtime.Stop();

        var executor = new ManualExecutor();
        RuntimeServiceContributions.RegisterDefault<IBackgroundWorkExecutor>(executor);
        try
        {
            using var scheduler = new BackgroundWorkScheduler(1, 1);
            var owner = typeof(ExtensionChecks).Assembly;
            Throws<ArgumentNullException>(() => scheduler.RunForAssembly<int>(null!, _ => 1), "Null work owner rejected");
            Throws<ArgumentNullException>(() => scheduler.RunForAssembly<int>(owner, null!), "Null owned work rejected");
            executor.IsSupported = false;
            Throws<PlatformNotSupportedException>(() => scheduler.RunForAssembly(owner, _ => 1), "Unavailable worker capability stays explicit");
            executor.IsSupported = true;
            var task = scheduler.RunForAssembly(owner, _ => 7);
            Throws<InvalidOperationException>(() => scheduler.RunForAssembly(owner, _ => 8), "Owned work respects bounded queue admission");
            executor.Complete();
            check(!task.IsCompleted, "Worker completion waits for owner frame");
            BTaskScheduler.Main.Tick(TimeSpan.Zero);
            check(task.IsCompleted && task.GetAwaiter().GetResult() == 7 && scheduler.Snapshot.Active == 0,
                "Owned work publishes result on engine frame");

            var context = new AssemblyLoadContext("owned-job-probe", isCollectible: true);
            var collectibleOwner = context.LoadFromAssemblyPath(owner.Location);
            var discarded = 0;
            var canceled = scheduler.RunForAssembly(collectibleOwner, _ => 13, discard: value => discarded += value);
            context.Unload();
            check(executor.Token.IsCancellationRequested && !canceled.IsCompleted && discarded == 0,
                "Explicit job assembly unload cancels without releasing an active worker's input");
            executor.Complete();
            BTaskScheduler.Main.Tick(TimeSpan.Zero);
            Throws<BOperationCanceledException>(() => canceled.GetAwaiter().GetResult(), "Unloaded job result is canceled");
            check(discarded == 13 && scheduler.Snapshot.Active == 0, "Canceled worker result discarded exactly once after acknowledgement");
            BTaskScheduler.Main.Tick(TimeSpan.Zero);
            check(discarded == 13, "Later frames never repeat discard");

            var implicitOwner = scheduler.Run(_ => 19);
            executor.Complete(); BTaskScheduler.Main.Tick(TimeSpan.Zero);
            check(implicitOwner.GetAwaiter().GetResult() == 19, "Existing inferred-owner scheduling remains available");
            var expected = new IOException("worker-failure");
            var failed = scheduler.RunForAssembly<int>(owner, _ => throw expected);
            executor.Complete(); BTaskScheduler.Main.Tick(TimeSpan.Zero);
            Throws<IOException>(() => failed.GetAwaiter().GetResult(), "Owned worker failure is preserved");
            scheduler.Dispose();
            Throws<ObjectDisposedException>(() => scheduler.RunForAssembly(owner, _ => 1), "Disposed scheduler rejects owned work");
        }
        finally { RuntimeServiceContributions.UnregisterAssembly(typeof(ExtensionChecks).Assembly); }
        return allocated;
    }

    private static int Enumerate(GameObject gameObject)
    {
        var count = 0;
        using var enumerator = gameObject.GetComponentsEnumerator();
        while (enumerator.MoveNext()) count++;
        return count;
    }

    private sealed class ManualExecutor : IBackgroundWorkExecutor
    {
        public bool IsSupported { get; set; } = true;
        public CancellationToken Token;
        private Action? _complete;
        public Task<T> Run<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<T>();
            Token = cancellationToken;
            _complete = () =>
            {
                try { completion.SetResult(work(cancellationToken)); }
                catch (Exception error) { completion.SetException(error); }
            };
            return completion.Task;
        }
        public void Complete() { var action = _complete!; _complete = null; action(); }
    }
}

public sealed class MessageProbe : MonoBehaviour
{
    public bool FrameActive;
    public int ZeroCalls;
    public string? Argument;
    public override void Update() => FrameActive = Time.isFrameActive;
    private void Zero() => ZeroCalls++;
    private void One(string value) => Argument = value;
}
public sealed class OtherProbe : MonoBehaviour;
