using BEngine.ProjectSystem;

namespace BEngine.Editor;

/// <summary>Window-local derived lists; mutable package metadata is checked before reuse.</summary>
internal sealed class PackageManagerListCache
{
    private IReadOnlyList<BPackageDefinition>? _source;
    private Entry[] _entries = [];
    private string? _search;
    private string? _type;
    private BPackageDefinition[] _visible = [];
    private string[] _versionLabels = [];

    internal IReadOnlyList<BPackageDefinition> ExtensionPackages { get; private set; } = [];
    internal IReadOnlyList<BPackageDefinition> ProjectPackages { get; private set; } = [];
    internal string ExtensionHeading { get; private set; } = string.Empty;
    internal string ProjectHeading { get; private set; } = string.Empty;

    internal IReadOnlyList<BPackageDefinition> GetPackages(
        IReadOnlyList<BPackageDefinition> source, string search, string? type)
    {
        if (ReferenceEquals(source, _source) && search == _search && type == _type && Matches(source))
            return _visible;
        return Rebuild(source, search, type);
    }

    private IReadOnlyList<BPackageDefinition> Rebuild(
        IReadOnlyList<BPackageDefinition> source, string search, string? type)
    {
        _source = source;
        _search = search;
        _type = type;
        _entries = new Entry[source.Count];
        for (var i = 0; i < source.Count; i++) _entries[i] = new Entry(source[i]);
        _visible = source.Where(item => (type is null || item.Document.Type == type) &&
                (item.Document.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 item.Document.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 item.Document.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Document.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        ExtensionPackages = _visible.Where(item => item.Document.Type == "package").ToArray();
        ProjectPackages = _visible.Where(item => item.Document.Type == "project").ToArray();
        ExtensionHeading = $"package ({ExtensionPackages.Count})";
        ProjectHeading = $"project ({ProjectPackages.Count})";
        return _visible;
    }

    internal string[] GetVersionLabels(IReadOnlyList<BPackageDefinition> versions)
    {
        var matches = _versionLabels.Length == versions.Count;
        for (var i = 0; matches && i < versions.Count; i++)
            matches = _versionLabels[i] == versions[i].Document.PackageVersion;
        if (matches) return _versionLabels;
        var labels = new string[versions.Count];
        for (var i = 0; i < versions.Count; i++) labels[i] = versions[i].Document.PackageVersion;
        return _versionLabels = labels;
    }

    private bool Matches(IReadOnlyList<BPackageDefinition> source)
    {
        if (_entries.Length != source.Count) return false;
        for (var i = 0; i < source.Count; i++)
            if (!_entries[i].Matches(source[i])) return false;
        return true;
    }

    private readonly struct Entry(BPackageDefinition definition)
    {
        private readonly BPackageDefinition _definition = definition;
        private readonly string _id = definition.Document.Id;
        private readonly string _name = definition.Document.DisplayName;
        private readonly string _description = definition.Document.Description;
        private readonly string _type = definition.Document.Type;

        internal bool Matches(BPackageDefinition current) => ReferenceEquals(_definition, current) &&
            _id == current.Document.Id && _name == current.Document.DisplayName &&
            _description == current.Document.Description && _type == current.Document.Type;
    }
}
