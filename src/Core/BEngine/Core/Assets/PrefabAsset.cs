using BEngine.Documents;

namespace BEngine;

/// <summary>A reusable, YAML-backed GameObject hierarchy.</summary>
public sealed class PrefabAsset : BAsset
{
    private readonly PrefabDocument _document;
    private string _assetPath;

    internal PrefabDocument Document
    {
        get { MainThreadGuard.Ensure(); return _document; }
    }
    internal PrefabDocument DocumentUnchecked => _document;

    internal PrefabAsset(PrefabDocument document, string sourcePath)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _assetPath = sourcePath ?? string.Empty;
        name = document.Name;
    }

    public Guid assetId
    {
        get { MainThreadGuard.Ensure(); return _document.Id; }
    }
    public string assetPath
    {
        get { MainThreadGuard.Ensure(); return _assetPath; }
        internal set => _assetPath = value;
    }
    public int objectCount
    {
        get { MainThreadGuard.Ensure(); return _document.GameObjects.Count; }
    }
    public int componentCount
    {
        get
        {
            MainThreadGuard.Ensure();
            return _document.GameObjects.Sum(item => item.Components.Count + 1);
        }
    }

    public GameObject Instantiate(Scene scene, Transform? parent = null)
    {
        MainThreadGuard.Ensure();
        return PrefabDocumentOperations.Instantiate(this, scene, parent);
    }
}
