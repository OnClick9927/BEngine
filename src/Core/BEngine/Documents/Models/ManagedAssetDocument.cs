namespace BEngine.Documents;

public sealed class ManagedAssetDocument : Document
{
    public string Format { get; set; } = "BEngine.ManagedAsset";
    public int Version { get; set; } = 1;
    public string TypeName { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}
