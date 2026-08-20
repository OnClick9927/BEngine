using System.Security.Cryptography;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed class AssetDatabaseDocument : Document
{
    public string Format { get; set; } = "BEngine.AssetDatabase";
    public int Version { get; set; } = 1;
    public List<AssetDatabaseEntryDocument> Assets { get; set; } = [];
}
