namespace BEngine;

/// <summary>
/// Optionally supplies constructed engine objects that are not BAsset instances, such as imported Sprites.
/// </summary>
public interface IResourceObjectProvider
{
    bool TryLoadObject(string path, string folderName, Type objectType, out BObject value);
}
