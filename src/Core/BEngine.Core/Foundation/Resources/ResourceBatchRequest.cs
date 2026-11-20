namespace BEngine;

/// <summary>Owns resource leases until disposed; cached/shared assets are never destroyed by a batch.</summary>
public sealed class ResourceBatchRequest<T> : AsyncLoadBatch<ResourceHandle<T>> where T : class
{
    internal ResourceBatchRequest(string[] paths, int maximumConcurrency,
        Func<T, AsyncLoadContext, BValueTask>? prepare, BCancellationToken cancellationToken)
        : base(paths.Length, (index, context) => LoadAsync(paths[index], prepare, context),
            maximumConcurrency, static handle => handle.Dispose(), cancellationToken) { }

    public T GetAsset(int index) => GetResult(index).asset;

    private static async BValueTask<ResourceHandle<T>> LoadAsync(string path,
        Func<T, AsyncLoadContext, BValueTask>? prepare, AsyncLoadContext context)
    {
        var loading = new AsyncLoadContext(context.cancellationToken,
            value => context.ReportProgress(prepare is null ? value : .5f * value)) { priority = context.priority };
        var asset = await ResourceLoader.LoadAsync(path, typeof(T), "Resources", loading) as T ??
            throw new FileNotFoundException($"Resource '{path}' of type {typeof(T).FullName} was not found.");
        context.cancellationToken.ThrowIfCancellationRequested();
        var handle = ResourceLeaseRegistry.AcquireLoaded(path, "Resources", asset);
        try
        {
            if (prepare is not null)
                await prepare(handle.asset, new AsyncLoadContext(context.cancellationToken,
                    value => context.ReportProgress(.5f + .5f * value)) { priority = context.priority });
            context.cancellationToken.ThrowIfCancellationRequested();
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
}
