namespace BEngine;

/// <summary>Optional metadata-only ownership lookup for resource reference preparation.</summary>
public interface IResourceReferenceOwner : IResourceProvider
{
    /// <summary>
    /// Reports whether this provider can supply the reference without loading its payload or constructing an object.
    /// A provider without this contract may shadow lower providers, so preparation cannot safely skip it.
    /// </summary>
    bool ContainsReference(string path, string folderName);
}
