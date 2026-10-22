namespace BEngine.UIElements;

/// <summary>Authoring defaults for a custom UXML control. Does not depend on editor assemblies.</summary>
public sealed class UxmlFactoryOptions
{
    public string? DisplayName { get; init; }
    public string Category { get; init; } = "Custom Controls";
    public string Tooltip { get; init; } = string.Empty;
    public bool ShowInLibrary { get; init; } = true;
    public IReadOnlyDictionary<string, string>? DefaultAttributes { get; init; }
}

public sealed record UxmlFactoryDescriptor(string ElementName, string DisplayName, string Category, string Tooltip);
