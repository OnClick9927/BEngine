using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine;

namespace BEngine.ExampleTests.BTaskRuntime;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            VerifyTypeContracts();
            VerifySynchronousValuePath();
            await VerifyAsyncBuilders().ConfigureAwait(false);
            await VerifyInteroperability().ConfigureAwait(false);
            await VerifyExceptionsAndCancellation().ConfigureAwait(false);
            await VerifyCombinators().ConfigureAwait(false);
            await VerifyCompletionSources().ConfigureAwait(false);
            Console.WriteLine(
                "BTASK_RUNTIME_OK|awaiters,builders,sync-value-path,interop,exceptions,cancellation," +
                "when-all,when-any,completion-source");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BTASK_RUNTIME_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyTypeContracts()
    {
        Require(typeof(BValueTask).IsValueType && typeof(BValueTask<int>).IsValueType,
            "BValueTask variants must remain value types.");
        Require(typeof(BValueTask).IsDefined(typeof(IsReadOnlyAttribute), inherit: false) &&
                typeof(BValueTask<int>).IsDefined(typeof(IsReadOnlyAttribute), inherit: false),
            "BValueTask variants must remain readonly structs.");
        Require(typeof(BTaskAwaiter).GetInterfaces().Contains(typeof(IAwaiter)) &&
                typeof(BTaskAwaiter<int>).GetInterfaces().Contains(typeof(IAwaiter<int>)) &&
                typeof(BValueTaskAwaiter).GetInterfaces().Contains(typeof(IAwaiter)) &&
                typeof(BValueTaskAwaiter<int>).GetInterfaces().Contains(typeof(IAwaiter<int>)),
            "BEngine awaiters do not implement the engine awaiter contracts.");
        Require(typeof(BTask).GetCustomAttribute<AsyncMethodBuilderAttribute>()?.BuilderType ==
                typeof(BTaskMethodBuilder) &&
                typeof(BValueTask).GetCustomAttribute<AsyncMethodBuilderAttribute>()?.BuilderType ==
                typeof(BValueTaskMethodBuilder),
            "Task-like async method builders are not declared on the public types.");
    }

    private static void VerifySynchronousValuePath()
    {
        Require(BValueTask.CompletedTask.IsCompletedSuccessfully,
            "Default BValueTask must represent successful completion.");
        Require(SynchronousValue(41).IsCompletedSuccessfully,
            "A synchronously completed async BValueTask<T> did not stay synchronous.");

        for (var index = 0; index < 1000; index++)
            Require(SynchronousValue(index).GetAwaiter().GetResult() == index + 1,
                "Synchronous BValueTask<T> returned the wrong value.");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        for (var index = 0; index < 10_000; index++)
            checksum += SynchronousValue(index).GetAwaiter().GetResult();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(checksum == 50_005_000, "Synchronous BValueTask<T> allocation probe was optimized away.");
        Require(allocated <= 1_024,
            $"Synchronous BValueTask<T> allocated {allocated} bytes; expected a struct completion path.");
    }

    private static async Task VerifyAsyncBuilders()
    {
        var task = DelayedTaskResult(20);
        Require(!task.IsCompletedSuccessfully, "Delayed BTask<T> unexpectedly completed synchronously.");
        Require(await task.ConfigureAwait(false) == 42, "BTask<T> async builder returned the wrong value.");

        var valueTask = DelayedValueTaskResult(20);
        Require(!valueTask.IsCompletedSuccessfully,
            "Delayed BValueTask<T> unexpectedly completed synchronously.");
        Require(await valueTask.ConfigureAwait(false) == 42,
            "BValueTask<T> async builder returned the wrong value.");

        await DelayedTask().ConfigureAwait(false);
        await DelayedValueTask().ConfigureAwait(false);
        await BTask.Yield().ConfigureAwait(false);
    }

    private static async Task VerifyInteroperability()
    {
        Task dotNetTask = Task.Delay(1);
        BTask engineTask = dotNetTask;
        await engineTask.ConfigureAwait(false);
        await ((Task)engineTask).ConfigureAwait(false);
        await engineTask.AsValueTask().ConfigureAwait(false);

        Task<int> dotNetGeneric = Task.FromResult(37);
        BTask<int> engineGeneric = dotNetGeneric;
        Require(await engineGeneric.ConfigureAwait(false) == 37 &&
                await ((Task<int>)engineGeneric).ConfigureAwait(false) == 37 &&
                await engineGeneric.AsValueTask().ConfigureAwait(false) == 37,
            "BTask<T> Task/ValueTask interoperability failed.");

        BValueTask valueTask = new ValueTask(Task.Delay(1));
        await valueTask.ConfigureAwait(false);
        await valueTask.AsTask().ConfigureAwait(false);

        BValueTask<int> genericValueTask = ValueTask.FromResult(73);
        Require(await genericValueTask.ConfigureAwait(false) == 73 &&
                await genericValueTask.AsTask().ConfigureAwait(false) == 73,
            "BValueTask<T> ValueTask/Task interoperability failed.");
    }

    private static async Task VerifyExceptionsAndCancellation()
    {
        var expected = new AsyncContractException("expected");
        try
        {
            await FaultedTask(expected).ConfigureAwait(false);
            throw new InvalidOperationException("Faulted BTask did not throw.");
        }
        catch (AsyncContractException actual) when (ReferenceEquals(actual, expected))
        {
        }

        try
        {
            await FaultedValueTask(expected).ConfigureAwait(false);
            throw new InvalidOperationException("Faulted BValueTask did not throw.");
        }
        catch (AsyncContractException actual) when (ReferenceEquals(actual, expected))
        {
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await RequireCanceled(BTask.FromCanceled(cancellation.Token).AsTask(), cancellation.Token)
            .ConfigureAwait(false);
        await RequireCanceled(BValueTask.FromCanceled(cancellation.Token).AsTask(), cancellation.Token)
            .ConfigureAwait(false);
        await RequireCanceled(CanceledTask(cancellation.Token).AsTask(), cancellation.Token)
            .ConfigureAwait(false);
        await RequireCanceled(CanceledValueTask(cancellation.Token).AsTask(), cancellation.Token)
            .ConfigureAwait(false);
    }

    private static async Task VerifyCombinators()
    {
        var first = DelayedTaskResult(20, 1);
        var second = DelayedTaskResult(1, 2);
        var completed = await BTask<int>.WhenAny(first, second).ConfigureAwait(false);
        Require(ReferenceEquals(completed, second), "BTask<T>.WhenAny did not return the completed wrapper.");

        var results = await BTask<int>.WhenAll(first, second).ConfigureAwait(false);
        Require(results.SequenceEqual([1, 2]), "BTask<T>.WhenAll did not preserve input order.");

        await BTask.WhenAll(DelayedTask(), BTask.CompletedTask).ConfigureAwait(false);
        var nongenericSecond = BTask.Delay(1);
        var nongenericFirst = BTask.Delay(20);
        Require(ReferenceEquals(
                await BTask.WhenAny(nongenericFirst, nongenericSecond).ConfigureAwait(false),
                nongenericSecond),
            "BTask.WhenAny did not return the completed wrapper.");
    }

    private static async Task VerifyCompletionSources()
    {
        var source = new BTaskCompletionSource<int>();
        var task = source.Task;
        Require(ReferenceEquals(task, source.Task), "Completion source must expose a stable BTask wrapper.");
        Require(source.TrySetResult(19) && !source.TrySetResult(20),
            "Completion source did not reject repeated completion.");
        Require(await task.ConfigureAwait(false) == 19, "Completion source returned the wrong result.");

        var nongeneric = new BTaskCompletionSource();
        Require(nongeneric.TrySetResult() && !nongeneric.TrySetResult(),
            "Non-generic completion source did not reject repeated completion.");
        await nongeneric.Task.ConfigureAwait(false);
    }

    private static async BValueTask<int> SynchronousValue(int value)
    {
        await BValueTask.CompletedTask;
        return value + 1;
    }

    private static async BTask DelayedTask() =>
        await Task.Delay(1).ConfigureAwait(false);

    private static async BTask<int> DelayedTaskResult(int delay, int result = 42)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        return result;
    }

    private static async BValueTask DelayedValueTask() =>
        await Task.Delay(1).ConfigureAwait(false);

    private static async BValueTask<int> DelayedValueTaskResult(int delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        return 42;
    }

    private static async BTask FaultedTask(Exception exception)
    {
        await Task.Yield();
        throw exception;
    }

    private static async BValueTask FaultedValueTask(Exception exception)
    {
        await Task.Yield();
        throw exception;
    }

    private static async BTask CanceledTask(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async BValueTask CanceledValueTask(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task RequireCanceled(Task task, CancellationToken expectedToken)
    {
        try
        {
            await task.ConfigureAwait(false);
            throw new InvalidOperationException("The operation did not report cancellation.");
        }
        catch (OperationCanceledException exception)
        {
            Require(exception.CancellationToken == expectedToken,
                "The operation did not preserve its cancellation token.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class AsyncContractException(string message) : Exception(message);
}
