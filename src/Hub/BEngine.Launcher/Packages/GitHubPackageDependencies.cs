using BEngine.Editor.Documents;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

/// <summary>Builds the required dependency closure before any installation is published.</summary>
internal static class GitHubPackageDependencies
{
    private sealed record Candidate(PackageDefinitionDocument Document, GitHubPackageRelease? Release);
    private sealed record Requirement(string Owner, PackageDependencyDocument Dependency);

    internal static async Task<IReadOnlyList<GitHubPackageRelease>> ResolveAsync(
        IReadOnlyList<GitHubPackageRelease> requested, IEnumerable<BPackageDefinition> installed,
        GitHubPackageRepository repository, string engineVersion, IProgress<string> progress, CancellationToken token,
        IDictionary<string, IReadOnlyList<GitHubPackageRelease>>? snapshots = null)
    {
        var roots = requested.Select(release => new Candidate(release.Document, release)).ToArray();
        var local = installed.Where(package => BPackageCompatibility.SupportsEngine(package.Document, engineVersion))
            .Select(package => new Candidate(package.Document, null)).ToArray();
        var choices = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, Candidate[]>(StringComparer.Ordinal);

        // Rebuild reachability when another declaration adds an assembly target. This also
        // drops dependencies belonging only to a replaced candidate version.
        for (var pass = 0; pass < 512; pass++)
        {
            token.ThrowIfCancellationRequested();
            var required = new Dictionary<string, List<Requirement>>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<PackageDefinitionDocument>();
            var pending = new Queue<Candidate>(roots);
            while (pending.TryDequeue(out var package))
            {
                if (!visited.Add(package.Document)) continue;
                foreach (var dependency in (package.Document.Runtime?.Dependencies ?? []).Concat(package.Document.Editor?.Dependencies ?? []))
                {
                    if (dependency.Optional) continue;
                    if (!required.TryGetValue(dependency.PackageId, out var declarations))
                        required.Add(dependency.PackageId, declarations = []);
                    declarations.Add(new(package.Document.Id, dependency));
                    if (choices.TryGetValue(dependency.PackageId, out var choice) && HasTarget(choice, dependency.Target))
                        pending.Enqueue(choice);
                }
            }
            if (required.Count > 512) throw new InvalidDataException("The GitHub dependency graph exceeds 512 packages.");

            var changed = false;
            string? missing = null;
            foreach (var (id, declarations) in required)
            {
                token.ThrowIfCancellationRequested();
                var targets = declarations.Select(item => item.Dependency.Target).Distinct().ToArray();
                bool Matches(Candidate candidate) => candidate.Document.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                    targets.All(target => HasTarget(candidate, target));
                Candidate? Latest(IEnumerable<Candidate> candidates) => candidates.Where(Matches)
                    .OrderByDescending(candidate => candidate.Document.PackageVersion, BPackageVersion.Comparer).FirstOrDefault();
                var candidate = Latest(roots) ?? Latest(local);
                if (candidate is null)
                {
                    var urls = declarations.Select(item => item.Dependency.GitUrl).Where(url => !string.IsNullOrWhiteSpace(url))
                        .Select(url =>
                        {
                            var address = GitHubPackageAddress.Parse(url!);
                            return address.TreePath.Length == 0 ? address.Source.Url : url!.Trim();
                        }).Distinct(StringComparer.Ordinal).ToArray();
                    if (urls.Length == 0)
                    {
                        // Another reachable dependency may supply the address on the next pass.
                        missing ??= $"{declarations[0].Owner} requires {id} ({string.Join(", ", targets)}), but no compatible local release or gitUrl is available.";
                        continue;
                    }
                    if (urls.Length != 1)
                        throw new InvalidDataException($"Conflicting gitUrl sources for dependency {id}. Use the same Git address in its declarations.");
                    if (!sources.TryGetValue(urls[0], out var releases))
                    {
                        progress.Report("Reading GitHub dependencies");
                        IReadOnlyList<GitHubPackageRelease> remote;
                        if (snapshots is not null && snapshots.TryGetValue(urls[0], out var cached)) remote = cached;
                        else
                        {
                            remote = await repository.ReadAsync(urls[0], progress, token).ConfigureAwait(false);
                            snapshots?.Add(urls[0], remote);
                        }
                        releases = remote.Where(release => BPackageCompatibility.SupportsEngine(release.Document, engineVersion))
                            .Select(release => new Candidate(release.Document, release)).ToArray();
                        sources.Add(urls[0], releases);
                    }
                    candidate = Latest(releases);
                    if (candidate is null)
                        throw new InvalidDataException($"Git source {urls[0]} has no engine-compatible release of {id} with target {string.Join(", ", targets)}.");
                }
                if (!ReferenceEquals(choices.GetValueOrDefault(id), candidate))
                {
                    choices[id] = candidate;
                    changed = true;
                }
            }
            if (changed) continue;
            if (missing is not null) throw new InvalidDataException(missing);
            // Only selected versions and reachable Git dependencies enter the transaction.
            return requested.Concat(required.Keys.Select(id => choices[id].Release).OfType<GitHubPackageRelease>()
                .Where(release => !requested.Contains(release)).Distinct()).ToArray();
        }
        throw new InvalidDataException("GitHub dependency resolution did not converge. Check conflicting package versions and sources.");
    }

    private static bool HasTarget(Candidate package, string target) =>
        (target == "runtime" ? package.Document.Runtime : package.Document.Editor) is not null;
}
