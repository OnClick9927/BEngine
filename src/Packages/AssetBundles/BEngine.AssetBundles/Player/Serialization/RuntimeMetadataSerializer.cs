using System.Text;
using BEngine.AssetBundles;
using BEngine.Platform;
using BEngine.Player.Serialization;
using static BEngine.Player.Serialization.PlayerMetadataBinary;

namespace BEngine.Player.ProjectSystem;

/// <summary>Compatibility facade contributing the content-specific settings section to Core metadata.</summary>
public static class RuntimeMetadataSerializer
{
    public const string FileName = PlayerMetadataSerializer.FileName;
    public const int MaximumFileBytes = PlayerMetadataSerializer.MaximumFileBytes;
    private const ushort ContentSectionId = 3;

    public static void Save(RuntimeMetadataDocument document, string path) =>
        PlayerMetadataSerializer.Save(ToCore(document), path);
    public static void Save(RuntimeMetadataDocument document, string path, FileUtility fileSystem, PathUtility pathService) =>
        PlayerMetadataSerializer.Save(ToCore(document), path, fileSystem, pathService);
    public static RuntimeMetadataDocument Load(string path) => FromCore(PlayerMetadataSerializer.Load(path));
    public static RuntimeMetadataDocument Load(string path, FileUtility fileSystem, PathUtility pathService) =>
        FromCore(PlayerMetadataSerializer.Load(path, fileSystem, pathService));
    public static byte[] Serialize(RuntimeMetadataDocument document) => PlayerMetadataSerializer.Serialize(ToCore(document));
    public static RuntimeMetadataDocument Deserialize(ReadOnlySpan<byte> data) => FromCore(PlayerMetadataSerializer.Deserialize(data));

    private static PlayerMetadataDocument ToCore(RuntimeMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.AssetBundles is null) throw new InvalidDataException("Runtime metadata is missing a required document.");
        AssetBundleSettingsValidation.Validate(document.AssetBundles);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            WriteAssetBundleSettings(writer, document.AssetBundles);
        return new PlayerMetadataDocument
        {
            Format = document.Format, Version = document.Version, Project = document.Project,
            ProjectSettings = document.ProjectSettings, PlayerBootstrap = document.PlayerBootstrap,
            ContentSections = [new(ContentSectionId, 2, stream.ToArray())]
        };
    }

    private static RuntimeMetadataDocument FromCore(PlayerMetadataDocument metadata)
    {
        if (metadata.ContentSections.Count != 1 || metadata.ContentSections[0].Id != ContentSectionId)
            throw new InvalidDataException("Runtime metadata is missing its content settings or contains an unsupported section.");
        var section = metadata.ContentSections[0];
        if (section.Version is not (1 or 2))
            throw new InvalidDataException("Unsupported runtime metadata content settings version.");
        using var reader = CreateReader(section.Data);
        var settings = ReadAssetBundleSettings(reader, section.Version);
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Runtime metadata content settings contain trailing data.");
        AssetBundleSettingsValidation.Validate(settings);
        return new RuntimeMetadataDocument
        {
            Format = metadata.Format, Version = metadata.Version, Project = metadata.Project,
            ProjectSettings = metadata.ProjectSettings, PlayerBootstrap = metadata.PlayerBootstrap,
            AssetBundles = settings
        };
    }

    private static void WriteAssetBundleSettings(
        BinaryWriter writer,
        AssetBundleSettingsDocument settings)
    {
        WriteString(writer, settings.Format);
        writer.Write(settings.Version);
        WriteBoolean(writer, settings.Enabled);
        WriteString(writer, settings.PackageName);
        WriteString(writer, settings.BuiltInDirectory);
        WriteString(writer, settings.RemoteBaseUrl);
        WriteString(writer, settings.CacheDirectory);
        WriteBoolean(writer, settings.CheckForUpdatesOnStartup);
        WriteBoolean(writer, settings.ApplyUpdatesOnStartup);
        WriteBoolean(writer, settings.FailStartupWhenUpdateFails);
        WriteBoolean(writer, settings.RequireHttps);
        writer.Write(settings.MaxRetries);
        writer.Write((int)settings.LoadMode);
    }

    private static AssetBundleSettingsDocument ReadAssetBundleSettings(BinaryReader reader, ushort version) => new()
    {
        Format = ReadString(reader),
        Version = reader.ReadInt32(),
        Enabled = ReadBoolean(reader),
        PackageName = ReadString(reader),
        BuiltInDirectory = ReadString(reader),
        RemoteBaseUrl = ReadString(reader),
        CacheDirectory = ReadString(reader),
        CheckForUpdatesOnStartup = ReadBoolean(reader),
        ApplyUpdatesOnStartup = ReadBoolean(reader),
        FailStartupWhenUpdateFails = ReadBoolean(reader),
        RequireHttps = ReadBoolean(reader),
        MaxRetries = reader.ReadInt32(),
        LoadMode = version >= 2 ? (AssetBundleLoadMode)reader.ReadInt32() : AssetBundleLoadMode.PersistentCache
    };

}
