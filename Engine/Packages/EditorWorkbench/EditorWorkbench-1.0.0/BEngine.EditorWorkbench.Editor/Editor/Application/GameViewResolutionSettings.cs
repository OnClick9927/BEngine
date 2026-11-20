
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed class GameViewResolutionSettings
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
        selected = Matches1605() ?? BuiltInOptions[0];
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Editor.GameViewResolution? Matches1605()
        {
            var matchSource1605 = Options;
            ArgumentNullException.ThrowIfNull(matchSource1605, "source");
            global::System.ReadOnlySpan<global::BEngine.Editor.GameViewResolution> matchValues1605;
            if (matchSource1605.GetType() == typeof(global::BEngine.Editor.GameViewResolution[]))
            {
                matchValues1605 = (global::BEngine.Editor.GameViewResolution[])(object)matchSource1605;
            }
            else if (matchSource1605.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.GameViewResolution>))
            {
                matchValues1605 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.GameViewResolution>)(object)matchSource1605);
            }
            else
            {
                foreach (var option in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.GameViewResolution>)matchSource1605)
                {
                    if (option.Id.Equals(selectedId, StringComparison.Ordinal))
                        return option;
                }
        
                return default!;
            }
        
            foreach (var option in matchValues1605)
            {
                if (option.Id.Equals(selectedId, StringComparison.Ordinal))
                    return option;
            }
        
            return default!;
        }
    }

    public GameViewResolution selected { get; private set; }
    public IReadOnlyList<GameViewResolution> builtInOptions => BuiltInOptions;
    public IReadOnlyList<GameViewResolution> customOptions => _customOptions;
    public IReadOnlyList<GameViewResolution> Options => [.. BuiltInOptions, .. _customOptions];

    public void Select(GameViewResolution option)
    {
        ArgumentNullException.ThrowIfNull(option);
        selected = Matches2183() ?? throw new ArgumentException("The resolution is not part of this Game View collection.", nameof(option));
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Editor.GameViewResolution? Matches2183()
        {
            var matchSource2183 = Options;
            ArgumentNullException.ThrowIfNull(matchSource2183, "source");
            global::System.ReadOnlySpan<global::BEngine.Editor.GameViewResolution> matchValues2183;
            if (matchSource2183.GetType() == typeof(global::BEngine.Editor.GameViewResolution[]))
            {
                matchValues2183 = (global::BEngine.Editor.GameViewResolution[])(object)matchSource2183;
            }
            else if (matchSource2183.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.GameViewResolution>))
            {
                matchValues2183 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.GameViewResolution>)(object)matchSource2183);
            }
            else
            {
                foreach (var candidate in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.GameViewResolution>)matchSource2183)
                {
                    if (candidate.Id.Equals(option.Id, StringComparison.Ordinal))
                        return candidate;
                }
        
                return default!;
            }
        
            foreach (var candidate in matchValues2183)
            {
                if (candidate.Id.Equals(option.Id, StringComparison.Ordinal))
                    return candidate;
            }
        
            return default!;
        }
        EditorPrefs.SetString(SelectedPreferenceKey, selected.Id);
        ApplyFixedScreenSize();
    }

    public GameViewResolution AddCustom(string name, int width, int height)
    {
        ValidateDimensions(width, height);
        name = NormalizeName(name, width, height);
        var existing = Matches2755();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Editor.GameViewResolution? Matches2755()
        {
            var matchSource2755 = _customOptions;
            ArgumentNullException.ThrowIfNull(matchSource2755, "source");
            if (matchSource2755.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.Editor.GameViewResolution>))
            {
                foreach (var option in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.GameViewResolution>)matchSource2755)
                {
                    if (option.Width == width && option.Height == height && option.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return option;
                }
        
                return default!;
            }
        
            global::System.ReadOnlySpan<global::BEngine.Editor.GameViewResolution> matchValues2755 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource2755);
            foreach (var option in matchValues2755)
            {
                if (option.Width == width && option.Height == height && option.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return option;
            }
        
            return default!;
        }
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
            var stored = YamlUtility.Deserialize<List<StoredResolution>>(json) ?? [];
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
        catch (InvalidDataException exception)
        {
            Debug.LogWarning($"Could not load custom Game View resolutions: {exception.Message}");
            return [];
        }
    }

    private void SaveCustomOptions()
    {
        var projectionSource6963 = _customOptions;
        ArgumentNullException.ThrowIfNull(projectionSource6963, "source");
        var projectionValues6963 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionSource6963);
        var projectionResult6963 = projectionValues6963.Length == 0 ? global::System.Array.Empty<global::BEngine.Editor.GameViewResolutionSettings.StoredResolution>() : new global::BEngine.Editor.GameViewResolutionSettings.StoredResolution[projectionValues6963.Length];
        for (var projectionIndex6963 = 0; projectionIndex6963 < projectionValues6963.Length; projectionIndex6963++)
        {
            var projectionItem6963 = projectionValues6963[projectionIndex6963];
            projectionResult6963[projectionIndex6963] = new StoredResolution
            {
                Id = projectionItem6963.Id,
                Name = projectionItem6963.Name,
                Width = projectionItem6963.Width,
                Height = projectionItem6963.Height
            };
        }
        
        var stored = projectionResult6963;
        EditorPrefs.SetString(CustomPreferenceKey, YamlUtility.Serialize(stored));
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
