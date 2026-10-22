namespace BEngine.Serialization;

internal sealed class ComponentGraphData
{
    public int Version { get; set; } = 1;
    public List<SerializedMemberData> Members { get; set; } = [];
}
