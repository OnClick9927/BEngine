namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private readonly Panel _packageBrowserPage = new();
    private readonly PackageDependencyGraphControl _packageCatalogView = new();
    private readonly PackageDependencyDiagramControl _packageDiagram = new();
    private readonly ComboBox _packageDependencyFilter = new();
    private readonly Label _packageGraphSummary = new(), _packageBrowserHelp = new();
    private readonly Panel _packageFocusHeader = new();
    private readonly Button _exitPackageFocusButton = new();
    private readonly Label _packageFocusTitle = new();
    private string? _graphFocusedPackage;
    private PackageGraphViewState? _packageOverviewView;
    private PackageDependencyIndex? _overviewIndex;
    private string[] _overviewRoots = [];
    private LauncherPackageGraph _overviewGraph = LauncherPackageGraph.Empty;
    private (string? Engine, string Type, string Search)? _packageBrowserScope;
    private readonly Label _packageDetailRequired = new();
    private readonly Label _packageDetailDependencies = new();
    private readonly Label _packageDetailDependents = new();
    private SplitContainer? _packageDetailsSplit;
    private LauncherPackageGraph _allPackageDependencies = LauncherPackageGraph.Empty;
    private IReadOnlyList<LauncherPackageInfo>? _indexedPackages;
    private string? _indexedEngine;
    private readonly Dictionary<string, PackageDependencyIndex> _packageIndexes = [];
    private string? _graphSelectedPackage;
    private double _packageSplitterRatio = .64;
    private bool _resizingPackageSplitter;

    private void BuildPackageBrowserToolbar(TableLayoutPanel toolbar)
    {
        _packageDependencyFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _packageDependencyFilter.FlatStyle = FlatStyle.Flat;
        _packageDependencyFilter.BackColor = FieldBackground;
        _packageDependencyFilter.ForeColor = PrimaryText;
        _packageDependencyFilter.Dock = DockStyle.Top;
        _packageDependencyFilter.Margin = new Padding(0, 4, 0, 0);
        _packageDependencyFilter.AccessibleName = "Dependency kind";
        _packageDependencyFilter.Items.AddRange(["All dependencies", "Runtime dependencies", "Editor dependencies"]);
        _packageDependencyFilter.SelectedIndex = 0;
        _packageDependencyFilter.SelectedIndexChanged += (_, _) => { if (!_populatingSettings) RefreshPackageBrowser(); };
        toolbar.Controls.Add(_packageDependencyFilter, 2, 0);
    }

    private void BuildPackageBrowserView(Control parent)
    {
        _packageBrowserPage.Dock = DockStyle.Fill;
        _packageBrowserPage.BackColor = PanelBackground;
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, BackColor = PanelBackground,
            Padding = new Padding(8, 6, 6, 4), Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { _packageGraphSummary, _packageBrowserHelp })
        {
            label.AutoSize = true; label.Dock = DockStyle.Fill; label.ForeColor = SecondaryText;
            label.Margin = new Padding(0, 0, 0, 3); label.Font = new System.Drawing.Font("Segoe UI", 8.5f);
            footer.Controls.Add(label);
        }
        _packageCatalogView.Dock = _packageDiagram.Dock = DockStyle.Fill;
        void Selected(string? id) { _graphSelectedPackage = id; UpdatePackageDetails(); }
        _packageCatalogView.PackageSelected += Selected;
        _packageDiagram.PackageSelected += Selected;
        _packageCatalogView.PackageActivated += EnterPackageFocus;
        _packageDiagram.PackageActivated += EnterPackageFocus;
        _packageDiagram.Visible = false;
        _packageFocusHeader.Dock = DockStyle.Top;
        _packageFocusHeader.Height = 42;
        _packageFocusHeader.BackColor = PanelBackground;
        _packageFocusHeader.Visible = false;
        _exitPackageFocusButton.Text = "Back to all packages";
        ConfigureButton(_exitPackageFocusButton, false);
        _exitPackageFocusButton.SetBounds(8, 3, 172, 32);
        _exitPackageFocusButton.Click += (_, _) => ExitPackageFocus();
        _packageFocusTitle.SetBounds(192, 3, 100, 32);
        _packageFocusTitle.AutoEllipsis = true;
        _packageFocusTitle.TextAlign = ContentAlignment.MiddleLeft;
        _packageFocusTitle.ForeColor = PrimaryText;
        _packageFocusTitle.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _packageFocusHeader.Controls.AddRange([_exitPackageFocusButton, _packageFocusTitle]);
        _packageFocusHeader.SizeChanged += (_, _) => _packageFocusTitle.Width = Math.Max(0, _packageFocusHeader.ClientSize.Width - 200);
        _packageBrowserPage.Controls.AddRange([_packageCatalogView, _packageDiagram, _packageFocusHeader, footer]);
        parent.Controls.Add(_packageBrowserPage);
    }

    private void RefreshPackageBrowser()
    {
        var engine = SelectedEngine?.DirectoryPath;
        if (!ReferenceEquals(_indexedPackages, _packages) || _indexedEngine != engine)
        {
            _indexedPackages = _packages; _indexedEngine = engine;
            _allPackageDependencies = LauncherPackageGraph.Create(_packages, _packages.Select(package => package.Id));
            _packageIndexes.Clear();
        }
        var kind = _packageDependencyFilter.SelectedIndex switch { 1 => "runtime", 2 => "editor", _ => "All" };
        if (!_packageIndexes.TryGetValue(kind, out var index))
            _packageIndexes.Add(kind, index = new PackageDependencyIndex(_allPackageDependencies, kind));
        var type = _packageTypeFilter.SelectedItem as string ?? "All";
        var scope = (engine, type, _packageSearch.Text.Trim());
        var changedScope = _packageBrowserScope is not null && _packageBrowserScope.Value != scope;
        _packageBrowserScope = scope;
        if (changedScope) { _graphFocusedPackage = null; _packageOverviewView = null; }
        var ids = _packages.Where(package => (type == "All" || package.Type == type) &&
            MatchesPackageSearch(package, scope.Item3)).Select(package => package.Id).ToArray();
        if (changedScope && scope.Item3.Length > 0)
            _graphSelectedPackage = _packages.FirstOrDefault(package => ids.Contains(package.Id) &&
                (package.DisplayName.Equals(scope.Item3, StringComparison.OrdinalIgnoreCase) ||
                 package.Id.Equals(scope.Item3, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(package.RuntimeAssembly, scope.Item3, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(package.EditorAssembly, scope.Item3, StringComparison.OrdinalIgnoreCase)))?.Id
                ?? ids.FirstOrDefault();
        var overview = OverviewGraph(index, ids);
        if (_graphFocusedPackage is { } focus && !index.Nodes.ContainsKey(focus))
        { _graphFocusedPackage = null; _packageOverviewView = null; }
        if (_graphSelectedPackage is { } selected && (!index.Nodes.ContainsKey(selected) || (_graphFocusedPackage is null && !Query6597())))
            _graphSelectedPackage = null;
        bool Query6597()
        {
            var querySource6597 = overview.Nodes;
            ArgumentNullException.ThrowIfNull(querySource6597, "source");
            if (querySource6597.GetType() == typeof(global::BEngine.Launcher.LauncherPackageNode[]) || querySource6597.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageNode>))
            {
                global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageNode> queryValues6597 = querySource6597.GetType() == typeof(global::BEngine.Launcher.LauncherPackageNode[]) ? (global::BEngine.Launcher.LauncherPackageNode[])(object)querySource6597 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageNode>)(object)querySource6597);
                foreach (var node in queryValues6597)
                {
                    if (node.Id == selected)
                        return true;
                }
            }
            else
            {
                foreach (var node in querySource6597)
                {
                    if (node.Id == selected)
                        return true;
                }
            }
        
            return false;
        }
        _packageCatalogView.SetAppearance(_palette, _localization);
        _packageDiagram.SetAppearance(_palette, _localization);
        _packageCatalogView.SetGraph(overview, _graphSelectedPackage, SelectedEngine is null
            ? "Select an installed engine version." : _packages.Count == 0 ? "No compatible packages for this engine version."
            : "No packages match the current type and search");
        // A new search must reveal its results even after the previous graph was panned or zoomed.
        if (changedScope) _packageCatalogView.Fit();
        _packageFocusHeader.Visible = _packageDiagram.Visible = _graphFocusedPackage is not null;
        _packageCatalogView.Visible = _graphFocusedPackage is null;
        if (_graphFocusedPackage is { } root)
        {
            if (!ReferenceEquals(_packageDiagram.Index, index) || _packageDiagram.RootId != root) _graphSelectedPackage = root;
            _packageDiagram.SetRoot(index, root);
            SetLocalizedText(_packageFocusTitle, "Package view: {0}", PackageName(root));
            _packageGraphSummary.Text = T("{0} direct dependencies · {1} direct users", index.Adjacent(root, false).Count, index.Adjacent(root, true).Count);
        }
        else
        {
            _packageDiagram.ClearData();
            _packageGraphSummary.Text = T("{0} packages · {1} direct dependency links", overview.Nodes.Count, overview.Edges.Count);
        }
        SetLocalizedText(_packageBrowserHelp, _graphFocusedPackage is null
            ? "Double-click: package dependencies · Wheel: zoom · Drag: pan · Home: overview"
            : "Arrows point to dependencies · Double-click: inspect package");
        UpdatePackageDetails();
    }

    private LauncherPackageGraph OverviewGraph(PackageDependencyIndex index, string[] roots)
    {
        if (ReferenceEquals(_overviewIndex, index) && _overviewRoots.SequenceEqual(roots)) return _overviewGraph;
        _overviewIndex = index; _overviewRoots = roots;
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var id))
        {
            if (!included.Add(id)) continue;
            foreach (var edge in index.Adjacent(id, false)) pending.Push(edge.Target);
        }
        return _overviewGraph = included.Count == index.Graph.Nodes.Count ? index.Graph : new LauncherPackageGraph(
            index.Graph.Nodes.Where(node => included.Contains(node.Id)).ToArray(),
            index.Graph.Edges.Where(edge => included.Contains(edge.Source)).ToArray());
    }

    private void EnterPackageFocus(string id)
    {
        if (_graphFocusedPackage is null) _packageOverviewView = _packageCatalogView.CaptureView();
        _graphFocusedPackage = _graphSelectedPackage = id;
        RefreshPackageBrowser(); _packageDiagram.FocusRoot();
    }

    private void ExitPackageFocus()
    {
        var overview = _packageOverviewView;
        _graphFocusedPackage = null; _packageOverviewView = null;
        _graphSelectedPackage = overview?.SelectedId;
        RefreshPackageBrowser();
        if (overview is { } state) _packageCatalogView.RestoreView(state);
        _packageCatalogView.Focus();
    }
    private string PackageName(string id) => _allPackageDependencies.Nodes.FirstOrDefault(node =>
        string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase))?.Title ?? id;

    private string DependencyDescription(LauncherPackageEdge edge, LauncherPackageDependency declaration, string id) =>
        $"{PackageName(id)} [{T(declaration.Source)} → {T(declaration.Target)}]" +
        (declaration.Optional ? " · " + T("Optional") : string.Empty) +
        (edge.Unavailable ? " · " + T("Unavailable dependency") : string.Empty);

    private void UpdatePackageDependencyDetails()
    {
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Launcher.LauncherPackageNode? Query11647(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageNode> querySource11647)
        {
            ArgumentNullException.ThrowIfNull(querySource11647, "source");
            bool Matches11647(global::BEngine.Launcher.LauncherPackageNode node) => string.Equals(node.Id, _graphSelectedPackage, StringComparison.OrdinalIgnoreCase);
            global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageNode> queryValues11647;
            if (querySource11647.GetType() == typeof(global::BEngine.Launcher.LauncherPackageNode[]))
                queryValues11647 = (global::BEngine.Launcher.LauncherPackageNode[])querySource11647;
            else if (querySource11647.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageNode>))
                queryValues11647 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageNode>)querySource11647);
            else
            {
                foreach (var node in querySource11647)
                    if (Matches11647(node))
                        return node;
                return default!;
            }
        
            foreach (var node in queryValues11647)
                if (Matches11647(node))
                    return node;
            return default!;
        }
        
        var selected = Query11647(_allPackageDependencies.Nodes);
        _packageDetailRequired.Visible = selected?.Package?.IsRequired == true;
        _packageDetailRequired.Text = selected?.Package is { IsRequired: true } package
            ? T("Required package") + Environment.NewLine + (package.Required
                ? T("Required for every project")
                : T("Required by: {0}", string.Join(", ", package.RequiredBy.Select(PackageName)
                    .Order(StringComparer.CurrentCultureIgnoreCase))))
            : string.Empty;
        _packageDetailDependencies.Visible = _packageDetailDependents.Visible = selected is not null;
        if (selected is null)
        {
            _packageDetailDependencies.Text = _packageDetailDependents.Text = string.Empty;
            return;
        }
        var dependencies = _allPackageDependencies.Edges
            .Where(edge => string.Equals(edge.Source, selected.Id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(edge => edge.Declarations.Select(item => DependencyDescription(edge, item, edge.Target))).ToArray();
        var dependents = _allPackageDependencies.Edges
            .Where(edge => string.Equals(edge.Target, selected.Id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(edge => edge.Declarations.Select(item => DependencyDescription(edge, item, edge.Source))).ToArray();
        _packageDetailDependencies.Text = T("Dependencies") + Environment.NewLine +
            (dependencies.Length == 0 ? T("None") : string.Join(Environment.NewLine, dependencies));
        _packageDetailDependents.Text = T("Used by") + Environment.NewLine +
            (dependents.Length == 0 ? T("None") : string.Join(Environment.NewLine, dependents));
    }
}
