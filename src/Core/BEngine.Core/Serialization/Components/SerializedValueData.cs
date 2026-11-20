namespace BEngine.Serialization;

internal sealed class SerializedValueData
{
    public string Kind { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int ObjectId { get; set; }
    public int ReferenceId { get; set; }
    public List<int> Dimensions { get; set; } = [];
    public List<SerializedValueData> Items { get; set; } = [];
    public List<SerializedMemberData> Members { get; set; } = [];
    public List<SerializedDictionaryEntryData> Entries { get; set; } = [];
}
