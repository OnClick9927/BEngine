namespace BEngine.Content;

internal sealed class BuiltInResourceArchiveEntry
{
    internal BuiltInResourceArchiveEntry(
        string address,
        string assetType,
        Guid guid,
        Guid ownerGuid,
        long localIdentifier,
        string importer,
        IReadOnlyDictionary<string, string> importerSettings,
        long payloadOffset,
        long size,
        string sha256)
    {
        Address = address;
        AssetType = assetType;
        Guid = guid;
        OwnerGuid = ownerGuid;
        LocalIdentifier = localIdentifier;
        Importer = importer;
        ImporterSettings = importerSettings;
        PayloadOffset = payloadOffset;
        Size = size;
        Sha256 = sha256;
    }

    internal string Address { get; }
    internal string AssetType { get; }
    internal Guid Guid { get; }
    internal Guid OwnerGuid { get; }
    internal long LocalIdentifier { get; }
    internal string Importer { get; }
    internal IReadOnlyDictionary<string, string> ImporterSettings { get; }
    internal long Size { get; }
    internal string Sha256 { get; }
    internal long PayloadOffset { get; }
}
