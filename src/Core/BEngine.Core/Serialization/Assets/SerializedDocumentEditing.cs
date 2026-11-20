namespace BEngine.Serialization;

public readonly partial struct SerializedHierarchy
{
    /// <summary>Edits tags and layer values in this detached hierarchy. Mapping and validation
    /// policy belong to the caller. Returns the number of changed metadata values.</summary>
    public int RewriteMetadata(Func<string, string>? tags = null, Func<ulong, ulong>? layers = null)
    {
        var changed = 0;
        foreach (var item in Data)
        {
            if (tags is not null)
            {
                var next = tags(item.Tag);
                ArgumentNullException.ThrowIfNull(next);
                if (!string.Equals(next, item.Tag, StringComparison.Ordinal)) { item.Tag = next; changed++; }
            }
            if (layers is not null)
            {
                var next = layers(item.Layer);
                if (next != item.Layer) { item.Layer = next; changed++; }
            }
        }
        return changed;
    }

    /// <summary>Rewrites top-level scalar members of each component, including its Transform.
    /// Arguments are member name, serialized type name (null for legacy untyped fields), and text.
    /// Nested objects, arrays and references remain untouched. Members changed in both legacy and
    /// graph representations count once per component, using case-insensitive member identity.
    /// This edits the detached snapshot in place; clone first when failure must leave it unchanged.</summary>
    public int RewriteScalars(Func<string, string?, string, string> rewrite)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        var changed = 0;
        foreach (var item in Data)
        {
            changed += Rewrite(item.Transform.Fields, item.Transform.Graph);
            foreach (var component in item.Components) changed += Rewrite(component.Fields, component.Graph);
        }
        return changed;

        int Rewrite(Dictionary<string, string> fields, ComponentGraphData? graph)
        {
            HashSet<string>? names = null;
            foreach (var name in fields.Keys.ToArray())
            {
                var previous = fields[name];
                var next = rewrite(name, null, previous);
                ArgumentNullException.ThrowIfNull(next);
                if (string.Equals(previous, next, StringComparison.Ordinal)) continue;
                fields[name] = next;
                (names ??= new(StringComparer.OrdinalIgnoreCase)).Add(name);
            }
            if (graph is not null)
                foreach (var member in graph.Members)
                {
                    var value = member.Value;
                    if (!string.Equals(value.Kind, "Scalar", StringComparison.Ordinal)) continue;
                    var next = rewrite(member.Name, value.Type, value.Value);
                    ArgumentNullException.ThrowIfNull(next);
                    if (string.Equals(value.Value, next, StringComparison.Ordinal)) continue;
                    value.Value = next;
                    (names ??= new(StringComparer.OrdinalIgnoreCase)).Add(member.Name);
                }
            return names?.Count ?? 0;
        }
    }
}

