using BEngine.Documents;

namespace BEngine.Editor;

internal sealed class TagLayerSettingsDraft
{
    private readonly List<TagEntry> _tagEntries = [];
    private readonly List<string> _originalTags = [];
    private readonly List<string> _editableTags = [];
    private readonly List<string> _tags = [];
    private readonly List<LayerEntry> _layerEntries = [];
    private readonly List<SortingLayerDocument> _sortingLayers = [];
    private readonly List<string> _editableLayerNames = [];
    private readonly List<LayerSnapshot> _originalLayers = [];
    private readonly Dictionary<string, string> _tagReplacements = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, ulong> _layerReplacements = [];
    private ulong _defaultOriginalValue;

    internal IReadOnlyList<string> Tags => _tags;
    internal IReadOnlyList<SortingLayerDocument> SortingLayers => _sortingLayers;
    internal IReadOnlyDictionary<string, string> TagReplacements => _tagReplacements;
    internal IReadOnlyDictionary<ulong, ulong> LayerReplacements => _layerReplacements;
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

        _layerEntries.Clear();
        _originalLayers.Clear();
        foreach (var layer in EditorProjectSettings.current.SortingLayers)
        {
            var name = layer.Name?.Trim() ?? string.Empty;
            _layerEntries.Add(new LayerEntry(layer.Value, name, layer.BuiltIn, layer.IsUi, layer.BuiltInId));
            _originalLayers.Add(new LayerSnapshot(layer.Value, name, layer.BuiltIn, layer.IsUi,
                layer.BuiltInId));
        }
        _defaultOriginalValue = SortingLayer.Default;
        RefreshState();
    }

    internal void AddTag() => AddTag(CreateUniqueName("New Tag", _tagEntries.Select(item => item.Name)));

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
        var offset = index - SortingLayer.MinimumIndex;
        if (offset < 0 || offset >= _layerEntries.Count) return;
        _layerEntries[offset].Name = name ?? string.Empty;
        RefreshState();
    }

    internal void AddLayer() => AddLayer(CreateUniqueName("New Layer",
        _layerEntries.Select(static item => item.Name)));

    internal void AddLayer(string name)
    {
        if (_layerEntries.Count >= SortingLayer.MaximumIndex) return;
        _layerEntries.Add(new LayerEntry(null, name ?? string.Empty, false, false, string.Empty));
        RefreshState();
    }

    internal bool CanRemoveLayer(int index)
    {
        var offset = index - SortingLayer.MinimumIndex;
        return offset >= 0 && offset < _layerEntries.Count && !_layerEntries[offset].BuiltIn;
    }

    internal void RemoveLayer(int index)
    {
        if (!CanRemoveLayer(index)) return;
        _layerEntries.RemoveAt(index - SortingLayer.MinimumIndex);
        RefreshState();
    }

    internal bool CanMoveLayer(int index, int direction)
    {
        var offset = index - SortingLayer.MinimumIndex;
        var target = offset + Math.Sign(direction);
        return offset >= 0 && offset < _layerEntries.Count && target >= 0 && target < _layerEntries.Count;
    }

    internal void MoveLayer(int index, int direction)
    {
        if (!CanMoveLayer(index, direction)) return;
        var offset = index - SortingLayer.MinimumIndex;
        var target = offset + Math.Sign(direction);
        (_layerEntries[offset], _layerEntries[target]) = (_layerEntries[target], _layerEntries[offset]);
        RefreshState();
    }

    private static string CreateUniqueName(string baseName, IEnumerable<string> names)
    {
        var reserved = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!reserved.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!reserved.Contains(candidate)) return candidate;
        }
    }

    private void RefreshState()
    {
        _editableTags.Clear();
        _editableTags.AddRange(_tagEntries.Select(static entry => entry.Name));
        _tags.Clear();
        _tags.AddRange(_editableTags.Select(static tag => tag.Trim()));

        _sortingLayers.Clear();
        _editableLayerNames.Clear();
        for (var offset = 0; offset < _layerEntries.Count; offset++)
        {
            var entry = _layerEntries[offset];
            _editableLayerNames.Add(entry.Name);
            _sortingLayers.Add(new SortingLayerDocument
            {
                Value = (ulong)(offset + SortingLayer.MinimumIndex),
                Name = entry.Name.Trim(),
                BuiltIn = entry.BuiltIn,
                IsUi = entry.IsUi,
                BuiltInId = entry.BuiltInId
            });
        }

        _tagReplacements.Clear();
        foreach (var original in _originalTags.Skip(1))
        {
            var current = _tagEntries.FirstOrDefault(entry =>
                entry.OriginalName?.Equals(original, StringComparison.Ordinal) == true);
            if (current is null) _tagReplacements[original] = "Untagged";
            else if (!current.Name.Trim().Equals(original, StringComparison.Ordinal))
                _tagReplacements[original] = current.Name.Trim();
        }

        _layerReplacements.Clear();
        var defaultOffset = _layerEntries.FindIndex(entry => entry.OriginalValue == _defaultOriginalValue);
        var defaultLayer = defaultOffset < 0
            ? SortingLayer.MinimumIndex
            : defaultOffset + SortingLayer.MinimumIndex;
        foreach (var original in _originalLayers)
        {
            var offset = _layerEntries.FindIndex(entry => entry.OriginalValue == original.Value);
            var next = offset < 0 ? (ulong)defaultLayer : (ulong)(offset + SortingLayer.MinimumIndex);
            if (next != original.Value) _layerReplacements[original.Value] = next;
        }

        Error = Validate();
        var tagIdentityChanged = _tagEntries.Count != _originalTags.Count ||
                                 _tagEntries.Where((entry, index) => index >= _originalTags.Count ||
                                     !string.Equals(entry.OriginalName, _originalTags[index],
                                         StringComparison.Ordinal)).Any();
        var tagNamesChanged = !_editableTags.SequenceEqual(_originalTags, StringComparer.Ordinal);
        var currentLayers = _sortingLayers.Select(static item =>
            new LayerSnapshot(item.Value, item.Name, item.BuiltIn, item.IsUi, item.BuiltInId));
        IsDirty = tagIdentityChanged || tagNamesChanged ||
                  !currentLayers.SequenceEqual(_originalLayers);
    }

    private string Validate()
    {
        if (_tags.Count == 0 || !_tags[0].Equals("Untagged", StringComparison.Ordinal))
            return "Untagged must be the first tag.";
        var emptyTag = _editableTags.FindIndex(static tag => string.IsNullOrWhiteSpace(tag));
        if (emptyTag >= 0) return $"Tag {emptyTag} needs a name.";
        var duplicateTag = _tags.GroupBy(static tag => tag, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateTag is not null) return $"Tag '{duplicateTag.Key}' is duplicated.";

        var emptyLayer = _editableLayerNames.FindIndex(static name => string.IsNullOrWhiteSpace(name));
        if (emptyLayer >= 0) return $"Layer {emptyLayer + SortingLayer.MinimumIndex} needs a name.";
        var duplicateLayer = _sortingLayers.Select(static layer => layer.Name.Trim())
            .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        return duplicateLayer is null ? string.Empty : $"Layer name '{duplicateLayer.Key}' is duplicated.";
    }

    private sealed class TagEntry(string? originalName, string name)
    {
        internal string? OriginalName { get; } = originalName;
        internal string Name { get; set; } = name;
    }

    private sealed class LayerEntry(ulong? originalValue, string name, bool builtIn, bool isUi,
        string builtInId)
    {
        internal ulong? OriginalValue { get; } = originalValue;
        internal string Name { get; set; } = name;
        internal bool BuiltIn { get; } = builtIn;
        internal bool IsUi { get; } = isUi;
        internal string BuiltInId { get; } = builtInId;
    }

    private readonly record struct LayerSnapshot(
        ulong Value, string Name, bool BuiltIn, bool IsUi, string BuiltInId);
}
