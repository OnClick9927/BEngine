using System.Collections.Concurrent;
using BEngine.Editor;

namespace BEngine.ExampleTests.ThreadingArchitecture;

internal static class EditorTaskSchedulerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static Task RunAsync()
    {
        VerifyDefaultWorkerReservation();
        VerifyTasksRunConcurrently();
        VerifyPriorityOrder();
        VerifyCancellation();
        VerifyFaultIsolation();
        VerifyMainThreadContinuation();
        VerifyAuxiliaryWorkDoesNotBlockFrames();
        VerifySchedulePostDisposeRace();
        VerifyWorkerDisposeRejected();
        VerifyDisposeContract();
        return Task.CompletedTask;
    }

    private static void VerifyDefaultWorkerReservation()
    {
        using var scheduler = new EditorTaskScheduler();
        var expected = Math.Clamp(Environment.ProcessorCount - 2, 1, 8);
        TestAssert.Require(scheduler.WorkerCount == expected,
            $"The default Editor worker pool did not reserve two cores for the main/render path. " +
            $"Expected {expected}, received {scheduler.WorkerCount}.");
        TestAssert.Require(scheduler.IsMainThread,
            "A newly-created Editor scheduler did not capture its owner thread.");
    }

    private static void VerifyTasksRunConcurrently()
    {
        using var scheduler = new EditorTaskScheduler(workerCount: 2);
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var workerIds = new ConcurrentQueue<int>();

        Task Schedule(string name) => scheduler.ScheduleAsync(name, token =>
        {
            workerIds.Enqueue(Environment.CurrentManagedThreadId);
            entered.Signal();
            release.Wait(token);
        });

        var first = Schedule("Concurrency first");
        var second = Schedule("Concurrency second");
        try
        {
            TestAssert.Require(entered.Wait(Timeout),
                "Two Editor tasks did not enter concurrently with two configured workers.");
            TestAssert.Require(!first.IsCompleted && !second.IsCompleted,
                "A concurrency probe completed before its shared release gate opened.");
        }
        finally
        {
            release.Set();
        }

        Task.WhenAll(first, second).GetAwaiter().GetResult();
        var distinctWorkers = workerIds.Distinct().ToArray();
        TestAssert.Require(distinctWorkers.Length == 2 &&
                           distinctWorkers.All(id => id != Environment.CurrentManagedThreadId),
            "Editor background work did not use two worker threads distinct from the owner thread.");
    }

    private static void VerifyPriorityOrder()
    {
        using var scheduler = new EditorTaskScheduler(workerCount: 1);
        using var blockerEntered = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        var order = new ConcurrentQueue<string>();
        var blocker = scheduler.ScheduleAsync("Priority blocker", token =>
        {
            blockerEntered.Set();
            releaseBlocker.Wait(token);
        }, EditorTaskPriority.Background);
        TestAssert.Require(blockerEntered.Wait(Timeout),
            "The priority test could not occupy its single worker.");

        var background = scheduler.ScheduleAsync("Background", _ => order.Enqueue("Background"),
            EditorTaskPriority.Background);
        var normal = scheduler.ScheduleAsync("Normal", _ => order.Enqueue("Normal"),
            EditorTaskPriority.Normal);
        var critical = scheduler.ScheduleAsync("Critical", _ => order.Enqueue("Critical"),
            EditorTaskPriority.Critical);
        releaseBlocker.Set();
        Task.WhenAll(blocker, background, normal, critical).GetAwaiter().GetResult();

        TestAssert.Require(order.SequenceEqual(["Critical", "Normal", "Background"]),
            $"Editor task priority order was wrong: {string.Join(", ", order)}.");
    }

    private static void VerifyCancellation()
    {
        using var scheduler = new EditorTaskScheduler(workerCount: 1);
        using var blockerEntered = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        var blocker = scheduler.ScheduleAsync("Cancellation blocker", token =>
        {
            blockerEntered.Set();
            releaseBlocker.Wait(token);
        });
        TestAssert.Require(blockerEntered.Wait(Timeout),
            "The cancellation test could not occupy its worker.");

        using var queuedCancellation = new CancellationTokenSource();
        var queuedRan = 0;
        var queued = scheduler.ScheduleAsync("Canceled while queued", _ =>
        {
            Interlocked.Increment(ref queuedRan);
        },
            EditorTaskPriority.Normal, queuedCancellation.Token);
        queuedCancellation.Cancel();
        releaseBlocker.Set();
        blocker.GetAwaiter().GetResult();
        RequireCanceled(queued, "A task canceled while queued did not complete as canceled.");
        TestAssert.Require(queuedRan == 0,
            "A task canceled while queued still executed its operation.");

        using var runningCancellation = new CancellationTokenSource();
        using var runningEntered = new ManualResetEventSlim();
        var running = scheduler.ScheduleAsync("Canceled while running", async token =>
        {
            runningEntered.Set();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
        }, EditorTaskPriority.Normal, runningCancellation.Token);
        TestAssert.Require(runningEntered.Wait(Timeout),
            "The running-cancellation task never entered its operation.");
        runningCancellation.Cancel();
        RequireCanceled(running, "A running task did not observe its cancellation token.");
    }

    private static void VerifyFaultIsolation()
    {
        using var scheduler = new EditorTaskScheduler(workerCount: 1);
        const string sentinel = "EDITOR_TASK_FAULT_SENTINEL";
        var healthyCalls = 0;
        var logs = new List<LogEntry>();
        BEngine.Debug.MessageLogged += Capture;
        try
        {
            var faulting = scheduler.ScheduleAsync("Faulting work", _ =>
            {
                throw new InvalidOperationException(sentinel);
            });
            var healthy = scheduler.ScheduleAsync("Healthy work", _ =>
            {
                Interlocked.Increment(ref healthyCalls);
            });

            var fault = CaptureTaskException(faulting);
            healthy.GetAwaiter().GetResult();
            TestAssert.Require(fault is InvalidOperationException && fault.Message == sentinel,
                "A background task did not preserve its original exception.");
            TestAssert.Require(healthyCalls == 1,
                "A faulting background task stopped the worker before a healthy task ran.");
            TestAssert.Require(SpinWait.SpinUntil(() => scheduler.PendingMainThreadCount > 0, Timeout),
                "A background failure was not posted for main-thread reporting.");

            var callbackCalls = 0;
            scheduler.Post(() => throw new InvalidOperationException("POST_FAULT_SENTINEL"), "Faulting callback");
            scheduler.Post(() => callbackCalls++, "Healthy callback");
            scheduler.PumpMainThread(Timeout);
            TestAssert.Require(callbackCalls == 1,
                "A faulting main-thread continuation stopped the following continuation.");
            TestAssert.Require(logs.Any(entry => entry.Type == LogType.Error &&
                                                entry.Message.Contains(sentinel, StringComparison.Ordinal)),
                "The main thread did not report a background task failure to the Editor log.");
        }
        finally
        {
            BEngine.Debug.MessageLogged -= Capture;
        }

        void Capture(LogEntry entry) => logs.Add(entry);
    }

    private static void VerifyMainThreadContinuation()
    {
        using var scheduler = new EditorTaskScheduler(workerCount: 1);
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var workerThreadId = 0;
        var continuationThreadId = 0;
        var work = scheduler.ScheduleAsync("Produce main-thread continuation", _ =>
        {
            workerThreadId = Environment.CurrentManagedThreadId;
            scheduler.Post(() => continuationThreadId = Environment.CurrentManagedThreadId,
                "Apply background result");
        });
        work.GetAwaiter().GetResult();

        TestAssert.Require(workerThreadId != 0 && workerThreadId != ownerThreadId,
            "Scheduled work ran on the Editor owner thread.");
        TestAssert.Require(continuationThreadId == 0 && scheduler.PendingMainThreadCount == 1,
            "A posted continuation ran before the owner explicitly pumped it.");
        TestAssert.Require(scheduler.PumpMainThread(Timeout) == 1 &&
                           continuationThreadId == ownerThreadId,
            "A background result continuation did not run on the Editor owner thread.");

        var workerPumpError = CaptureWorkerException(() => scheduler.PumpMainThread(Timeout));
        TestAssert.Require(workerPumpError is InvalidOperationException,
            "A worker thread was allowed to pump Editor main-thread callbacks.");
    }

    private static void VerifyAuxiliaryWorkDoesNotBlockFrames()
    {
        using var scheduler = new EditorTaskScheduler(workerCount: 1);
        using var auxiliaryEntered = new ManualResetEventSlim();
        using var releaseAuxiliary = new ManualResetEventSlim();
        var auxiliary = scheduler.ScheduleAsync("Long auxiliary operation", token =>
        {
            auxiliaryEntered.Set();
            releaseAuxiliary.Wait(token);
        }, EditorTaskPriority.Background);
        TestAssert.Require(auxiliaryEntered.Wait(Timeout),
            "The long auxiliary task never entered its worker.");

        var scene = new Scene("Frame isolation");
        var runtime = new SceneRuntime(scene);
        try
        {
            runtime.Start();
            var before = Time.frameCount;
            runtime.Tick(Fix64.Parse("0.016"));
            TestAssert.Require(Time.frameCount == before + 1 && !auxiliary.IsCompleted,
                "The runtime frame waited for unrelated Editor auxiliary work.");
        }
        finally
        {
            runtime.Stop();
            scene.Dispose();
            releaseAuxiliary.Set();
        }
        auxiliary.GetAwaiter().GetResult();
    }

    private static void VerifyDisposeContract()
    {
        var scheduler = new EditorTaskScheduler(workerCount: 1);
        using var runningEntered = new ManualResetEventSlim();
        var running = scheduler.ScheduleAsync("Dispose running task", token =>
        {
            runningEntered.Set();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
        });
        TestAssert.Require(runningEntered.Wait(Timeout),
            "The disposal test task never entered its worker.");
        var queued = scheduler.ScheduleAsync("Dispose queued task", _ => { });
        scheduler.Dispose();

        RequireCanceled(running, "Disposing the scheduler did not cancel running work.");
        RequireCanceled(queued, "Disposing the scheduler did not cancel queued work.");
        TestAssert.Require(CaptureException(() => scheduler.Post(() => { })) is ObjectDisposedException &&
                           CaptureException(() => scheduler.ScheduleAsync("After dispose", _ => { }))
                               is ObjectDisposedException,
            "A disposed Editor scheduler accepted new work.");
    }

    private static void VerifySchedulePostDisposeRace()
    {
        var scheduler = new EditorTaskScheduler(workerCount: 2);
        using var producersReady = new CountdownEvent(4);
        using var beginRace = new ManualResetEventSlim();
        var scheduled = new ConcurrentQueue<Task>();
        var callbackCalls = 0;
        var acceptedCallbacks = 0;

        var producers = Enumerable.Range(0, 4).Select(producerIndex => Task.Run(() =>
        {
            Submit(producerIndex, -1);
            producersReady.Signal();
            beginRace.Wait();
            for (var index = 0; index < 64; index++) Submit(producerIndex, index);
        })).ToArray();

        TestAssert.Require(producersReady.Wait(Timeout),
            "Concurrent scheduler producers did not reach the disposal race.");
        beginRace.Set();
        scheduler.Dispose();
        Task.WaitAll(producers);

        var returnedTasks = scheduled.ToArray();
        TestAssert.Require(returnedTasks.Length >= producers.Length &&
                           returnedTasks.All(task => task.IsCompleted),
            "ScheduleAsync raced with Dispose and left a returned Task incomplete.");
        TestAssert.Require(returnedTasks.All(task => task.IsCanceled),
            "A shutdown-only scheduler operation completed without observing Dispose cancellation.");
        TestAssert.Require(acceptedCallbacks >= producers.Length && callbackCalls == 0 &&
                           scheduler.PendingMainThreadCount == 0 &&
                           scheduler.PendingBackgroundCount == 0 &&
                           scheduler.PumpMainThread(Timeout) == 0,
            "Post raced with Dispose and left a callback or pending operation behind.");

        void Submit(int producerIndex, int itemIndex)
        {
            try
            {
                scheduled.Enqueue(scheduler.ScheduleAsync(
                    $"Dispose race {producerIndex}:{itemIndex}", token =>
                    {
                        token.WaitHandle.WaitOne();
                        token.ThrowIfCancellationRequested();
                    }));
            }
            catch (ObjectDisposedException) { }

            try
            {
                scheduler.Post(() => Interlocked.Increment(ref callbackCalls),
                    $"Dispose race callback {producerIndex}:{itemIndex}");
                Interlocked.Increment(ref acceptedCallbacks);
            }
            catch (ObjectDisposedException) { }
        }
    }

    private static void VerifyWorkerDisposeRejected()
    {
        var scheduler = new EditorTaskScheduler(workerCount: 1);
        try
        {
            var workerFailure = scheduler.ScheduleAsync<Exception?>(
                "Reject worker disposal", _ => CaptureException(scheduler.Dispose))
                .GetAwaiter().GetResult();
            TestAssert.Require(workerFailure is InvalidOperationException &&
                               workerFailure.Message.Contains("main thread", StringComparison.OrdinalIgnoreCase),
                "A worker Dispose call was not rejected with a clear main-thread ownership error.");
            scheduler.ScheduleAsync("Usable after rejected disposal", _ => { })
                .GetAwaiter().GetResult();
        }
        finally
        {
            scheduler.Dispose();
        }
    }

    private static void RequireCanceled(Task task, string message)
    {
        var exception = CaptureTaskException(task);
        TestAssert.Require(task.IsCanceled && exception is OperationCanceledException, message);
    }

    private static Exception? CaptureTaskException(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? CaptureWorkerException(Action action) => Task.Run(() => CaptureException(action))
        .GetAwaiter().GetResult();

    private static Exception? CaptureException(Action action)
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
    }
}
