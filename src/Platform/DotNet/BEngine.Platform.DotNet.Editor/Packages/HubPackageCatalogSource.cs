using System.Diagnostics;
using System.IO.Pipes;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using BEngine.Tooling;

namespace BEngine.Platform;

/// <summary>Only Hub traverses the shared repository. The editor exchanges metadata over a local pipe.</summary>
public sealed class HubPackageCatalogSource : IPackageCatalogSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PackageCatalogSnapshot> _snapshots = new(StringComparer.Ordinal);

    public PackageCatalogSnapshot GetSnapshot(string packagesRoot, string engineVersion)
    {
        lock (_gate)
            return RequestAsync(Path.GetFullPath(packagesRoot), engineVersion).GetAwaiter().GetResult();
    }

    private async Task<PackageCatalogSnapshot> RequestAsync(string root, string engineVersion)
    {
        BPackageVersion.Validate(engineVersion);
        var key = PackageCatalogTransport.PipeName(root) + ":" + engineVersion;
        _snapshots.TryGetValue(key, out var previous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var pipe = new NamedPipeClientStream(".", PackageCatalogTransport.PipeName(root), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            try { await pipe.ConnectAsync(300, timeout.Token).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                StartHub(root);
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            }
            await PackageCatalogTransport.WriteAsync(pipe, YamlUtility.Serialize(new PackageCatalogRequest
            { EngineVersion = engineVersion, Revision = previous?.Revision ?? string.Empty }), timeout.Token).ConfigureAwait(false);
            var response = YamlUtility.Deserialize<PackageCatalogResponse>(
                await PackageCatalogTransport.ReadAsync(pipe, timeout.Token).ConfigureAwait(false));
            if (response.ProtocolVersion != 1) throw new InvalidDataException("Unsupported Hub package catalog protocol.");
            if (response.Error.Length > 0) throw new InvalidDataException(response.Error);
            if (response.NotModified && previous is not null) return previous;
            var snapshot = response.Snapshot ?? throw new InvalidDataException("Hub returned an empty package catalog response.");
            if (snapshot.EngineVersion != engineVersion ||
                !Path.TrimEndingDirectorySeparator(snapshot.PackagesRoot).Equals(Path.TrimEndingDirectorySeparator(root),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Hub returned a package catalog for another engine or installation.");
            _snapshots[key] = snapshot;
            return snapshot;
        }
        catch (OperationCanceledException exception)
        { throw new IOException("Hub package catalog request timed out. Please reopen Hub and try again.", exception); }
    }

    private static void StartHub(string root)
    {
        var candidates = new List<string?> { Environment.GetEnvironmentVariable("BENGINE_HUB_EXECUTABLE") };
        if (Environment.GetEnvironmentVariable("BENGINE_HUB_DIRECTORY") is { Length: > 0 } configured)
            candidates.Add(Path.Combine(configured, "BEngine.Hub.exe"));
        candidates.Add(Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))!, "BEngine.Hub.exe"));
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            candidates.Add(Path.Combine(directory.FullName, "BEngine.Hub.exe"));
        var executable = Matches3767() ?? throw new FileNotFoundException("The package catalog requires BEngine Hub. Open Hub, or set BENGINE_HUB_EXECUTABLE to its path.");
        [global::BEngine.SkipLoopGuard]
        string? Matches3767()
        {
            var matchSource3767 = candidates;
            ArgumentNullException.ThrowIfNull(matchSource3767, "source");
            if (matchSource3767.GetType() != typeof(global::System.Collections.Generic.List<string?>))
            {
                foreach (var path in (global::System.Collections.Generic.IEnumerable<string?>)matchSource3767)
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        return path;
                }
        
                return default!;
            }
        
            global::System.ReadOnlySpan<string?> matchValues3767 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource3767);
            foreach (var path in matchValues3767)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }
        
            return default!;
        }
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        start.ArgumentList.Add("--ensure-running");
        start.ArgumentList.Add("--packages-root");
        start.ArgumentList.Add(root);
        using var process = Process.Start(start) ?? throw new IOException("Could not start BEngine Hub.");
    }
}
