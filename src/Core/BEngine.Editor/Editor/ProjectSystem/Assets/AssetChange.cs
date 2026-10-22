using System.Security.Cryptography;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed record AssetChange(AssetChangeKind Kind, Guid Guid, string AssetPath, string? PreviousPath = null);
