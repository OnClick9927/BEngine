using System.Diagnostics.CodeAnalysis;

namespace BEngine;

/// <summary>A pool used on one thread. Disposing it does not invalidate outstanding rentals.</summary>
public interface IObjectPool<T> : IDisposable where T : class
{
    int CountActive { get; }
    int CountInactive { get; }
    int CountAll { get; }
    int MaxSize { get; }
    int MaxActive { get; }
    bool IsDisposed { get; }
    PoolStatistics Statistics { get; }
    T Get();
    bool TryGet([MaybeNullWhen(false)] out T item);
    PooledObject<T> Rent();
    void Release(T item);
    bool TryRelease(T item);
    int Prewarm(int targetInactiveCount);
    int Trim(int targetInactiveCount = 0);
    void Clear();
}
