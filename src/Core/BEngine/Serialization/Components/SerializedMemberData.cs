namespace BEngine.Serialization;

internal sealed class SerializedMemberData
{
    public string Name { get; set; } = string.Empty;
    public SerializedValueData Value { get; set; } = new();
}
