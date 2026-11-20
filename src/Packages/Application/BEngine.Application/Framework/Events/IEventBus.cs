using System;

namespace BEngine.App.Events;

public interface IEventHandler<in T>
{
    void Handle(T message);
}

public interface IAsyncEventHandler<in T>
{
    BTask HandleAsync(T message, BCancellationToken cancellationToken);
}

/// <summary>A single-game-loop, instance-owned event bus. Null and empty topics identify the
/// default topic. All calls, cancellation and async continuations belong to the same loop.</summary>
public interface IEventBus : IGameService
{
    IDisposable Subscribe<T>(Action<T> handler, string? topic = null);
    IDisposable Subscribe<T>(IEventHandler<T> handler, string? topic = null);
    IDisposable SubscribeAsync<T>(Func<T, BCancellationToken, BTask> handler, string? topic = null);
    IDisposable SubscribeAsync<T>(IAsyncEventHandler<T> handler, string? topic = null);

    /// <summary>Invokes only synchronous subscribers; asynchronous subscribers require PublishAsync.</summary>
    void Publish<T>(T message, string? topic = null);

    /// <summary>Invokes synchronous and asynchronous subscribers sequentially in subscription order.</summary>
    BTask PublishAsync<T>(T message, BCancellationToken cancellationToken = default, string? topic = null);

    /// <summary>Returns the next message on this typed topic. Cancellation and bus disposal remove the waiter.</summary>
    BTask<T> WaitAsync<T>(BCancellationToken cancellationToken = default, string? topic = null);
}
