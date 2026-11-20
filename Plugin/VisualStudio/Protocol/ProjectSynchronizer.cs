using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BEngine.Plugin.VisualStudio.Protocol
{
    internal static class ProjectSynchronizer
    {
        internal static async Task<string> SynchronizeAsync(string project, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (TargetDiscovery.Discover(project).Count == 0)
                throw new InvalidOperationException("Open this project in BEngine Editor with IDE Integration installed before synchronizing.");
            var ide = TargetDiscovery.IdeDirectory(project);
            var id = Guid.NewGuid().ToString("N");
            var request = Path.Combine(ide, "Requests", id + ".json");
            var response = Path.Combine(ide, "Responses", id + ".json");
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(timeout);
                try
                {
                    JsonFile.WriteAtomic(request, new SyncRequest { RequestId = id });
                    while (true)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        if (File.Exists(response))
                        {
                            var result = JsonFile.Read<SyncResponse>(response);
                            if (result.SchemaVersion != 1 || result.RequestId != id) throw new InvalidDataException("Mismatched IDE synchronization response.");
                            if (!result.Success) throw new InvalidOperationException("BEngine project synchronization failed: " + result.Error);
                            return TargetDiscovery.ValidateSolution(project, result.SolutionPath);
                        }
                        await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new TimeoutException("BEngine Editor did not respond within " + timeout.TotalSeconds + " seconds. Check that it is open and responsive."); }
                finally
                {
                    TryDelete(request);
                    TryDelete(response);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
