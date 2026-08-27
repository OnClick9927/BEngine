using System.Diagnostics;
using System.Runtime.InteropServices;
using BEngine.ProjectSystem;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed class ProjectLauncherForm : Form
{
    private static readonly DrawingColor WindowBackground = DrawingColor.FromArgb(30, 32, 35);
    private static readonly DrawingColor SidebarBackground = DrawingColor.FromArgb(24, 26, 29);
    private static readonly DrawingColor PanelBackground = DrawingColor.FromArgb(39, 42, 46);
    private static readonly DrawingColor RaisedBackground = DrawingColor.FromArgb(47, 50, 55);
    private static readonly DrawingColor FieldBackground = DrawingColor.FromArgb(52, 55, 60);
    private static readonly DrawingColor Border = DrawingColor.FromArgb(67, 71, 76);
    private static readonly DrawingColor Accent = DrawingColor.FromArgb(42, 156, 135);
    private static readonly DrawingColor PrimaryText = DrawingColor.FromArgb(236, 238, 240);
    private static readonly DrawingColor SecondaryText = DrawingColor.FromArgb(166, 172, 178);
    private static readonly DrawingColor WarningText = DrawingColor.FromArgb(235, 163, 72);
    private static readonly DrawingColor ErrorText = DrawingColor.FromArgb(235, 105, 96);

    private readonly string _editorPath;
    private readonly LauncherHistoryStore _historyStore;
    private readonly LauncherPackageCatalog _packageCatalog;
    private readonly LauncherProjectService _projectService;
    private LauncherSettingsData _settings = new();
    private IReadOnlyList<LauncherPackageInfo> _packages = [];
    private bool _launchingEditor;

    private readonly Panel _pageHost = new();
    private readonly Panel _projectsPage = new();
    private readonly Panel _packagesPage = new();
    private readonly Panel _createPage = new();
    private readonly Label _pageTitle = new();
    private readonly Label _statusLabel = new();
    private readonly Button _projectsNavigation = new();
    private readonly Button _packagesNavigation = new();

    private readonly TextBox _projectSearch = new();
    private readonly ListView _projectList = new();
    private readonly Label _projectEmptyLabel = new();
    private readonly Button _openSelectedButton = new();
    private readonly Button _removeProjectButton = new();

    private readonly TextBox _packageSearch = new();
    private readonly ListView _packageList = new();
    private readonly Label _packageRootLabel = new();
    private readonly Label _packageDetailTitle = new();
    private readonly Label _packageDetailVersion = new();
    private readonly Label _packageDetailId = new();
    private readonly Label _packageDetailDescription = new();
    private readonly Label _packageDetailContents = new();
    private readonly Label _packageDetailPath = new();

    private readonly TextBox _directoryField = new();
    private readonly TextBox _nameField = new();
    private readonly Label _createStatusLabel = new();
    private readonly Button _createButton = new();

    internal ProjectLauncherForm(string editorPath) : this(
        new LauncherOptions(editorPath),
        new LauncherHistoryStore(BEngine.Editor.EditorDataPaths.launcherSettingsPath),
        new LauncherPackageCatalog(),
        new LauncherProjectService()) { }

    internal ProjectLauncherForm(
        LauncherOptions options,
        LauncherHistoryStore historyStore,
        LauncherPackageCatalog packageCatalog,
        LauncherProjectService projectService)
    {
        _editorPath = options.EditorPath;
        _historyStore = historyStore;
        _packageCatalog = packageCatalog;
        _projectService = projectService;
        Text = "BEngine Hub";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 600);
        ClientSize = new Size(1120, 700);
        var iconPath = BEngine.Editor.EditorResource.FindPath("Icons/BEngine.ico");
        Icon = iconPath is null ? SystemIcons.Application : new Icon(iconPath);
        BackColor = WindowBackground;
        ForeColor = PrimaryText;
        Font = new DrawingFont("Segoe UI", 10f);
        BuildInterface();
        LoadHubState();
        ShowProjectsPage();
    }

    private void BuildInterface()
    {
        SuspendLayout();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = WindowBackground,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildMainArea(), 1, 0);
        ResumeLayout(true);
    }

    private Control BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = SidebarBackground };
        var brand = new Label
        {
            Text = "BENGINE",
            AutoSize = true,
            Font = new DrawingFont("Segoe UI Semibold", 18f),
            ForeColor = PrimaryText,
            Location = new Point(28, 25)
        };
        sidebar.Controls.Add(brand);

        var hub = new Label
        {
            Text = "HUB",
            AutoSize = true,
            Font = new DrawingFont("Segoe UI Semibold", 8f),
            ForeColor = Accent,
            Location = new Point(150, 35)
        };
        sidebar.Controls.Add(hub);

        ConfigureNavigationButton(_projectsNavigation, "Projects", 98);
        _projectsNavigation.Click += (_, _) => ShowProjectsPage();
        sidebar.Controls.Add(_projectsNavigation);

        ConfigureNavigationButton(_packagesNavigation, "Packages", 146);
        _packagesNavigation.Click += (_, _) => ShowPackagesPage();
        sidebar.Controls.Add(_packagesNavigation);

        var version = new Label
        {
            Text = "BEngine 0.1",
            AutoSize = true,
            ForeColor = DrawingColor.FromArgb(105, 111, 118),
            Font = new DrawingFont("Segoe UI", 8.5f),
            Location = new Point(28, 648),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        sidebar.Controls.Add(version);
        return sidebar;
    }

    private Control BuildMainArea()
    {
        var main = new Panel { Dock = DockStyle.Fill, BackColor = PanelBackground };
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = WindowBackground,
            Padding = new Padding(30, 0, 30, 0)
        };
        _pageTitle.AutoSize = true;
        _pageTitle.Font = new DrawingFont("Segoe UI Semibold", 17f);
        _pageTitle.ForeColor = PrimaryText;
        _pageTitle.Location = new Point(30, 20);
        header.Controls.Add(_pageTitle);
        main.Controls.Add(header);

        _statusLabel.Dock = DockStyle.Bottom;
        _statusLabel.Height = 31;
        _statusLabel.Padding = new Padding(24, 6, 0, 0);
        _statusLabel.ForeColor = SecondaryText;
        _statusLabel.BackColor = WindowBackground;
        main.Controls.Add(_statusLabel);

        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = PanelBackground;
        _pageHost.Padding = new Padding(24, 20, 24, 20);
        main.Controls.Add(_pageHost);
        _pageHost.BringToFront();

        BuildProjectsPage();
        BuildPackagesPage();
        BuildCreatePage();
        _pageHost.Controls.Add(_projectsPage);
        _pageHost.Controls.Add(_packagesPage);
        _pageHost.Controls.Add(_createPage);
        return main;
    }

    private void BuildProjectsPage()
    {
        _projectsPage.Dock = DockStyle.Fill;
        _projectsPage.BackColor = PanelBackground;

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = PanelBackground };
        ConfigureTextField(_projectSearch, "Search projects");
        _projectSearch.Location = new Point(0, 4);
        _projectSearch.Size = new Size(300, 30);
        _projectSearch.TextChanged += (_, _) => RefreshProjectList();
        toolbar.Controls.Add(_projectSearch);

        var locateButton = CreateButton("Open", false);
        locateButton.Size = new Size(92, 34);
        locateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        locateButton.Click += (_, _) => BrowseForExistingProject();
        toolbar.Controls.Add(locateButton);

        var newButton = CreateButton("New project", true);
        newButton.Size = new Size(120, 34);
        newButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        newButton.Click += (_, _) => ShowCreatePage();
        toolbar.Controls.Add(newButton);
        toolbar.Resize += (_, _) =>
        {
            newButton.Left = toolbar.ClientSize.Width - newButton.Width;
            locateButton.Left = newButton.Left - locateButton.Width - 10;
        };
        _projectsPage.Controls.Add(toolbar);

        var actions = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = PanelBackground };
        _openSelectedButton.Text = "Open selected";
        ConfigureButton(_openSelectedButton, true);
        _openSelectedButton.Size = new Size(132, 34);
        _openSelectedButton.Location = new Point(0, 10);
        _openSelectedButton.Click += (_, _) => OpenSelectedProject();
        actions.Controls.Add(_openSelectedButton);

        _removeProjectButton.Text = "Remove from list";
        ConfigureButton(_removeProjectButton, false);
        _removeProjectButton.Size = new Size(142, 34);
        _removeProjectButton.Location = new Point(142, 10);
        _removeProjectButton.Click += (_, _) => RemoveSelectedProject();
        actions.Controls.Add(_removeProjectButton);
        _projectsPage.Controls.Add(actions);

        ConfigureListView(_projectList);
        _projectList.Dock = DockStyle.Fill;
        _projectList.Columns.Add("Project", 250);
        _projectList.Columns.Add("Location", 470);
        _projectList.Columns.Add("Last opened", 150);
        _projectList.SelectedIndexChanged += (_, _) => UpdateProjectActions();
        _projectList.DoubleClick += (_, _) => OpenSelectedProject();
        _projectList.KeyDown += ProjectListKeyDown;
        _projectList.Resize += (_, _) => ResizeProjectColumns();
        _projectList.ContextMenuStrip = BuildProjectContextMenu();
        _projectsPage.Controls.Add(_projectList);
        _projectList.BringToFront();

        _projectEmptyLabel.Text = "No projects yet. Create a project or locate an existing one.";
        _projectEmptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        _projectEmptyLabel.ForeColor = SecondaryText;
        _projectEmptyLabel.BackColor = RaisedBackground;
        _projectEmptyLabel.Dock = DockStyle.Fill;
        _projectEmptyLabel.Visible = false;
        _projectsPage.Controls.Add(_projectEmptyLabel);
    }

    private void BuildPackagesPage()
    {
        _packagesPage.Dock = DockStyle.Fill;
        _packagesPage.BackColor = PanelBackground;

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = PanelBackground };
        ConfigureTextField(_packageSearch, "Search packages");
        _packageSearch.Location = new Point(0, 4);
        _packageSearch.Size = new Size(300, 30);
        _packageSearch.TextChanged += (_, _) => RefreshPackageList();
        toolbar.Controls.Add(_packageSearch);

        var refreshButton = CreateButton("Refresh", false);
        refreshButton.Size = new Size(92, 34);
        refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refreshButton.Click += (_, _) => ReloadPackages();
        toolbar.Controls.Add(refreshButton);
        toolbar.Resize += (_, _) => refreshButton.Left = toolbar.ClientSize.Width - refreshButton.Width;

        _packageRootLabel.Location = new Point(0, 43);
        _packageRootLabel.AutoEllipsis = true;
        _packageRootLabel.ForeColor = SecondaryText;
        _packageRootLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _packageRootLabel.Size = new Size(800, 22);
        toolbar.Controls.Add(_packageRootLabel);
        _packagesPage.Controls.Add(toolbar);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 2,
            BackColor = Border
        };
        split.SizeChanged += (_, _) => ConfigurePackageSplitter(split);
        split.Panel1.BackColor = RaisedBackground;
        split.Panel2.BackColor = RaisedBackground;
        _packagesPage.Controls.Add(split);
        split.BringToFront();

        ConfigureListView(_packageList);
        _packageList.Dock = DockStyle.Fill;
        _packageList.Columns.Add("Package", 235);
        _packageList.Columns.Add("Version", 90);
        _packageList.SelectedIndexChanged += (_, _) => UpdatePackageDetails();
        _packageList.Resize += (_, _) =>
        {
            if (_packageList.Columns.Count == 2)
                _packageList.Columns[0].Width = Math.Max(120, _packageList.ClientSize.Width - 100);
        };
        split.Panel1.Controls.Add(_packageList);
        BuildPackageDetails(split.Panel2);
    }

    private static void ConfigurePackageSplitter(SplitContainer split)
    {
        const int leftMinimum = 260;
        const int rightMinimum = 300;
        if (split.ClientSize.Width < leftMinimum + rightMinimum + split.SplitterWidth) return;
        split.Panel1MinSize = 0;
        split.Panel2MinSize = 0;
        split.SplitterDistance = Math.Clamp(350, leftMinimum,
            split.ClientSize.Width - rightMinimum - split.SplitterWidth);
        split.Panel1MinSize = leftMinimum;
        split.Panel2MinSize = rightMinimum;
    }

    private void BuildPackageDetails(Control parent)
    {
        var details = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = RaisedBackground };
        parent.Controls.Add(details);
        ConfigureDetailLabel(_packageDetailTitle, 20f, PrimaryText, 0, 0, 80);
        ConfigureDetailLabel(_packageDetailVersion, 10f, Accent, 0, 46, 24);
        ConfigureDetailLabel(_packageDetailId, 9.5f, SecondaryText, 0, 78, 24);
        ConfigureDetailLabel(_packageDetailDescription, 10.5f, PrimaryText, 0, 124, 110);
        ConfigureDetailLabel(_packageDetailContents, 9.5f, SecondaryText, 0, 252, 50);
        ConfigureDetailLabel(_packageDetailPath, 9f, SecondaryText, 0, 322, 90);
        foreach (var label in new[]
                 {
                     _packageDetailTitle, _packageDetailVersion, _packageDetailId,
                     _packageDetailDescription, _packageDetailContents, _packageDetailPath
                 })
            details.Controls.Add(label);
    }

    private void BuildCreatePage()
    {
        _createPage.Dock = DockStyle.Fill;
        _createPage.BackColor = PanelBackground;
        _createPage.AutoScroll = true;

        var content = new Panel { Dock = DockStyle.Top, Height = 390, BackColor = PanelBackground };
        _createPage.Controls.Add(content);

        var intro = new Label
        {
            Text = "Create a clean BEngine project. Add extension packages later from Package Manager.",
            AutoSize = true,
            ForeColor = SecondaryText,
            Location = new Point(0, 3)
        };
        content.Controls.Add(intro);

        content.Controls.Add(CreateFieldLabel("Project name", 0, 50));
        _nameField.Location = new Point(0, 76);
        _nameField.Size = new Size(790, 30);
        ConfigureTextField(_nameField);
        _nameField.TextChanged += (_, _) => UpdateCreateActions();
        content.Controls.Add(_nameField);

        content.Controls.Add(CreateFieldLabel("Location", 0, 126));
        _directoryField.Location = new Point(0, 152);
        _directoryField.Size = new Size(660, 30);
        ConfigureTextField(_directoryField);
        _directoryField.TextChanged += (_, _) => UpdateCreateActions();
        content.Controls.Add(_directoryField);

        var browseButton = CreateButton("Browse...", false);
        browseButton.Location = new Point(674, 150);
        browseButton.Size = new Size(116, 34);
        browseButton.Click += (_, _) => BrowseCreateDirectory();
        content.Controls.Add(browseButton);

        var packageHint = new Label
        {
            Text = "New projects contain only BEngine Core. Package dependencies are resolved when a package is enabled.",
            AutoSize = true,
            ForeColor = SecondaryText,
            Location = new Point(0, 216)
        };
        content.Controls.Add(packageHint);

        _createStatusLabel.Location = new Point(0, 258);
        _createStatusLabel.Size = new Size(500, 48);
        _createStatusLabel.ForeColor = SecondaryText;
        content.Controls.Add(_createStatusLabel);

        var cancelButton = CreateButton("Cancel", false);
        cancelButton.Size = new Size(110, 36);
        cancelButton.Location = new Point(558, 326);
        cancelButton.Click += (_, _) => ShowProjectsPage();
        content.Controls.Add(cancelButton);

        _createButton.Text = "Create project";
        ConfigureButton(_createButton, true);
        _createButton.Size = new Size(122, 36);
        _createButton.Location = new Point(678, 326);
        _createButton.Click += (_, _) => CreateProject();
        content.Controls.Add(_createButton);

        content.Resize += (_, _) => LayoutCreatePage(content, browseButton, cancelButton);
    }

    private ContextMenuStrip BuildProjectContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenSelectedProject());
        menu.Items.Add("Show in Explorer", null, (_, _) => ShowSelectedProjectInExplorer());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove from list", null, (_, _) => RemoveSelectedProject());
        menu.Opening += (_, _) =>
        {
            var hasSelection = SelectedProject is not null;
            foreach (ToolStripItem item in menu.Items) item.Enabled = hasSelection;
        };
        return menu;
    }

    private void LoadHubState()
    {
        try
        {
            _settings = _historyStore.Load();
        }
        catch (Exception exception)
        {
            _settings = new LauncherSettingsData();
            SetStatus($"Could not load project history: {exception.Message}", true);
        }

        ReloadPackages();
        RefreshProjectList();
    }

    private void ReloadPackages()
    {
        try
        {
            var root = _packageCatalog.ResolvePackagesRoot();
            _packages = _packageCatalog.Discover(root);
            _packageRootLabel.Text = $"Available packages: {root}";
            RefreshPackageList();
            SetStatus($"Found {_packages.Count} available extension package(s).", false);
        }
        catch (Exception exception)
        {
            _packages = [];
            _packageRootLabel.Text = $"Available packages: {_packageCatalog.ResolvePackagesRoot()}";
            RefreshPackageList();
            SetStatus($"Could not read available packages: {exception.Message}", true);
        }
    }

    private void RefreshProjectList()
    {
        var search = _projectSearch.Text.Trim();
        var selectedPath = SelectedProject?.Path;
        _projectList.BeginUpdate();
        _projectList.Items.Clear();
        foreach (var project in _settings.Projects.Where(project => MatchesProjectSearch(project, search)))
        {
            var exists = IsProject(project.Path);
            var item = new ListViewItem(exists ? project.Name : $"{project.Name}  (Missing)")
            {
                Tag = project,
                ForeColor = exists ? PrimaryText : WarningText,
                ToolTipText = project.Path
            };
            item.SubItems.Add(project.Path);
            item.SubItems.Add(FormatLastOpened(project.LastOpenedUtc));
            _projectList.Items.Add(item);
            if (selectedPath is not null && project.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                item.Selected = true;
        }
        _projectList.EndUpdate();
        _projectEmptyLabel.Visible = _projectList.Items.Count == 0;
        if (_projectEmptyLabel.Visible) _projectEmptyLabel.BringToFront();
        else _projectList.BringToFront();
        ResizeProjectColumns();
        UpdateProjectActions();
    }

    private void RefreshPackageList()
    {
        var search = _packageSearch.Text.Trim();
        var selectedId = SelectedPackage?.Id;
        _packageList.BeginUpdate();
        _packageList.Items.Clear();
        foreach (var package in _packages.Where(package => MatchesPackageSearch(package, search)))
        {
            var item = new ListViewItem(package.DisplayName) { Tag = package, ToolTipText = package.Id };
            item.SubItems.Add(package.Version);
            _packageList.Items.Add(item);
            if (selectedId is not null && package.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                item.Selected = true;
        }
        _packageList.EndUpdate();
        if (_packageList.SelectedItems.Count == 0 && _packageList.Items.Count > 0)
            _packageList.Items[0].Selected = true;
        UpdatePackageDetails();
    }

    private void UpdatePackageDetails()
    {
        var package = SelectedPackage;
        _packageDetailTitle.Text = package?.DisplayName ?? "No package selected";
        _packageDetailVersion.Text = package is null ? string.Empty : $"Version {package.Version}";
        _packageDetailId.Text = package?.Id ?? string.Empty;
        _packageDetailDescription.Text = package?.Description ??
            "Select an extension package to inspect its details.";
        _packageDetailContents.Text = package is null
            ? string.Empty
            : $"Assemblies   Runtime: {(package.HasRuntime ? "Yes" : "No")}    Editor: {(package.HasEditor ? "Yes" : "No")}";
        _packageDetailPath.Text = package is null ? string.Empty : $"Source\n{package.DirectoryPath}";
    }

    private void ShowProjectsPage()
    {
        ShowPage(_projectsPage, "Projects", _projectsNavigation);
        RefreshProjectList();
    }

    private void ShowPackagesPage() => ShowPage(_packagesPage, "Available packages", _packagesNavigation);

    private void ShowCreatePage()
    {
        foreach (var page in _pageHost.Controls.OfType<Control>()) page.Visible = ReferenceEquals(page, _createPage);
        _pageTitle.Text = "New project";
        SetNavigationState(null);
        _nameField.Clear();
        _directoryField.Text = ResolveSuggestedProjectDirectory();
        _nameField.Focus();
        UpdateCreateActions();
    }

    private void ShowPage(Panel page, string title, Button navigation)
    {
        foreach (var candidate in _pageHost.Controls.OfType<Control>())
            candidate.Visible = ReferenceEquals(candidate, page);
        page.BringToFront();
        _pageTitle.Text = title;
        SetNavigationState(navigation);
    }

    private void SetNavigationState(Button? active)
    {
        foreach (var button in new[] { _projectsNavigation, _packagesNavigation })
        {
            button.BackColor = ReferenceEquals(button, active) ? RaisedBackground : SidebarBackground;
            button.ForeColor = ReferenceEquals(button, active) ? PrimaryText : SecondaryText;
            button.FlatAppearance.BorderSize = 0;
        }
    }

    private async void BrowseForExistingProject()
    {
        if (_launchingEditor) return;
        using var dialog = CreateFolderDialog("Select a BEngine project folder");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await LaunchEditorAsync(_projectService.Open(dialog.SelectedPath));
        }
        catch (Exception exception)
        {
            ShowLaunchFailure(exception.Message);
        }
    }

    private void BrowseCreateDirectory()
    {
        using var dialog = CreateFolderDialog("Select the folder that will contain the new project");
        var current = _directoryField.Text.Trim();
        if (Directory.Exists(current)) dialog.SelectedPath = current;
        if (dialog.ShowDialog(this) == DialogResult.OK) _directoryField.Text = dialog.SelectedPath;
    }

    private async void CreateProject()
    {
        if (_launchingEditor) return;
        try
        {
            var workspace = _projectService.Create(
                _directoryField.Text.Trim(), _nameField.Text.Trim());
            await LaunchEditorAsync(workspace);
        }
        catch (Exception exception)
        {
            SetCreateStatus(exception.Message, true);
            ShowLaunchFailure(exception.Message);
        }
    }

    private async void OpenSelectedProject()
    {
        if (_launchingEditor) return;
        var project = SelectedProject;
        if (project is null) return;
        try
        {
            await LaunchEditorAsync(_projectService.Open(project.Path));
        }
        catch (Exception exception)
        {
            ShowLaunchFailure(exception.Message);
        }
    }

    private async Task LaunchEditorAsync(ProjectWorkspace workspace)
    {
        if (!File.Exists(_editorPath))
            throw new FileNotFoundException("BEngine.Editor.exe was not found. Rebuild src/BEngine.sln.", _editorPath);

        _settings = _historyStore.Remember(_settings, workspace.RootPath, workspace.Project.Name);
        RefreshProjectList();
        var startupToken = Guid.NewGuid().ToString("N");
        var statusPath = BEngine.Editor.EditorDataPaths.GetStartupStatusPath(startupToken);
        EditorStartupMonitor.TryDeleteStatus(statusPath);
        using var editorProcess = Process.Start(new ProcessStartInfo
        {
            FileName = _editorPath,
            WorkingDirectory = workspace.RootPath,
            UseShellExecute = true,
            ArgumentList = { workspace.ProjectFilePath, "--startup-token", startupToken }
        }) ?? throw new InvalidOperationException("BEngine Editor process could not be started.");
        _launchingEditor = true;
        SetStatus($"Starting '{workspace.Project.Name}'...", false);
        AllowSetForegroundWindow(editorProcess.Id);
        Hide();
        try
        {
            var outcome = await EditorStartupMonitor.WaitAsync(editorProcess, statusPath);
            if (outcome.IsReady)
            {
                Close();
                return;
            }

            var logPath = outcome.LogPath ?? WriteStartupFailureLog(workspace, editorProcess, outcome);
            ShowLaunchFailure(outcome.Message, logPath);
        }
        finally
        {
            EditorStartupMonitor.TryDeleteStatus(statusPath);
            _launchingEditor = false;
        }
    }

    private string? WriteStartupFailureLog(
        ProjectWorkspace workspace,
        Process editorProcess,
        EditorStartupOutcome outcome)
    {
        try
        {
            var logPath = Path.Combine(BEngine.Editor.EditorDataPaths.logsPath, "EditorStartup.log");
            var processId = 0;
            try { processId = editorProcess.Id; }
            catch (InvalidOperationException) { }
            File.AppendAllText(logPath,
                $"[{DateTimeOffset.Now:O}] {outcome.Kind}: {outcome.Message}{Environment.NewLine}" +
                $"Project: {workspace.ProjectFilePath}{Environment.NewLine}" +
                $"Editor: {_editorPath}{Environment.NewLine}" +
                $"Process: {processId}{Environment.NewLine}{Environment.NewLine}");
            return logPath;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Editor startup failure could not be logged: {exception}");
            return null;
        }
    }

    private void ShowLaunchFailure(string message, string? logPath = null)
    {
        if (IsDisposed) return;
        if (!Visible) Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        SetStatus(message, true);
        var details = $"BEngine Editor failed to start.{Environment.NewLine}{Environment.NewLine}{message}";
        if (!string.IsNullOrWhiteSpace(logPath))
            details += $"{Environment.NewLine}{Environment.NewLine}Log:{Environment.NewLine}{logPath}";
        MessageBox.Show(this, details, "BEngine", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void RemoveSelectedProject()
    {
        var project = SelectedProject;
        if (project is null) return;
        _historyStore.Remove(_settings, project.Path);
        RefreshProjectList();
        SetStatus($"Removed '{project.Name}' from the recent projects list. Files were not deleted.", false);
    }

    private void ShowSelectedProjectInExplorer()
    {
        var path = SelectedProject?.Path;
        if (path is null || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void UpdateProjectActions()
    {
        var project = SelectedProject;
        _openSelectedButton.Enabled = project is not null && IsProject(project.Path);
        _removeProjectButton.Enabled = project is not null;
    }

    private void UpdateCreateActions()
    {
        if (string.IsNullOrWhiteSpace(_nameField.Text))
        {
            _createButton.Enabled = false;
            SetCreateStatus("Enter a project name.", false);
            return;
        }
        if (string.IsNullOrWhiteSpace(_directoryField.Text))
        {
            _createButton.Enabled = false;
            SetCreateStatus("Choose a location for the new project.", false);
            return;
        }
        try
        {
            var target = _projectService.ResolveTargetPath(_directoryField.Text, _nameField.Text);
            var existingProject = File.Exists(Path.Combine(target, ProjectWorkspace.ProjectFileName));
            var nonEmptyDirectory = Directory.Exists(target) &&
                                    Directory.EnumerateFileSystemEntries(target).Any();
            var unavailable = existingProject || nonEmptyDirectory;
            _createButton.Enabled = !unavailable;
            SetCreateStatus(unavailable
                ? $"The target folder is not empty: {target}"
                : $"Project path: {target}\nThe project will contain BEngine Core only.", unavailable);
        }
        catch (ArgumentException exception)
        {
            _createButton.Enabled = false;
            SetCreateStatus(exception.Message, true);
        }
    }

    private void LayoutCreatePage(Control content, Control browseButton, Control cancelButton)
    {
        var width = Math.Max(360, content.ClientSize.Width);
        _nameField.Width = width;
        browseButton.Left = Math.Max(0, width - browseButton.Width);
        _directoryField.Width = Math.Max(180, browseButton.Left - 14);
        _createStatusLabel.Width = Math.Max(180, width - cancelButton.Width - _createButton.Width - 24);
        _createButton.Left = Math.Max(0, width - _createButton.Width);
        cancelButton.Left = Math.Max(0, _createButton.Left - cancelButton.Width - 10);
    }

    private void ProjectListKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode == Keys.Enter) OpenSelectedProject();
        if (args.KeyCode != Keys.Delete) return;
        RemoveSelectedProject();
        args.Handled = true;
    }

    private void ResizeProjectColumns()
    {
        if (_projectList.Columns.Count != 3) return;
        var available = Math.Max(500, _projectList.ClientSize.Width - 4);
        _projectList.Columns[0].Width = Math.Clamp((int)(available * 0.28), 180, 320);
        _projectList.Columns[2].Width = 155;
        _projectList.Columns[1].Width = Math.Max(200,
            available - _projectList.Columns[0].Width - _projectList.Columns[2].Width);
    }

    private string ResolveSuggestedProjectDirectory()
    {
        var recent = _settings.Projects.FirstOrDefault(project => Directory.Exists(project.Path));
        if (recent is not null && Directory.GetParent(recent.Path) is { } parent) return parent.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private LauncherProjectRecord? SelectedProject =>
        _projectList.SelectedItems.Count == 0 ? null : _projectList.SelectedItems[0].Tag as LauncherProjectRecord;

    private LauncherPackageInfo? SelectedPackage =>
        _packageList.SelectedItems.Count == 0 ? null : _packageList.SelectedItems[0].Tag as LauncherPackageInfo;

    private static FolderBrowserDialog CreateFolderDialog(string description) => new()
    {
        Description = description,
        ShowNewFolderButton = true,
        UseDescriptionForTitle = true
    };

    private static bool MatchesProjectSearch(LauncherProjectRecord project, string search) =>
        search.Length == 0 || project.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        project.Path.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPackageSearch(LauncherPackageInfo package, string search) =>
        search.Length == 0 || package.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        package.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        package.Description.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool IsProject(string path) =>
        Directory.Exists(path) && File.Exists(Path.Combine(path, ProjectWorkspace.ProjectFileName));

    private static string FormatLastOpened(string value) =>
        DateTimeOffset.TryParse(value, out var opened)
            ? opened.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "Unknown";

    private void SetStatus(string message, bool error)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = error ? ErrorText : SecondaryText;
    }

    private void SetCreateStatus(string message, bool error)
    {
        _createStatusLabel.Text = message;
        _createStatusLabel.ForeColor = error ? ErrorText : SecondaryText;
    }

    private static void ConfigureNavigationButton(Button button, string text, int top)
    {
        button.Text = text;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(28, 0, 0, 0);
        button.Location = new Point(0, top);
        button.Size = new Size(208, 44);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = SidebarBackground;
        button.ForeColor = SecondaryText;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private static void ConfigureTextField(TextBox field, string? placeholder = null)
    {
        field.BackColor = FieldBackground;
        field.ForeColor = PrimaryText;
        field.BorderStyle = BorderStyle.FixedSingle;
        field.PlaceholderText = placeholder ?? string.Empty;
    }

    private static void ConfigureButton(Button button, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = accent ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = accent ? Accent : FieldBackground;
        button.ForeColor = PrimaryText;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private static Button CreateButton(string text, bool accent)
    {
        var button = new Button { Text = text };
        ConfigureButton(button, accent);
        return button;
    }

    private static Label CreateFieldLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SecondaryText,
        Location = new Point(x, y)
    };

    private static void ConfigureListView(ListView list)
    {
        list.View = View.Details;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.MultiSelect = false;
        list.ShowItemToolTips = true;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.BackColor = RaisedBackground;
        list.ForeColor = PrimaryText;
        list.Font = new DrawingFont("Segoe UI", 10f);
    }

    private static void ConfigureDetailLabel(Label label, float size, DrawingColor color,
        int left, int top, int height)
    {
        label.Location = new Point(left, top);
        label.Size = new Size(500, height);
        label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        label.Font = new DrawingFont("Segoe UI", size, size >= 18f ? FontStyle.Bold : FontStyle.Regular);
        label.ForeColor = color;
        label.AutoEllipsis = true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
