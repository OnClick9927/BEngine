using System.Runtime.CompilerServices;

namespace BEngine;

internal struct BTaskMethodBuilderCore
{
    private IAsyncStateMachine? _stateMachine;

    internal void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    internal void SetStateMachine(IAsyncStateMachine stateMachine) =>
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

    internal void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        ValidateAwaiter<TAwaiter>();
        awaiter.OnCompleted(GetCompletionAction(ref stateMachine));
    }

    internal void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        ValidateAwaiter<TAwaiter>();
        awaiter.UnsafeOnCompleted(GetCompletionAction(ref stateMachine));
    }

    private Action GetCompletionAction<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        if (_stateMachine is null)
        {
            IAsyncStateMachine boxedStateMachine = stateMachine;
            _stateMachine = boxedStateMachine;
            boxedStateMachine.SetStateMachine(boxedStateMachine);
        }
        return _stateMachine.MoveNext;
    }

    private static void ValidateAwaiter<TAwaiter>()
    {
        if (!typeof(IBTaskAwaiter).IsAssignableFrom(typeof(TAwaiter)))
            throw new InvalidOperationException(
                $"BTask async methods can only await BEngine awaiters, not '{typeof(TAwaiter).FullName}'.");
    }
}
