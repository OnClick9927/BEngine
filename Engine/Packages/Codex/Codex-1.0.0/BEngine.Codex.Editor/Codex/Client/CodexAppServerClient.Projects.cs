using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BEngine.Editor.Codex;

public sealed partial class CodexAppServerClient
{
    private string _localProjectId = string.Empty;
    private string _projectCatalogError = string.Empty;

    private async Task EnsureLocalProjectAsync()
    {
        try
        {
            var normalizedRoot = NormalizeProjectRoot(ProjectRoot);
            var visitedCursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            do
            {
                var result = await SendRequestAsync("project/list", new { cursor, limit = 100 }).ConfigureAwait(false);
                if (!result.TryGetProperty("data", out var projects) || projects.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Codex did not return its local projects.");
                foreach (var project in projects.EnumerateArray())
                {
                    if (!project.TryGetProperty("roots", out var roots) || roots.ValueKind != JsonValueKind.Array) continue;
                    if (!roots.EnumerateArray().Any(root =>
                            ReadString(root, "path") is { Length: > 0 } path &&
                            NormalizeProjectRoot(path) == normalizedRoot)) continue;
                    SetLocalProject(ReadString(project, "id"));
                    return;
                }
                cursor = ReadString(result, "nextCursor");
                if (cursor.Length > 0 && !visitedCursors.Add(cursor))
                    throw new InvalidDataException("Codex repeated a local-project pagination cursor.");
            } while (!string.IsNullOrEmpty(cursor));

            var projectName = EditorHostServices.Paths.GetFileName(ProjectRoot.TrimEnd('/', '\\'));
            var created = await SendRequestAsync("project/create", new
            {
                idempotencyKey = "bengine:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot))),
                name = string.IsNullOrWhiteSpace(projectName) ? ProjectRoot : projectName,
                roots = new[] { new { path = ProjectRoot } }
            }).ConfigureAwait(false);
            if (!created.TryGetProperty("project", out var createdProject))
                throw new InvalidDataException("Codex did not return the registered local project.");
            SetLocalProject(ReadString(createdProject, "id"));
        }
        catch (Exception exception)
        {
            lock (_sync) _localProjectId = string.Empty;
            SetProjectCatalogError(exception.Message);
        }
    }

    private async Task AssignThreadProjectAsync(string threadId)
    {
        string projectId;
        lock (_sync) projectId = _localProjectId;
        if (string.IsNullOrEmpty(projectId)) return;
        try
        {
            await SendRequestAsync("thread/metadata/update", new { threadId, projectId }).ConfigureAwait(false);
            SetLocalProject(projectId);
        }
        catch (Exception exception)
        {
            SetProjectCatalogError(exception.Message);
        }
    }

    private void SetProjectCatalogError(string message)
    {
        lock (_sync)
        {
            _projectCatalogError = "Desktop project: " + message +
                " Local conversations remain available; use Open in Codex to view them.";
            _revision++;
        }
        RaiseChanged();
    }

    private void SetLocalProject(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new InvalidDataException("Codex returned an empty local-project ID.");
        lock (_sync)
        {
            _localProjectId = projectId;
            _projectCatalogError = string.Empty;
            _revision++;
        }
        RaiseChanged();
    }

    private static string NormalizeProjectRoot(string path)
    {
        if (EditorHostServices.Platform.IsWindows)
        {
            path = path.Replace('/', '\\');
            if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) path = "\\\\" + path[8..];
            else if (path.StartsWith("\\\\?\\", StringComparison.Ordinal)) path = path[4..];
        }
        path = EditorHostServices.Paths.GetFullPath(path).TrimEnd('/', '\\');
        return EditorHostServices.Platform.IsWindows ? path.ToUpperInvariant() : path;
    }
}
