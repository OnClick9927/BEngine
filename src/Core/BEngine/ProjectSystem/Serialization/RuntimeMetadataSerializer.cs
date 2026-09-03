using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Serialization;

namespace BEngine.ProjectSystem;

public static class RuntimeMetadataSerializer
{
    public const string FileName = "runtime.bmeta";
    public const int MaximumFileBytes = 4 * 1024 * 1024;

    private const ushort FileVersion = 1;
    private const ushort HeaderSize = 48;
    private const int HashSize = 32;
    private const int MaximumSectionBytes = 2 * 1024 * 1024;
    private const int MaximumStringBytes = 64 * 1024;
    private const int MaximumCollectionItems = 4096;
    private const int MinimumSectionCount = 4;
    private const int MaximumSectionCount = 5;
    private static readonly byte[] Magic = "BENGBMET"u8.ToArray();

    public static void Save(RuntimeMetadataDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var bytes = Serialize(document);
        var temporaryPath = $"{fullPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static RuntimeMetadataDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Runtime metadata was not found.", fullPath);
        using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096,
            FileOptions.SequentialScan);
        if (stream.Length is < HeaderSize or > MaximumFileBytes)
            throw new InvalidDataException(
                $"Runtime metadata length {stream.Length} is outside the supported range.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return Deserialize(bytes);
    }

    public static byte[] Serialize(RuntimeMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        var sections = new List<(SectionId Id, ushort Version, byte[] Data)>
        {
            (SectionId.Project, 1, WriteSection(writer => WriteProject(writer, document.Project))),
            (SectionId.ProjectSettings, 1,
                WriteSection(writer => WriteProjectSettings(writer, document.ProjectSettings))),
            (SectionId.AssetBundles, 1,
                WriteSection(writer => WriteAssetBundleSettings(writer, document.AssetBundles))),
            (SectionId.EnabledPackages, 1,
                WriteSection(writer => WritePackages(writer, document.EnabledPackages)))
        };
        if (document.PlayerBootstrap is not null)
            sections.Add((SectionId.PlayerBootstrap, 4,
                WriteSection(writer => WritePlayerBootstrap(writer, document.PlayerBootstrap))));

        byte[] payload;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteString(writer, document.Format);
            writer.Write(document.Version);
            writer.Write(sections.Count);
            foreach (var section in sections)
            {
                writer.Write((ushort)section.Id);
                writer.Write(section.Version);
                writer.Write(section.Data.Length);
                writer.Write(section.Data);
            }
            writer.Flush();
            payload = stream.ToArray();
        }
        if (payload.Length > MaximumFileBytes - HeaderSize)
            throw new InvalidDataException("Runtime metadata payload exceeds the maximum supported length.");

        var result = new byte[HeaderSize + payload.Length];
        Magic.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8, 2), FileVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10, 2), HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12, 4), payload.Length);
        SHA256.HashData(payload, result.AsSpan(16, HashSize));
        payload.CopyTo(result, HeaderSize);
        return result;
    }

    public static RuntimeMetadataDocument Deserialize(ReadOnlySpan<byte> data)
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

        RuntimeMetadataDocument document;
        using (var stream = new MemoryStream(payload.ToArray(), writable: false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
        {
            document = new RuntimeMetadataDocument
            {
                Format = ReadString(reader),
                Version = reader.ReadInt32()
            };
            var sectionCount = ReadCount(reader, "section", MaximumSectionCount);
            if (sectionCount < MinimumSectionCount)
                throw new InvalidDataException(
                    $"Runtime metadata must contain at least {MinimumSectionCount} sections.");
            var seen = new HashSet<SectionId>();
            for (var index = 0; index < sectionCount; index++)
            {
                var idValue = reader.ReadUInt16();
                if (!Enum.IsDefined(typeof(SectionId), idValue))
                    throw new InvalidDataException($"Runtime metadata section {idValue} is unsupported.");
                var id = (SectionId)idValue;
                if (!seen.Add(id))
                    throw new InvalidDataException($"Runtime metadata contains duplicate section '{id}'.");
                var sectionVersion = reader.ReadUInt16();
                if (sectionVersion != 1 &&
                    !(id == SectionId.PlayerBootstrap && sectionVersion is 2 or 3 or 4))
                    throw new InvalidDataException(
                        $"Unsupported runtime metadata section '{id}' version {sectionVersion}.");
                var sectionLength = reader.ReadInt32();
                if (sectionLength is < 0 or > MaximumSectionBytes || sectionLength > stream.Length - stream.Position)
                    throw new InvalidDataException($"Runtime metadata section '{id}' length is invalid.");
                var sectionData = ReadExactly(reader, sectionLength);
                ReadSection(document, id, sectionVersion, sectionData);
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Runtime metadata contains trailing payload data.");
            if (!seen.Contains(SectionId.Project) ||
                !seen.Contains(SectionId.ProjectSettings) ||
                !seen.Contains(SectionId.AssetBundles) ||
                !seen.Contains(SectionId.EnabledPackages))
                throw new InvalidDataException("Runtime metadata is missing a required section.");
        }
        Validate(document);
        return document;
    }

    private static byte[] WriteSection(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        writer.Flush();
        if (stream.Length > MaximumSectionBytes)
            throw new InvalidDataException("Runtime metadata section exceeds the maximum supported length.");
        return stream.ToArray();
    }

    private static void ReadSection(
        RuntimeMetadataDocument document,
        SectionId id,
        ushort version,
        byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        switch (id)
        {
            case SectionId.Project:
                document.Project = ReadProject(reader);
                break;
            case SectionId.ProjectSettings:
                document.ProjectSettings = ReadProjectSettings(reader);
                break;
            case SectionId.AssetBundles:
                document.AssetBundles = ReadAssetBundleSettings(reader);
                break;
            case SectionId.EnabledPackages:
                document.EnabledPackages = ReadPackages(reader);
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

    private static void WriteProject(BinaryWriter writer, ProjectData project)
    {
        WriteString(writer, project.Format);
        writer.Write(project.Version);
        WriteString(writer, project.EngineVersion);
        WriteString(writer, project.Name);
        WriteString(writer, project.StartupScene);
        WriteString(writer, project.AssetsDirectory);
        WriteString(writer, project.ScriptsDirectory);
        WriteString(writer, project.EditorScriptsDirectory);
        WriteString(writer, project.FixedDeltaTime);
        WriteString(writer, project.Window.Title);
        writer.Write(project.Window.Width);
        writer.Write(project.Window.Height);
        WriteBoolean(writer, project.Window.VSync);
    }

    private static ProjectData ReadProject(BinaryReader reader) => new()
    {
        Format = ReadString(reader),
        Version = reader.ReadInt32(),
        EngineVersion = ReadString(reader),
        Name = ReadString(reader),
        StartupScene = ReadString(reader),
        AssetsDirectory = ReadString(reader),
        ScriptsDirectory = ReadString(reader),
        EditorScriptsDirectory = ReadString(reader),
        FixedDeltaTime = ReadString(reader),
        Window = new WindowData
        {
            Title = ReadString(reader),
            Width = reader.ReadInt32(),
            Height = reader.ReadInt32(),
            VSync = ReadBoolean(reader)
        }
    };

    private static void WriteProjectSettings(BinaryWriter writer, ProjectSettingsData settings)
    {
        WriteString(writer, settings.Format);
        writer.Write(settings.Version);
        WriteString(writer, settings.Locale);
        WriteString(writer, settings.CompanyName);
        WriteString(writer, settings.ProductName);
        writer.Write(settings.DefaultScreenWidth);
        writer.Write(settings.DefaultScreenHeight);
        WriteBoolean(writer, settings.FullScreen);
        WriteString(writer, settings.GraphicsBackend);
        WriteStrings(writer, settings.ScriptingDefineSymbols);
        WriteStrings(writer, settings.Tags);
        WriteCount(writer, settings.SortingLayers.Count, "sorting layer");
        foreach (var layer in settings.SortingLayers)
        {
            writer.Write(layer.Value);
            WriteString(writer, layer.Name);
            WriteBoolean(writer, layer.BuiltIn);
            WriteBoolean(writer, layer.IsUi);
            WriteString(writer, layer.BuiltInId);
        }
    }

    private static ProjectSettingsData ReadProjectSettings(BinaryReader reader)
    {
        var settings = new ProjectSettingsData
        {
            Format = ReadString(reader),
            Version = reader.ReadInt32(),
            Locale = ReadString(reader),
            CompanyName = ReadString(reader),
            ProductName = ReadString(reader),
            DefaultScreenWidth = reader.ReadInt32(),
            DefaultScreenHeight = reader.ReadInt32(),
            FullScreen = ReadBoolean(reader),
            GraphicsBackend = ReadString(reader),
            ScriptingDefineSymbols = ReadStrings(reader),
            Tags = ReadStrings(reader),
            SortingLayers = []
        };
        var count = ReadCount(reader, "sorting layer");
        for (var index = 0; index < count; index++)
            settings.SortingLayers.Add(new SortingLayerData
            {
                Value = reader.ReadUInt64(),
                Name = ReadString(reader),
                BuiltIn = ReadBoolean(reader),
                IsUi = ReadBoolean(reader),
                BuiltInId = ReadString(reader)
            });
        return settings;
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
    }

    private static AssetBundleSettingsDocument ReadAssetBundleSettings(BinaryReader reader) => new()
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
        MaxRetries = reader.ReadInt32()
    };

    private static void WritePackages(
        BinaryWriter writer,
        IReadOnlyCollection<RuntimePackageReferenceData> packages)
    {
        WriteCount(writer, packages.Count, "package");
        foreach (var package in packages)
        {
            WriteString(writer, package.Id);
            WriteString(writer, package.Version);
        }
    }

    private static List<RuntimePackageReferenceData> ReadPackages(BinaryReader reader)
    {
        var count = ReadCount(reader, "package");
        var packages = new List<RuntimePackageReferenceData>(count);
        for (var index = 0; index < count; index++)
            packages.Add(new RuntimePackageReferenceData
            {
                Id = ReadString(reader),
                Version = ReadString(reader)
            });
        return packages;
    }

    private static void WritePlayerBootstrap(BinaryWriter writer, PlayerBootstrapManifest manifest)
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
        WriteString(writer, manifest.AssemblyDirectory);
        WriteString(writer, manifest.BuildTargetManifest);
        WriteBoolean(writer, manifest.SplashScreenEnabled);
        WriteString(writer, manifest.SplashImage);
        WriteString(writer, manifest.SplashBackgroundColor);
        writer.Write(manifest.SplashMinimumDurationSeconds);
        WriteString(writer, manifest.CacheDirectory);
        WriteString(writer, manifest.HotUpdateStartupScene);
        WriteString(writer, manifest.PlayerResourceArchive);
        WriteString(writer, manifest.BuildTargetResource);
        WriteString(writer, manifest.SplashImageResource);
    }

    private static PlayerBootstrapManifest ReadPlayerBootstrap(BinaryReader reader, ushort version)
    {
        var manifest = new PlayerBootstrapManifest
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
            AssemblyDirectory = ReadString(reader),
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
            manifest.HotUpdateStartupScene = ReadString(reader);
        }
        if (version >= 4)
        {
            manifest.PlayerResourceArchive = ReadString(reader);
            manifest.BuildTargetResource = ReadString(reader);
            manifest.SplashImageResource = ReadString(reader);
        }
        return manifest;
    }

    private static void WriteStrings(BinaryWriter writer, IReadOnlyCollection<string> values)
    {
        WriteCount(writer, values.Count, "string");
        foreach (var value in values) WriteString(writer, value);
    }

    private static List<string> ReadStrings(BinaryReader reader)
    {
        var count = ReadCount(reader, "string");
        var result = new List<string>(count);
        for (var index = 0; index < count; index++) result.Add(ReadString(reader));
        return result;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        if (value is null) throw new InvalidDataException("Runtime metadata strings cannot be null.");
        var length = Encoding.UTF8.GetByteCount(value);
        if (length > MaximumStringBytes)
            throw new InvalidDataException("Runtime metadata string exceeds the maximum supported length.");
        writer.Write(length);
        if (length == 0) return;
        writer.Write(Encoding.UTF8.GetBytes(value));
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 0 or > MaximumStringBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Runtime metadata string length is invalid.");
        if (length == 0) return string.Empty;
        var bytes = ReadExactly(reader, length);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Runtime metadata contains invalid UTF-8.", exception);
        }
    }

    private static void WriteBoolean(BinaryWriter writer, bool value) => writer.Write(value ? (byte)1 : (byte)0);

    private static bool ReadBoolean(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Runtime metadata boolean value is invalid.")
    };

    private static void WriteCount(BinaryWriter writer, int count, string description)
    {
        if (count is < 0 or > MaximumCollectionItems)
            throw new InvalidDataException(
                $"Runtime metadata {description} count exceeds the supported range.");
        writer.Write(count);
    }

    private static int ReadCount(
        BinaryReader reader,
        string description,
        int maximum = MaximumCollectionItems)
    {
        var count = reader.ReadInt32();
        if (count is < 0 || count > maximum)
            throw new InvalidDataException($"Runtime metadata {description} count is invalid.");
        return count;
    }

    private static byte[] ReadExactly(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("Runtime metadata ended unexpectedly.");
        return bytes;
    }

    private static void Validate(RuntimeMetadataDocument document)
    {
        if (document.Format != RuntimeMetadataDocument.DocumentFormat ||
            document.Version != RuntimeMetadataDocument.CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported runtime metadata '{document.Format}' v{document.Version}.");
        if (document.Project is null || document.ProjectSettings is null || document.AssetBundles is null ||
            document.EnabledPackages is null)
            throw new InvalidDataException("Runtime metadata is missing a required document.");
        if (document.Project.Window is null ||
            document.ProjectSettings.ScriptingDefineSymbols is null ||
            document.ProjectSettings.Tags is null ||
            document.ProjectSettings.SortingLayers is null ||
            document.ProjectSettings.SortingLayers.Any(static layer => layer is null))
            throw new InvalidDataException("Runtime metadata contains a null project field.");
        AssetDataValidation.ValidateProject(document.Project);
        AssetDataValidation.ValidateProjectSettings(document.ProjectSettings);
        AssetDataValidation.ValidateAssetBundleSettings(document.AssetBundles);
        document.PlayerBootstrap?.Validate();
        if (document.EnabledPackages.Count > MaximumCollectionItems ||
            document.EnabledPackages.Any(package => package is null ||
                string.IsNullOrWhiteSpace(package.Id) || package.Id.Length > 256 ||
                string.IsNullOrWhiteSpace(package.Version) || package.Version.Length > 128) ||
            document.EnabledPackages.Select(package => package.Id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.EnabledPackages.Count)
            throw new InvalidDataException("Runtime metadata enabled package list is invalid.");
    }

    private enum SectionId : ushort
    {
        Project = 1,
        ProjectSettings = 2,
        AssetBundles = 3,
        EnabledPackages = 4,
        PlayerBootstrap = 5
    }
}
