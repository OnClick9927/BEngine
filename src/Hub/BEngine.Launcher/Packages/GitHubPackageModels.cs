using BEngine.Editor.Documents;

namespace BEngine.Launcher;

internal sealed record GitHubPackageSource(string Owner, string Repository, string Reference, string Directory)
{
    internal string RepositoryUrl => $"https://github.com/{Owner}/{Repository}";
    internal string Url => RepositoryUrl + ".git" +
        (Directory.Length == 0 ? "" : "?path=/" + string.Join('/', Directory.Split('/').Select(Uri.EscapeDataString))) +
        (Reference.Length == 0 ? "" : "#" + Uri.EscapeDataString(Reference));
}

internal sealed record GitHubPackageFile(string Path, string Sha, long Size);
internal sealed record GitHubPackageRelease(GitHubPackageSource Source, string Commit, string Directory,
    PackageDefinitionDocument Document, IReadOnlyList<GitHubPackageFile> Files);

internal sealed class GitHubPackageReceipt
{
    public string Format { get; set; } = "BEngine.GitHubPackage";
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string PackageVersion { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public GitHubPackageSource Source { get; set; } = new("", "", "", "");
    public string Commit { get; set; } = "";
    public string PackageDirectory { get; set; } = "";
    public List<GitHubPackageFile> Files { get; set; } = [];
}

internal sealed record InstalledGitHubPackage(string Directory, GitHubPackageReceipt Receipt);

internal sealed record GitHubPackageProgress(string Stage, string Detail = "", long CompletedBytes = 0,
    long TotalBytes = 0, int CompletedFiles = 0, int TotalFiles = 0, double? Percent = null);

internal sealed class GitHubProgressSink<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
