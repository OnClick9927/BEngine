using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BEngine.Plugin.VisualStudio.Protocol;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace BEngine.Plugin.VisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("BEngine Tools", "Attach to BEngine and synchronize C# IntelliSense projects", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageId)]
    public sealed class BEnginePackage : AsyncPackage
    {
        public const string PackageId = "e4d861c6-d365-477d-bef9-694961769352";
        internal static readonly Guid CommandSet = new Guid("dc41ed4f-28a2-4e90-a7b5-1079b2ab7dc9");
        // Visual Studio's managed CoreCLR debug engine; Attach2 accepts engine IDs,
        // avoiding localized engine display names and native/Framework autodetection.
        internal const string CoreClrEngine = "{2E36F1D4-B23C-435D-AB41-18E608940038}";
        private DTE2? _dte;
        private bool _busy;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            var automation = await GetServiceAsync(typeof(DTE)).ConfigureAwait(false);
            var menuService = await GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(false);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _dte = automation as DTE2 ?? throw new InvalidOperationException("Visual Studio automation is unavailable.");
            var commands = menuService as OleMenuCommandService ?? throw new InvalidOperationException("Visual Studio menu service is unavailable.");
            AddCommand(commands, 0x100, AttachAsync);
            AddCommand(commands, 0x101, SynchronizeAsync);
            AddCommand(commands, 0x102, OpenSolutionAsync);
        }

        private void AddCommand(OleMenuCommandService commands, int id, Func<Task> action)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var command = new OleMenuCommand((_, __) => JoinableTaskFactory.RunAsync(async () =>
            {
                if (_busy) return;
                _busy = true;
                try { await action(); }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    VsShellUtilities.ShowMessageBox(this, exception.Message, "BEngine", OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
                finally { _busy = false; }
            }).FileAndForget("BEngine/Command"), new CommandID(CommandSet, id));
            command.BeforeQueryStatus += (_, __) => command.Enabled = !_busy;
            commands.AddCommand(command);
        }

        private string? ResolveProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = TargetDiscovery.FindProject(_dte?.Solution?.FullName) ?? TargetDiscovery.FindProject(_dte?.ActiveDocument?.FullName);
            if (project is not null) return project;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a BEngine project folder (contains Project.yaml)",
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                project = TargetDiscovery.FindProject(dialog.SelectedPath);
                if (project is null) throw new InvalidOperationException("The selected folder is not inside a BEngine project (Project.yaml was not found).");
                return project;
            }
        }

        private async Task AttachAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (_dte is null) throw new InvalidOperationException("Visual Studio automation is not available.");
            if (_dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
                throw new InvalidOperationException("Stop or detach the current debugging session before attaching to BEngine.");
            var project = ResolveProject();
            if (project is null) return;
            var targets = await Task.Run(() => TargetDiscovery.Discover(project), DisposalToken).ConfigureAwait(false);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (targets.Count == 0)
                throw new InvalidOperationException("No running BEngine Editor was found for this project. Install its IDE Integration package and open the project in BEngine Editor.");
            var selected = targets.Count == 1 ? targets[0] : TargetPicker.Select(targets);
            if (selected is null) return;
            if (!TargetDiscovery.IsCurrent(project, selected))
                throw new InvalidOperationException("The selected Editor exited or restarted. Attach again to refresh the process list.");
            foreach (Process2 process in _dte.Debugger.LocalProcesses)
            {
                if (process.ProcessID != selected.ProcessId) continue;
                // Revalidate immediately before the actual attach to reject reused PIDs.
                if (!TargetDiscovery.IsCurrent(project, selected)) throw new InvalidOperationException("The Editor process changed before attachment.");
                process.Attach2(CoreClrEngine);
                _dte.StatusBar.Text = $"BEngine debugger attached: {selected.ProjectName} (PID {selected.ProcessId})";
                return;
            }
            throw new InvalidOperationException("Visual Studio cannot access the selected Editor process. Run both tools as the same user and with matching privileges.");
        }

        private async Task SynchronizeAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var project = ResolveProject();
            if (project is null) return;
            _dte!.StatusBar.Text = "BEngine: synchronizing C# projects…";
            using (var progress = new SyncProgressDialog())
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(DisposalToken))
            {
                progress.Canceled += () => cancellation.Cancel();
                progress.Show();
                try
                {
                    await Task.Run(() => ProjectSynchronizer.SynchronizeAsync(project, TimeSpan.FromSeconds(30), cancellation.Token), cancellation.Token).ConfigureAwait(false);
                    await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                    _dte.StatusBar.Text = "BEngine: C# projects synchronized. IntelliSense references are up to date.";
                }
                finally
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    progress.Close();
                }
            }
        }

        private async Task OpenSolutionAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var project = ResolveProject();
            if (project is null) return;
            var solution = await Task.Run(() =>
            {
                var descriptor = TargetDiscovery.ReadProject(project);
                return TargetDiscovery.ValidateSolution(project, descriptor.SolutionPath);
            }, DisposalToken).ConfigureAwait(false);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            // Opening through VS preserves its normal save/close prompts for any
            // currently open solution; the plugin never force-closes documents.
            _dte!.Solution.Open(solution);
        }
    }
}
