using System.Diagnostics;
using System.Runtime.InteropServices;
using BEngine.ProjectSystem;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm : Form
{
    private LauncherPalette _palette = LauncherPalette.From(new LauncherPersonalization());
    private DrawingColor WindowBackground => _palette.Window;
    private DrawingColor SidebarBackground => _palette.Sidebar;
    private DrawingColor PanelBackground => _palette.Panel;
    private DrawingColor RaisedBackground => _palette.Raised;
    private DrawingColor FieldBackground => _palette.Field;
    private DrawingColor Border => _palette.Border;
    private DrawingColor Accent => _palette.Accent;
    private DrawingColor AccentText => _palette.AccentText;
    private DrawingColor PrimaryText => _palette.Primary;
    private DrawingColor SecondaryText => _palette.Secondary;
    private DrawingColor WarningText => _palette.Warning;
    private DrawingColor ErrorText => _palette.Error;
    private Button? _activeNavigation;

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

    private readonly TextBox _projectSearch = new();
    private readonly ListView _projectList = new();
    private readonly Label _projectEmptyLabel = new();
    private readonly Button _openSelectedButton = new();
    private readonly Button _removeProjectButton = new();

    private readonly TextBox _packageSearch = new();
    private readonly ComboBox _packageTypeFilter = new();
    private readonly ListView _packageList = new();
    private readonly Label _packageDetailTitle = new();
    private readonly Label _packageDetailVersion = new();
    private readonly Label _packageDetailId = new();
    private readonly Label _packageDetailDescription = new();
    private readonly Label _packageDetailContents = new();
    private readonly Label _packageDetailPath = new();
    private readonly Button _openPackageDocumentationButton = new();

    private readonly TextBox _directoryField = new();
    private readonly TextBox _nameField = new();
    private readonly Label _createStatusLabel = new();
    private readonly Button _createButton = new();

    internal ProjectLauncherForm(string editorPath) : this(
        new LauncherOptions(editorPath),
        new LauncherHistoryStore(BEngine.Editor.EditorDataPaths.launcherSettingsPath),
        new LauncherPackageCatalog(),
        new LauncherEngineCatalog(),
        new LauncherProjectService()) { }

    internal ProjectLauncherForm(
        LauncherOptions options,
        LauncherHistoryStore historyStore,
        LauncherPackageCatalog packageCatalog,
        LauncherEngineCatalog engineCatalog,
        LauncherProjectService projectService)
    {
        _editorPath = options.EditorPath;
        _historyStore = historyStore;
        _packageCatalog = packageCatalog;
        _engineCatalog = engineCatalog;
        _projectService = projectService;
        Text = "BEngine Hub";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 600);
        ClientSize = new Size(1120, 700);
        Icon = LoadWindowIcon();
        BackColor = WindowBackground;
        ForeColor = PrimaryText;
        Font = new DrawingFont("Segoe UI", 10f);
        BuildInterface();
        BindLocalization(this);
        LoadHubState();
        ApplyPersonalization();
        PopulateSettings(_settings.Personalization);
        switch (_settings.Personalization.StartupPage)
        {
            case "Engines":
            case "Packages": ShowEnginesPage(); break;
            default: ShowProjectsPage(); break;
        }
    }

    private static Icon LoadWindowIcon()
    {
        var iconPath = BEngine.Editor.EditorResource.FindPath("Icons/BEngine.ico");
        if (iconPath is not null) return new Icon(iconPath);

        // The exported Hub is a single file and may be started before an
        // editor resource root is registered. Keep the taskbar icon inside
        // the launcher assembly as a fallback, so it never depends on a
        // sibling Hub/Editor directory.
        using var stream = typeof(ProjectLauncherForm).Assembly.GetManifestResourceStream(
            "BEngine.Launcher.BEngine.ico");
        if (stream is null) return (Icon)SystemIcons.Application.Clone();
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
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
            ForeColor = AccentText,
            Location = new Point(28, 62)
        };
        sidebar.Controls.Add(hub);

        ConfigureNavigationButton(_projectsNavigation, "Projects", 98);
        _projectsNavigation.Click += (_, _) => ShowProjectsPage();
        sidebar.Controls.Add(_projectsNavigation);

        ConfigureNavigationButton(_enginesNavigation, "Engines & packages", 146);
        _enginesNavigation.Click += (_, _) => ShowEnginesPage();
        sidebar.Controls.Add(_enginesNavigation);

        ConfigureNavigationButton(_settingsNavigation, "Settings", 194);
        _settingsNavigation.Click += (_, _) => ShowSettingsPage();
        sidebar.Controls.Add(_settingsNavigation);

        var version = new Label
        {
            Text = "BEngine " + EngineVersion.Current,
            AutoSize = true,
            ForeColor = SecondaryText,
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
        BuildSettingsPage();
        _pageHost.Controls.Add(_projectsPage);
        _pageHost.Controls.Add(_packagesPage);
        _pageHost.Controls.Add(_createPage);
        _pageHost.Controls.Add(_settingsPage);
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
            _projectSearch.Width = Math.Max(120, locateButton.Left - 12);
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
        _projectList.Columns.Add("Engine version", 130);
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
        _packagesPage.Controls.Add(BuildEngineSelector());
        var content = new Panel { Dock = DockStyle.Fill, BackColor = PanelBackground };
        _packagesPage.Controls.Add(content);
        content.BringToFront();

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 42, BackColor = PanelBackground,
            ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(BuildPackageViewSwitcher(), 0, 0);
        ConfigureTextField(_packageSearch, "Search packages");
        _packageSearch.Dock = DockStyle.Top;
        _packageSearch.Margin = new Padding(4, 4, 12, 0);
        _packageSearch.TextChanged += (_, _) => RefreshPackageList();
        toolbar.Controls.Add(_packageSearch, 1, 0);

        _packageTypeFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _packageTypeFilter.FlatStyle = FlatStyle.Flat;
        _packageTypeFilter.BackColor = FieldBackground;
        _packageTypeFilter.ForeColor = PrimaryText;
        _packageTypeFilter.Font = new DrawingFont("Segoe UI", 10f);
        _packageTypeFilter.Dock = DockStyle.Top;
        _packageTypeFilter.Margin = new Padding(0, 4, 0, 0);
        _packageTypeFilter.AccessibleName = "Package type";
        _packageTypeFilter.Items.AddRange(["All", "package", "project"]);
        _packageTypeFilter.SelectedIndex = 0;
        _packageTypeFilter.SelectedIndexChanged += (_, _) => { if (!_populatingSettings) RefreshPackageList(); };
        toolbar.Controls.Add(_packageTypeFilter, 2, 0);
        content.Controls.Add(toolbar);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = PanelBackground };
        content.Controls.Add(body);
        body.BringToFront();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 2,
            BackColor = Border
        };
        split.SizeChanged += (_, _) => ConfigureCatalogSplitter(split);
        split.Panel1.BackColor = RaisedBackground;
        split.Panel2.BackColor = RaisedBackground;
        body.Controls.Add(split);
        _packageDetailsSplit = split;

        ConfigureListView(_packageList);
        _packageList.ShowGroups = true;
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
        BuildPackageGraphView(body);
    }

    private static void ConfigureCatalogSplitter(SplitContainer split)
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
        var details = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = RaisedBackground,
            AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        parent.Controls.Add(details);
        ConfigureDetailLabel(_packageDetailTitle, 20f, PrimaryText, 0, 0, 80);
        ConfigureDetailLabel(_packageDetailVersion, 10f, AccentText, 0, 46, 24);
        ConfigureDetailLabel(_packageDetailId, 9.5f, SecondaryText, 0, 78, 24);
        ConfigureDetailLabel(_packageDetailDescription, 10.5f, PrimaryText, 0, 124, 110);
        ConfigureDetailLabel(_packageDetailContents, 9.5f, SecondaryText, 0, 252, 50);
        ConfigureDetailLabel(_packageDetailPath, 9f, SecondaryText, 0, 322, 90);

        _openPackageDocumentationButton.Text = "Open Documentation";
        ConfigureButton(_openPackageDocumentationButton, true);
        _openPackageDocumentationButton.Size = new Size(190, 36);
        _openPackageDocumentationButton.Margin = new Padding(0, 0, 0, 18);
        _openPackageDocumentationButton.Enabled = false;
        _openPackageDocumentationButton.Click += (_, _) => OpenSelectedPackageDocumentation();

        details.Controls.AddRange([
            _packageDetailTitle, _packageDetailVersion, _packageDetailId,
            _openPackageDocumentationButton,
            _packageDetailDescription, _packageDetailContents, _packageDetailPath
        ]);
        foreach (var label in details.Controls.OfType<Label>())
        {
            label.AutoSize = true;
            label.AutoEllipsis = false;
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            label.Margin = new Padding(0, 0, 0, 18);
        }
        void ReflowDetails()
        {
            var width = Math.Max(100, details.ClientSize.Width - details.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
            foreach (var label in details.Controls.OfType<Label>()) label.MaximumSize = new Size(width, 0);
        }
        details.SizeChanged += (_, _) => ReflowDetails();
        ReflowDetails();
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
            SetStatus("Could not load project history: {0}", true, exception.Message);
        }

        ReloadEngines();
        RefreshProjectList();
    }

    private void ReloadPackages()
    {
        try
        {
            var root = _packageCatalog.ResolvePackagesRoot();
            var engine = SelectedEngine;
            _packages = engine is null ? [] : _packageCatalog.Discover(root, engine.Version);
            RefreshPackageList();
            if (engine is null) SetStatus("Select an installed engine version.", false);
            else SetStatus("Found {0} package(s) and {1} project package(s) for BEngine {2}.", false,
                _packages.Count(package => package.Type == "package"), _packages.Count(package => package.Type == "project"), engine.Version);
        }
        catch (Exception exception)
        {
            _packages = [];
            RefreshPackageList();
            SetStatus("Could not read available packages: {0}", true, exception.Message);
        }
    }

    private void RefreshProjectList()
    {
        var search = _projectSearch.Text.Trim();
        var selectedPath = SelectedProject?.Path;
        _projectList.BeginUpdate();
        _projectList.Items.Clear();
        foreach (var project in _settings.Personalization.SortProjects(
                     _settings.Projects.Where(project => MatchesProjectSearch(project, search))))
        {
            var exists = IsProject(project.Path);
            var item = new ListViewItem(exists ? project.Name : T("{0}  (Missing)", project.Name))
            {
                Tag = project,
                ForeColor = exists ? PrimaryText : WarningText,
                ToolTipText = project.Path
            };
            item.SubItems.Add(project.Path);
            item.SubItems.Add(FormatLastOpened(project.LastOpenedUtc));
            project.EngineVersion = LauncherHistoryStore.ResolveEngineVersion(project.Path, project.EngineVersion);
            item.SubItems.Add(string.IsNullOrWhiteSpace(project.EngineVersion) ? T("Unknown") : project.EngineVersion);
            _projectList.Items.Add(item);
            if (selectedPath is not null && project.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                item.Selected = true;
        }
        _projectList.EndUpdate();
        _projectEmptyLabel.Text = T(search.Length == 0
            ? "No projects yet. Create a project or locate an existing one."
            : "No projects match your search.");
        var empty = _projectList.Items.Count == 0;
        _projectEmptyLabel.Visible = empty;
        if (empty) _projectEmptyLabel.BringToFront();
        else _projectList.BringToFront();
        ResizeProjectColumns();
        UpdateProjectActions();
    }

    private void RefreshPackageList()
    {
        var search = _packageSearch.Text.Trim();
        var type = _packageTypeFilter.SelectedItem as string ?? "All";
        var selectedId = SelectedPackage?.Id;
        _packageList.BeginUpdate();
        _packageList.Items.Clear();
        _packageList.Groups.Clear();
        var matches = _packages.Where(package => (type == "All" || package.Type == type) &&
            MatchesPackageSearch(package, search)).ToArray();
        foreach (var groupType in new[] { "package", "project" })
        {
            var packages = matches.Where(package => package.Type == groupType)
                .OrderBy(package => package.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
            if (packages.Length == 0) continue;
            var group = new ListViewGroup(groupType, $"{T(groupType)} ({packages.Length})");
            _packageList.Groups.Add(group);
            foreach (var package in packages)
            {
                var item = new ListViewItem(package.DisplayName, group)
                    { Tag = package, ToolTipText = package.Id + "\n" + T("Type: {0}", T(package.Type)) };
                item.SubItems.Add(package.Version);
                _packageList.Items.Add(item);
                if (selectedId is not null && package.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                    item.Selected = true;
            }
        }
        _packageList.EndUpdate();
        if (_packageList.SelectedItems.Count == 0 && _packageList.Items.Count > 0)
            _packageList.Items[0].Selected = true;
        UpdatePackageDetails();
        RefreshPackageGraph();
    }

    private void UpdatePackageDetails()
    {
        var package = SelectedPackage;
        _packageDetailTitle.Text = package?.DisplayName ?? T(SelectedEngine is null
            ? "Select an installed engine version."
            : _packages.Count == 0 ? "No compatible packages for this engine version."
            : "No packages match the current type and search");
        _packageDetailVersion.Text = package is null ? string.Empty : T("Version {0}", package.Version);
        _packageDetailId.Text = package?.Id ?? string.Empty;
        _packageDetailDescription.Text = package?.Description ??
            T("Select an extension package to inspect its details.");
        _packageDetailContents.Text = package is null
            ? string.Empty
            : package.DescribeContents(_localization);
        _packageDetailPath.Text = package is null ? string.Empty : T("Source\n{0}", package.DirectoryPath);
        _openPackageDocumentationButton.Enabled = package is not null &&
            LauncherPackageCatalog.FindDocumentation(package.DirectoryPath) is not null;
    }

    private void OpenSelectedPackageDocumentation()
    {
        var package = SelectedPackage;
        if (package is null) return;

        try
        {
            var path = LauncherPackageCatalog.FindDocumentation(package.DirectoryPath);
            if (path is null)
            {
                UpdatePackageDetails();
                SetStatus("Documentation is not available for '{0}'.", true, package.DisplayName);
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            SetStatus("Opened documentation for '{0}'.", false, package.DisplayName);
        }
        catch (Exception exception)
        {
            SetStatus("Could not open documentation for '{0}': {1}", true, package.DisplayName, exception.Message);
        }
    }

    private void ShowProjectsPage()
    {
        ShowPage(_projectsPage, "Projects", _projectsNavigation);
        RefreshProjectList();
    }

    private void ShowCreatePage()
    {
        foreach (var page in _pageHost.Controls.OfType<Control>()) page.Visible = ReferenceEquals(page, _createPage);
        SetLocalizedText(_pageTitle, "New project");
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
        SetLocalizedText(_pageTitle, title);
        SetNavigationState(navigation);
    }

    private void SetNavigationState(Button? active)
    {
        _activeNavigation = active;
        foreach (var button in new[] { _projectsNavigation, _enginesNavigation, _settingsNavigation })
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
            throw new FileNotFoundException(T("The editor was not found at '{0}'. Check the installed engine version.", _editorPath), _editorPath);

        _settings = _historyStore.Remember(_settings, workspace.RootPath, workspace.Project.Name);
        RefreshProjectList();
        var startupToken = Guid.NewGuid().ToString("N");
        var statusPath = BEngine.Editor.EditorDataPaths.GetStartupStatusPath(
            workspace.RootPath, startupToken);
        EditorStartupMonitor.TryDeleteStatus(statusPath);
        using var editorProcess = Process.Start(new ProcessStartInfo
        {
            FileName = _editorPath,
            WorkingDirectory = workspace.RootPath,
            UseShellExecute = true,
            ArgumentList = { workspace.ProjectFilePath, "--startup-token", startupToken }
        }) ?? throw new InvalidOperationException(T("BEngine Editor process could not be started."));
        _launchingEditor = true;
        UpdateProjectActions();
        SetStatus("Starting '{0}'...", false, workspace.Project.Name);
        AllowSetForegroundWindow(editorProcess.Id);
        try
        {
            var outcome = await EditorStartupMonitor.WaitAsync(editorProcess, statusPath);
            if (outcome.IsReady)
            {
                if (!IsDisposed)
                {
                    SetStatus("Opened '{0}'.", false, workspace.Project.Name);
                    if (_settings.Personalization.MinimizeOnOpen) WindowState = FormWindowState.Minimized;
                }
                return;
            }

            var logPath = outcome.LogPath ?? WriteStartupFailureLog(workspace, editorProcess, outcome);
            var message = outcome.Kind switch
            {
                EditorStartupOutcomeKind.UnexpectedExit => T("The Editor process exited before its first frame (exit code {0}).", outcome.ExitCode),
                EditorStartupOutcomeKind.TimedOut => T("The Editor startup timed out. See the log for details."),
                _ => T(outcome.Message)
            };
            ShowLaunchFailure(message, logPath);
        }
        finally
        {
            EditorStartupMonitor.TryDeleteStatus(statusPath);
            _launchingEditor = false;
            if (!IsDisposed) UpdateProjectActions();
        }
    }

    private string? WriteStartupFailureLog(
        ProjectWorkspace workspace,
        Process editorProcess,
        EditorStartupOutcome outcome)
    {
        try
        {
            var logPath = BEngine.Editor.EditorDataPaths.GetProjectLogPath(
                workspace.RootPath, "EditorStartup.log");
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
        SetStatus("{0}", true, T(message));
        var details = T("BEngine Editor failed to start.") + Environment.NewLine + Environment.NewLine + T(message);
        if (!string.IsNullOrWhiteSpace(logPath))
            details += Environment.NewLine + Environment.NewLine + T("Log:") + Environment.NewLine + logPath;
        MessageBox.Show(this, details, "BEngine", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void RemoveSelectedProject()
    {
        var project = SelectedProject;
        if (project is null) return;
        _historyStore.Remove(_settings, project.Path);
        RefreshProjectList();
        SetStatus("Removed '{0}' from the recent projects list. Files were not deleted.", false, project.Name);
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
        _openSelectedButton.Enabled = !_launchingEditor && project is not null && IsProject(project.Path);
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
                ? "The target folder is not empty: {0}"
                : "Project path: {0}\nThe project will contain BEngine Core only.", unavailable, target);
        }
        catch (ArgumentException exception)
        {
            _createButton.Enabled = false;
            SetCreateStatus("{0}", true, T(exception is ArgumentException { ParamName: "projectName" }
                ? "Project name contains characters that cannot be used in a folder name." : exception.Message));
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
        if (_projectList.Columns.Count != 4) return;
        var available = Math.Max(500, _projectList.ClientSize.Width - 4);
        _projectList.Columns[0].Width = Math.Clamp((int)(available * 0.28), 180, 320);
        _projectList.Columns[2].Width = 155;
        _projectList.Columns[3].Width = 130;
        if (!_settings.Personalization.ShowProjectPaths)
        {
            _projectList.Columns[1].Width = 0;
            _projectList.Columns[0].Width = available - 285;
            return;
        }
        _projectList.Columns[1].Width = Math.Max(200,
            available - _projectList.Columns[0].Width - _projectList.Columns[2].Width - _projectList.Columns[3].Width);
    }

    private string ResolveSuggestedProjectDirectory()
    {
        var preferred = _settings.Personalization.DefaultProjectDirectory;
        if (!string.IsNullOrWhiteSpace(preferred) && Path.IsPathFullyQualified(preferred)) return preferred;
        var recent = _settings.Projects.FirstOrDefault(project => Directory.Exists(project.Path));
        if (recent is not null && Directory.GetParent(recent.Path) is { } parent) return parent.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private LauncherProjectRecord? SelectedProject =>
        _projectList.SelectedItems.Count == 0 ? null : _projectList.SelectedItems[0].Tag as LauncherProjectRecord;

    private LauncherPackageInfo? SelectedPackage =>
        _packageList.SelectedItems.Count == 0 ? null : _packageList.SelectedItems[0].Tag as LauncherPackageInfo;

    private FolderBrowserDialog CreateFolderDialog(string description) => new()
    {
        Description = T(description),
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

    private string FormatLastOpened(string value) =>
        DateTimeOffset.TryParse(value, out var opened)
            ? opened.ToLocalTime().ToString("g", _localization.Culture)
            : T("Unknown");

    private void SetStatus(string message, bool error, params object?[] arguments)
    {
        SetLocalizedText(_statusLabel, message, arguments);
        _statusLabel.ForeColor = error ? ErrorText : SecondaryText;
    }

    private void SetCreateStatus(string message, bool error, params object?[] arguments)
    {
        SetLocalizedText(_createStatusLabel, message, arguments);
        _createStatusLabel.ForeColor = error ? ErrorText : SecondaryText;
    }

    private void ConfigureNavigationButton(Button button, string text, int top)
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

    private void ConfigureTextField(TextBox field, string? placeholder = null)
    {
        field.BackColor = FieldBackground;
        field.ForeColor = PrimaryText;
        field.BorderStyle = BorderStyle.FixedSingle;
        field.PlaceholderText = placeholder ?? string.Empty;
    }

    private void ConfigureButton(Button button, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = accent ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = accent ? Accent : FieldBackground;
        button.ForeColor = accent ? DrawingColor.White : PrimaryText;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private Button CreateButton(string text, bool accent)
    {
        var button = new Button { Text = text };
        ConfigureButton(button, accent);
        return button;
    }

    private Label CreateFieldLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SecondaryText,
        Location = new Point(x, y)
    };

    private void ConfigureListView(ListView list)
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
