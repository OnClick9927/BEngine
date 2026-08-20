using System.Security.Cryptography;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed record AssetRecord(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string MetaPath,
    string ArtifactPath,
    string AssetType,
    string SourceHash,
    bool IsDirectory);
