using BEngine.Serialization;

namespace BEngine.UIElements;

[EditorIcon("Icons/Assets/AssetMarkup.png")]
public sealed class VisualTreeAsset : ScriptableObject
{
    private UIAssetDocument _document = UIAssetSerializer.CreateDefaultDocument();
    private VisualElement? _sourceRoot;
    private UxmlSerializer.UxmlTemplate? _uxmlTemplate;
    private string _sourcePath = string.Empty;
    private readonly StyleSheet _inlineStyleSheet = new() { name = "inlineStyle" };
    private readonly List<StyleSheet> _ownedStyles = [];

    /// <summary>
    /// Importer-owned representation of the inline style data stored inside this UXML document.
    /// It has no independent source file and is exposed by the editor as a sub-asset.
    /// </summary>
    public StyleSheet inlineStyleSheet => _inlineStyleSheet;
    public override IReadOnlyList<BAsset> GetImportedRepresentations()
    {
        _inlineStyleSheet.Id = Id;
        _inlineStyleSheet.name = "inlineStyle";
        _inlineStyleSheet.BindSubAssetReference(assetPath, Id, 919748196331920512, Id);
        return [_inlineStyleSheet];
    }

    public static VisualTreeAsset Create(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new VisualTreeAsset { _document = UIAssetSerializer.ToDocument(root), _sourceRoot = root };
    }

    internal static VisualTreeAsset FromUxml(string xml, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var template = UxmlSerializer.ParseTemplate(xml, sourcePath.Replace('\\', '/'));
        var asset = new VisualTreeAsset
        {
            _uxml = true,
            _sourcePath = sourcePath.Replace('\\', '/'),
            _uxmlTemplate = template
        };
        asset.BindAssetReference(sourcePath);
        return asset;
    }

    public static VisualTreeAsset Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RejectLegacyExtension(path);
        if (RuntimeServiceAccess.FileSystem.FileExists(path))
            return Document<VisualTreeAsset>.Read(path, LoadAsset).ToAsset();
        if (!RuntimeServiceAccess.PathService.GetExtension(path)
                .Equals(".bxml", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("UI document was not found.", path);
        return new VisualTreeAsset
        {
            _uxml = true,
            name = RuntimeServiceAccess.PathService.GetFileNameWithoutExtension(path),
            _sourcePath = path.Replace('\\', '/'),
            _uxmlTemplate = UxmlSerializer.LoadTemplate(path)
        };
    }

    private static VisualTreeAsset LoadAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RejectLegacyExtension(path);
        if (RuntimeServiceAccess.PathService.GetExtension(path)
            .Equals(".bxml", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = RuntimeServiceAccess.PathService.GetFullPath(path);
            var uxmlAsset = new VisualTreeAsset
            {
                _uxml = true,
                _sourcePath = fullPath,
                _uxmlTemplate = UxmlSerializer.LoadTemplate(fullPath)
            };
            uxmlAsset.BindAssetReference(fullPath);
            return uxmlAsset;
        }
        var sourcePath = RuntimeServiceAccess.PathService.GetFullPath(path);
        var document = BEngine.YamlUtility.Load<UIAssetDocument>(sourcePath);
        UIAssetSerializer.Validate(document);
        return FromDocument(document, sourcePath);
    }

    private static void RejectLegacyExtension(string path)
    {
        var extension = RuntimeServiceAccess.PathService.GetExtension(path);
        if (extension.Equals(".uxml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Legacy UI document extension '.uxml' is not supported; use .bxml.");
    }

    private bool _uxml;

    public VisualElement Instantiate()
    {
        ThrowIfDestroyed();
        return _uxml ? (_uxmlTemplate ??= UxmlSerializer.LoadTemplate(_sourcePath)).Instantiate()
            : UIAssetSerializer.CreateElement(_document.Root);
    }

    protected override object? CaptureClonePayload() => new object[] { _uxml, _sourcePath,
        _uxml ? (_uxmlTemplate ??= UxmlSerializer.LoadTemplate(_sourcePath)).Capture() : YamlUtility.Serialize(CreateDocument()) };

    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        var data = (object[])payload!;
        _uxml = (bool)data[0]; _sourcePath = (string)data[1];
        if (_uxml) _uxmlTemplate = UxmlSerializer.UxmlTemplate.Restore(data[2], cloneObject, _ownedStyles);
        else _document = YamlUtility.Deserialize<UIAssetDocument>((string)data[2]);
    }

    internal VisualTreeAsset CloneAsset()
    {
        ThrowIfDestroyed();
        var clone = new VisualTreeAsset
        {
            name = name, hideFlags = hideFlags, _uxml = _uxml, _sourcePath = _sourcePath
        };
        try
        {
            clone.CopyAssetReferenceFrom(this);
            if (_uxml)
                clone._uxmlTemplate = (_uxmlTemplate ??= UxmlSerializer.LoadTemplate(_sourcePath))
                    .Clone(clone._ownedStyles);
            else
                clone._document = YamlUtility.Deserialize<UIAssetDocument>(YamlUtility.Serialize(CreateDocument()));
            return clone;
        }
        catch { Destroy(clone); throw; }
    }

    protected override void ReleaseAssetResources()
    {
        Destroy(_inlineStyleSheet);
        foreach (var sheet in _ownedStyles) Destroy(sheet);
        _ownedStyles.Clear();
        _sourceRoot = null;
        _uxmlTemplate = null;
        base.ReleaseAssetResources();
    }

    internal override void MarkOwnedAssetsRuntimeOnly()
    {
        _inlineStyleSheet.IsRuntimeOnly = true;
        foreach (var sheet in _ownedStyles) sheet.IsRuntimeOnly = true;
    }

    public void Save(string path)
    {
        Document<VisualTreeAsset>.FromAsset(this).Write(path,
            static (asset, destination) => asset.SaveAsset(destination));
    }

    private void SaveAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (RuntimeServiceAccess.PathService.GetExtension(path)
            .Equals(".bxml", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = RuntimeServiceAccess.PathService.GetFullPath(path);
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
        var document = CreateDocument();
        UIAssetSerializer.Validate(document);
        _sourcePath = RuntimeServiceAccess.PathService.GetFullPath(path);
        BEngine.YamlUtility.Save(document, _sourcePath);
        _document = document;
        BindAssetReference(_sourcePath);
    }

    internal static VisualTreeAsset FromDocument(UIAssetDocument document, string path)
    {
        UIAssetSerializer.Validate(document);
        var fullPath = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : RuntimeServiceAccess.PathService.GetFullPath(path);
        var asset = new VisualTreeAsset { _sourcePath = fullPath, _document = document };
        asset.BindAssetReference(fullPath);
        return asset;
    }

    internal UIAssetDocument CreateDocument() => _sourceRoot is null
        ? _document
        : UIAssetSerializer.ToDocument(_sourceRoot);
}
