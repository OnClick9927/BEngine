namespace BEngine;

public sealed class ResourceRequest : AsyncOperation
{
    public object? asset { get; private set; }

    internal ResourceRequest(Func<CancellationToken, object?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        Start(async cancellationToken =>
        {
            SetProgress(0.05f);
            asset = await Task.Run(() => loader(cancellationToken), cancellationToken).ConfigureAwait(false);
            SetProgress(0.95f);
        });
    }
}
