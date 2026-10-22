using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace BEngine.Testing;

public static class TestExecutor
{
    public static async Task<TestRunResult> RunAsync(
        IEnumerable<TestCase> tests,
        TestRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tests);
        options ??= new TestRunOptions();
        var selected = tests.ToArray();
        var started = DateTimeOffset.UtcNow;
        if (options.CollectCoverage) CodeCoverage.BeginSession();
        var results = new List<TestResult>(selected.Length);
        foreach (var group in selected.GroupBy(test => test.FixtureType))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var test in group) Add(Canceled(test));
                continue;
            }
            await RunFixtureAsync(group.Key, group.ToArray(), options, cancellationToken, Add)
                .ConfigureAwait(false);
        }
        return new TestRunResult(started, DateTimeOffset.UtcNow, results,
            options.CollectCoverage ? CodeCoverage.Snapshot() : null);

        void Add(TestResult result)
        {
            results.Add(result);
            options.ResultReported?.Invoke(result);
        }
    }

    private static async Task RunFixtureAsync(
        Type fixtureType,
        IReadOnlyList<TestCase> tests,
        TestRunOptions options,
        CancellationToken cancellationToken,
        Action<TestResult> add)
    {
        var instance = tests.All(test => test.Method.IsStatic)
            ? null
            : Activator.CreateInstance(fixtureType, nonPublic: true);
        var oneTimeSetUp = LifecycleMethods<OneTimeSetUpAttribute>(fixtureType, baseFirst: true);
        var oneTimeTearDown = LifecycleMethods<OneTimeTearDownAttribute>(fixtureType, baseFirst: false);
        Exception? fixtureFailure = null;
        try { await InvokeAllAsync(oneTimeSetUp, instance, null, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { fixtureFailure = Unwrap(exception); }
        try
        {
            foreach (var test in tests)
            {
                if (fixtureFailure is not null)
                {
                    add(Failure(test, TestStatus.Failed, TimeSpan.Zero,
                        "One-time setup failed: " + fixtureFailure.Message, fixtureFailure));
                    continue;
                }
                add(await RunOneAsync(test, instance, options, cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            try { await InvokeAllAsync(oneTimeTearDown, instance, null, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception)
            {
                if (tests.Count > 0)
                    add(Failure(tests[^1], TestStatus.Failed, TimeSpan.Zero,
                        "One-time teardown failed: " + Unwrap(exception).Message, Unwrap(exception)));
            }
            if (instance is IDisposable disposable) disposable.Dispose();
            if (instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<TestResult> RunOneAsync(
        TestCase test,
        object? instance,
        TestRunOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(test.IgnoreReason))
            return new(test.Id, test.DisplayName, test.Mode, TestStatus.Skipped, 0,
                test.IgnoreReason, string.Empty, test.SourceFile, test.SourceLine);
        if (cancellationToken.IsCancellationRequested) return Canceled(test);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(test.TimeoutMilliseconds);
        Scene? ownedScene = null;
        SceneRuntime? ownedRuntime = null;
        var scene = options.PlayerScene;
        if (test.Mode == TestMode.Play)
        {
            ownedScene = new Scene($"Test: {test.DisplayName}");
            ownedRuntime = new SceneRuntime(ownedScene);
            ownedRuntime.Start();
            scene = ownedScene;
        }
        var context = new TestContext(test.Mode, timeout.Token, scene, ownedRuntime);
        var watch = Stopwatch.StartNew();
        Exception? failure = null;
        var timedOut = false;
        try
        {
            using var scope = TestContext.Push(context);
            var setup = LifecycleMethods<SetUpAttribute>(test.FixtureType, baseFirst: true);
            var teardown = LifecycleMethods<TearDownAttribute>(test.FixtureType, baseFirst: false);
            try
            {
                await WithTimeout(InvokeAllAsync(setup, instance, context, timeout.Token), timeout.Token)
                    .ConfigureAwait(false);
                await WithTimeout(InvokeAsync(test.Method, instance, context, timeout.Token), timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                failure = timedOut
                    ? new TimeoutException($"Test exceeded {test.TimeoutMilliseconds} ms.")
                    : new OperationCanceledException(cancellationToken);
            }
            catch (Exception exception) { failure = Unwrap(exception); }
            finally
            {
                try { await InvokeAllAsync(teardown, instance, context, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) { failure ??= Unwrap(exception); }
            }
        }
        finally
        {
            watch.Stop();
            if (ownedRuntime is not null)
            {
                if (ownedRuntime.IsRunning) ownedRuntime.Stop();
                ownedScene?.Dispose();
            }
        }

        if (failure is null)
            return new(test.Id, test.DisplayName, test.Mode, TestStatus.Passed,
                watch.Elapsed.TotalMilliseconds, string.Empty, string.Empty, test.SourceFile, test.SourceLine);
        var status = cancellationToken.IsCancellationRequested ? TestStatus.Canceled :
            timedOut ? TestStatus.TimedOut : TestStatus.Failed;
        return Failure(test, status, watch.Elapsed, failure.Message, failure);
    }

    private static async Task InvokeAllAsync(
        IEnumerable<MethodInfo> methods,
        object? instance,
        TestContext? context,
        CancellationToken cancellationToken)
    {
        foreach (var method in methods)
            await InvokeAsync(method, method.IsStatic ? null : instance, context, cancellationToken)
                .ConfigureAwait(false);
    }

    private static async Task InvokeAsync(
        MethodInfo method,
        object? instance,
        TestContext? context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object? value;
        try { value = method.Invoke(method.IsStatic ? null : instance, null); }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        switch (value)
        {
            case null:
                return;
            case Task task:
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            case ValueTask valueTask:
                await valueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            case BTask bTask:
                while (!bTask.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
                bTask.GetAwaiter().GetResult();
                return;
            case IEnumerator enumerator:
                while (enumerator.MoveNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (context?.Mode == TestMode.Play)
                        context.Tick(enumerator.Current is int frames ? Math.Max(1, frames) : 1);
                    else await Task.Yield();
                }
                return;
            default:
                throw new InvalidOperationException(
                    $"Test lifecycle method '{method.DeclaringType?.FullName}.{method.Name}' returned unsupported type '{value.GetType().FullName}'.");
        }
    }

    private static MethodInfo[] LifecycleMethods<TAttribute>(Type fixtureType, bool baseFirst)
        where TAttribute : Attribute
    {
        var hierarchy = new List<Type>();
        for (var current = fixtureType; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Add(current);
        if (baseFirst) hierarchy.Reverse();
        return hierarchy.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.IsDefined(typeof(TAttribute), inherit: false))
                .OrderBy(method => method.MetadataToken))
            .ToArray();
    }

    private static async Task WithTimeout(Task operation, CancellationToken cancellationToken) =>
        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);

    private static TestResult Failure(
        TestCase test,
        TestStatus status,
        TimeSpan duration,
        string message,
        Exception exception) => new(test.Id, test.DisplayName, test.Mode, status,
        duration.TotalMilliseconds, message, exception.ToString(), test.SourceFile, test.SourceLine);

    private static TestResult Canceled(TestCase test) => new(test.Id, test.DisplayName, test.Mode,
        TestStatus.Canceled, 0, "Canceled.", string.Empty, test.SourceFile, test.SourceLine);

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException or AggregateException)
            exception = exception is TargetInvocationException target && target.InnerException is not null
                ? target.InnerException
                : ((AggregateException)exception).Flatten().InnerExceptions.First();
        return exception;
    }
}
