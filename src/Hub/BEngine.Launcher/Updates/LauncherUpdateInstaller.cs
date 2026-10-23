using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BEngine.Launcher;

internal static class LauncherUpdateInstaller
{
    private const string PendingFileName = ".bengine-update-pending.json";
    private const string LockFileName = ".bengine-update.lock";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static bool HasPendingUpdate(string installationRoot) => File.Exists(Path.Combine(installationRoot, PendingFileName));

    internal static bool IsUpdateInProgress(string installationRoot)
    {
        var lockPath = Path.Combine(installationRoot, LockFileName);
        if (!File.Exists(lockPath)) return false;
        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    internal static bool TryStartRecovery(string installationRoot, string language, out string error)
    {
        try
        {
            var markerPath = Path.Combine(Path.GetFullPath(installationRoot), PendingFileName);
            EnsureNoReparsePoints(markerPath);
            var marker = JsonSerializer.Deserialize<PendingInstallation>(File.ReadAllText(markerPath), JsonOptions)
                         ?? throw new InvalidDataException("Invalid interrupted update marker.");
            EnsureNoReparsePoints(marker.PlanPath);
            var plan = JsonSerializer.Deserialize<LauncherUpdatePlan>(File.ReadAllText(marker.PlanPath), JsonOptions)
                       ?? throw new InvalidDataException("Invalid interrupted update plan.");
            ValidatePlan(plan);
            if (!Path.GetFullPath(plan.InstallationRoot).Equals(Path.GetFullPath(installationRoot), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(plan.PlanPath).Equals(Path.GetFullPath(marker.PlanPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The interrupted update belongs to a different installation.");
            StartExternalUpdater(plan, language, SafePath(plan.WorkspaceRoot, "installer/BEngine.Hub.Updater.exe"), recoveryOnly: true);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) { error = exception.Message; return false; }
    }

    internal static bool TryStart(LauncherUpdatePlan plan, string language, out string error)
    {
        try
        {
            ValidatePlan(plan);
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the Hub executable.");
            if (!Path.GetFileName(executable).Equals("BEngine.Hub.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Please use the exported BEngine.Hub.exe to install updates.");
            if (!Path.GetFullPath(executable).Equals(Path.Combine(Path.GetFullPath(plan.InstallationRoot), "BEngine.Hub.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update must belong to this Hub installation.");
            var installerDirectory = SafePath(plan.WorkspaceRoot, "installer");
            Directory.CreateDirectory(installerDirectory);
            EnsureNoReparsePoints(installerDirectory);
            var installer = Path.Combine(installerDirectory, "BEngine.Hub.Updater.exe");
            File.Copy(executable, installer, true);
            StartExternalUpdater(plan, language, installer, recoveryOnly: false);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void StartExternalUpdater(LauncherUpdatePlan plan, string language, string installer, bool recoveryOnly)
    {
            using var parent = Process.GetCurrentProcess();
            plan = plan with { ParentProcessId = parent.Id, ParentProcessStartTimeUtcTicks = parent.StartTime.ToUniversalTime().Ticks };
            WriteJson(plan.PlanPath, plan);
            var token = Guid.NewGuid().ToString("N");
            var readyPath = Path.Combine(plan.WorkspaceRoot, $"installer-ready-{token}.json");
            var start = new ProcessStartInfo(installer) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installer)! };
            BEngine.Tooling.ToolEnvironment.ApplyTo(start.Environment);
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(plan.PlanPath);
            start.ArgumentList.Add("--plan-sha256");
            start.ArgumentList.Add(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(plan.PlanPath))));
            start.ArgumentList.Add("--language");
            start.ArgumentList.Add(language);
            start.ArgumentList.Add("--installer-ready");
            start.ArgumentList.Add(readyPath);
            if (recoveryOnly) start.ArgumentList.Add("--recover-only");
            using var child = Process.Start(start) ?? throw new IOException("The external updater could not be started.");
            var timer = Stopwatch.StartNew();
            var ready = false;
            while (timer.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (child.HasExited) throw new IOException($"The external updater exited before it was ready (exit code {child.ExitCode}).");
                if (File.Exists(readyPath))
                {
                    EnsureNoReparsePoints(readyPath);
                    var acknowledgement = JsonSerializer.Deserialize<InstallerReady>(File.ReadAllText(readyPath), JsonOptions);
                    if (acknowledgement?.ProcessId == child.Id && acknowledgement.Token == token) { ready = true; break; }
                }
                Thread.Sleep(50);
            }
            if (!ready)
            {
                if (!child.HasExited) child.Kill();
                throw new IOException("The external updater did not become ready. The current Hub has been kept open.");
            }
    }

    // Called before initializing the editor, service provider, or resources from the installation.
    internal static int? RunUpdater(string[] args)
    {
        var index = Array.IndexOf(args, "--apply-update");
        if (index < 0) return null;
        var languageIndex = Array.IndexOf(args, "--language");
        var language = languageIndex >= 0 && languageIndex + 1 < args.Length ? args[languageIndex + 1] : "zh-CN";
        try
        {
            if (index + 1 >= args.Length) throw new ArgumentException("Missing update plan path.");
            var planPath = Path.GetFullPath(args[index + 1]);
            EnsureNoReparsePoints(planPath);
            var planBytes = File.ReadAllBytes(planPath);
            var hashIndex = Array.IndexOf(args, "--plan-sha256");
            if (hashIndex >= 0 && (hashIndex + 1 >= args.Length ||
                !Convert.ToHexStringLower(SHA256.HashData(planBytes)).Equals(args[hashIndex + 1], StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The update plan changed after it was confirmed. Check for updates again.");
            var plan = JsonSerializer.Deserialize<LauncherUpdatePlan>(planBytes, JsonOptions)
                       ?? throw new InvalidDataException("Invalid update plan.");
            ValidatePlan(plan);
            if (!planPath.Equals(Path.GetFullPath(plan.PlanPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update plan is outside its workspace.");
            var expectedUpdater = SafePath(plan.WorkspaceRoot, "installer/BEngine.Hub.Updater.exe");
            if (!Path.GetFullPath(Environment.ProcessPath ?? AppContext.BaseDirectory).Equals(expectedUpdater, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The updater must run from this update workspace's protected installer directory.");
            BEngine.Tooling.ToolEnvironment.Configure(plan.InstallationRoot);
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            var readyIndex = Array.IndexOf(args, "--installer-ready");
            Action? acknowledgeReady = null;
            if (readyIndex >= 0)
            {
                if (readyIndex + 1 >= args.Length) throw new ArgumentException("Missing updater readiness path.");
                var readyPath = Path.GetFullPath(args[readyIndex + 1]);
                if (!Path.GetDirectoryName(readyPath)!.Equals(Path.GetFullPath(plan.WorkspaceRoot), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The updater readiness path is outside its workspace.");
                var name = Path.GetFileNameWithoutExtension(readyPath);
                if (!name.StartsWith("installer-ready-", StringComparison.Ordinal) || !Guid.TryParseExact(name[16..], "N", out var token))
                    throw new InvalidDataException("Invalid updater readiness token.");
                acknowledgeReady = () => WriteJson(readyPath, new InstallerReady(Environment.ProcessId, token.ToString("N")));
            }
            using var form = new LauncherUpdateInstallerForm(plan, language, acknowledgeReady, Array.IndexOf(args, "--recover-only") >= 0);
            System.Windows.Forms.Application.Run(form);
            return form.Succeeded ? 0 : 1;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.GetBaseException().Message, "BEngine Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static async Task InstallAsync(LauncherUpdatePlan plan, IProgress<LauncherUpdateProgress> progress,
        CancellationToken cancellationToken = default, bool restartHub = true, bool stopProcesses = true, bool recoveryOnly = false)
    {
        ValidatePlan(plan);
        var root = Path.GetFullPath(plan.InstallationRoot);
        Directory.CreateDirectory(root);
        EnsureNoReparsePoints(root);
        var lockPath = Path.Combine(root, LockFileName);
        EnsureNoReparsePoints(lockPath);
        FileStream installLock;
        try { installLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new IOException("Another updater is already using this installation. Wait for it to finish.", exception); }
        await using var heldLock = installLock;
        if (stopProcesses)
        {
            progress.Report(new("closing", "Waiting for this Hub to exit", Percent: 0));
            await WaitForParentAsync(plan, cancellationToken);
            await CloseInstallationProcessesAsync(root, progress, cancellationToken);
        }

        RecoverInterruptedInstallation(root, progress);
        if (recoveryOnly)
        {
            if (restartHub)
            {
                progress.Report(new("restarting", "Starting the restored Hub", Percent: 100));
                using var restored = Process.Start(new ProcessStartInfo(SafePath(root, "BEngine.Hub.exe")) { WorkingDirectory = root, UseShellExecute = false })
                                     ?? throw new IOException("The previous engine was restored, but the Hub could not be started. Open BEngine.Hub.exe manually.");
            }
            progress.Report(new("completed", "The previous installation has been restored.", Percent: 100));
            return;
        }
        await VerifyOriginalFilesAsync(plan, cancellationToken);
        progress.Report(new("verifying", "Verifying downloaded files and unchanged installed files", Percent: 0));
        var changes = plan.Changes.ToDictionary(change => change.Path, StringComparer.OrdinalIgnoreCase);
        long verified = 0;
        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = changes.TryGetValue(file.Path, out var change) && change.Kind != LauncherUpdateChangeKind.Deleted;
            var path = SafePath(changed ? plan.StagingRoot : root, file.Path);
            await VerifyFileAsync(path, file, cancellationToken);
            verified++;
            progress.Report(new("verifying", file.Path, verified, plan.Files.Count, verified * 100d / plan.Files.Count));
        }

        var journal = new InstallJournal { Plan = plan };
        var journalPath = Path.Combine(plan.WorkspaceRoot, "install-journal.json");
        var pendingPath = Path.Combine(root, PendingFileName);
        WriteJson(journalPath, journal);
        File.WriteAllText(Path.Combine(plan.WorkspaceRoot, "RECOVERY.txt"),
            "If an update was interrupted, close all BEngine processes from this installation and run:\r\n\r\n" +
            $"\"{Path.Combine(plan.WorkspaceRoot, "installer", "BEngine.Hub.Updater.exe")}\" --apply-update \"{plan.PlanPath}\" --recover-only --language zh-CN\r\n\r\n" +
            "Backups are under this workspace's backup directory. Do not delete this workspace until Hub starts successfully.\r\n");
        WriteJson(pendingPath, new PendingInstallation(plan.PlanPath));
        Process? restartedHub = null;
        try
        {
            var paths = plan.Changes.Select(change => change.Path).Append(LauncherUpdateManifest.FileName).ToArray();
            var backedUp = 0;
            foreach (var relative in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new("backup", relative, backedUp, paths.Length, backedUp * 100d / paths.Length));
                var target = SafePath(root, relative, allowMetadata: true);
                var exists = File.Exists(target);
                if (Directory.Exists(target)) throw new IOException($"An installation file is occupied by a directory: {relative}");
                if (exists)
                {
                    var backup = SafePath(Path.Combine(plan.WorkspaceRoot, "backup"), relative, allowMetadata: true);
                    CreateParent(backup);
                    File.Copy(target, backup, false);
                }
                journal.Entries.Add(new JournalEntry(relative, exists));
                WriteJson(journalPath, journal);
                backedUp++;
            }

            journal.Phase = "installing";
            WriteJson(journalPath, journal);
            var completed = 0;
            foreach (var change in plan.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = SafePath(root, change.Path);
                if (change.Kind == LauncherUpdateChangeKind.Deleted)
                {
                    if (File.Exists(target)) File.Delete(target);
                }
                else
                {
                    var source = SafePath(plan.StagingRoot, change.Path);
                    ReplaceFile(source, target);
                }
                completed++;
                progress.Report(new("installing", change.Path, completed, plan.Changes.Count, completed * 100d / plan.Changes.Count));
            }
            progress.Report(new("verifying-installed", "Verifying the installed engine", Percent: 0));
            verified = 0;
            foreach (var file in plan.Files)
            {
                await VerifyFileAsync(SafePath(root, file.Path), file, cancellationToken);
                verified++;
                progress.Report(new("verifying-installed", file.Path, verified, plan.Files.Count, verified * 100d / plan.Files.Count));
            }
            WriteJson(Path.Combine(root, LauncherUpdateManifest.FileName), new LauncherUpdateManifest
            {
                CommitSha = plan.CommitSha,
                EngineTreeSha = plan.EngineTreeSha,
                Files = plan.Files
            });
            if (restartHub)
            {
                progress.Report(new("restarting", "Starting the updated Hub", Percent: 100));
                var hubPath = SafePath(root, "BEngine.Hub.exe");
                restartedHub = Process.Start(new ProcessStartInfo(hubPath) { WorkingDirectory = root, UseShellExecute = false })
                               ?? throw new IOException("The updated Hub could not be started.");
                var startupTimer = Stopwatch.StartNew();
                TimeSpan? shownAt = null;
                while (startupTimer.Elapsed < TimeSpan.FromSeconds(30))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    restartedHub.Refresh();
                    if (restartedHub.HasExited)
                        throw new IOException($"The updated Hub exited during startup (exit code {restartedHub.ExitCode}).");
                    if (restartedHub.MainWindowHandle != IntPtr.Zero && restartedHub.MainWindowTitle.Equals("BEngine Hub", StringComparison.Ordinal))
                    {
                        shownAt ??= startupTimer.Elapsed;
                        if (startupTimer.Elapsed - shownAt.Value >= TimeSpan.FromSeconds(1)) break;
                    }
                    else shownAt = null;
                    await Task.Delay(100, cancellationToken);
                }
                if (shownAt is null || startupTimer.Elapsed - shownAt.Value < TimeSpan.FromSeconds(1))
                    throw new IOException("The updated Hub did not open its main window. The previous version will be restored.");
            }
            journal.Phase = "committed";
            WriteJson(journalPath, journal);
            File.Delete(pendingPath);
        }
        catch (Exception installationError)
        {
            if (restartedHub is not null)
            {
                try
                {
                    if (!restartedHub.HasExited && IsInstallationProcess(root, restartedHub.MainModule?.FileName, []))
                    {
                        restartedHub.Kill();
                        await restartedHub.WaitForExitAsync(CancellationToken.None);
                    }
                }
                catch (Exception exception) { AppendLog(plan, exception.ToString()); }
            }
            try
            {
                progress.Report(new("rollback", "Restoring the previous installation", Percent: 0));
                Rollback(journal);
                File.Delete(pendingPath);
                journal.Phase = "rolled-back";
                WriteJson(journalPath, journal);
            }
            catch (Exception rollbackError)
            {
                AppendLog(plan, rollbackError.ToString());
                throw new IOException($"Update failed: {installationError.GetBaseException().Message}\nAutomatic restore could not finish: {rollbackError.GetBaseException().Message}\nBackup and recovery journal: {plan.WorkspaceRoot}\nRun the updater again to retry recovery.", installationError);
            }
            AppendLog(plan, installationError.ToString());
            throw new IOException($"Update failed; the previous installation was restored.\n{installationError.GetBaseException().Message}\nLog and backup: {plan.WorkspaceRoot}", installationError);
        }
        finally { restartedHub?.Dispose(); }

        // Cleanup is deliberately outside the install/rollback transaction. It
        // runs only after the new Hub has passed startup verification and the
        // journal is committed; a busy cache file must never undo an update.
        progress.Report(new("cleanup", "Removing completed update downloads", Percent: 100));
        CleanupSuccessfulUpdate(plan);
        progress.Report(new("completed", "Update complete. Your projects and preferences have been preserved.", Percent: 100));
    }

    private static void CleanupSuccessfulUpdate(LauncherUpdatePlan plan)
    {
        // Temp also owns portable SDK caches and active tool data. An update
        // may remove only its completed downloads and rollback copies.
        // Keep the tiny committed journal/log and the currently running updater.
        // Downloads and rollback copies are no longer needed after success.
        foreach (var relative in new[] { "staging", "partial", "backup" })
        {
            try { DeleteTemporaryTree(SafePath(plan.WorkspaceRoot, relative), plan, Stopwatch.StartNew()); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            { AppendLog(plan, $"Update workspace cleanup: {relative}: {exception.Message}"); }
        }
        try { File.Delete(SafePath(plan.WorkspaceRoot, "RECOVERY.txt")); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { AppendLog(plan, $"Update workspace cleanup: {exception.Message}"); }
    }

    private static void DeleteTemporaryTree(string path, LauncherUpdatePlan plan, Stopwatch cleanupTimer)
    {
        EnsureNoReparsePoints(path);
        if (!Directory.Exists(path)) return;
        // Close the directory enumeration before deleting children. Windows can
        // keep a directory busy while a parent enumerator still owns its handle.
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            try
            {
                var attributes = File.GetAttributes(entry);
                // Treat a newly introduced link as an occupied cache entry.
                // Never recurse through it or recursively delete an untrusted tree.
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) DeleteTemporaryTree(entry, plan, cleanupTimer);
                else RetryDelete(() =>
                {
                    EnsureNoReparsePoints(entry);
                    if ((File.GetAttributes(entry) & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(entry, File.GetAttributes(entry) & ~FileAttributes.ReadOnly);
                    File.Delete(entry);
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            { AppendLog(plan, $"Update workspace cleanup: {entry}: {exception.Message}"); }
        }
        if (!Directory.EnumerateFileSystemEntries(path).Any()) RetryDelete(() =>
        {
            EnsureNoReparsePoints(path);
            Directory.Delete(path);
        });

        void RetryDelete(Action action)
        {
            for (var attempt = 0; ; attempt++)
            {
                try { action(); return; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException &&
                    attempt < 3 && cleanupTimer.Elapsed < TimeSpan.FromSeconds(3))
                { Thread.Sleep(50 * (attempt + 1)); }
            }
        }
    }

    internal static void ValidatePlan(LauncherUpdatePlan plan)
    {
        LauncherUpdateWorkspace.Validate(plan.InstallationRoot, plan.WorkspaceRoot);
        var root = Path.GetFullPath(plan.InstallationRoot);
        if (!IsSha(plan.CommitSha) || !IsSha(plan.EngineTreeSha)) throw new InvalidDataException("The update is not pinned to a valid Git commit and tree.");
        var files = new Dictionary<string, LauncherUpdateFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            _ = SafePath(root, file.Path);
            _ = SafePath(plan.StagingRoot, file.Path);
            if (!IsSha(file.Sha) || file.Size < 0 || !files.TryAdd(file.Path, file))
                throw new InvalidDataException($"Invalid or duplicate manifest entry: {file.Path}");
        }
        if (!files.ContainsKey("BEngine.Hub.exe")) throw new InvalidDataException("The engine manifest does not include BEngine.Hub.exe.");
        var previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in plan.PreviousManagedFiles)
        {
            _ = SafePath(root, path);
            if (!previous.Add(path)) throw new InvalidDataException($"Duplicate previously managed file: {path}");
        }
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in plan.Changes)
        {
            _ = SafePath(root, change.Path);
            if (!Enum.IsDefined(change.Kind) || !changed.Add(change.Path)) throw new InvalidDataException($"Invalid or duplicate operation: {change.Path}");
            if (change.Kind == LauncherUpdateChangeKind.Deleted)
            {
                if (files.ContainsKey(change.Path) || !previous.Contains(change.Path))
                    throw new InvalidDataException($"Refusing to delete an unmanaged or still required file: {change.Path}");
            }
            else if (!files.TryGetValue(change.Path, out var file) || file.Size != change.Size)
                throw new InvalidDataException($"The operation does not match the fixed manifest: {change.Path}");
        }
        var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in plan.OriginalFiles)
        {
            _ = SafePath(root, original.Path);
            if (!originals.Add(original.Path) || original.Size < 0 || original.Exists && !IsSha(original.Sha))
                throw new InvalidDataException($"Invalid original file in the update plan: {original.Path}");
        }
        if (files.Keys.Concat(changed).Any(path => !originals.Contains(path)))
            throw new InvalidDataException("The update plan is missing the local preview baseline. Check for updates again.");
    }

    internal static string SafePath(string root, string relative, bool allowMetadata = false)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || Path.IsPathRooted(relative))
            throw new InvalidDataException($"Invalid relative update path: {relative}");
        var segments = relative.Split('/');
        if (segments[0].Equals("Cache", StringComparison.OrdinalIgnoreCase) ||
            segments[0].Equals("Projects", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Refusing to modify protected user data: {relative}");
        foreach (var segment in segments)
        {
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.Any(character => character < 32 || "<>:\"|?*".Contains(character)))
                throw new InvalidDataException($"Invalid update path: {relative}");
            var device = segment.Split('.')[0];
            if (device.Equals("CON", StringComparison.OrdinalIgnoreCase) || device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("AUX", StringComparison.OrdinalIgnoreCase) || device.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                device.Length == 4 && (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && char.IsAsciiDigit(device[3]))
                throw new InvalidDataException($"Reserved device name in update path: {relative}");
            if (segment.Equals("Preferences", StringComparison.OrdinalIgnoreCase) || segment.Equals("EditorData", StringComparison.OrdinalIgnoreCase) || segment.Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".git", StringComparison.OrdinalIgnoreCase) || (!allowMetadata && segment.StartsWith(".bengine-update", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Refusing to modify protected user data: {relative}");
        }
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!IsWithin(root, full) || full.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update path escapes the installation: {relative}");
        EnsureNoReparsePoints(full);
        return full;
    }

    internal static bool IsInstallationProcess(string installationRoot, string? executablePath, IEnumerable<string> modulePaths)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        var name = Path.GetFileNameWithoutExtension(executablePath);
        if (!name.StartsWith("BEngine.", StringComparison.OrdinalIgnoreCase) || name.Contains("Updater", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsWithin(installationRoot, executablePath)) return true;
        return modulePaths.Any(path => IsWithin(installationRoot, path) &&
            Path.GetFileName(path).StartsWith("BEngine.", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WaitForParentAsync(LauncherUpdatePlan plan, CancellationToken cancellationToken)
    {
        if (plan.ParentProcessId <= 0) return;
        try
        {
            using var parent = Process.GetProcessById(plan.ParentProcessId);
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentProcessStartTimeUtcTicks) return;
            if (!IsInstallationProcess(plan.InstallationRoot, parent.MainModule?.FileName, []))
                throw new InvalidDataException("The recorded parent is not this installation's Hub.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await parent.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new IOException("The Hub has not exited. Close this installation's Hub and retry the update."); }
        }
        catch (ArgumentException) { /* The parent has already exited. */ }
    }

    private static async Task CloseInstallationProcessesAsync(string root, IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                ProcessIdentity? identity;
                try { identity = ReadInstallationProcess(root, process); }
                catch (InvalidOperationException) { continue; }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    // Do not kill a process that cannot be positively identified. Locked files will fail safely before replacement.
                    Trace.WriteLine(exception);
                    continue;
                }
                if (identity is null) continue;
                progress.Report(new("closing", $"{process.ProcessName} (PID {process.Id})", Percent: 0));
                try
                {
                    process.CloseMainWindow();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(6));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var current = ReadInstallationProcess(root, process);
                        if (current != identity) throw new IOException("A process changed identity while the update was waiting.");
                        process.Kill();
                        using var killTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        killTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                        await process.WaitForExitAsync(killTimeout.Token);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { throw new IOException($"Cannot close BEngine process {process.Id}. The previous installation has been kept. {exception.Message}", exception); }
            }
        }
    }

    private static ProcessIdentity? ReadInstallationProcess(string root, Process process)
    {
        if (process.HasExited || !process.ProcessName.StartsWith("BEngine.", StringComparison.OrdinalIgnoreCase)) return null;
        var path = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var modules = IsWithin(root, path) ? [] : process.Modules.Cast<ProcessModule>().Select(module => module.FileName).ToArray();
        return IsInstallationProcess(root, path, modules) ? new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks, path) : null;
    }

    private static async Task VerifyFileAsync(string path, LauncherUpdateFile file, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != file.Size) throw new InvalidDataException($"File size changed or download is incomplete: {file.Path}. Check for updates again.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.UTF8.GetBytes($"blob {file.Size}\0"));
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0) hash.AppendData(buffer, 0, read);
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
        if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(file.Sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"File verification failed: {file.Path}. Check for updates again.");
    }

    private static async Task VerifyOriginalFilesAsync(LauncherUpdatePlan plan, CancellationToken cancellationToken)
    {
        foreach (var original in plan.OriginalFiles)
        {
            var path = SafePath(plan.InstallationRoot, original.Path);
            if (!original.Exists)
            {
                if (File.Exists(path) || Directory.Exists(path))
                    throw new IOException($"The installation changed after the update preview: {original.Path}. Check for updates again.");
            }
            else await VerifyFileAsync(path, new LauncherUpdateFile(original.Path, original.Sha, original.Size), cancellationToken);
        }
    }

    private static void RecoverInterruptedInstallation(string root, IProgress<LauncherUpdateProgress> progress)
    {
        var markerPath = Path.Combine(root, PendingFileName);
        EnsureNoReparsePoints(markerPath);
        if (!File.Exists(markerPath)) return;
        var marker = JsonSerializer.Deserialize<PendingInstallation>(File.ReadAllText(markerPath), JsonOptions)
                     ?? throw new InvalidDataException("Invalid interrupted update marker.");
        EnsureNoReparsePoints(marker.PlanPath);
        var previous = JsonSerializer.Deserialize<LauncherUpdatePlan>(File.ReadAllText(marker.PlanPath), JsonOptions)
                       ?? throw new InvalidDataException("Invalid interrupted update plan.");
        ValidatePlan(previous);
        if (!Path.GetFullPath(previous.InstallationRoot).Equals(root, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(previous.PlanPath).Equals(Path.GetFullPath(marker.PlanPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The interrupted update belongs to a different installation.");
        var journalPath = SafePath(previous.WorkspaceRoot, "install-journal.json");
        var journal = JsonSerializer.Deserialize<InstallJournal>(File.ReadAllText(journalPath), JsonOptions)
                      ?? throw new InvalidDataException("Invalid interrupted update journal.");
        if (journal.Plan is null || !Path.GetFullPath(journal.Plan.InstallationRoot).Equals(root, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(journal.Plan.WorkspaceRoot).Equals(Path.GetFullPath(previous.WorkspaceRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The recovery journal has an invalid installation or workspace.");
        ValidatePlan(journal.Plan);
        if (journal.Phase is not ("committed" or "rolled-back"))
        {
            progress.Report(new("rollback", "Recovering an interrupted installation", Percent: 0));
            Rollback(journal);
            journal.Phase = "rolled-back";
            WriteJson(journalPath, journal);
        }
        File.Delete(markerPath);
    }

    private static void Rollback(InstallJournal journal)
    {
        // A prepared journal only contains backups; no installed file has been modified yet.
        if (journal.Phase == "prepared") return;
        var allowed = new HashSet<string>(journal.Plan.Changes.Select(change => change.Path), StringComparer.OrdinalIgnoreCase)
        { LauncherUpdateManifest.FileName };
        var failures = new List<Exception>();
        foreach (var entry in journal.Entries.AsEnumerable().Reverse())
        {
            try
            {
                if (!allowed.Contains(entry.Path)) throw new InvalidDataException($"Recovery journal contains an unmanaged path: {entry.Path}");
                var target = SafePath(journal.Plan.InstallationRoot, entry.Path, allowMetadata: true);
                if (entry.Existed)
                {
                    var backup = SafePath(Path.Combine(journal.Plan.WorkspaceRoot, "backup"), entry.Path, allowMetadata: true);
                    if (!File.Exists(backup)) throw new IOException($"Recovery backup is missing: {entry.Path}");
                    if (!entry.Path.Equals(LauncherUpdateManifest.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        var original = journal.Plan.OriginalFiles.Single(file => file.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase));
                        if (!original.Exists) throw new InvalidDataException($"Recovery journal does not match the original file: {entry.Path}");
                        VerifyFileAsync(backup, new LauncherUpdateFile(entry.Path, original.Sha, original.Size), CancellationToken.None).GetAwaiter().GetResult();
                    }
                    ReplaceFile(backup, target);
                }
                else if (File.Exists(target)) File.Delete(target);
            }
            catch (Exception exception) { failures.Add(new IOException($"{entry.Path}: {exception.Message}", exception)); }
        }
        if (failures.Count > 0) throw new AggregateException("Some files could not be restored: " + string.Join("; ", failures.Select(exception => exception.Message)), failures);
    }

    private static void ReplaceFile(string source, string target)
    {
        EnsureNoReparsePoints(source);
        CreateParent(target);
        var temporary = target + ".bengine-install-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary, false);
            EnsureNoReparsePoints(target);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void CreateParent(string path)
    {
        EnsureNoReparsePoints(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EnsureNoReparsePoints(path);
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Refusing to follow a symbolic link or junction: {current}");
        }
    }

    private static bool IsWithin(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static void WriteJson<T>(string path, T value)
    {
        CreateParent(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void AppendLog(LauncherUpdatePlan plan, string message)
    {
        try { File.AppendAllText(Path.Combine(plan.WorkspaceRoot, "installer.log"), $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); }
        catch (Exception exception) { Trace.WriteLine(exception); }
    }

    private sealed record ProcessIdentity(int Id, long StartTimeUtcTicks, string ExecutablePath);
    private sealed record PendingInstallation(string PlanPath);
    private sealed record InstallerReady(int ProcessId, string Token);
    private sealed record JournalEntry(string Path, bool Existed);
    private sealed record InstallJournal
    {
        public LauncherUpdatePlan Plan { get; init; } = new();
        public string Phase { get; set; } = "prepared";
        public List<JournalEntry> Entries { get; init; } = [];
    }
}
