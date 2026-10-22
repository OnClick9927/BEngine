namespace BEngine.ProjectSystem.Editor;

public sealed class SubAssetMetaDocument
{
    public string Guid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}
