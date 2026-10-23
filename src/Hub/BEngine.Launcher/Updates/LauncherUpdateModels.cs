namespace BEngine.Launcher;

internal sealed record LauncherUpdateProgress(
    string Stage,
    string Message,
    long Completed = 0,
    long Total = 0,
    double? Percent = null,
    string? FilePath = null,
    string? Status = null);

internal sealed record LauncherUpdateFile(string Path, string Sha, long Size);

internal enum LauncherUpdateChangeKind { Added, Modified, Deleted }

internal sealed record LauncherUpdateChange(string Path, LauncherUpdateChangeKind Kind, long Size = 0);

internal sealed record LauncherUpdateLocalFile(string Path, bool Exists, string Sha, long Size);

internal sealed record LauncherUpdateManifest
{
    internal const string FileName = ".bengine-update-manifest.json";
    public string CommitSha { get; init; } = string.Empty;
    public string EngineTreeSha { get; init; } = string.Empty;
    public IReadOnlyList<LauncherUpdateFile> Files { get; init; } = [];
}

internal sealed record LauncherUpdatePlan
{
    public string InstallationRoot { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string CommitSha { get; init; } = string.Empty;
    public string EngineTreeSha { get; init; } = string.Empty;
    public string CommitMessage { get; init; } = string.Empty;
    public string CommitUrl { get; init; } = string.Empty;
    public IReadOnlyList<LauncherUpdateFile> Files { get; init; } = [];
    public IReadOnlyList<LauncherUpdateChange> Changes { get; init; } = [];
    public IReadOnlyList<LauncherUpdateLocalFile> OriginalFiles { get; init; } = [];
    public IReadOnlyList<string> PreviousManagedFiles { get; init; } = [];
    public DateTimeOffset CheckedAtUtc { get; init; }
    public int ParentProcessId { get; init; }
    public long ParentProcessStartTimeUtcTicks { get; init; }
    public string StagingRoot => System.IO.Path.Combine(WorkspaceRoot, "staging");
    public string PlanPath => System.IO.Path.Combine(WorkspaceRoot, "update-plan.json");
    public bool HasUpdates => Changes.Count != 0;
    public long DownloadBytes => GetChangedBlobGroups().Sum(group => group[0].Size);

    // Git blob identity is content based, so equal content at several installation paths
    // requires one network transfer while every destination remains in the install plan.
    internal LauncherUpdateFile[][] GetChangedBlobGroups()
    {
        var changedPaths = Changes.Where(change => change.Kind != LauncherUpdateChangeKind.Deleted)
            .Select(change => change.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Files.Where(file => changedPaths.Contains(file.Path))
            .GroupBy(file => (Sha: file.Sha.ToUpperInvariant(), file.Size))
            .Select(group => group.ToArray()).ToArray();
    }
}
