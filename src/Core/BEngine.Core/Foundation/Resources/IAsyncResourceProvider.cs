namespace BEngine;

public readonly record struct ResourceLoadResult(bool Handled, object? Asset);

/// <summary>Optional typed cooperative loading. Consulted before synchronous providers for async requests.</summary>
public interface IAsyncResourceProvider : IResourceProvider
{
    BTask<ResourceLoadResult> LoadAsync(string path, string folderName, Type type, AsyncLoadContext context);
}
