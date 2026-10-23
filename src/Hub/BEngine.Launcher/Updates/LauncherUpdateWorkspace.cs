using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BEngine.Launcher;

internal static class LauncherUpdateWorkspace
{
    private const string SnapshotFileName = "download-snapshot.json";

    // Keep downloaded content on the same volume as the Hub and outside every
    // managed install destination. Temp is protected by both manifest validators.
    internal static string Create(string installationRoot)
    {
        var workspace = Path.Combine(installationRoot, "Temp", "HubUpdates", Guid.NewGuid().ToString("N"));
        Validate(installationRoot, workspace);
        return Path.GetFullPath(workspace);
    }

    // A snapshot keeps its workspace across checks and Hub restarts. An older
    // Hub did not record the snapshot until all downloads succeeded; adopt its
    // unfinished staging directory without trusting any of its file contents.
    internal static string GetOrCreate(string installationRoot, string commitSha)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        if (commitSha.Length != 40 || commitSha.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The update snapshot must have a valid commit SHA.");
        commitSha = commitSha.ToLowerInvariant();
        var identity = new DownloadSnapshot(root, commitSha);
        var parent = Path.Combine(root, "Temp", "HubUpdates");
        EnsureNoReparsePoints(parent);
        Directory.CreateDirectory(parent);
        var selectionPath = Path.Combine(parent, ".workspace-selection.lock");
        EnsureNoReparsePoints(selectionPath);
        using var selectionLock = OpenExclusiveLock(selectionPath);
        var candidates = Directory.EnumerateDirectories(parent)
            .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        foreach (var candidate in candidates)
        {
            Validate(root, candidate);
            var metadataPath = Path.Combine(candidate, SnapshotFileName);
            EnsureNoReparsePoints(metadataPath);
            if (File.Exists(metadataPath))
            {
                try
                {
                    var metadata = JsonSerializer.Deserialize<DownloadSnapshot>(File.ReadAllText(metadataPath));
                    if (metadata is not null && string.Equals(metadata.CommitSha, commitSha, StringComparison.OrdinalIgnoreCase) &&
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(metadata.InstallationRoot))
                            .Equals(root, StringComparison.OrdinalIgnoreCase)) return candidate;
                }
                catch (Exception error) when (error is IOException or JsonException or ArgumentException or NotSupportedException)
                {
                    // Unknown or damaged ownership metadata is not an unclaimed workspace.
                }
            }
        }
        foreach (var candidate in candidates)
        {
            // Never repurpose a workspace that already belongs to another snapshot.
            if (!File.Exists(Path.Combine(candidate, SnapshotFileName)) && IsUnclaimedDownload(candidate))
            {
                try
                {
                    using var downloadLock = AcquireDownloadLock(root, candidate);
                    WriteSnapshot(candidate, identity);
                    return candidate;
                }
                catch (IOException) { /* Another Hub is still using this old download. */ }
            }
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant() + "\n" + commitSha)))[..32]
            .ToLowerInvariant();
        var workspace = Path.Combine(parent, key);
        Validate(root, workspace);
        Directory.CreateDirectory(workspace);
        WriteSnapshot(workspace, identity);
        return workspace;
    }

    internal static FileStream AcquireDownloadLock(string installationRoot, string workspaceRoot)
    {
        Validate(installationRoot, workspaceRoot);
        Directory.CreateDirectory(workspaceRoot);
        var path = Path.Combine(workspaceRoot, "download.lock");
        EnsureNoReparsePoints(path);
        return OpenExclusiveLock(path);
    }

    internal static void ThrowIfInstallationBusy(string installationRoot)
    {
        const string message = "An update installation is already in progress. Finish it before resuming downloads.";
        var root = Path.GetFullPath(installationRoot);
        var pending = Path.Combine(root, ".bengine-update-pending.json");
        var path = Path.Combine(root, ".bengine-update.lock");
        EnsureNoReparsePoints(root);
        EnsureNoReparsePoints(pending);
        EnsureNoReparsePoints(path);
        if (File.Exists(pending)) throw new IOException(message);
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new IOException(message, error); }
        if (File.Exists(pending)) throw new IOException(message);
    }

    internal static bool HasRetainedDownload(LauncherUpdatePlan plan)
    {
        Validate(plan.InstallationRoot, plan.WorkspaceRoot);
        foreach (var name in new[] { "staging", "partial" })
        {
            var path = Path.Combine(plan.WorkspaceRoot, name);
            EnsureNoReparsePoints(path);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) return true;
        }
        return false;
    }

    private static bool IsUnclaimedDownload(string path) =>
        !new[] { "update-plan.json", "update-plan.json.part", "install-journal.json", "RECOVERY.txt", "backup", "installer" }
            .Any(name => File.Exists(Path.Combine(path, name)) || Directory.Exists(Path.Combine(path, name))) &&
        (Directory.Exists(Path.Combine(path, "staging")) || Directory.Exists(Path.Combine(path, "partial")));

    private static FileStream OpenExclusiveLock(string path) =>
        new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void WriteSnapshot(string workspace, DownloadSnapshot identity)
    {
        var path = Path.Combine(workspace, SnapshotFileName);
        var temporary = path + ".part";
        EnsureNoReparsePoints(path);
        EnsureNoReparsePoints(temporary);
        File.WriteAllText(temporary, JsonSerializer.Serialize(identity));
        File.Move(temporary, path, true);
    }

    private sealed record DownloadSnapshot(string InstallationRoot, string CommitSha);

    internal static void Validate(string installationRoot, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(installationRoot) || string.IsNullOrWhiteSpace(workspaceRoot) ||
            !Path.IsPathFullyQualified(installationRoot) || !Path.IsPathFullyQualified(workspaceRoot))
            throw new InvalidDataException("The installation and workspace must be absolute paths.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        var workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (IsWithin(root, workspace))
        {
            var workspaceParent = Path.GetDirectoryName(workspace);
            var expectedParent = Path.Combine(root, "Temp", "HubUpdates");
            if (!string.Equals(workspaceParent, expectedParent, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(workspace), "N", out _))
                throw new InvalidDataException("The update workspace must be a unique directory under this Hub's Temp/HubUpdates folder.");
        }
        // Existing external plans must remain recoverable after this upgrade.
        // An enclosing workspace could include managed files and is never safe.
        else if (IsWithin(workspace, root))
            throw new InvalidDataException("The update workspace cannot contain the installation directory.");
        EnsureNoReparsePoints(root);
        EnsureNoReparsePoints(workspace);
    }

    private static bool IsWithin(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Refusing to follow a symbolic link or junction: {current}");
    }
}
