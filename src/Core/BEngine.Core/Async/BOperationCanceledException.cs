namespace BEngine;

public sealed class BOperationCanceledException : Exception
{
    public BOperationCanceledException(BCancellationToken cancellationToken)
        : base("The BEngine operation was canceled.") => CancellationToken = cancellationToken;

    public BCancellationToken CancellationToken { get; }
}
