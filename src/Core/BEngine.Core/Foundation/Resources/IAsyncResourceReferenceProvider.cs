namespace BEngine;

/// <summary>Optional preparation of provider-owned reference data before synchronous object binding.</summary>
public interface IAsyncResourceReferenceProvider : IResourceReferenceOwner
{
    /// <returns>True when this provider owns and has prepared the reference.</returns>
    BTask<bool> PrepareReferenceAsync(string path, string folderName, AsyncLoadContext context);
}
