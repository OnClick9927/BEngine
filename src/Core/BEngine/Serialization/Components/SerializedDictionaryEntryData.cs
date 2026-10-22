namespace BEngine.Serialization;

internal sealed class SerializedDictionaryEntryData
{
    public SerializedValueData Key { get; set; } = new();
    public SerializedValueData Value { get; set; } = new();
}
