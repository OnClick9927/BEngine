using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>
/// Cooperative loop protection used by editor-instrumented project assemblies. A Player has no
/// active execution scope, so injected checkpoints are inert outside the editor.
/// </summary>
public static class LoopExecutionGuard
{
    public const int DefaultIterationLimit = 1000;

    // Scopes protect a synchronous editor callback on its executing thread.
    // Data workers must neither inherit that callback nor change its budget.
    [ThreadStatic]
    private static ExecutionScopeState? _current;
    private static long _nextScopeId;

    internal static event Action<LoopExecutionLimitException>? LimitExceeded;

    /// <summary>Allows a known, data-bounded batch to exceed the editor's default limit.</summary>
    public static ExecutionScope AllowIterations(int iterationCount)
    {
        if (iterationCount < 0) throw new ArgumentOutOfRangeException(nameof(iterationCount));
        var current = _current;
        return current is null || iterationCount <= current.IterationLimit
            ? default
            : BeginScope(current.EntryPoint, iterationCount);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ExecutionScope BeginScope(
        string entryPoint,
        int iterationLimit = DefaultIterationLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        if (iterationLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(iterationLimit), iterationLimit,
                "The loop iteration limit must be positive.");

        var previous = _current;
        var state = new ExecutionScopeState(Interlocked.Increment(ref _nextScopeId), entryPoint, iterationLimit, previous);
        _current = state;
        return new ExecutionScope(state);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Checkpoint(ref Counter counter, string sourcePath, int line)
    {
        var scope = _current;
        if (scope is null) return;

        if (counter.ScopeId != scope.Id)
        {
            counter.ScopeId = scope.Id;
            counter.Iterations = 0;
        }

        counter.Iterations++;
        if (counter.Iterations <= scope.IterationLimit) return;

        var exception = new LoopExecutionLimitException(
            scope.EntryPoint,
            string.IsNullOrWhiteSpace(sourcePath) ? "<unknown>" : sourcePath,
            Math.Max(1, line),
            scope.IterationLimit);
        LimitExceeded?.Invoke(exception);
        throw exception;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct Counter
    {
        internal long ScopeId;
        internal int Iterations;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct ExecutionScope : IDisposable
    {
        private readonly ExecutionScopeState? _state;

        internal ExecutionScope(ExecutionScopeState state) => _state = state;

        public void Dispose()
        {
            if (_state is not null && ReferenceEquals(_current, _state))
                _current = _state.Previous;
        }
    }

    internal sealed class ExecutionScopeState(
        long id,
        string entryPoint,
        int iterationLimit,
        ExecutionScopeState? previous)
    {
        internal long Id { get; } = id;
        internal string EntryPoint { get; } = entryPoint;
        internal int IterationLimit { get; } = iterationLimit;
        internal ExecutionScopeState? Previous { get; } = previous;
    }
}
