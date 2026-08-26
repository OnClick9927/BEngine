using System.Globalization;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.UIElements;

public sealed class VisualTreeAsset : ScriptableObject
{
    private UIAssetDocument _document = UIAssetSerializer.CreateDefaultDocument();
    private VisualElement? _sourceRoot;
    private UxmlSerializer.UxmlTemplate? _uxmlTemplate;
    private string _sourcePath = string.Empty;

    public static VisualTreeAsset Create(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new VisualTreeAsset { _document = UIAssetSerializer.ToDocument(root), _sourceRoot = root };
    }

    public static VisualTreeAsset Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.GetExtension(path).Equals(".uxml", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(path);
            var uxmlAsset = new VisualTreeAsset
            {
                _uxml = true,
                _sourcePath = fullPath,
                _uxmlTemplate = UxmlSerializer.LoadTemplate(fullPath)
            };
            uxmlAsset.BindAssetReference(fullPath);
            return uxmlAsset;
        }
        return Document.LoadBObject<UIAssetDocument, VisualTreeAsset>(path);
    }

    private bool _uxml;

    public VisualElement Instantiate() => _uxml
        ? (_uxmlTemplate ??= UxmlSerializer.LoadTemplate(_sourcePath)).Instantiate()
        : UIAssetSerializer.CreateElement(_document.Root);

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.GetExtension(path).Equals(".uxml", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(path);
            var root = _sourceRoot ?? (_uxml && _uxmlTemplate is not null
                ? _uxmlTemplate.Instantiate()
                : UIAssetSerializer.CreateElement(_document.Root));
            UxmlSerializer.Save(root, fullPath);
            _sourcePath = fullPath;
            BindAssetReference(fullPath);
            _uxml = true;
            _uxmlTemplate = UxmlSerializer.LoadTemplate(fullPath);
            return;
        }
        Document.SaveBObject<UIAssetDocument>(this, path);
        _sourcePath = Path.GetFullPath(path);
        BindAssetReference(_sourcePath);
    }

    internal static VisualTreeAsset FromDocument(UIAssetDocument document, string path)
    {
        var fullPath = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        var asset = new VisualTreeAsset { _sourcePath = fullPath, _document = document };
        asset.BindAssetReference(fullPath);
        return asset;
    }

    internal UIAssetDocument CreateDocument() => _sourceRoot is null
        ? _document
        : UIAssetSerializer.ToDocument(_sourceRoot);
}
