using System.Reflection;
using BEngine.Launcher;

internal static class GitHubPackagesUiTests
{
    internal static void Run(GitHubPackageManager manager, GitHubPackageRepository repository, GitTestRepository remote, string root, Action<bool, string> check)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { Verify(manager, repository, remote, root, check); } catch (Exception failure) { error = failure; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw new Exception("Git UI verification failed", error);
    }
    private static void Verify(GitHubPackageManager manager, GitHubPackageRepository repository, GitTestRepository remote, string root, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Field(object form, string name) => form.GetType().GetField(name, flags)!.GetValue(form)!;
        object? Call(object form, string name, params object[] values) => form.GetType().GetMethod(name, flags)!.Invoke(form, values);
        void Wait(Task task)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && elapsed.Elapsed.TotalSeconds < 60) { Application.DoEvents(); Thread.Sleep(1); }
            if (!task.IsCompleted) throw new TimeoutException("UI operation timed out");
            task.GetAwaiter().GetResult(); Application.DoEvents();
        }
        void Execute(GitHubPackagesForm form, string method, string title)
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var action = (Func<CancellationToken, Task>)form.GetType().GetMethod(method, flags)!.CreateDelegate(typeof(Func<CancellationToken, Task>), form);
            Wait((Task)Call(form, "RunAsync", action, title)!);
        }
        void Save(GitHubPackagesForm form, string name)
        {
            Application.DoEvents(); using var image = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(root, name));
        }
        using var dialog = new GitHubPackagesForm(manager, repository, new("zh-CN"), LauncherPalette.From(new() { Theme = "Dark", AccentColor = "Blue" }), "0.1.0");
        dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.Show();
        var list = (ListView)Field(dialog, "_packages"); var url = (TextBox)Field(dialog, "_url");
        var read = (Button)Field(dialog, "_read"); var primary = (Button)Field(dialog, "_primary"); var cancel = (Button)Field(dialog, "_cancel");
        check(read.Visible && !primary.Visible && !cancel.Visible, "Install initially shows only the read action");
        check(dialog.GetType().GetFields(flags).Count(field => field.FieldType == typeof(TextBox)) == 1, "Package pages contain no detail text box below their lists");
        url.Text = "https://github.com/vendor/packages.git#main";
        Execute(dialog, "ReadAsync", "Read GitHub packages");
        check(list.Items.Count == 2 && primary.Visible && primary.Left > read.Right && !cancel.Visible, "Git discovery preserves package rows and the stage-dependent action layout");
        Execute(dialog, "ApplyAsync", "Install");
        check(!read.Visible && !primary.Visible && !cancel.Visible && ((Label)Field(dialog, "_resultTitle")).Text.Contains("安装成功", StringComparison.Ordinal), "Git installation reports success and hides completed actions"); Probe.Clean(root);
        Call(dialog, "SwitchPage", GitHubPackagePage.Update);
        remote.Publish(GitTestRepository.Packages(3, true));
        foreach (ListViewItem item in list.Items) item.Selected = false;
        Execute(dialog, "CheckUpdatesAsync", "Check updates");
        var animation = list.Items.Cast<ListViewItem>().Single(item => item.Text == "com.vendor.animation");
        var helper = list.Items.Cast<ListViewItem>().Single(item => item.Text == "com.vendor.helper");
        check(animation.SubItems[4].Text == "可更新" && animation.SubItems[3].Text.Contains("内容更新", StringComparison.Ordinal) && helper.SubItems[4].Text == "已是最新",
            "Git-based checking displays actual changes per package without false updates for unchanged packages");
        Save(dialog, "GitUpdateDark.png");
        Execute(dialog, "ApplyAsync", "Update");
        check(list.Items.Cast<ListViewItem>().Single(item => item.Text == "com.vendor.animation").SubItems[4].Text == "已更新" && !primary.Visible,
            "The Git update keeps per-package completion results visible"); Probe.Clean(root);
        Save(dialog, "GitUpdateSuccess.png");
        Call(dialog, "SwitchPage", GitHubPackagePage.Remove);
        check(list.Items.Count == 2 && primary.Visible, "Removal continues to show individual packages and source-wide operations");
        Call(dialog, "SwitchPage", GitHubPackagePage.Install); url.Text = "https://github.com/vendor/packages.git#missing";
        Execute(dialog, "ReadAsync", "Read GitHub packages");
        check(((Label)Field(dialog, "_resultTitle")).Text.Contains("失败", StringComparison.Ordinal) && read.Visible && !primary.Visible && !cancel.Visible,
            "Git discovery failures keep an actionable result and restore the read action"); Probe.Clean(root);
        using var light = new GitHubPackagesForm(manager, repository, new("zh-CN"), LauncherPalette.From(new() { Theme = "Light", AccentColor = "Blue" }), "0.1.0");
        light.StartPosition = FormStartPosition.Manual; light.Location = new Point(-20000, -20000); light.Show();
        Call(light, "SwitchPage", GitHubPackagePage.Update); Save(light, "GitUpdateLight.png");
    }
}
