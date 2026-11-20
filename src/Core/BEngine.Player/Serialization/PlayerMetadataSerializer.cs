using BEngine.Hosting;
using BEngine.ProjectSystem;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BEngine.Build;
using BEngine.Platform;
using BEngine.Serialization;

using static BEngine.Player.Serialization.PlayerMetadataBinary;

namespace BEngine.Player.Serialization;

public static class PlayerMetadataSerializer
{
    public const string FileName = "runtime.bmeta";
    public const int MaximumFileBytes = 4 * 1024 * 1024;

    private const ushort FileVersion = 1;
    private const ushort HeaderSize = 48;
    private const int HashSize = 32;
    private const int MaximumSectionBytes = 2 * 1024 * 1024;
            private const int MinimumSectionCount = 2;
    private const int MaximumSectionCount = 128;
    private static readonly byte[] Magic = "BENGBMET"u8.ToArray();

    public static void Save(PlayerMetadataDocument document, string path) =>
        Save(document, path, RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>(), RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>());

    public static void Save(
        PlayerMetadataDocument document,
        string path,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullPath = pathService.GetFullPath(path);
        fileSystem.CreateDirectory(pathService.GetDirectoryName(fullPath)!);
        var bytes = Serialize(document);
        var temporaryPath = $"{fullPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = fileSystem.OpenFile(
                       temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.SequentialScan))
            {
                stream.Write(bytes);
                fileSystem.Flush(stream, flushToDisk: true);
            }
            fileSystem.MoveFile(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (fileSystem.FileExists(temporaryPath)) fileSystem.DeleteFile(temporaryPath);
        }
    }

    public static PlayerMetadataDocument Load(string path) =>
        Load(path, RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>(), RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>());

    public static PlayerMetadataDocument Load(
        string path,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullPath = pathService.GetFullPath(path);
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException("Runtime metadata was not found.", fullPath);
        using var stream = fileSystem.OpenFile(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.SequentialScan);
        if (stream.Length is < HeaderSize or > MaximumFileBytes)
            throw new InvalidDataException(
                $"Runtime metadata length {stream.Length} is outside the supported range.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        var payload = ValidatePayload(bytes);
        return ReadPayload(bytes, HeaderSize, payload.Length);
    }

    public static byte[] Serialize(PlayerMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        using var stream = new MetadataWriteStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        stream.SetLength(HeaderSize);
        stream.Position = HeaderSize;
        WriteString(writer, document.Format);
        // Rewriting legacy metadata always emits the current build-only contract.
        writer.Write(PlayerMetadataDocument.CurrentVersion);
        writer.Write(2 + document.ContentSections.Count + (document.PlayerBootstrap is null ? 0 : 1));
        WriteSection(writer, SectionId.Project, 2, document);
        WriteSection(writer, SectionId.ProjectSettings, 3, document);
        foreach (var section in document.ContentSections)
        {
            writer.Write(section.Id);
            writer.Write(section.Version);
            writer.Write(section.Data.Length);
            writer.Write(section.Data.Span);
        }
        if (document.PlayerBootstrap is not null) WriteSection(writer, SectionId.PlayerBootstrap, 4, document);
        writer.Flush();
        if (stream.Length > MaximumFileBytes)
            throw new InvalidDataException("Runtime metadata payload exceeds the maximum supported length.");
        var data = stream.GetBuffer().AsSpan(0, checked((int)stream.Length));
        Magic.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(8, 2), FileVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(10, 2), HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(12, 4), data.Length - HeaderSize);
        SHA256.HashData(data[HeaderSize..], data.Slice(16, HashSize));
        return stream.ToArray();
    }

    public static PlayerMetadataDocument Deserialize(ReadOnlySpan<byte> data)
    {
        var payload = ValidatePayload(data);
        // The caller may own or mutate its buffer, so the span API retains one
        // private copy. Load already owns its bytes and can parse them directly.
        return ReadPayload(payload.ToArray(), 0, payload.Length);
    }

    private static ReadOnlySpan<byte> ValidatePayload(ReadOnlySpan<byte> data)
    {
        if (data.Length is < HeaderSize or > MaximumFileBytes)
            throw new InvalidDataException(
                $"Runtime metadata length {data.Length} is outside the supported range.");
        if (!data[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Runtime metadata magic is invalid.");
        var fileVersion = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
        if (fileVersion != FileVersion)
            throw new InvalidDataException($"Unsupported runtime metadata file version {fileVersion}.");
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2));
        if (headerSize != HeaderSize)
            throw new InvalidDataException($"Unsupported runtime metadata header size {headerSize}.");
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(12, 4));
        if (payloadLength < 0 || payloadLength != data.Length - HeaderSize)
            throw new InvalidDataException("Runtime metadata payload length is invalid.");
        var payload = data[HeaderSize..];
        Span<byte> actualHash = stackalloc byte[HashSize];
        SHA256.HashData(payload, actualHash);
        if (!CryptographicOperations.FixedTimeEquals(data.Slice(16, HashSize), actualHash))
            throw new InvalidDataException("Runtime metadata failed its SHA-256 integrity check.");
        return payload;
    }

    private static PlayerMetadataDocument ReadPayload(byte[] buffer, int offset, int length)
    {
        PlayerMetadataDocument document;
        using (var stream = new MemoryStream(buffer, offset, length, writable: false, publiclyVisible: true))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
        {
            document = new PlayerMetadataDocument
            {
                Format = ReadString(reader),
                Version = reader.ReadInt32()
            };
            var sectionCount = ReadCount(reader, "section", MaximumSectionCount);
            if (sectionCount < MinimumSectionCount)
                throw new InvalidDataException(
                    $"Runtime metadata must contain at least {MinimumSectionCount} sections.");
            var seen = new HashSet<ushort>();
            for (var index = 0; index < sectionCount; index++)
            {
                var idValue = reader.ReadUInt16();
                if (idValue == 0)
                    throw new InvalidDataException($"Runtime metadata section {idValue} is unsupported.");
                var id = (SectionId)idValue;
                if (!seen.Add(idValue))
                    throw new InvalidDataException($"Runtime metadata contains duplicate section '{id}'.");
                var sectionVersion = reader.ReadUInt16();
                if (sectionVersion != 1 &&
                    !(id == SectionId.ProjectSettings && sectionVersion is 2 or 3) &&
                    !(id == SectionId.Project && sectionVersion == 2) &&
                    !(id == SectionId.PlayerBootstrap && sectionVersion is 2 or 3 or 4) &&
                    !(sectionVersion > 0 && !Enum.IsDefined(id)))
                    throw new InvalidDataException(
                        $"Unsupported runtime metadata section '{id}' version {sectionVersion}.");
                var sectionLength = reader.ReadInt32();
                if (sectionLength is < 0 or > MaximumSectionBytes || sectionLength > stream.Length - stream.Position)
                    throw new InvalidDataException($"Runtime metadata section '{id}' length is invalid.");
                var sectionOffset = checked(offset + (int)stream.Position);
                stream.Position += sectionLength;
                ReadSection(document, id, sectionVersion, buffer, sectionOffset, sectionLength);
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Runtime metadata contains trailing payload data.");
            if (!seen.Contains((ushort)SectionId.Project) ||
                !seen.Contains((ushort)SectionId.ProjectSettings))
                throw new InvalidDataException("Runtime metadata is missing a required section.");
            if (document.Version == 1 && !seen.Contains((ushort)SectionId.LegacyEnabledPackages))
                throw new InvalidDataException("Legacy runtime metadata is missing its package section.");
        }
        Validate(document);
        return document;
    }

    private static void WriteSection(BinaryWriter writer, SectionId id, ushort version, PlayerMetadataDocument document)
    {
        var stream = writer.BaseStream;
        writer.Write((ushort)id);
        writer.Write(version);
        var lengthPosition = stream.Position;
        writer.Write(0);
        var start = stream.Position;
        switch (id)
        {
            case SectionId.Project: WriteProject(writer, document.Project); break;
            case SectionId.ProjectSettings: WriteProjectSettings(writer, document.ProjectSettings); break;
            case SectionId.PlayerBootstrap: WritePlayerBootstrap(writer, document.PlayerBootstrap!); break;
            default: throw new InvalidOperationException("Unsupported runtime metadata output section.");
        }
        var end = stream.Position;
        if (end - start > MaximumSectionBytes)
            throw new InvalidDataException("Runtime metadata section exceeds the maximum supported length.");
        stream.Position = lengthPosition;
        writer.Write(checked((int)(end - start)));
        stream.Position = end;
    }

    private static void ReadSection(
        PlayerMetadataDocument document,
        SectionId id,
        ushort version,
        byte[] data,
        int offset,
        int length)
    {
        if (!Enum.IsDefined(id))
        {
            document.ContentSections.Add(new((ushort)id, version, data.AsMemory(offset, length)));
            return;
        }
        // A bounded view preserves section over-read/trailing-data checks without
        // allocating a separate array for each section.
        using var stream = new MemoryStream(data, offset, length, writable: false, publiclyVisible: true);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        switch (id)
        {
            case SectionId.Project:
                document.Project = ReadProject(reader, version);
                break;
            case SectionId.ProjectSettings:
                document.ProjectSettings = ReadProjectSettings(reader, version);
                break;
            case SectionId.LegacyEnabledPackages:
                // Version-one Players shipped a package inventory. New Players
                // validate and discard it; package resolution belongs to build time.
                SkipLegacyPackages(reader);
                break;
            case SectionId.PlayerBootstrap:
                document.PlayerBootstrap = ReadPlayerBootstrap(reader, version);
                break;
            default:
                throw new InvalidDataException($"Runtime metadata section '{id}' is unsupported.");
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException($"Runtime metadata section '{id}' contains trailing data.");
    }

    private static void WriteProject(BinaryWriter writer, RuntimeProjectData project)
    {
        WriteString(writer, project.Name); WriteString(writer, project.StartupScene);
        WriteString(writer, project.AssetsDirectory); WriteString(writer, project.FixedDeltaTime);
        WriteString(writer, project.Window.Title); writer.Write(project.Window.Width);
        writer.Write(project.Window.Height); WriteBoolean(writer, project.Window.VSync);
    }

    private static RuntimeProjectData ReadProject(BinaryReader reader, ushort version)
    {
        if (version == 1)
        {
            var format = ReadString(reader); var schema = reader.ReadInt32();
            if (format != "BEngine.Project" || schema != 1) throw new InvalidDataException("Unsupported legacy project document.");
            _ = ReadString(reader); // Authoring engine version.
        }
        var project = new RuntimeProjectData { Name = ReadString(reader), StartupScene = ReadString(reader), AssetsDirectory = ReadString(reader) };
        if (version == 1) { _ = ReadString(reader); _ = ReadString(reader); } // Source and editor source directories.
        project.FixedDeltaTime = ReadString(reader);
        project.Window = new() { Title = ReadString(reader), Width = reader.ReadInt32(), Height = reader.ReadInt32(), VSync = ReadBoolean(reader) };
        return project;
    }

    private static void WriteProjectSettings(BinaryWriter writer, RuntimeSettingsData settings)
    {
        WriteString(writer, settings.CompanyName); WriteString(writer, settings.ProductName);
        writer.Write(settings.DefaultScreenWidth); writer.Write(settings.DefaultScreenHeight);
        WriteBoolean(writer, settings.FullScreen); WriteString(writer, settings.GraphicsBackend);
        WriteStrings(writer, settings.Tags);
        WriteCount(writer, settings.SortingLayers.Count, "sorting layer");
        foreach (var layer in settings.SortingLayers)
        {
            writer.Write(layer.Value); WriteString(writer, layer.Name); WriteBoolean(writer, layer.BuiltIn);
            WriteBoolean(writer, layer.IsUi); WriteString(writer, layer.BuiltInId);
        }
        WriteBoolean(writer, settings.RunInBackground);
    }

    private static RuntimeSettingsData ReadProjectSettings(BinaryReader reader, ushort version)
    {
        if (version < 3)
        {
            var format = ReadString(reader); var schema = reader.ReadInt32();
            if (format != "BEngine.ProjectSettings" || schema != 3) throw new InvalidDataException("Unsupported legacy project settings.");
            _ = ReadString(reader); // Authoring locale.
        }
        var settings = new RuntimeSettingsData
        {
            CompanyName = ReadString(reader), ProductName = ReadString(reader), DefaultScreenWidth = reader.ReadInt32(),
            DefaultScreenHeight = reader.ReadInt32(), FullScreen = ReadBoolean(reader), GraphicsBackend = ReadString(reader), SortingLayers = []
        };
        if (version < 3) _ = ReadStrings(reader); // Compile-time defines do not belong to runtime settings.
        settings.Tags = ReadStrings(reader);
        var count = ReadCount(reader, "sorting layer");
        for (var index = 0; index < count; index++) settings.SortingLayers.Add(new()
        {
            Value = reader.ReadUInt64(), Name = ReadString(reader), BuiltIn = ReadBoolean(reader),
            IsUi = ReadBoolean(reader), BuiltInId = ReadString(reader)
        });
        if (version >= 2) settings.RunInBackground = ReadBoolean(reader);
        return settings;
    }

    private static void SkipLegacyPackages(BinaryReader reader)
    {
        var count = ReadCount(reader, "package");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            var id = ReadString(reader);
            var version = ReadString(reader);
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 ||
                string.IsNullOrWhiteSpace(version) || version.Length > 128 || !ids.Add(id.Trim()))
                throw new InvalidDataException("Legacy runtime metadata package list is invalid.");
        }
    }

    private static void WritePlayerBootstrap(BinaryWriter writer, HostDeploymentManifest manifest)
    {
        WriteString(writer, manifest.Format);
        writer.Write(manifest.Version);
        WriteString(writer, manifest.ProductName);
        WriteString(writer, manifest.BuildVersion);
        WriteBoolean(writer, manifest.DevelopmentBuild);
        WriteBoolean(writer, manifest.WritePlayerLog);
        WriteString(writer, manifest.Executable);
        WriteString(writer, manifest.DataDirectory);
        WriteString(writer, manifest.ResourceDirectory);
        WriteString(writer, manifest.NativeLibraryDirectory);
        WriteString(writer, manifest.BuildTargetManifest);
        WriteBoolean(writer, manifest.SplashScreenEnabled);
        WriteString(writer, manifest.SplashImage);
        WriteString(writer, manifest.SplashBackgroundColor);
        writer.Write(manifest.SplashMinimumDurationSeconds);
        WriteString(writer, manifest.CacheDirectory);
        WriteString(writer, manifest.ApplicationScene);
        WriteString(writer, manifest.PlayerResourceArchive);
        WriteString(writer, manifest.BuildTargetResource);
        WriteString(writer, manifest.SplashImageResource);
    }

    private static HostDeploymentManifest ReadPlayerBootstrap(BinaryReader reader, ushort version)
    {
        var manifest = new HostDeploymentManifest
        {
            Format = ReadString(reader),
            Version = reader.ReadInt32(),
            ProductName = ReadString(reader),
            BuildVersion = ReadString(reader),
            DevelopmentBuild = ReadBoolean(reader),
            WritePlayerLog = ReadBoolean(reader),
            Executable = ReadString(reader),
            DataDirectory = ReadString(reader),
            ResourceDirectory = ReadString(reader),
            NativeLibraryDirectory = ReadString(reader),
            BuildTargetManifest = ReadString(reader)
        };
        if (version >= 2)
        {
            manifest.SplashScreenEnabled = ReadBoolean(reader);
            manifest.SplashImage = ReadString(reader);
            manifest.SplashBackgroundColor = ReadString(reader);
            manifest.SplashMinimumDurationSeconds = reader.ReadSingle();
        }
        else
        {
            manifest.Version = 1;
            manifest.SplashScreenEnabled = false;
            manifest.SplashImage = string.Empty;
            manifest.SplashMinimumDurationSeconds = 0;
        }
        if (version >= 3)
        {
            manifest.CacheDirectory = ReadString(reader);
            manifest.ApplicationScene = ReadString(reader);
        }
        if (version >= 4)
        {
            manifest.PlayerResourceArchive = ReadString(reader);
            manifest.BuildTargetResource = ReadString(reader);
            manifest.SplashImageResource = ReadString(reader);
        }
        return manifest;
    }

    private static void Validate(PlayerMetadataDocument document)
    {
        if (document.Format != PlayerMetadataDocument.DocumentFormat ||
            document.Version is < 1 or > PlayerMetadataDocument.CurrentVersion)
            throw new InvalidDataException($"Unsupported runtime metadata '{document.Format}' v{document.Version}.");
        if (document.Project is null || document.ProjectSettings is null || document.ContentSections is null)
            throw new InvalidDataException("Runtime metadata is missing a required document.");
        if (document.Project.Window is null || document.ProjectSettings.Tags is null || document.ProjectSettings.SortingLayers is null)
            throw new InvalidDataException("Runtime metadata contains a null project field.");
        foreach (var layer in document.ProjectSettings.SortingLayers)
            if (layer is null) throw new InvalidDataException("Runtime metadata contains a null project field.");
        RuntimeConfiguration.Validate(document.Project);
        RuntimeConfiguration.Validate(document.ProjectSettings);
        document.PlayerBootstrap?.Validate();
        if (document.ContentSections.Count > MaximumSectionCount - 2 - (document.PlayerBootstrap is null ? 0 : 1))
            throw new InvalidDataException("Runtime metadata section count exceeds the supported range.");
        var ids = new HashSet<ushort>();
        foreach (var section in document.ContentSections)
        {
            if (section.Id == 0 || Enum.IsDefined(typeof(SectionId), section.Id) || !ids.Add(section.Id))
                throw new InvalidDataException("Runtime metadata content section identity is invalid or duplicated.");
            if (section.Version == 0 || section.Data.Length > MaximumSectionBytes)
                throw new InvalidDataException("Runtime metadata content section version or length is invalid.");
        }
    }

    private sealed class MetadataWriteStream : MemoryStream
    {
        private void Reserve(int count)
        {
            if (count > MaximumFileBytes - Position)
                throw new InvalidDataException("Runtime metadata payload exceeds the maximum supported length.");
        }
        public override void Write(byte[] buffer, int offset, int count) { Reserve(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Reserve(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Reserve(1); base.WriteByte(value); }
    }

    private enum SectionId : ushort
    {
        Project = 1,
        ProjectSettings = 2,
        LegacyEnabledPackages = 4,
        PlayerBootstrap = 5
    }
}
