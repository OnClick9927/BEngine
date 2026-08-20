using System.Collections;

namespace BEngine;

internal sealed class ThreadGuardedEnumerator<T> : IEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    internal ThreadGuardedEnumerator(IEnumerator<T> inner) => _inner = inner;

    public T Current
    {
        get { MainThreadGuard.Ensure(); return _inner.Current; }
    }

    object? IEnumerator.Current => Current;

    public bool MoveNext()
    {
        MainThreadGuard.Ensure();
        return _inner.MoveNext();
    }

    public void Reset()
    {
        MainThreadGuard.Ensure();
        _inner.Reset();
    }

    public void Dispose() => _inner.Dispose();
}
