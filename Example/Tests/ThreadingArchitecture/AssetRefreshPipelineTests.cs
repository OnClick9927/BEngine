using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.ThreadingArchitecture;

internal static class AssetRefreshPipelineTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineAssetPipeline_{Guid.NewGuid():N}");
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(root, "Asset Pipeline Threading");
            var database = new ProjectAssetDatabase(workspace);
            database.Refresh();
            var ownerThreadId = Environment.CurrentManagedThreadId;
            var baselinePaths = database.assets.Select(asset => asset.AssetPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var notifications = new List<AssetNotification>();

            database.assetsChanged += Capture;
            try
            {
                var newDirectory = Path.Combine(workspace.AssetsPath, "Threading");
                var newSourcePath = Path.Combine(newDirectory, "Prepared.txt");
                Directory.CreateDirectory(newDirectory);
                File.WriteAllText(newSourcePath, "prepared in background");

                var prepareThreadId = 0;
                var progressThreadIds = new List<int>();
                var snapshot = Task.Run(() =>
                {
                    prepareThreadId = Environment.CurrentManagedThreadId;
                    return database.PrepareRefresh(_ =>
                        progressThreadIds.Add(Environment.CurrentManagedThreadId));
                }).GetAwaiter().GetResult();

                TestAssert.Require(prepareThreadId != ownerThreadId &&
                                   progressThreadIds.Count > 0 &&
                                   progressThreadIds.All(id => id == prepareThreadId),
                    "Asset PrepareRefresh or its progress callbacks did not stay on the background worker.");
                TestAssert.Require(notifications.Count == 0,
                    "Asset PrepareRefresh raised assetsChanged before the main-thread Apply.");
                TestAssert.Require(database.GetRecord("Assets/Threading/Prepared.txt") is null &&
                                   baselinePaths.SetEquals(database.assets.Select(asset => asset.AssetPath)),
                    "Asset PrepareRefresh mutated the currently visible AssetDatabase snapshot.");

                var applied = database.ApplyRefresh(snapshot);
                TestAssert.Require(database.GetRecord("Assets/Threading/Prepared.txt") is not null,
                    "Asset ApplyRefresh did not publish the prepared file.");
                TestAssert.Require(applied.Any(change =>
                        change.Kind == AssetChangeKind.Imported &&
                        change.AssetPath.Equals("Assets/Threading/Prepared.txt",
                            StringComparison.OrdinalIgnoreCase)),
                    "Asset ApplyRefresh did not return the prepared file's import change.");
                TestAssert.Require(notifications is [{ ThreadId: var notificationThreadId, Changes: var changes }] &&
                                   notificationThreadId == ownerThreadId &&
                                   changes.Any(change => change.AssetPath.Equals(
                                       "Assets/Threading/Prepared.txt", StringComparison.OrdinalIgnoreCase)),
                    "Asset ApplyRefresh did not raise exactly one assetsChanged notification on the owner thread.");

                VerifyCanceledPrepareDoesNotApply(workspace, database, notifications);
            }
            finally
            {
                database.assetsChanged -= Capture;
            }

            void Capture(IReadOnlyList<AssetChange> changes) =>
                notifications.Add(new AssetNotification(
                    Environment.CurrentManagedThreadId, changes.ToArray()));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void VerifyCanceledPrepareDoesNotApply(
        ProjectWorkspace workspace,
        ProjectAssetDatabase database,
        List<AssetNotification> notifications)
    {
        notifications.Clear();
        var before = database.assets.Select(asset => asset.AssetPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canceledSourcePath = Path.Combine(workspace.AssetsPath, "Threading", "Canceled.txt");
        File.WriteAllText(canceledSourcePath, "must not be applied");
        using var cancellation = new CancellationTokenSource();

        var failure = Task.Run<Exception?>(() =>
        {
            try
            {
                _ = database.PrepareRefresh(progress =>
                {
                    if (progress.Completed > 0) cancellation.Cancel();
                }, cancellation.Token);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }).GetAwaiter().GetResult();

        TestAssert.Require(failure is OperationCanceledException,
            $"A canceled Asset PrepareRefresh completed with {failure?.GetType().Name ?? "no exception"}.");
        TestAssert.Require(notifications.Count == 0 &&
                           database.GetRecord("Assets/Threading/Canceled.txt") is null &&
                           before.SetEquals(database.assets.Select(asset => asset.AssetPath)),
            "A canceled Asset PrepareRefresh changed or notified the visible snapshot without Apply.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record AssetNotification(int ThreadId, IReadOnlyList<AssetChange> Changes);
}
