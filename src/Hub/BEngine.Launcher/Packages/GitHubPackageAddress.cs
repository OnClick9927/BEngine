namespace BEngine.Launcher;

/// <summary>A single Git URL carries the repository, optional package path and optional Git revision.</summary>
internal sealed record GitHubPackageAddress(GitHubPackageSource Source, string TreePath)
{
    internal static GitHubPackageAddress Parse(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Enter an HTTPS github.com Git repository URL.");
        var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length < 2 || parts.Any(part => part is "" or "." or ".." || part.Contains('/') || part.Contains('\\') || part.Any(char.IsControl)))
            throw new InvalidDataException("Invalid GitHub repository URL.");
        var repository = parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1];
        if (repository.Length == 0 || parts.Length > 2 && (parts[2] != "tree" || parts.Length < 4))
            throw new InvalidDataException("Use a Git repository URL or a GitHub /tree/ URL.");
        var directory = "";
        if (uri.Query.Length > 0)
        {
            var parameters = uri.Query[1..].Split('&');
            if (parameters.Length != 1 || !parameters[0].StartsWith("path=", StringComparison.Ordinal))
                throw new InvalidDataException("Use ?path=/package/directory and #branch in the Git URL.");
            directory = Uri.UnescapeDataString(parameters[0]["path=".Length..]);
            if (directory.StartsWith("//", StringComparison.Ordinal)) throw new InvalidDataException("Invalid package directory in Git URL.");
            directory = directory.Trim('/');
            if (directory.Length > 0) LauncherUpdateService.ValidateRelativePath(directory);
        }
        var reference = uri.Fragment.Length == 0 ? "" : Uri.UnescapeDataString(uri.Fragment[1..]);
        if (reference.Any(char.IsControl) || reference.Any(char.IsWhiteSpace)) throw new InvalidDataException("Invalid Git revision in URL.");
        return new(new(parts[0], repository, reference, directory), parts.Length > 2 ? string.Join('/', parts.Skip(3)) : "");
    }
}
