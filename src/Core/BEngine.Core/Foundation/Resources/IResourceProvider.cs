namespace BEngine;

/// <summary>Supplies logical resource content without exposing its storage implementation.</summary>
public interface IResourceProvider
{
    bool TryLoad(string path, string folderName, out ResourceContent content);

    IEnumerable<string> Enumerate(string path, string folderName);
}
