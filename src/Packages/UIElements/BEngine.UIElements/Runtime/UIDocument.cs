using System.Globalization;
using BEngine.Serialization;

namespace BEngine.UIElements;

[AddComponentMenu("UI Toolkit/UI Document")]
[DisallowMultipleComponent]
public sealed class UIDocument : MonoBehaviour
{
    private static readonly Material DefaultMaterial = new(Shader.Find("BEngine/UIElements/Solid"));
    private VisualElement? _root;
    private string _loadedSource = string.Empty;
    private ulong _sortingLayer = SortingLayer.Ui;
    private Material _material = DefaultMaterial;

    public string sourceAsset { get; set; } = string.Empty;
    public int sortingOrder { get; set; }
    public ulong sortingLayer
    {
        get => _sortingLayer;
        set
        {
            SortingLayer.Validate(value);
            if (!SortingLayer.IsUi(value))
                throw new ArgumentOutOfRangeException(nameof(value), "UI documents must use one of the five UI layers.");
            _sortingLayer = value;
        }
    }
    public Material material
    {
        get => _material;
        set => _material = value ?? throw new ArgumentNullException(nameof(value));
    }
    public string atlas { get; set; } = string.Empty;
    public bool interactable { get; set; } = true;
    public PanelScaleMode scaleMode { get; set; } = PanelScaleMode.ScaleWithScreenSize;
    public int referenceWidth { get; set; } = 1280;
    public int referenceHeight { get; set; } = 720;
    public VisualElement rootVisualElement => EnsureRoot();

    public override void OnEnable() => Reload();

    public override void Update()
    {
        if (!interactable || !Input.GetMouseButtonDown(0)) return;
        var scene = gameObject.scene;
        if (scene is null) return;
        var documents = scene.QueryComponents<UIDocument>().ToArray()
            .Where(item => item.gameObject.activeInHierarchy && item.enabled && item.interactable)
            .OrderByDescending(item => item.sortingOrder)
            .ToArray();
        if (!ReferenceEquals(documents.FirstOrDefault(), this)) return;

        var position = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        _ = DispatchPointerDown(position, Screen.width, Screen.height);
    }

    public void Reload()
    {
        _loadedSource = sourceAsset;
        if (string.IsNullOrWhiteSpace(sourceAsset))
        {
            _root = CreateFallbackRoot();
            return;
        }

        try
        {
            var filePath = ResolveSourcePath(sourceAsset);
            _root = VisualTreeAsset.Load(File.Exists(filePath) ? filePath : sourceAsset).Instantiate();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Debug.LogError($"Could not load UI document '{sourceAsset}': " +
                           $"{exception.GetType().FullName}: {exception}");
            _root = CreateFallbackRoot();
        }
    }

    public void SetVisualTree(VisualElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _loadedSource = sourceAsset;
    }

    public bool DispatchPointerDown(Vector2 position, int viewportWidth, int viewportHeight)
    {
        if (!interactable) return false;
        var renderList = UIRenderListBuilder.Build(
            EnsureRoot(), viewportWidth, viewportHeight, ResolveScale(viewportWidth, viewportHeight));
        for (var target = renderList.Pick(position); target is not null; target = target.parent)
        {
            if (target is not Button button) continue;
            button.Click();
            return true;
        }
        return false;
    }

    internal Fix64 ResolveScale(int width, int height)
    {
        if (scaleMode == PanelScaleMode.ConstantPixelSize) return Fix64.One;
        var widthScale = (Fix64)Math.Max(1, width) / Math.Max(1, referenceWidth);
        var heightScale = (Fix64)Math.Max(1, height) / Math.Max(1, referenceHeight);
        return (widthScale + heightScale) / 2;
    }

    private VisualElement EnsureRoot()
    {
        if (_root is null || !string.Equals(_loadedSource, sourceAsset, StringComparison.Ordinal)) Reload();
        return _root!;
    }

    private static VisualElement CreateFallbackRoot()
    {
        var root = new VisualElement { name = "Root" };
        root.style.flexGrow = 1;
        return root;
    }

    private static string ResolveSourcePath(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var dataPath = Path.GetFullPath(Application.dataPath);
            var projectRoot = Path.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(dataPath)?.FullName ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }
        return Path.GetFullPath(Path.Combine(Application.dataPath, normalized));
    }
}
