namespace BEngine.Editor.Build;
public sealed class PlayerStrippingReport
{
    public string Format { get; set; } = "BEngine.PlayerStrippingReport";
    public int Version { get; set; } = 1;
    public string Target { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int DependencyPasses { get; set; }
    public List<PlayerStrippingReportEntry> Assemblies { get; set; } = [];
    public List<PlayerStrippingReportEntry> Resources { get; set; } = [];
    public string LinkerLog { get; set; } = string.Empty;
    public string HostPublishLogPath { get; set; } = string.Empty;
    public List<PlayerStrippingReportEntry> HostAssemblies { get; set; } = [];
    public bool TextBindingsIncluded { get; set; } = true;
    public List<string> LinkDescriptors { get; set; } = [];
    public string? ContentEncryptionKeyFingerprint { get; set; }
    public long AotArchiveBytes { get; set; }
    public long PlayerResourceArchiveBytes { get; set; }
    public List<string> GraphicsBackends { get; set; } = [];
    public bool RuntimeShaderCompilerIncluded { get; set; }
    public int OfflineShaderCount { get; set; }
    public long OfflineShaderBytes { get; set; }
    public string? HostContractPath { get; set; }
}

public sealed class PlayerStrippingReportEntry
{
    public string Name { get; set; } = string.Empty;
    public long OriginalBytes { get; set; }
    public long PublishedBytes { get; set; }
    public long? StoredBytes { get; set; }
    public bool Included { get; set; }
    public string Reason { get; set; } = string.Empty;
}
