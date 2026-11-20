using System.Diagnostics.CodeAnalysis;

namespace BEngine.Serialization;

/// <summary>A detached snapshot of persistent object state, without exposing the graph DTOs.
/// Shared references, cycles and engine-object references use Core's existing graph protocol.</summary>
[SerializationDataAdapter(typeof(Adapter))]
public sealed class ObjectStateSnapshot
{
    private readonly ComponentGraphData _data;
    private ObjectStateSnapshot(ComponentGraphData data) => _data = data;
    public int Version => _data.Version;
    public int MemberCount => _data.Members.Count;

    /// <summary>Captures all persistent members, or an explicit subset by ordinal member name.</summary>
    public static ObjectStateSnapshot Capture(BObject source, IEnumerable<string>? memberNames = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.isDestroyed, source);
        if (memberNames is null) return new(ComponentObjectGraphSerializer.Capture(source));
        var requested = new HashSet<string>(memberNames, StringComparer.Ordinal);
        var selected = ComponentObjectGraphSerializer.PersistentMembers(source)
            .Where(member => requested.Remove(member.Name)).ToArray();
        if (requested.Count != 0)
            throw new ArgumentException("The selection contains a member that is not persistent on the source object.", nameof(memberNames));
        return new(ComponentObjectGraphSerializer.Capture(source, selected));
    }

    public void Restore(BObject target, Func<Guid, BObject?>? sceneObjectResolver = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(target.isDestroyed, target);
        ComponentObjectGraphSerializer.Restore(target, _data, sceneObjectResolver);
    }

    public ObjectStateSnapshot Clone() => new(ComponentObjectGraphSerializer.Clone(_data));
    public string ToYaml() => YamlUtility.Serialize(this);
    public static ObjectStateSnapshot Parse(string yaml) => YamlUtility.Deserialize<ObjectStateSnapshot>(yaml);

    // The serializer bridge is discoverable without friend access. The mutable wire
    // model remains internal and never becomes the developer-facing snapshot API.
    private sealed class Adapter : SerializationDataAdapter
    {
        public Adapter() { }
        public override Type ObjectType => typeof(ObjectStateSnapshot);
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
        public override Type DataType
        {
            [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties, typeof(ComponentGraphData))]
            [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties, typeof(SerializedMemberData))]
            [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties, typeof(SerializedValueData))]
            [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties, typeof(SerializedDictionaryEntryData))]
            get => typeof(ComponentGraphData);
        }
        public override object CaptureData(object value) => ((ObjectStateSnapshot)value)._data;
        public override object RestoreData(object data)
        {
            var graph = (ComponentGraphData)data;
            if (graph.Version != 1)
                throw new InvalidDataException($"Unsupported component object graph version {graph.Version}.");
            return new ObjectStateSnapshot(graph);
        }
    }
}
