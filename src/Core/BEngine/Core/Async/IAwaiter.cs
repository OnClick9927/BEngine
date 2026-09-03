using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>
/// Non-generic awaiter contract used by BEngine task-like types.
/// </summary>
public interface IAwaiter : INotifyCompletion, ICriticalNotifyCompletion
{
    bool IsCompleted { get; }
    void GetResult();
}

/// <summary>
/// Generic awaiter contract used by BEngine task-like types.
/// </summary>
public interface IAwaiter<out TResult> : INotifyCompletion, ICriticalNotifyCompletion
{
    bool IsCompleted { get; }
    TResult GetResult();
}
