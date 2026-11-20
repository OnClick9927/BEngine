using BEngine.ProjectSystem;

namespace BEngine.Launcher;

internal sealed record GitHubPackageChanges(IReadOnlyList<string> Added, IReadOnlyList<string> Modified, IReadOnlyList<string> Retained)
{
    internal bool HasUpdates => Added.Count > 0 || Modified.Count > 0;

    internal static GitHubPackageChanges Compare(IReadOnlyList<InstalledGitHubPackage> installed, IReadOnlyList<GitHubPackageRelease> remote)
    {
        var added = new List<string>(); var modified = new List<string>();
        foreach (var release in remote)
        {
            var local = installed.SingleOrDefault(package => package.Receipt.Id.Equals(release.Document.Id, StringComparison.OrdinalIgnoreCase) &&
                package.Receipt.PackageVersion == release.Document.PackageVersion);
            if (local is null) added.Add(release.Document.PackageVersion);
            else if (!SameFiles(local.Receipt, release)) modified.Add(release.Document.PackageVersion);
        }
        var retained = installed.Where(package => !remote.Any(release =>
            release.Document.Id.Equals(package.Receipt.Id, StringComparison.OrdinalIgnoreCase) && release.Document.PackageVersion == package.Receipt.PackageVersion))
            .Select(package => package.Receipt.PackageVersion).ToArray();
        return new(Sort(added), Sort(modified), Sort(retained));
    }

    private static string[] Sort(IEnumerable<string> versions) => versions.Distinct().OrderDescending(BPackageVersion.Comparer).ToArray();
    private static bool SameFiles(GitHubPackageReceipt local, GitHubPackageRelease remote)
    {
        if (local.Files.Count != remote.Files.Count) return false;
        var prefix = local.PackageDirectory.Length == 0 ? "" : local.PackageDirectory + "/";
        var remotePrefix = remote.Directory.Length == 0 ? "" : remote.Directory + "/";
        var files = local.Files.Where(file => file.Path.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(file => file.Path[prefix.Length..], StringComparer.Ordinal);
        return remote.Files.All(file => file.Path.StartsWith(remotePrefix, StringComparison.Ordinal) &&
            files.TryGetValue(file.Path[remotePrefix.Length..], out var previous) && previous.Size == file.Size &&
            previous.Sha.Equals(file.Sha, StringComparison.OrdinalIgnoreCase));
    }
}
