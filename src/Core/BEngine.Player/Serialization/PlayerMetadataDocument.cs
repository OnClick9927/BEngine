using BEngine.Hosting;
using BEngine.ProjectSystem;

namespace BEngine.Player.Serialization;

/// <summary>Runtime execution inputs and opaque sections owned by content providers.</summary>
public sealed class PlayerMetadataDocument
{
    public const string DocumentFormat = "BEngine.RuntimeMetadata";
    public const int CurrentVersion = 3;
    public string Format { get; set; } = DocumentFormat;
    public int Version { get; set; } = CurrentVersion;
    public RuntimeProjectData Project { get; set; } = new();
    public RuntimeSettingsData ProjectSettings { get; set; } = new();
    public HostDeploymentManifest? PlayerBootstrap { get; set; }
    /// <summary>Serialize borrows section memory; Deserialize and Load own the returned data.</summary>
    public List<PlayerMetadataSection> ContentSections { get; set; } = [];
}

/// <summary>Provider-defined identity/version and bytes. Core IDs 1, 2, 4 and 5 are reserved.</summary>
public readonly record struct PlayerMetadataSection(ushort Id, ushort Version, ReadOnlyMemory<byte> Data);
