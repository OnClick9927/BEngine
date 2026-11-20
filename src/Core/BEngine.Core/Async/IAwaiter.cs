using System.Runtime.CompilerServices;

namespace BEngine;

public interface IAwaiter : INotifyCompletion, ICriticalNotifyCompletion
{
    bool IsCompleted { get; }
    void GetResult();
}

public interface IAwaiter<out TResult> : INotifyCompletion, ICriticalNotifyCompletion
{
    bool IsCompleted { get; }
    TResult GetResult();
}

internal interface IBTaskAwaiter
{
}
