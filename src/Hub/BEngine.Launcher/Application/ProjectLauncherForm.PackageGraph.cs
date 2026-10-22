namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private readonly Panel _packageGraphPage = new();
    private readonly PackageDependencyGraphControl _packageGraph = new();
    private readonly ComboBox _packageDependencyFilter = new();
    private readonly TextBox _packageGraphDetails = new();
    private readonly Button _packageDetailsButton = new();
    private readonly Button _packageGraphButton = new();
    private SplitContainer? _packageDetailsSplit;
    private LauncherPackageGraph _allPackageDependencies = LauncherPackageGraph.Empty;
    private string? _graphSelectedPackage;
    private bool _showingPackageGraph;

    private Control BuildPackageViewSwitcher()
    {
        var switcher = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            WrapContents = false, BackColor = PanelBackground, Margin = Padding.Empty, Padding = Padding.Empty
        };
        _packageDetailsButton.Text = "Details";
        _packageGraphButton.Text = "Dependency graph";
        foreach (var button in new[] { _packageDetailsButton, _packageGraphButton })
        {
            ConfigureButton(button, false);
            button.AutoSize = true;
            button.Height = 34;
            button.Padding = new Padding(12, 0, 12, 0);
            button.Margin = new Padding(0, 0, 8, 0);
            switcher.Controls.Add(button);
        }
        _packageDetailsButton.Click += (_, _) => ShowPackageDetailsView();
        _packageGraphButton.Click += (_, _) =>
        {
            _graphSelectedPackage = SelectedPackage?.Id ?? _graphSelectedPackage;
            ShowPackageGraphView();
        };
        return switcher;
    }

    private void BuildPackageGraphView(Control parent)
    {
        _packageGraphPage.Dock = DockStyle.Fill;
        _packageGraphPage.BackColor = PanelBackground;
        _packageGraphPage.Visible = false;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = PanelBackground, WrapContents = false };
        _packageDependencyFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _packageDependencyFilter.FlatStyle = FlatStyle.Flat;
        _packageDependencyFilter.BackColor = FieldBackground;
        _packageDependencyFilter.ForeColor = PrimaryText;
        _packageDependencyFilter.Width = 174;
        _packageDependencyFilter.AccessibleName = "Dependency kind";
        _packageDependencyFilter.Items.AddRange(["All dependencies", "Runtime dependencies", "Editor dependencies"]);
        _packageDependencyFilter.SelectedIndex = 0;
        _packageDependencyFilter.SelectedIndexChanged += (_, _) => { if (!_populatingSettings) RefreshPackageGraph(); };
        toolbar.Controls.Add(_packageDependencyFilter);
        foreach (var (text, action) in new (string, Action)[] {
            ("Fit", _packageGraph.Fit), ("−", () => _packageGraph.ZoomBy(1 / 1.2f)), ("+", () => _packageGraph.ZoomBy(1.2f)) })
        {
            var button = CreateButton(text, false);
            button.Size = new Size(text == "Fit" ? 62 : 34, 30);
            button.AccessibleName = text switch { "−" => "Zoom out", "+" => "Zoom in", _ => "Fit graph" };
            button.Click += (_, _) => action();
            toolbar.Controls.Add(button);
        }
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 46, ForeColor = SecondaryText,
            Text = "Blue: depends on · Orange: used by · Dashed: optional\nHover/click: highlight · Wheel: zoom · Drag: pan · Double-click: details"
        };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(6), BackColor = RaisedBackground };
        _packageGraphDetails.Multiline = true;
        _packageGraphDetails.ReadOnly = true;
        _packageGraphDetails.BorderStyle = BorderStyle.None;
        _packageGraphDetails.ScrollBars = ScrollBars.Vertical;
        _packageGraphDetails.Dock = DockStyle.Fill;
        _packageGraphDetails.BackColor = RaisedBackground;
        _packageGraphDetails.ForeColor = PrimaryText;
        _packageGraphDetails.AccessibleName = "Selected package dependencies";
        footer.Controls.Add(_packageGraphDetails);
        _packageGraph.Dock = DockStyle.Fill;
        _packageGraph.PackageSelected += id =>
        {
            _graphSelectedPackage = id;
            UpdatePackageGraphDetails();
        };
        _packageGraph.PackageActivated += id =>
        {
            if (!_packages.Any(package => package.Id == id)) return;
            _graphSelectedPackage = id;
            ShowPackageDetailsView();
        };
        _packageGraphPage.Controls.AddRange([_packageGraph, footer, hint, toolbar]);
        parent.Controls.Add(_packageGraphPage);
        UpdatePackageViewButtons();
    }

    private void ShowPackageGraphView()
    {
        if (!_showingPackageGraph)
        {
            // Show all compatible packages for the selected engine before applying graph filters.
            _packageSearch.Clear();
            _packageTypeFilter.SelectedIndex = 0;
            _packageDependencyFilter.SelectedIndex = 0;
        }
        _showingPackageGraph = true;
        _packageDetailsSplit!.Visible = false;
        _packageGraphPage.Visible = true;
        _packageGraphPage.BringToFront();
        RefreshPackageGraph();
    }

    private void ShowPackageDetailsView()
    {
        if (!_showingPackageGraph) return;
        _showingPackageGraph = false;
        _packageGraphPage.Visible = false;
        _packageDetailsSplit!.Visible = true;
        _packageDetailsSplit.BringToFront();
        UpdatePackageViewButtons();
        var selected = _packages.FirstOrDefault(package => string.Equals(package.Id, _graphSelectedPackage, StringComparison.OrdinalIgnoreCase));
        if (selected is null) return;
        if (!_packageList.Items.Cast<ListViewItem>().Any(item => ((LauncherPackageInfo)item.Tag!).Id == selected.Id))
        {
            _packageSearch.Clear();
            _packageTypeFilter.SelectedIndex = 0;
        }
        foreach (ListViewItem item in _packageList.Items)
            item.Selected = ((LauncherPackageInfo)item.Tag!).Id == selected.Id;
        UpdatePackageDetails();
    }

    private void RefreshPackageGraph()
    {
        _packageGraph.SetAppearance(_palette, _localization);
        UpdatePackageViewButtons();
        if (!_showingPackageGraph) return;
        var kind = _packageDependencyFilter.SelectedIndex switch { 1 => "runtime", 2 => "editor", _ => "All" };
        _allPackageDependencies = LauncherPackageGraph.Create(_packages, _packages.Select(package => package.Id), kind);
        var type = _packageTypeFilter.SelectedItem as string ?? "All";
        var roots = _packages.Where(package => (type == "All" || package.Type == type) &&
            MatchesPackageSearch(package, _packageSearch.Text.Trim())).Select(package => package.Id).ToArray();
        if (_graphSelectedPackage is null || !_allPackageDependencies.Nodes.Any(node => node.Id == _graphSelectedPackage))
            _graphSelectedPackage = SelectedPackage?.Id ?? roots.FirstOrDefault();
        // Search matches remain roots, while their dependencies remain visible as context.
        var graph = LauncherPackageGraph.Create(_packages, roots, kind);
        if (!graph.Nodes.Any(node => node.Id == _graphSelectedPackage)) _graphSelectedPackage = roots.FirstOrDefault();
        _packageGraph.SetGraph(graph, _graphSelectedPackage, SelectedEngine is null
            ? "Select an installed engine version."
            : _packages.Count == 0 ? "No compatible packages for this engine version."
            : "No packages match the current type and search");
        UpdatePackageGraphDetails();
    }

    private void UpdatePackageViewButtons()
    {
        ConfigureButton(_packageDetailsButton, !_showingPackageGraph);
        ConfigureButton(_packageGraphButton, _showingPackageGraph);
    }

    private void UpdatePackageGraphDetails()
    {
        var selected = _allPackageDependencies.Nodes.FirstOrDefault(node => node.Id == _graphSelectedPackage);
        var summary = T("{0} packages · {1} dependency links", _packageGraph.Graph.Nodes.Count, _packageGraph.Graph.Edges.Count);
        if (selected is null) { _packageGraphDetails.Text = summary; return; }
        string Name(string id) => _allPackageDependencies.Nodes.FirstOrDefault(node => node.Id == id)?.Title ?? id;
        var dependencies = _allPackageDependencies.Edges.Where(edge => edge.Source == selected.Id)
            .SelectMany(edge => edge.Declarations.Select(item => $"{Name(edge.Target)} [{T(item.Source)} → {T(item.Target)}]" +
                (item.Optional ? " · " + T("Optional") : string.Empty) +
                (edge.Unavailable ? " · " + T("Unavailable dependency") : string.Empty))).ToArray();
        var dependents = _allPackageDependencies.Edges.Where(edge => edge.Target == selected.Id).Select(edge => Name(edge.Source)).ToArray();
        _packageGraphDetails.Text = $"{selected.Title} · {selected.Id}    |    {summary}" +
            (selected.Package is null ? " · " + T("Missing package") : selected.InCycle ? " · " + T("Package cycle") : string.Empty) +
            Environment.NewLine + T("Depends on: {0}", dependencies.Length == 0 ? T("None") : string.Join("; ", dependencies)) +
            Environment.NewLine + T("Used by: {0}", dependents.Length == 0 ? T("None") : string.Join(", ", dependents));
    }
}
