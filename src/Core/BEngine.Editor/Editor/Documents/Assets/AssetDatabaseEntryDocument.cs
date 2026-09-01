using System.Security.Cryptography;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed class AssetDatabaseEntryDocument
{
    public string Guid { get; set; } = string.Empty;
    public string AssetPath { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string ArtifactPath { get; set; } = string.Empty;
    public string ArtifactHash { get; set; } = string.Empty;
    public long ArtifactSize { get; set; }
    public string ParentGuid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; }
}
