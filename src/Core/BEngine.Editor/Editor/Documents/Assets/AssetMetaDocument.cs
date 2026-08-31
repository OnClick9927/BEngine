using System.Security.Cryptography;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed class AssetMetaDocument : Document
{
    public string Format { get; set; } = "BEngine.AssetMeta";
    public int Version { get; set; } = 1;
    public string Guid { get; set; } = string.Empty;
    public string Importer { get; set; } = "DefaultImporter";
    public string AssetType { get; set; } = "DefaultAsset";
    public string SourceHash { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = [];
    public string ParentGuid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; }
    public long NextLocalIdentifier { get; set; } = 100000;
    public List<SubAssetMetaDocument> SubAssets { get; set; } = [];
}

public sealed class SubAssetMetaDocument
{
    public string Guid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}
