using BEngine.Documents;

namespace BEngine;

/// <summary>A reusable, YAML-backed GameObject hierarchy.</summary>
public sealed class PrefabAsset : BAsset
{
    private readonly PrefabDocument _document;

    internal PrefabDocument Document
    {
        get { return _document; }
    }
    internal PrefabDocument DocumentUnchecked => _document;

    internal PrefabAsset(PrefabDocument document, string sourcePath)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        assetPath = sourcePath ?? string.Empty;
        name = document.Name;
    }

    public Guid assetId
    {
        get { return _document.Id; }
    }
    public int objectCount
    {
        get { return _document.GameObjects.Count; }
    }
    public int componentCount
    {
        get
        {
            return _document.GameObjects.Sum(item => item.Components.Count + 1);
        }
    }

    public GameObject Instantiate(Scene scene, Transform? parent = null)
    {
        return PrefabDocumentOperations.Instantiate(this, scene, parent);
    }
}
