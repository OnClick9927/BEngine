using System.Text.Json;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

internal sealed record GameViewResolution(
    string Id,
    string Name,
    int Width,
    int Height,
    string Category,
    bool IsBuiltIn)
{
    public bool IsFreeAspect => Width <= 0 || Height <= 0;
    public string SizeLabel => IsFreeAspect ? "Free Aspect" : $"{Width} x {Height}";
    public string MenuLabel => IsFreeAspect ? Name : $"{Name} ({SizeLabel})";
}

internal sealed class GameViewResolutionSettings
{
    internal const string SelectedPreferenceKey = "BEngine.GameView.Resolution.Selected";
    internal const string CustomPreferenceKey = "BEngine.GameView.Resolution.Custom";
    internal const int MaximumDimension = 16384;

    private static readonly GameViewResolution[] BuiltInOptions =
    [
        new("free", "Free Aspect", 0, 0, string.Empty, true),
        new("standalone-1280x720", "HD", 1280, 720, "Standalone", true),
        new("standalone-1366x768", "Laptop", 1366, 768, "Standalone", true),
        new("standalone-1920x1080", "Full HD", 1920, 1080, "Standalone", true),
        new("standalone-2560x1440", "QHD", 2560, 1440, "Standalone", true),
        new("standalone-3840x2160", "4K UHD", 3840, 2160, "Standalone", true),
        new("standalone-800x600", "SVGA 4:3", 800, 600, "Standalone", true),
        new("standalone-1024x768", "XGA 4:3", 1024, 768, "Standalone", true),
        new("standalone-1920x1200", "WUXGA 16:10", 1920, 1200, "Standalone", true),
        new("mobile-750x1334", "Phone Portrait", 750, 1334, "Mobile", true),
        new("mobile-1080x1920", "Full HD Portrait", 1080, 1920, "Mobile", true),
        new("mobile-1920x1080", "Full HD Landscape", 1920, 1080, "Mobile", true)
    ];

    private readonly List<GameViewResolution> _customOptions;

    public GameViewResolutionSettings()
    {
        _customOptions = LoadCustomOptions();
        var selectedId = EditorPrefs.GetString(SelectedPreferenceKey, BuiltInOptions[0].Id);
        selected = Options.FirstOrDefault(option => option.Id.Equals(selectedId, StringComparison.Ordinal)) ??
                   BuiltInOptions[0];
    }

    public GameViewResolution selected { get; private set; }
    public IReadOnlyList<GameViewResolution> builtInOptions => BuiltInOptions;
    public IReadOnlyList<GameViewResolution> customOptions => _customOptions;
    public IReadOnlyList<GameViewResolution> Options => [.. BuiltInOptions, .. _customOptions];

    public void Select(GameViewResolution option)
    {
        ArgumentNullException.ThrowIfNull(option);
        selected = Options.FirstOrDefault(candidate =>
                       candidate.Id.Equals(option.Id, StringComparison.Ordinal)) ??
                   throw new ArgumentException("The resolution is not part of this Game View collection.",
                       nameof(option));
        EditorPrefs.SetString(SelectedPreferenceKey, selected.Id);
        ApplyFixedScreenSize();
    }

    public GameViewResolution AddCustom(string name, int width, int height)
    {
        ValidateDimensions(width, height);
        name = NormalizeName(name, width, height);
        var existing = _customOptions.FirstOrDefault(option =>
            option.Width == width && option.Height == height &&
            option.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Select(existing);
            return existing;
        }

        var option = new GameViewResolution(
            $"custom-{Guid.NewGuid():N}", name, width, height, "Custom", false);
        _customOptions.Add(option);
        SaveCustomOptions();
        Select(option);
        return option;
    }

    public bool RemoveCustom(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var removed = _customOptions.RemoveAll(option =>
            option.Id.Equals(id, StringComparison.Ordinal)) > 0;
        if (!removed) return false;
        SaveCustomOptions();
        if (selected.Id.Equals(id, StringComparison.Ordinal)) Select(BuiltInOptions[0]);
        return true;
    }

    public GraphicsRect FitViewport(GraphicsRect available)
    {
        if (selected.IsFreeAspect || available.Width <= 0 || available.Height <= 0) return available;

        int width;
        int height;
        if ((long)available.Width * selected.Height > (long)available.Height * selected.Width)
        {
            height = available.Height;
            width = Math.Clamp((int)Math.Round(
                (double)height * selected.Width / selected.Height,
                MidpointRounding.AwayFromZero), 1, available.Width);
        }
        else
        {
            width = available.Width;
            height = Math.Clamp((int)Math.Round(
                (double)width * selected.Height / selected.Width,
                MidpointRounding.AwayFromZero), 1, available.Height);
        }

        return new GraphicsRect(
            available.X + (available.Width - width) / 2,
            available.Y + (available.Height - height) / 2,
            width,
            height);
    }

    public (int Width, int Height) ResolveTargetSize(GraphicsRect fittedViewport)
    {
        var width = selected.IsFreeAspect ? Math.Max(1, fittedViewport.Width) : selected.Width;
        var height = selected.IsFreeAspect ? Math.Max(1, fittedViewport.Height) : selected.Height;
        return (width, height);
    }

    public void ApplyScreenSize(GraphicsRect fittedViewport)
    {
        var target = ResolveTargetSize(fittedViewport);
        ApplyScreenSize(target.Width, target.Height);
    }

    private void ApplyFixedScreenSize()
    {
        if (!selected.IsFreeAspect) ApplyScreenSize(selected.Width, selected.Height);
    }

    private static void ApplyScreenSize(int width, int height)
    {
        var current = Screen.currentResolution;
        if (current.width == width && current.height == height) return;
        Screen.SetResolution(width, height, Screen.fullScreenMode, current.refreshRate);
    }

    private static List<GameViewResolution> LoadCustomOptions()
    {
        var json = EditorPrefs.GetString(CustomPreferenceKey);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredResolution>>(json) ?? [];
            var result = new List<GameViewResolution>(stored.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in stored)
            {
                if (item.Width is <= 0 or > MaximumDimension ||
                    item.Height is <= 0 or > MaximumDimension)
                    continue;
                var id = string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)
                    ? $"custom-{Guid.NewGuid():N}"
                    : item.Id;
                ids.Add(id);
                result.Add(new GameViewResolution(id,
                    NormalizeName(item.Name, item.Width, item.Height),
                    item.Width, item.Height, "Custom", false));
            }
            return result;
        }
        catch (JsonException exception)
        {
            Debug.LogWarning($"Could not load custom Game View resolutions: {exception.Message}");
            return [];
        }
    }

    private void SaveCustomOptions()
    {
        var stored = _customOptions.Select(option => new StoredResolution
        {
            Id = option.Id,
            Name = option.Name,
            Width = option.Width,
            Height = option.Height
        }).ToArray();
        EditorPrefs.SetString(CustomPreferenceKey, JsonSerializer.Serialize(stored));
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width is <= 0 or > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Game View width must be between 1 and {MaximumDimension}.");
        if (height is <= 0 or > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(height),
                $"Game View height must be between 1 and {MaximumDimension}.");
    }

    private static string NormalizeName(string? name, int width, int height)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? $"{width} x {height}" : name.Trim();
        normalized = normalized.Replace('/', '-').Replace('\\', '-');
        return normalized[..Math.Min(64, normalized.Length)];
    }

    private sealed class StoredResolution
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
    }
}

internal sealed class GameViewResolutionWindow : EditorWindow
{
    private Action<string, int, int>? _accepted;
    private string _name = "Custom";
    private int _width = 1920;
    private int _height = 1080;
    private string _validationMessage = string.Empty;

    public GameViewResolutionWindow()
    {
        titleContent = new GUIContent("Add Game View Resolution");
        saveToLayout = false;
        minSize = new Vector2(330, 165);
        maxSize = new Vector2(520, 220);
    }

    public static void Open(Rect rootAnchor, Action<string, int, int> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        var window = CreateWindow<GameViewResolutionWindow>();
        window._accepted = accepted;
        window.position = new Rect(rootAnchor.x, rootAnchor.yMax, 390, 180);
        window.ShowPopup();
        window.Focus();
    }

    protected override void OnGUI()
    {
        GUILayout.Space(8);
        _name = EditorGUILayout.TextField("Name", _name);
        _width = EditorGUILayout.IntField("Width", _width);
        _height = EditorGUILayout.IntField("Height", _height);
        if (_validationMessage.Length > 0)
            GUILayout.Label(_validationMessage, EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(76))) Close();
        if (GUILayout.Button("Add", GUILayout.Width(76))) Accept();
        GUILayout.EndHorizontal();
    }

    private void Accept()
    {
        if (_width is <= 0 or > GameViewResolutionSettings.MaximumDimension ||
            _height is <= 0 or > GameViewResolutionSettings.MaximumDimension)
        {
            _validationMessage = $"Width and height must be between 1 and " +
                                 $"{GameViewResolutionSettings.MaximumDimension}.";
            return;
        }

        var accepted = _accepted;
        _accepted = null;
        accepted?.Invoke(_name, _width, _height);
        Close();
    }
}
