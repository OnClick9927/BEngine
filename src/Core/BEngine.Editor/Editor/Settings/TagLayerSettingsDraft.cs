using BEngine.Documents;

namespace BEngine.Editor;

internal sealed class TagLayerSettingsDraft
{
    private readonly List<TagEntry> _tagEntries = [];
    private readonly List<string> _originalTags = [];
    private readonly List<string> _editableTags = [];
    private readonly List<string> _tags = [];
    private readonly List<SortingLayerDocument> _sortingLayers = [];
    private readonly List<string> _editableLayerNames = [];
    private readonly List<string> _originalLayerNames = [];
    private readonly Dictionary<string, string> _tagReplacements = new(StringComparer.Ordinal);

    internal IReadOnlyList<string> Tags => _tags;
    internal IReadOnlyList<SortingLayerDocument> SortingLayers => _sortingLayers;
    internal IReadOnlyDictionary<string, string> TagReplacements => _tagReplacements;
    internal IReadOnlyList<string> EditableTags => _editableTags;
    internal IReadOnlyList<string> EditableLayerNames => _editableLayerNames;
    internal bool IsDirty { get; private set; }
    internal string Error { get; private set; } = string.Empty;

    internal void Reload()
    {
        _tagEntries.Clear();
        _originalTags.Clear();
        var sourceTags = EditorProjectSettings.current.Tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Prepend("Untagged")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var tag in sourceTags)
        {
            _originalTags.Add(tag);
            _tagEntries.Add(new TagEntry(tag, tag));
        }

        _sortingLayers.Clear();
        _editableLayerNames.Clear();
        _originalLayerNames.Clear();
        var supplied = EditorProjectSettings.current.SortingLayers
            .ToDictionary(static layer => layer.Value);
        foreach (var definition in SortingLayerRegistry.CreateDefaults())
        {
            var name = supplied.TryGetValue(definition.Value, out var layer) &&
                       !string.IsNullOrWhiteSpace(layer.Name)
                ? layer.Name.Trim()
                : definition.Name;
            _sortingLayers.Add(new SortingLayerDocument { Value = definition.Value, Name = name });
            _editableLayerNames.Add(name);
            _originalLayerNames.Add(name);
        }

        RefreshState();
    }

    internal void AddTag() => AddTag(CreateUniqueTagName());

    internal void AddTag(string name)
    {
        _tagEntries.Add(new TagEntry(null, name ?? string.Empty));
        RefreshState();
    }

    internal void RenameTag(int index, string name)
    {
        if (index <= 0 || index >= _tagEntries.Count) return;
        _tagEntries[index].Name = name ?? string.Empty;
        RefreshState();
    }

    internal void RemoveTag(int index)
    {
        if (index <= 0 || index >= _tagEntries.Count) return;
        _tagEntries.RemoveAt(index);
        RefreshState();
    }

    internal void SetLayerName(int index, string name)
    {
        if (index is < SortingLayer.MinimumIndex or > SortingLayer.MaximumIndex) return;
        _editableLayerNames[index - SortingLayer.MinimumIndex] = name ?? string.Empty;
        RefreshState();
    }

    private string CreateUniqueTagName()
    {
        const string baseName = "New Tag";
        if (_tagEntries.All(entry => !entry.Name.Equals(baseName, StringComparison.Ordinal)))
            return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (_tagEntries.All(entry => !entry.Name.Equals(candidate, StringComparison.Ordinal)))
                return candidate;
        }
    }

    private void RefreshState()
    {
        _editableTags.Clear();
        _editableTags.AddRange(_tagEntries.Select(static entry => entry.Name));
        _tags.Clear();
        _tags.AddRange(_editableTags.Select(static tag => tag.Trim()));
        for (var index = 0; index < _sortingLayers.Count; index++)
            _sortingLayers[index].Name = _editableLayerNames[index].Trim();

        _tagReplacements.Clear();
        foreach (var original in _originalTags.Skip(1))
        {
            var current = _tagEntries.FirstOrDefault(entry =>
                entry.OriginalName?.Equals(original, StringComparison.Ordinal) == true);
            if (current is null)
                _tagReplacements[original] = "Untagged";
            else if (!current.Name.Trim().Equals(original, StringComparison.Ordinal))
                _tagReplacements[original] = current.Name.Trim();
        }

        Error = Validate();
        var tagIdentityChanged = _tagEntries.Count != _originalTags.Count ||
                                 _tagEntries.Where((entry, index) =>
                                         index >= _originalTags.Count ||
                                         !string.Equals(entry.OriginalName, _originalTags[index],
                                             StringComparison.Ordinal))
                                     .Any();
        var tagNamesChanged = !_editableTags.SequenceEqual(_originalTags, StringComparer.Ordinal);
        var layerNamesChanged = !_editableLayerNames.SequenceEqual(_originalLayerNames,
            StringComparer.Ordinal);
        IsDirty = tagIdentityChanged || tagNamesChanged || layerNamesChanged;
    }

    private string Validate()
    {
        if (_tags.Count == 0 || !_tags[0].Equals("Untagged", StringComparison.Ordinal))
            return "Untagged must be the first tag.";
        var emptyTag = _editableTags.FindIndex(static tag => string.IsNullOrWhiteSpace(tag));
        if (emptyTag >= 0) return $"Tag {emptyTag} needs a name.";
        var duplicateTag = _tags.Select(static tag => tag.Trim())
            .GroupBy(static tag => tag, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateTag is not null) return $"Tag '{duplicateTag.Key}' is duplicated.";

        var emptyLayer = _editableLayerNames.FindIndex(static name => string.IsNullOrWhiteSpace(name));
        if (emptyLayer >= 0)
            return $"Layer 2^{emptyLayer + SortingLayer.MinimumIndex} needs a name.";
        var duplicateLayer = _sortingLayers.Select(static layer => layer.Name.Trim())
            .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        return duplicateLayer is null
            ? string.Empty
            : $"Layer name '{duplicateLayer.Key}' is duplicated.";
    }

    private sealed class TagEntry(string? originalName, string name)
    {
        internal string? OriginalName { get; } = originalName;
        internal string Name { get; set; } = name;
    }
}
