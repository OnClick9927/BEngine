namespace BEngine.Documents;

public sealed class ComponentDocument : Document
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Guid? PrefabAsset { get; set; }
    public Guid? PrefabSource { get; set; }
    public Dictionary<string, string> Fields { get; set; } = [];
}
