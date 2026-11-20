namespace BEngine;

public sealed class ResourceRequest : AsyncOperation
{
    public object? asset { get; private set; }

    internal ResourceRequest(Func<AsyncLoadContext, BTask<object?>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        Start(async cancellationToken =>
        {
            SetProgress(0.05f);
            asset = await loader(LoadContext);
            cancellationToken.ThrowIfCancellationRequested();
            SetProgress(0.95f);
        });
    }

    internal ResourceRequest(Func<BCancellationToken, object?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        Start(async cancellationToken =>
        {
            SetProgress(0.05f);
            asset = await BTask.Run(() => loader(cancellationToken), cancellationToken);
            SetProgress(0.95f);
        });
    }
}
