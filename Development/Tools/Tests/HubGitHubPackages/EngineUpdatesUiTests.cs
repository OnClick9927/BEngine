using System.Reflection;
using System.Text;
using BEngine.Launcher;

internal static class EngineUpdatesUiTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(string root, Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(root, check); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Engine updates window verification failed", failure);
    }
    private static void Verify(string root, Action<bool, string> check)
    {
        object Field(object target, string name) => target.GetType().GetField(name, Members)!.GetValue(target)!;
        object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
        void Pump(Func<bool> completed)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!completed() && elapsed.Elapsed.TotalSeconds < 30) { Application.DoEvents(); Thread.Sleep(1); }
            if (!completed()) throw new TimeoutException("Engine update UI did not settle.");
        }
        void Save(Form window, string name)
        {
            Application.DoEvents(); using var bitmap = new System.Drawing.Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size)); bitmap.Save(Path.Combine(root, name));
        }
        var remote = new GitTestRepository(Path.Combine(GitTestRepository.RemotesRoot, "engine-window"));
        remote.Publish(new() { ["Engine/BEngine.Hub.exe"] = Encoding.UTF8.GetBytes("updated Hub"), ["Engine/BEgine/current/new.bin"] = Encoding.UTF8.GetBytes("new engine file") });
        var editor = Path.Combine(root, "BEgine", "BEgine-0.1.0", "BEngine.Editor.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!); File.WriteAllText(editor, "");
        var settings = Path.Combine(root, "UpdateWindowSettings.yaml");
        File.WriteAllText(settings, "format: BEngine.LauncherSettings\nversion: 2\npersonalization:\n  language: zh-CN\n  theme: Dark\n  startupPage: Engines\n");
        using var catalog = new LauncherPackageCatalog(root, Path.Combine(root, "Packages"));
        var unavailable = false;
        ProjectLauncherForm Create(string baseRoot) => new(new LauncherOptions(editor), new LauncherHistoryStore(settings), catalog,
            new LauncherEngineCatalog(baseRoot), new LauncherProjectService(catalog), new LauncherUpdateService("https://github.com/vendor/engine.git",
                new GitRepositoryClient(_ => unavailable ? new Uri(Path.Combine(root, "missing-remote")).AbsoluteUri : remote.Url)));
        using var hub = Create(root);
        hub.StartPosition = FormStartPosition.Manual; hub.Location = new Point(-20000, -20000); hub.Show(); Application.DoEvents();
        Pump(() => !(bool)Field(hub, "_environmentBusy"));
        Call(hub, "ShowEnginesPage");
        var button = (Button)Field(hub, "_engineUpdatesButton");
        var github = (Button)Field(hub, "_githubPackagesButton");
        var window = (Form)Field(hub, "_engineUpdatesWindow");
        check(button.Visible && button.Text == "引擎更新" && button.Right < github.Left, "Engine updates appear immediately left of GitHub package management");
        check(((Button)Field(hub, "_enginesNavigation")).Parent!.Controls.OfType<Button>().Count() == 3,
            "Only projects, engines/packages and settings remain in the sidebar");
        check(!((Control)Field(hub, "_pageHost")).Controls.Contains((Control)Field(hub, "_updatesPage")), "The update content belongs to the separate window");
        hub.Size = hub.MinimumSize; Application.DoEvents();
        check(button.Left > ((Label)Field(hub, "_pageTitle")).Right && github.Left > button.Right,
            "The new header entry fits at the Hub's minimum window size");
        Save(hub, "HubEngineUpdateHeader.png");
        hub.ClientSize = new Size(1120, 700);

        void Modal(Action action)
        {
            Exception? error = null; using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += (_, _) =>
            {
                if (!window.Visible) return;
                timer.Stop();
                try { action(); }
                catch (Exception caught) { error = caught; window.Close(); }
            };
            timer.Start(); button.PerformClick();
            if (error is not null) throw error;
        }
        Modal(() =>
        {
            check(window.Owner == hub && window.Modal && window.Text == "引擎更新" && window.BackColor == ((LauncherPalette)Field(hub, "_palette")).Panel,
                "The entry opens an owned modal window with the Hub's title, palette and icon");
            Save(window, "EngineUpdateIdleDark.png");
            Pump(() => !(bool)Field(hub, "_environmentBusy"));
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var checking = (Task)Call(hub, "CheckForUpdatesAsync")!;
            Pump(() => checking.IsCompleted); checking.GetAwaiter().GetResult();
            Save(window, "EngineUpdatePreviewDark.png");
            check(Field(hub, "_pendingUpdate") is LauncherUpdatePlan { HasUpdates: true } && ((Control)Field(hub, "_installUpdateButton")).Visible,
                "The new window checks real Git content and offers installation after a verified preview: " +
                ((Label)Field(hub, "_updateStage")).Text + " · " + ((Label)Field(hub, "_updateDetail")).Text);
            var changeTree = (TreeView)Field(Field(hub, "_updateChanges"), "_tree");
            check(((Label)Field(hub, "_updateSummary")).Text.Contains("下载后确定", StringComparison.Ordinal) &&
                changeTree.Nodes.Cast<TreeNode>().Any(node => node.ToolTipText.Contains("下载后确定", StringComparison.Ordinal)),
                "Metadata-only previews show pending sizes in the summary and file tree instead of zero-byte downloads");
            window.Close();
        });
        check(!window.Visible && !window.IsDisposed && hub.Enabled && Field(hub, "_pendingUpdate") is null,
            "Closing the window returns to the engines page and clears the detached preview"); Probe.Clean(root);
        Task? cancelled = null;
        Modal(() =>
        {
            check(!((Control)Field(hub, "_installUpdateButton")).Visible, "Reopening starts with an idle update window");
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            cancelled = (Task)Call(hub, "CheckForUpdatesAsync")!;
            window.Close();
        });
        Pump(() => cancelled!.IsCompleted); cancelled!.GetAwaiter().GetResult();
        check(!window.Visible && hub.Enabled, "Closing during a Git check cancels the operation before dismissing the modal window"); Probe.Clean(root);
        unavailable = true;
        Modal(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var failed = (Task)Call(hub, "CheckForUpdatesAsync")!; Pump(() => failed.IsCompleted); failed.GetAwaiter().GetResult();
            check(!((Control)Field(hub, "_updateSplit")).Visible && ((Label)Field(hub, "_updateDetail")).Text.Contains("读取", StringComparison.Ordinal),
                "A failed check identifies the Git phase and hides empty commit and change panels");
            Save(window, "EngineUpdateFailure.png"); window.Close();
        });
        unavailable = false;
        var preferences = ((LauncherSettingsData)Field(hub, "_settings")).Personalization;
        preferences.Theme = "Light"; Call(hub, "ApplyPersonalization");
        Modal(() => { check(window.BackColor == LauncherPalette.From(preferences).Panel, "The update window follows a changed Hub theme"); Save(window, "EngineUpdateLight.png"); window.Close(); });
        Call(hub, "ShowSettingsPage"); check(!button.Visible && !github.Visible, "The two management entries are hidden outside the engines page");
        hub.Hide();

        var emptyRoot = Path.Combine(root, "EmptyHub"); Directory.CreateDirectory(emptyRoot);
        using var empty = Create(emptyRoot);
        empty.StartPosition = FormStartPosition.Manual; empty.Location = new Point(-20000, -20000); empty.Show(); Application.DoEvents();
        check(((Control)Field(empty, "_packagesPage")).Visible && ((Button)Field(empty, "_enginesNavigation")).Enabled &&
            !((Button)Field(empty, "_projectsNavigation")).Enabled, "An empty Hub opens the engines page and keeps installation accessible");
        check(((Button)Field(empty, "_engineUpdatesButton")).Visible && !((Button)Field(empty, "_githubPackagesButton")).Enabled,
            "An engine-free installation exposes engine updates while package management waits for an engine");
        empty.Hide();

        var pending = Path.Combine(root, ".bengine-update-pending.json"); File.WriteAllText(pending, "{}");
        try
        {
            using var recovery = Create(root);
            var recoveryWindow = (Form)Field(recovery, "_engineUpdatesWindow");
            var shown = false; Exception? recoveryError = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += (_, _) =>
            {
                if (!recoveryWindow.Visible) return;
                timer.Stop();
                try
                {
                    check(((Control)Field(recovery, "_recoverUpdateButton")).Visible, "Interrupted updates automatically open the same window with a recovery action");
                    Save(recoveryWindow, "EngineUpdateRecovery.png");
                }
                catch (Exception error) { recoveryError = error; }
                finally { shown = true; recoveryWindow.Close(); }
            };
            recovery.StartPosition = FormStartPosition.Manual; recovery.Location = new Point(-20000, -20000); timer.Start(); recovery.Show();
            Pump(() => shown);
            if (recoveryError is not null) throw recoveryError;
            check(((Button)Field(recovery, "_enginesNavigation")).Enabled, "Recovery remains reachable after dismissing the update window");
            recovery.Hide();
        }
        finally { File.Delete(pending); }
        hub.Show(); Call(hub, "ShowEnginesPage");
        Modal(() =>
        {
            hub.GetType().GetField("_startingUpdateInstaller", Members)!.SetValue(hub, true);
            hub.Close();
        });
        check(hub.IsDisposed && window.IsDisposed, "Installer handoff can close the Hub and its owned update window together");
    }
}
