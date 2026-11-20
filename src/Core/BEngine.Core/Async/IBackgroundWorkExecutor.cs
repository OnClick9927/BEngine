namespace BEngine;

/// <summary>Optional pure-data execution capability. Engine continuations remain on their owner frame.</summary>
public interface IBackgroundWorkExecutor
{
    bool IsSupported { get; }
    Task<T> Run<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken);
}
