using System.Security.Cryptography;
using System.Text;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;
using ProjectAssetRecord = BEngine.ProjectSystem.Editor.AssetRecord;

namespace BEngine.Editor;

internal static class AssetBundleMenuCommands
{
    private static CancellationTokenSource? _activeBuild;

    [MenuItem("Assets/Build Asset Bundles...", false, 1120)]
    private static void BuildAssetBundles()
    {
        if (_activeBuild is not null || string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return;
        var selections = SelectedAssetPaths();
        EditorFileDialog.OpenFolder("Build Asset Bundles", EditorApplication.projectPath,
            outputDirectory => StartBuild(outputDirectory, selections));
    }

    [MenuItem("Assets/Build Asset Bundles...", true)]
    private static bool ValidateBuildAssetBundles() =>
        _activeBuild is null && !string.IsNullOrWhiteSpace(EditorApplication.projectPath);

    private static void StartBuild(string outputDirectory, IReadOnlyList<string> selections)
    {
        if (_activeBuild is not null) return;
        var cancellation = new CancellationTokenSource();
        _activeBuild = cancellation;
        BuildPipeline.RaiseBuildStarted(outputDirectory);
        Task<AssetBundleBuildResult> buildTask;
        try
        {
            var workspace = ProjectWorkspace.Open(EditorApplication.projectPath);
            var database = new ProjectAssetDatabase(workspace);
            var refresh = database.PrepareRefresh(progress =>
            {
                var fraction = progress.Total <= 0 ? 0f : (float)progress.Completed / progress.Total;
                if (EditorUtility.DisplayCancelableProgressBar("AssetBundle Build",
                        $"Scanning assets: {progress.AssetPath}", fraction))
                    cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }, cancellation.Token);
            database.ApplyRefresh(refresh);
            IReadOnlyList<string> selectedPaths = selections.Count == 0 ? ["Assets"] : selections;
            var definition = new AssetBundleBuildDefinition
            {
                Name = "main",
                AssetPaths = selectedPaths
            };
            var options = new AssetBundleBuildOptions
            {
                PackageName = MakePackageIdentifier(workspace.Project.Name),
                Version = ComputeContentVersion(database.assets, selectedPaths),
                OutputDirectory = outputDirectory
            };
            buildTask = AssetBundleBuilder.BuildAsync(workspace, database, [definition], options,
                new AssetBundleBuildProgressReporter(cancellation), cancellation.Token);
        }
        catch (Exception exception)
        {
            FinishBuild(null, exception, outputDirectory, cancellation);
            return;
        }
        _ = ObserveBuildAsync(buildTask, outputDirectory, cancellation);
    }

    private static async Task ObserveBuildAsync(
        Task<AssetBundleBuildResult> buildTask,
        string outputDirectory,
        CancellationTokenSource cancellation)
    {
        AssetBundleBuildResult? result = null;
        Exception? failure = null;
        try
        {
            result = await buildTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        EditorApplication.QueueMainThread(() => FinishBuild(result, failure, outputDirectory, cancellation),
            "Finish AssetBundle build");
    }

    private static void FinishBuild(
        AssetBundleBuildResult? result,
        Exception? failure,
        string outputDirectory,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (failure is OperationCanceledException)
                Debug.LogWarning("AssetBundle build was canceled.");
            else if (failure is not null)
                Debug.LogException(failure);
            else if (result is not null)
                Debug.Log($"Built AssetBundle package '{result.Catalog.PackageName}' version " +
                          $"'{result.Catalog.Version}' with {result.Catalog.Bundles.Count} bundles and " +
                          $"{result.Catalog.Assets.Count} assets at '{result.VersionDirectory}'.");
        }
        finally
        {
            BuildPipeline.RaiseBuildFinished(outputDirectory, failure is null && result is not null);
            EditorUtility.ClearProgressBar();
            if (ReferenceEquals(_activeBuild, cancellation)) _activeBuild = null;
            cancellation.Dispose();
        }
    }

    private static string[] SelectedAssetPaths()
    {
        var selected = Selection.objects.Select(AssetDatabase.GetAssetPath);
        if (Selection.activeObject is { } active) selected = selected.Append(AssetDatabase.GetAssetPath(active));
        return selected.Where(path => path.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                                     path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            .Select(path => path.Replace('\\', '/').TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ComputeContentVersion(
        IEnumerable<ProjectAssetRecord> records,
        IReadOnlyList<string> selections)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var record in records.Where(record => IsRuntimeAsset(record) &&
                     selections.Any(selection => IsSelected(record.AssetPath, selection)))
                 .OrderBy(record => record.AssetPath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"{record.Guid:N}\0{record.AssetPath.Replace('\\', '/')}\0" +
                $"{record.ArtifactHash}\0{record.ArtifactSize}\0"));
        }
        var value = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return $"content-{value[..24]}";
    }

    private static bool IsRuntimeAsset(ProjectAssetRecord record)
    {
        if (record.IsDirectory) return false;
        var path = record.AssetPath.Replace('\\', '/');
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase)) return false;
        return !path.Split('/').Skip(1).Any(segment =>
            segment.Equals("Editor", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSelected(string assetPath, string selection)
    {
        var path = assetPath.Replace('\\', '/').TrimEnd('/');
        var root = selection.Replace('\\', '/').TrimEnd('/');
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string MakePackageIdentifier(string projectName)
    {
        projectName ??= string.Empty;
        var builder = new StringBuilder(projectName.Length);
        var previousSeparator = false;
        foreach (var character in projectName)
        {
            var allowed = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-';
            if (allowed)
            {
                builder.Append(character);
                previousSeparator = false;
            }
            else if (!previousSeparator)
            {
                builder.Append('-');
                previousSeparator = true;
            }
        }
        var value = builder.ToString().Trim('-', '.', '_');
        if (value.Length == 0) return "game";
        if (value[0] is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9'))
            value = "game-" + value;
        return value.Length <= 128 ? value : value[..128].TrimEnd('-', '.', '_');
    }
}
