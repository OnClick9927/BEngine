using System.Diagnostics;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

internal enum GitHubPackagePage { Install, Update, Remove }

internal sealed class GitHubPackagesForm : Form
{
    private sealed record SourceRow(GitHubPackageSource Source, IReadOnlyList<InstalledGitHubPackage> Installed,
        IReadOnlyList<GitHubPackageRelease> Releases);
    private readonly GitHubPackageManager _manager;
    private readonly GitHubPackageRepository _repository;
    private readonly HubLocalization _language;
    private readonly LauncherPalette _palette;
    private readonly string _engineVersion;
    private readonly TableLayoutPanel _layout = new(), _urlRow = new();
    private readonly TextBox _url = new();
    private readonly Label _resultDetail = new();
    private readonly GitHubPackageListView _packages;
    private readonly Label _hint = new(), _status = new(), _file = new(), _summary = new(), _resultTitle = new();
    private readonly Panel _result = new();
    private readonly Button _read = new LauncherNavigationButton(), _check = new LauncherNavigationButton(),
        _refresh = new LauncherNavigationButton(), _primary = new LauncherNavigationButton(), _cancel = new LauncherNavigationButton();
    private readonly Button[] _modeButtons = [new LauncherNavigationButton(), new LauncherNavigationButton(), new LauncherNavigationButton()];
    private readonly GitHubPackageProgressBar _progress;
    private readonly System.Windows.Forms.Timer _progressTimer = new() { Interval = 100 };
    private readonly Stopwatch _elapsed = new();
    private readonly Dictionary<GitHubPackageSource, IReadOnlyList<GitHubPackageRelease>> _updates = [];
    private readonly Dictionary<GitHubPackageSource, string> _checkErrors = [];
    private readonly Dictionary<(GitHubPackageSource Source, string Id), GitHubPackageChanges> _completedChanges = [];
    private GitHubPackageSource? _checkingSource;
    private IReadOnlyList<GitHubPackageRelease> _installPreview = [];
    private CancellationTokenSource? _operation;
    private GitHubPackageProgress? _lastProgress;
    private string _lastText = "";
    private GitHubPackagePage _page;
    private bool _closeAfterCancel;
    private bool _changingRows, _hasResult, _installCompleted;
    private string T(string key, params object?[] args) => _language.Text(key, args);
    private IProgress<string> Messages => new GitHubProgressSink<string>(value => Volatile.Write(ref _lastText, value));
    private IProgress<GitHubPackageProgress> Progress => new GitHubProgressSink<GitHubPackageProgress>(value => Volatile.Write(ref _lastProgress, value));

    internal GitHubPackagesForm(GitHubPackageManager manager, GitHubPackageRepository repository,
        HubLocalization language, LauncherPalette palette, string engineVersion)
    {
        _manager = manager; _repository = repository; _language = language; _palette = palette; _engineVersion = engineVersion;
        _packages = new(palette); _progress = new(palette);
        Text = T("GitHub packages"); StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 720); MinimumSize = new Size(880, 640);
        BackColor = palette.Panel; ForeColor = palette.Primary;
        Font = new System.Drawing.Font("Segoe UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = false; MinimizeBox = false;
        Icon = ProjectLauncherForm.LoadWindowIcon();
        BEngine.Tools.WindowsBrandingIcons? windowIcons = null;
        HandleCreated += (_, _) => windowIcons = new(Handle, "BEngine.Launcher");
        HandleDestroyed += (_, _) => { windowIcons?.Dispose(); windowIcons = null; };
        _layout.Dock = DockStyle.Fill; _layout.Padding = new Padding(22, 16, 22, 16);
        _layout.ColumnCount = 1; _layout.RowCount = 7; _layout.BackColor = palette.Panel;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 48f, 46f, 42f, 44f }) _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 0f, 78f }) _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(_layout);
        _hint.Dock = DockStyle.Fill; _hint.ForeColor = palette.Secondary; _hint.UseMnemonic = false;
        _layout.Controls.Add(_hint, 0, 0);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = palette.Window, Padding = new Padding(3), WrapContents = false,
            AccessibleName = T("Package operations"), AccessibleRole = AccessibleRole.PageTabList };
        for (var index = 0; index < _modeButtons.Length; index++)
        {
            var page = (GitHubPackagePage)index;
            var button = _modeButtons[index]; Style(button, page.ToString());
            button.Size = new Size(140, 34); button.Margin = new Padding(0, 0, 4, 0);
            button.AccessibleRole = AccessibleRole.PageTab; button.Click += (_, _) => SwitchPage(page);
            tabs.Controls.Add(button);
        }
        _layout.Controls.Add(tabs, 0, 1);
        _urlRow.Dock = DockStyle.Fill; _urlRow.ColumnCount = 2; _urlRow.RowCount = 1; _urlRow.Margin = Padding.Empty;
        _urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); _urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _urlRow.Controls.Add(new Label { Text = T("Git URL"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _url.Dock = DockStyle.Top; _url.Margin = new Padding(0, 5, 0, 0);
        _url.AccessibleName = T("Git URL"); _url.BorderStyle = BorderStyle.FixedSingle;
        _url.BackColor = palette.Field; _url.ForeColor = palette.Primary;
        _url.PlaceholderText = "https://github.com/owner/repository.git?path=/Packages#main";
        _url.TextChanged += (_, _) =>
        {
            _installPreview = []; _installCompleted = false;
            if (_page == GitHubPackagePage.Install)
            {
                _packages.Items.Clear(); HideResult();
                _hint.Text = T("Paste one Git URL. Every package version under that directory will be installed.");
                _status.Text = T("Paste a Git URL to begin."); _progress.Percent = 0; _summary.Text = _file.Text = "";
            }
            UpdateActions();
        };
        _urlRow.Controls.Add(_url, 1, 0); _layout.Controls.Add(_urlRow, 0, 2);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var button in new[] { _read, _check, _refresh, _primary, _cancel })
        { button.AutoSize = true; button.MinimumSize = new Size(130, 34); tools.Controls.Add(button); }
        Style(_read, "Read GitHub packages"); Style(_check, "Check updates"); Style(_refresh, "Refresh");
        Style(_primary, "Install all versions", true); Style(_cancel, "Cancel");
        _primary.MinimumSize = new Size(190, 34);
        _layout.Controls.Add(tools, 0, 3);
        _packages.Dock = DockStyle.Fill; _packages.AccessibleName = T("GitHub package sources");
        foreach (var column in new[] { "Package", "Installed versions", "Remote versions", "Package changes", "Status" }) _packages.Columns.Add(T(column));
        _packages.SizeChanged += (_, _) => ResizeColumns();
        _packages.SelectedIndexChanged += (_, _) => UpdateActions();
        _packages.ItemCheck += (_, args) =>
        {
            if (_changingRows) return;
            if (_operation is not null) { args.NewValue = args.CurrentValue; return; }
            // A source is installed or removed as a whole, even when its packages occupy separate rows.
            var source = ((SourceRow)_packages.Items[args.Index].Tag!).Source;
            _changingRows = true;
            try
            {
                foreach (ListViewItem item in _packages.Items)
                    if (item.Index != args.Index && ((SourceRow)item.Tag!).Source == source) item.Checked = args.NewValue == CheckState.Checked;
            }
            finally { _changingRows = false; }
        };
        _packages.ItemChecked += (_, _) => UpdateActions();
        _layout.Controls.Add(_packages, 0, 4);
        _result.Dock = DockStyle.Fill; _result.BackColor = palette.Raised; _result.Padding = new Padding(10, 4, 10, 4); _result.Visible = false;
        _resultTitle.Dock = DockStyle.Top; _resultTitle.Height = 24; _resultTitle.Font = new System.Drawing.Font(Font, FontStyle.Bold);
        _resultDetail.Dock = DockStyle.Fill; _resultDetail.AutoEllipsis = true; _resultDetail.UseMnemonic = false;
        _resultDetail.BackColor = palette.Raised; _resultDetail.ForeColor = palette.Primary;
        _result.Controls.Add(_resultDetail); _result.Controls.Add(_resultTitle); _layout.Controls.Add(_result, 0, 5);
        var activity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        foreach (var height in new[] { 22f, 10f, 22f, 22f }) activity.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        foreach (var label in new[] { _status, _file, _summary })
        { label.Dock = DockStyle.Fill; label.AutoEllipsis = true; label.UseMnemonic = false; label.Margin = Padding.Empty; }
        _status.ForeColor = palette.Primary; _file.ForeColor = _summary.ForeColor = palette.Secondary;
        _file.Font = _summary.Font = new System.Drawing.Font("Segoe UI", 9f);
        _progress.Dock = DockStyle.Fill; _progress.Margin = Padding.Empty;
        _progress.AccessibleName = T("Package operation progress");
        activity.Controls.Add(_status, 0, 0); activity.Controls.Add(_progress, 0, 1); activity.Controls.Add(_file, 0, 2); activity.Controls.Add(_summary, 0, 3);
        _layout.Controls.Add(activity, 0, 6);
        _read.Click += async (_, _) => await RunAsync(ReadAsync, "Read GitHub packages");
        _check.Click += async (_, _) => await RunAsync(CheckUpdatesAsync, "Check updates");
        _refresh.Click += async (_, _) => await RunAsync(token => { token.ThrowIfCancellationRequested(); ClearChecks(); RefreshInstalled(); return Task.CompletedTask; }, "Refresh");
        _primary.Click += async (_, _) =>
        {
            if (_page == GitHubPackagePage.Remove && MessageBox.Show(this,
                T("Remove all locally installed versions from these sources?\n{0}", string.Join("\n", SelectedSources().Select(row => row.Source.Url))),
                T("GitHub packages"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await RunAsync(ApplyAsync, _page.ToString());
        };
        _cancel.Click += (_, _) => { _operation?.Cancel(); UpdateActions(); };
        _progressTimer.Tick += (_, _) => RenderProgress();
        FormClosing += (_, args) => { if (_operation is null) return; args.Cancel = true; _closeAfterCancel = true; _operation.Cancel(); };
        Disposed += (_, _) => _progressTimer.Dispose();
        SwitchPage(GitHubPackagePage.Install);
    }

    private void Style(Button button, string title, bool accent = false)
    {
        button.Text = T(title); button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false;
        button.BackColor = accent ? _palette.Accent : _palette.Field;
        button.ForeColor = accent ? System.Drawing.Color.White : _palette.Primary;
        button.FlatAppearance.BorderColor = _palette.Border; button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 3, 8, 0);
    }

    private void SwitchPage(GitHubPackagePage page)
    {
        if (_operation is not null) return;
        _page = page; ClearChecks(); _installPreview = []; _installCompleted = false; HideResult();
        _urlRow.Visible = _read.Visible = page == GitHubPackagePage.Install;
        _layout.RowStyles[2].Height = page == GitHubPackagePage.Install ? 42 : 0;
        _check.Visible = page == GitHubPackagePage.Update; _refresh.Visible = page != GitHubPackagePage.Install;
        _packages.CheckBoxes = page != GitHubPackagePage.Install;
        for (var index = 0; index < _modeButtons.Length; index++)
        {
            _modeButtons[index].BackColor = index == (int)page ? _palette.Raised : _palette.Window;
            _modeButtons[index].ForeColor = index == (int)page ? _palette.AccentText : _palette.Primary;
        }
        _hint.Text = T(page switch
        {
            GitHubPackagePage.Install => "Paste one Git URL. Every package version under that directory will be installed.",
            GitHubPackagePage.Update => "Check installed Git sources, then update every version from the selected sources.",
            _ => "Remove all locally installed versions belonging to the selected Git sources."
        });
        _primary.Text = T(page switch { GitHubPackagePage.Install => "Install all versions", GitHubPackagePage.Update => "Update all versions", _ => "Remove selected sources" });
        _progress.Percent = 0; _summary.Text = _file.Text = "";
        if (page == GitHubPackagePage.Install) { _packages.Items.Clear(); _status.Text = T("Paste a Git URL to begin."); }
        else
        {
            try { RefreshInstalled(); }
            catch (Exception error)
            {
                _packages.Items.Clear();
                GitHubPackageDiagnostics.Write("Reading installed Git sources", error);
                ShowResult(T("{0} failed", T(page.ToString())), error.Message, false);
            }
        }
        UpdateActions();
    }

    private static string InstalledName(InstalledGitHubPackage package)
    {
        if (!string.IsNullOrWhiteSpace(package.Receipt.DisplayName)) return package.Receipt.DisplayName;
        try
        {
            LauncherUpdateService.EnsureNoReparsePoints(package.Directory, "package.yaml");
            return PackageDefinitionLoader.Normalize(YamlUtility.Deserialize<BEngine.Editor.Documents.PackageDefinitionDocument>(
                File.ReadAllText(Path.Combine(package.Directory, "package.yaml")))).DisplayName;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { return package.Receipt.Id; }
    }

    private void ClearChecks() { _updates.Clear(); _checkErrors.Clear(); _completedChanges.Clear(); }
    private static string Id(SourceRow row) => row.Installed.FirstOrDefault()?.Receipt.Id ?? row.Releases[0].Document.Id;
    private string Changes(GitHubPackageChanges changes)
    {
        var parts = new List<string>();
        if (changes.Added.Count > 0) parts.Add(T("New: {0}", string.Join(", ", changes.Added)));
        if (changes.Modified.Count > 0) parts.Add(T("Content changed: {0}", string.Join(", ", changes.Modified)));
        if (changes.Retained.Count > 0) parts.Add(T("Keep local: {0}", string.Join(", ", changes.Retained)));
        return parts.Count > 0 ? string.Join("; ", parts) : T("No content changes");
    }
    private static GitHubPackageChanges Compare(SourceRow row) => GitHubPackageChanges.Compare(row.Installed, row.Releases);

    private void RefreshInstalled()
    {
        var selected = _packages.SelectedItems.Cast<ListViewItem>().Select(item => (SourceRow)item.Tag!).Select(row => (row.Source, Id(row))).ToHashSet();
        var checkedSources = _packages.CheckedItems.Cast<ListViewItem>().Select(item => ((SourceRow)item.Tag!).Source).ToHashSet();
        var installed = _manager.Installed();
        _changingRows = true; _packages.BeginUpdate();
        try
        {
            _packages.Items.Clear();
            foreach (var group in installed.GroupBy(package => package.Receipt.Source))
            {
                var releases = _updates.GetValueOrDefault(group.Key, []);
                var ids = group.Select(package => package.Receipt.Id).Concat(releases.Select(release => release.Document.Id)).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var id in ids)
                {
                    var row = new SourceRow(group.Key, group.Where(package => package.Receipt.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray(),
                        releases.Where(release => release.Document.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray());
                    var changes = Compare(row);
                    var completed = _completedChanges.GetValueOrDefault((group.Key, id));
                    var status = _page == GitHubPackagePage.Remove ? "Installed" : _checkingSource == group.Key ? "Checking..." :
                        _checkErrors.ContainsKey(group.Key) ? "Check failed" : !_updates.ContainsKey(group.Key) ? "Not checked" :
                        changes.HasUpdates ? "Update available" : completed is not null ? "Updated" : "Up to date";
                    var detail = _page == GitHubPackagePage.Remove ? T("All installed versions") : _checkErrors.GetValueOrDefault(group.Key) ??
                        (_updates.ContainsKey(group.Key) ? Changes(completed ?? changes) : "—");
                    var item = Add(row, detail, T(status));
                    item.Checked = checkedSources.Contains(group.Key); item.Selected = selected.Contains((group.Key, id));
                }
            }
            if (_packages.SelectedItems.Count == 0 && selected.Count == 0 && _packages.Items.Count > 0)
            { _packages.Items[0].Selected = true; _packages.Items[0].Focused = true; }
        }
        finally { _packages.EndUpdate(); _changingRows = false; }
        _status.Text = T("{0} installed Git source(s)", installed.Select(package => package.Receipt.Source).Distinct().Count()); UpdateActions();
    }

    private ListViewItem Add(SourceRow row, string changes, string status)
    {
        var name = row.Releases.FirstOrDefault()?.Document.DisplayName ?? InstalledName(row.Installed[0]);
        var local = string.Join(", ", row.Installed.Select(package => package.Receipt.PackageVersion).Distinct().OrderDescending(BPackageVersion.Comparer));
        var remote = string.Join(", ", row.Releases.Select(release => release.Document.PackageVersion).Distinct().OrderDescending(BPackageVersion.Comparer));
        var item = new ListViewItem([name, local.Length == 0 ? "—" : local, remote.Length == 0 ? "—" : remote, changes, status])
        {
            Tag = row, UseItemStyleForSubItems = false,
            ToolTipText = row.Source.Url + "\n" + Id(row) + "\n" + T("Installed versions") + ": " + local + "\n" + T("Remote versions") + ": " + remote + "\n" + changes
        };
        if (status == T("Update available") || status == T("Updated")) item.SubItems[4].ForeColor = _palette.AccentText;
        else if (status == T("Check failed")) item.SubItems[4].ForeColor = _palette.Error;
        _packages.Items.Add(item); return item;
    }

    private SourceRow[] SelectedSources()
    {
        var items = _packages.CheckedItems.Count > 0 ? _packages.CheckedItems.Cast<ListViewItem>() : _packages.SelectedItems.Cast<ListViewItem>();
        var selected = items.Select(item => (SourceRow)item.Tag!).ToArray();
        var sources = (selected.Length > 0 || _page == GitHubPackagePage.Remove ? selected : _packages.Items.Cast<ListViewItem>().Select(item => (SourceRow)item.Tag!))
            .Select(row => row.Source).ToHashSet();
        return _packages.Items.Cast<ListViewItem>().Select(item => (SourceRow)item.Tag!).Where(row => sources.Contains(row.Source))
            .GroupBy(row => row.Source).Select(group => new SourceRow(group.Key, group.SelectMany(row => row.Installed).ToArray(), group.SelectMany(row => row.Releases).ToArray())).ToArray();
    }

    private async Task ReadAsync(CancellationToken token)
    {
        _installPreview = []; _packages.Items.Clear();
        var releases = await _repository.ReadAsync(_url.Text, Messages, token);
        _installPreview = releases;
        var installed = _manager.Installed();
        foreach (var group in releases.GroupBy(release => release.Document.Id, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SourceRow(group.First().Source, installed.Where(package => package.Receipt.Source == group.First().Source &&
                package.Receipt.Id.Equals(group.Key, StringComparison.OrdinalIgnoreCase)).ToArray(), group.ToArray());
            Add(row, Changes(Compare(row)), T("Ready to install"));
        }
        _status.Text = T("{0} package version(s) ready. The entire source directory will be installed.", releases.Count);
        UpdateActions();
    }

    private async Task CheckUpdatesAsync(CancellationToken token)
    {
        var selected = SelectedSources();
        var loaded = new Dictionary<GitHubPackageSource, IReadOnlyList<GitHubPackageRelease>>();
        foreach (var row in selected)
        {
            _updates.Remove(row.Source); _checkErrors.Remove(row.Source);
            foreach (var key in _completedChanges.Keys.Where(key => key.Source == row.Source).ToArray()) _completedChanges.Remove(key);
            _checkingSource = row.Source; RefreshInstalled();
            try { _updates[row.Source] = loaded[row.Source] = await _repository.ReadAsync(row.Source.Url, Messages, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            { GitHubPackageDiagnostics.Write("Checking " + row.Source.Url, error); _checkErrors[row.Source] = DescribeError(error); }
            finally { _checkingSource = null; RefreshInstalled(); }
        }
        _changingRows = true;
        try
        {
            var changedSources = _packages.Items.Cast<ListViewItem>().Select(item => (SourceRow)item.Tag!)
                .Where(row => loaded.ContainsKey(row.Source) && Compare(row).HasUpdates).Select(row => row.Source).ToHashSet();
            foreach (ListViewItem item in _packages.Items) item.Checked = changedSources.Contains(((SourceRow)item.Tag!).Source);
        }
        finally { _changingRows = false; }
        var errors = selected.Where(row => _checkErrors.ContainsKey(row.Source)).ToArray();
        if (errors.Length > 0) ShowResult(T("{0} failed", T("Check updates")),
            string.Join("\n", errors.Select(row => row.Source.Owner + "/" + row.Source.Repository + ": " + _checkErrors[row.Source])), false);
        else
        {
            var changed = _packages.Items.Cast<ListViewItem>().Select(item => (SourceRow)item.Tag!)
                .Count(row => loaded.ContainsKey(row.Source) && Compare(row).HasUpdates);
            _status.Text = T("Check completed: {0} package(s) can be updated.", changed);
        }
        UpdateActions();
    }

    private async Task ApplyAsync(CancellationToken token)
    {
        if (_page == GitHubPackagePage.Remove)
        {
            var installed = SelectedSources().SelectMany(row => row.Installed).ToArray();
            await Task.Run(() => _manager.Remove(installed, _engineVersion, Progress, token), token);
            RefreshInstalled(); ShowResult(T("Remove succeeded"), T("Removed {0} local package version(s).", installed.Length), true);
        }
        else
        {
            var selected = SelectedSources().Select(row => row.Source).ToHashSet();
            var changes = _packages.Items.Cast<ListViewItem>().Select(item => (SourceRow)item.Tag!)
                .Where(row => selected.Contains(row.Source) && Compare(row).HasUpdates)
                .ToDictionary(row => (row.Source, Id(row)), Compare);
            var releases = _page == GitHubPackagePage.Install ? _installPreview : selected.SelectMany(source => _updates[source]).ToArray();
            await _manager.InstallAsync(releases, _engineVersion, Messages, token, Progress, allVersions: true);
            var title = T(_page == GitHubPackagePage.Install ? "Install succeeded" : "Update succeeded");
            var detail = T("Installed / updated all {0} package version(s). Existing projects keep their recorded versions.", releases.Count);
            if (_page == GitHubPackagePage.Update)
            {
                foreach (var change in changes) _completedChanges[change.Key] = change.Value;
                RefreshInstalled();
            }
            else
            {
                _installPreview = [];
                _installCompleted = true;
                _hint.Text = T("Change the Git URL to install another source.");
                foreach (ListViewItem item in _packages.Items) item.SubItems[4].Text = T("Installed");
            }
            ShowResult(title, detail, true);
        }
    }

    private string DescribeError(Exception error)
    {
        return T(error.Message);
    }

    private void UpdateActions()
    {
        var idle = _operation is null;
        foreach (var button in _modeButtons) button.Enabled = idle;
        _url.ReadOnly = !idle; _read.Enabled = idle && !string.IsNullOrWhiteSpace(_url.Text); _refresh.Enabled = idle;
        _read.Visible = idle && _page == GitHubPackagePage.Install && !_installCompleted;
        _check.Visible = idle && _page == GitHubPackagePage.Update;
        _refresh.Visible = idle && _page != GitHubPackagePage.Install;
        _check.Enabled = idle && _packages.Items.Count > 0;
        _primary.Enabled = idle && (_page == GitHubPackagePage.Install ? _installPreview.Count > 0 :
            _packages.Items.Count > 0 && (_page == GitHubPackagePage.Remove ? SelectedSources().Length > 0 :
                SelectedSources().All(row => _updates.ContainsKey(row.Source)) && SelectedSources().Any(row => Compare(row).HasUpdates)));
        _primary.Visible = _primary.Enabled;
        _cancel.Visible = !idle;
        _cancel.Enabled = !idle && !_operation!.IsCancellationRequested;
        _cancel.Text = T(!idle && _operation!.IsCancellationRequested ? "Cancelling..." : "Cancel");
        _layout.RowStyles[3].Height = idle && _page == GitHubPackagePage.Install && _installCompleted ? 0 : 44;
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, string operationName)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource(); _operation = operation;
        HideResult(); Volatile.Write(ref _lastProgress, null); Volatile.Write(ref _lastText, operationName);
        _elapsed.Restart(); _progressTimer.Start(); _progress.Percent = null; UpdateActions(); RenderProgress();
        try { await action(operation.Token); }
        catch (OperationCanceledException)
        { ShowResult(T("Package operation cancelled."), T("The previous installation was preserved."), false); }
        catch (Exception error)
        {
            GitHubPackageDiagnostics.Write(operationName, error);
            ShowResult(T("{0} failed", T(operationName)), DescribeError(error), false);
        }
        finally
        {
            _progressTimer.Stop(); _elapsed.Stop(); _operation = null;
            if (!_hasResult) { _progress.Percent = 100; _summary.Text = T("Completed in {0:F1} s", _elapsed.Elapsed.TotalSeconds); }
            UpdateActions(); if (_closeAfterCancel) Close();
        }
    }

    private void RenderProgress()
    {
        if (_operation is null || _hasResult) return;
        var value = Volatile.Read(ref _lastProgress);
        _progress.Percent = value?.Percent; _progress.Animation = (int)(_elapsed.ElapsedMilliseconds / 14 % 160);
        var message = Volatile.Read(ref _lastText);
        _status.Text = T(message.StartsWith("Retrying", StringComparison.Ordinal) ? message : value?.Stage ?? message);
        _file.Text = value?.Detail ?? "";
        if (_page == GitHubPackagePage.Remove && value?.Percent is { } removalPercent)
        {
            _summary.Text = T("{0:F0}% · {1}/{2} versions · {3:F1} s", removalPercent, value.CompletedFiles, value.TotalFiles, _elapsed.Elapsed.TotalSeconds);
            return;
        }
        _summary.Text = value?.Percent is { } percent
            ? T("{0:F0}% · {1}/{2} files · {3}/{4} · {5:F1} s", percent, value.CompletedFiles, value.TotalFiles,
                Bytes(value.CompletedBytes), Bytes(value.TotalBytes), _elapsed.Elapsed.TotalSeconds)
            : T("Working · {0:F1} s", _elapsed.Elapsed.TotalSeconds);
    }

    private static string Bytes(long value) => value >= 1024 * 1024 ? $"{value / (1024d * 1024):F1} MB" : value >= 1024 ? $"{value / 1024d:F1} KB" : value + " B";
    private void HideResult() { _hasResult = false; _result.Visible = false; _layout.RowStyles[5].Height = 0; }
    private void ShowResult(string title, string detail, bool success)
    {
        _resultTitle.Text = (success ? "✓ " : "! ") + title;
        _hasResult = true;
        _resultTitle.ForeColor = success ? _palette.AccentText : _palette.Error;
        _resultDetail.Text = detail; _result.Visible = true; _layout.RowStyles[5].Height = 82;
        _progress.Percent = success ? 100 : 0; _status.Text = title;
        _file.Text = ""; _summary.Text = T("Elapsed: {0:F1} s", _elapsed.Elapsed.TotalSeconds);
    }
    private void ResizeColumns()
    {
        var width = Math.Max(600, _packages.ClientSize.Width - 4);
        var ratios = new[] { .23, .16, .16, .30, .15 };
        for (var index = 0; index < ratios.Length; index++) _packages.Columns[index].Width = (int)(width * ratios[index]);
    }
}
