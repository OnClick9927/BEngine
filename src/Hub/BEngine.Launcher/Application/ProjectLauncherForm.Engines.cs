using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private readonly LauncherEngineCatalog _engineCatalog;
    private IReadOnlyList<LauncherEngineInfo> _engines = [];
    private readonly LauncherNavigationButton _enginesNavigation = new();
    private readonly ComboBox _engineSelector = new();
    private readonly Button _openEngineDocumentationButton = new();
    private bool _updatingEngineSelection;
    private bool _primaryNavigationAllowed = true;

    private LauncherEngineInfo? SelectedEngine => _engineSelector.SelectedItem as LauncherEngineInfo;
    private bool HasLocalEngine => _engines.Any(engine =>
        File.Exists(Path.Combine(engine.DirectoryPath, "BEngine.Editor.exe")));

    private void RefreshPrimaryNavigation(bool allowed)
    {
        _primaryNavigationAllowed = allowed;
        var enabled = allowed && HasLocalEngine;
        _projectsNavigation.Enabled = enabled;
        _enginesNavigation.Enabled = enabled;
    }

    private Control BuildEngineSelector()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = PanelBackground };
        var caption = new Label
        {
            Text = "Engine version", AutoSize = true, ForeColor = PrimaryText,
            Location = new Point(0, 8)
        };
        header.Controls.Add(caption);

        _engineSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _engineSelector.FlatStyle = FlatStyle.Standard;
        _engineSelector.BackColor = FieldBackground;
        _engineSelector.ForeColor = PrimaryText;
        _engineSelector.Font = new DrawingFont("Segoe UI", 10f);
        _engineSelector.DisplayMember = nameof(LauncherEngineInfo.DisplayName);
        _engineSelector.AccessibleName = "Engine version";
        _engineSelector.Location = new Point(116, 4);
        _engineSelector.Width = 340;
        _engineSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingEngineSelection) return;
            UpdateEngineDetails();
            _graphSelectedPackage = null;
            ReloadPackages();
        };
        header.Controls.Add(_engineSelector);

        _openEngineDocumentationButton.Text = "Engine documentation";
        ConfigureButton(_openEngineDocumentationButton, false);
        _openEngineDocumentationButton.Size = new Size(166, 34);
        _openEngineDocumentationButton.Enabled = false;
        _openEngineDocumentationButton.Click += (_, _) => OpenSelectedEngineDocumentation();
        header.Controls.Add(_openEngineDocumentationButton);

        var refresh = CreateButton("Refresh", false);
        refresh.Size = new Size(92, 34);
        refresh.Click += (_, _) => ReloadEngines();
        header.Controls.Add(refresh);

        header.SizeChanged += (_, _) =>
        {
            refresh.Left = header.ClientSize.Width - refresh.Width;
            _openEngineDocumentationButton.Left = refresh.Left - _openEngineDocumentationButton.Width - 10;
            _engineSelector.Width = Math.Clamp(_openEngineDocumentationButton.Left - _engineSelector.Left - 10, 100, 520);
        };
        return header;
    }

    private void ShowEnginesPage()
    {
        if (!HasLocalEngine || !_primaryNavigationAllowed)
        {
            RefreshPrimaryNavigation(_primaryNavigationAllowed);
            ShowUpdatesPage();
            return;
        }
        ShowPage(_packagesPage, "Engines & packages", _enginesNavigation);
    }

    private void ReloadEngines()
    {
        try
        {
            _engines = _engineCatalog.Discover();
            RefreshEngineSelector();
        }
        catch (Exception exception)
        {
            _engines = [];
            RefreshEngineSelector();
            SetStatus("Could not read installed engines: {0}", true, exception.Message);
        }
        RefreshPrimaryNavigation(_primaryNavigationAllowed);
        if (!HasLocalEngine && (_activeNavigation == _projectsNavigation ||
            _activeNavigation == _enginesNavigation || _createPage.Visible)) ShowUpdatesPage();
    }

    private void RefreshEngineSelector()
    {
        var selectedPath = SelectedEngine?.DirectoryPath;
        _updatingEngineSelection = true;
        _engineSelector.BeginUpdate();
        try
        {
            _engineSelector.Items.Clear();
            _engineSelector.Items.AddRange(_engines.Cast<object>().ToArray());
            var selected = _engines.FirstOrDefault(engine =>
                engine.DirectoryPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                ?? _engines.FirstOrDefault(engine => Path.Combine(engine.DirectoryPath, "BEngine.Editor.exe")
                    .Equals(_editorPath, StringComparison.OrdinalIgnoreCase))
                ?? _engines.FirstOrDefault();
            _engineSelector.SelectedItem = selected;
            _engineSelector.Enabled = _engines.Count > 0;
        }
        finally
        {
            _engineSelector.EndUpdate();
            _updatingEngineSelection = false;
        }
        UpdateEngineDetails();
        ReloadPackages();
    }

    private void UpdateEngineDetails()
    {
        var engine = SelectedEngine;
        _openEngineDocumentationButton.Enabled = engine is not null && File.Exists(engine.DocumentationPath);
    }

    private void OpenSelectedEngineDocumentation()
    {
        var engine = SelectedEngine;
        if (engine is null) return;
        try
        {
            if (!File.Exists(engine.DocumentationPath))
            {
                UpdateEngineDetails();
                SetStatus("Documentation is not available for '{0}'.", true, engine.DisplayName);
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = engine.DocumentationPath, UseShellExecute = true
            });
            SetStatus("Opened documentation for '{0}'.", false, engine.DisplayName);
        }
        catch (Exception exception)
        {
            SetStatus("Could not open documentation for '{0}': {1}", true, engine.DisplayName, exception.Message);
        }
    }
}
