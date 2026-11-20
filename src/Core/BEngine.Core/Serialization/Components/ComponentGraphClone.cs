namespace BEngine.Serialization;

internal static class ComponentGraphClone
{
    internal static ComponentGraphData Clone(ComponentGraphData graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var clone = new ComponentGraphData { Version = graph.Version };
        CopyMembers(graph.Members, clone.Members);
        return clone;
    }

    private static void CopyMembers(List<SerializedMemberData> source, List<SerializedMemberData> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        destination.EnsureCapacity(source.Count);
        foreach (var member in source)
        {
            var clone = new SerializedMemberData { Name = member.Name };
            CopyValue(member.Value, clone.Value);
            destination.Add(clone);
        }
    }

    private static SerializedValueData Clone(SerializedValueData value)
    {
        var clone = new SerializedValueData();
        CopyValue(value, clone);
        return clone;
    }

    private static void CopyValue(SerializedValueData source, SerializedValueData destination)
    {
        // Reuse destination-owned nodes and lists when cloning nested values.
        destination.Kind = source.Kind;
        destination.Type = source.Type;
        destination.Value = source.Value;
        destination.ObjectId = source.ObjectId;
        destination.ReferenceId = source.ReferenceId;
        ArgumentNullException.ThrowIfNull(source.Dimensions, "source");
        destination.Dimensions.AddRange(source.Dimensions);
        ArgumentNullException.ThrowIfNull(source.Items, "source");
        destination.Items.EnsureCapacity(source.Items.Count);
        foreach (var item in source.Items) destination.Items.Add(Clone(item));
        CopyMembers(source.Members, destination.Members);
        ArgumentNullException.ThrowIfNull(source.Entries, "source");
        destination.Entries.EnsureCapacity(source.Entries.Count);
        foreach (var entry in source.Entries)
        {
            var clone = new SerializedDictionaryEntryData();
            CopyValue(entry.Key, clone.Key);
            CopyValue(entry.Value, clone.Value);
            destination.Entries.Add(clone);
        }
    }
}
