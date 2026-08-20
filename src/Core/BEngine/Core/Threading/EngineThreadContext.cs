using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>
/// Owns the thread affinity for live engine state. Hosts bind this context for their lifetime;
/// standalone serializers and data-only tools can remain unbound.
/// </summary>
public static class EngineThreadContext
{
    private static readonly object BindingSync = new();
    private static int _mainThreadId;
    private static int _bindingCount;
    private static string? _owner;

    public static bool IsBound => Volatile.Read(ref _mainThreadId) != 0;
    public static int MainThreadId => Volatile.Read(ref _mainThreadId);
    public static bool IsMainThread
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var mainThreadId = Volatile.Read(ref _mainThreadId);
            return mainThreadId != 0 && mainThreadId == Environment.CurrentManagedThreadId;
        }
    }

    /// <summary>Bind live BEngine state to the current managed thread until the returned scope is disposed.</summary>
    public static IDisposable BindCurrentThread(string owner = "BEngine")
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        lock (BindingSync)
        {
            if (_mainThreadId != 0 && _mainThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    $"BEngine is already bound to managed thread {_mainThreadId} by '{_owner ?? "BEngine"}'.");
            }

            if (_mainThreadId == 0)
            {
                _owner = string.IsNullOrWhiteSpace(owner) ? "BEngine" : owner.Trim();
                Volatile.Write(ref _mainThreadId, currentThreadId);
            }

            _bindingCount++;
        }

        return new ThreadBinding(currentThreadId);
    }

    /// <summary>
    /// Throw when a bound engine API is called from a worker thread. The check is intentionally a no-op
    /// before a host binds the context so document-only tools and isolated tests remain usable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AssertMainThread([CallerMemberName] string? operation = null)
    {
        var mainThreadId = Volatile.Read(ref _mainThreadId);
        if (mainThreadId == 0 || mainThreadId == Environment.CurrentManagedThreadId) return;
        ThrowWrongThread(operation, mainThreadId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowWrongThread(string? operation, int mainThreadId)
    {
        string owner;
        lock (BindingSync) owner = _owner ?? "BEngine";
        throw new InvalidOperationException(
            $"{operation ?? "BEngine API"} can only be called from the BEngine main thread. " +
            $"'{owner}' is bound to managed thread {mainThreadId}; current thread is " +
            $"{Environment.CurrentManagedThreadId}.");
    }

    private static void Release(int bindingThreadId)
    {
        if (bindingThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "The BEngine main-thread binding must be disposed on the thread that created it.");
        }

        lock (BindingSync)
        {
            if (_mainThreadId != bindingThreadId || _bindingCount <= 0) return;
            if (--_bindingCount != 0) return;
            _owner = null;
            Volatile.Write(ref _mainThreadId, 0);
        }
    }

    private sealed class ThreadBinding(int threadId) : IDisposable
    {
        private int _threadId = threadId;

        public void Dispose()
        {
            var bindingThreadId = Volatile.Read(ref _threadId);
            if (bindingThreadId == 0) return;
            if (bindingThreadId != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException(
                    "The BEngine main-thread binding must be disposed on the thread that created it.");
            }

            if (Interlocked.Exchange(ref _threadId, 0) != 0) Release(bindingThreadId);
        }
    }
}
