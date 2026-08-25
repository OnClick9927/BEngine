using System.Globalization;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.UIElements;

public sealed class VisualTreeAsset : ScriptableObject
{
    private static readonly Dictionary<string, VisualTreeAsset> UxmlCache =
        new(StringComparer.OrdinalIgnoreCase);
    private UIAssetDocument _document = UIAssetSerializer.CreateDefaultDocument();
    private VisualElement? _sourceRoot;
    private UxmlSerializer.UxmlTemplate? _uxmlTemplate;

    public string assetPath { get; private set; } = string.Empty;

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
            if (UxmlCache.TryGetValue(fullPath, out var cached) &&
                cached._uxmlTemplate is { IsCurrent: true }) return cached;
            var uxmlAsset = new VisualTreeAsset
            {
                assetPath = fullPath,
                _uxml = true,
                _uxmlTemplate = UxmlSerializer.LoadTemplate(fullPath)
            };
            UxmlCache[fullPath] = uxmlAsset;
            return uxmlAsset;
        }
        return Document.LoadBObject<UIAssetDocument, VisualTreeAsset>(path);
    }

    private bool _uxml;

    public VisualElement Instantiate() => _uxml
        ? (_uxmlTemplate ??= UxmlSerializer.LoadTemplate(assetPath)).Instantiate()
        : UIAssetSerializer.CreateElement(_document.Root);

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.GetExtension(path).Equals(".uxml", StringComparison.OrdinalIgnoreCase))
        {
            var root = _sourceRoot ?? (_uxml && _uxmlTemplate is not null
                ? _uxmlTemplate.Instantiate()
                : UIAssetSerializer.CreateElement(_document.Root));
            UxmlSerializer.Save(root, path);
            assetPath = Path.GetFullPath(path);
            _uxml = true;
            _uxmlTemplate = UxmlSerializer.LoadTemplate(assetPath);
            UxmlCache[assetPath] = this;
            return;
        }
        Document.SaveBObject<UIAssetDocument>(this, path);
        assetPath = Path.GetFullPath(path);
    }

    internal static VisualTreeAsset FromDocument(UIAssetDocument document, string path) => new()
    {
        assetPath = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path),
        _document = document
    };

    internal UIAssetDocument CreateDocument() => _sourceRoot is null
        ? _document
        : UIAssetSerializer.ToDocument(_sourceRoot);
}
