using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private readonly Panel _settingsPage = new();
    private readonly Button _settingsNavigation = new();
    private readonly ComboBox _themeSetting = new();
    private readonly ComboBox _languageSetting = new();
    private readonly ComboBox _accentSetting = new();
    private readonly ComboBox _startupSetting = new();
    private readonly ComboBox _sortSetting = new();
    private readonly TextBox _defaultDirectorySetting = new();
    private readonly CheckBox _showPathsSetting = new();
    private readonly CheckBox _minimizeSetting = new();
    private readonly Label _settingsMessage = new();
    private bool _populatingSettings;

    private void BuildSettingsPage()
    {
        _settingsPage.Dock = DockStyle.Fill;
        _settingsPage.BackColor = PanelBackground;
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = PanelBackground };
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1,
            BackColor = PanelBackground, Padding = new Padding(0, 8, 12, 0)
        };
        _settingsPage.Controls.Add(footer);
        _settingsPage.Controls.Add(scroller);
        scroller.BringToFront();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
            BackColor = PanelBackground, Padding = new Padding(0, 0, 12, 12),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroller.Controls.Add(layout);

        void AddWide(Control control)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }

        void AddChoice(string label, ComboBox choice, string[] options)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = CreateFieldLabel(label, 0, 0);
            caption.Margin = new Padding(0, 8, 12, 12);
            layout.Controls.Add(caption, 0, row);
            choice.AccessibleName = label;
            choice.DropDownStyle = ComboBoxStyle.DropDownList;
            choice.FlatStyle = FlatStyle.Flat;
            choice.BackColor = FieldBackground;
            choice.ForeColor = PrimaryText;
            choice.Width = 280;
            choice.Margin = new Padding(0, 2, 0, 12);
            choice.Items.AddRange(options);
            layout.Controls.Add(choice, 1, row);
        }

        void AddSection(string text)
        {
            AddWide(new Label
            {
                Text = text, AutoSize = true, ForeColor = PrimaryText,
                Font = new DrawingFont("Segoe UI Semibold", 12f), Margin = new Padding(0, 16, 0, 12)
            });
        }

        AddWide(new Label
        {
            Text = "Personalize this Hub. These preferences are independent of engine and project settings.",
            AutoSize = true, ForeColor = SecondaryText, Margin = new Padding(0, 0, 0, 4)
        });
        AddSection("Development environment");
        AddWide(BuildEnvironmentControls());
        AddSection("Appearance");
        AddChoice("Language", _languageSetting, ["system", "zh-CN", "en"]);
        AddChoice("Theme", _themeSetting, ["Dark", "Light"]);
        AddChoice("Accent color", _accentSetting, ["Teal", "Blue", "Purple", "Orange", "Rose"]);
        AddSection("Projects and startup");
        AddChoice("Startup page", _startupSetting, ["Projects", "Engines"]);
        AddChoice("Project order", _sortSetting, ["Last opened", "Name A–Z", "Name Z–A"]);

        _showPathsSetting.Text = "Show project location column";
        _minimizeSetting.Text = "Minimize Hub after the editor is ready";
        foreach (var toggle in new[] { _showPathsSetting, _minimizeSetting })
        {
            toggle.AutoSize = true;
            toggle.ForeColor = PrimaryText;
            toggle.Margin = new Padding(180, 2, 0, 12);
            AddWide(toggle);
        }

        AddSection("Default project folder");
        var folderRow = new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        ConfigureTextField(_defaultDirectorySetting, "Use the most recent project location");
        _defaultDirectorySetting.AccessibleName = "Default project folder";
        _defaultDirectorySetting.Dock = DockStyle.Fill;
        _defaultDirectorySetting.Margin = new Padding(0, 5, 12, 4);
        folderRow.Controls.Add(_defaultDirectorySetting, 0, 0);
        var browse = CreateButton("Browse...", false);
        browse.Size = new Size(116, 34);
        browse.Margin = Padding.Empty;
        browse.Click += (_, _) =>
        {
            using var dialog = CreateFolderDialog("Choose the default folder for new projects");
            if (Directory.Exists(_defaultDirectorySetting.Text)) dialog.SelectedPath = _defaultDirectorySetting.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK) _defaultDirectorySetting.Text = dialog.SelectedPath;
        };
        folderRow.Controls.Add(browse, 1, 0);
        AddWide(folderRow);
        AddWide(new Label
        {
            Text = "Leave blank to use the most recent project location, then Documents.",
            AutoSize = true, ForeColor = SecondaryText, Margin = new Padding(0, 6, 0, 12)
        });

        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8), WrapContents = true
        };
        var apply = CreateButton("Apply settings", true);
        apply.Size = new Size(140, 36);
        apply.Margin = new Padding(0, 0, 12, 0);
        apply.Click += (_, _) => SavePersonalization();
        var reset = CreateButton("Restore defaults", false);
        reset.Size = new Size(150, 36);
        reset.Click += (_, _) =>
        {
            PopulateSettings(new LauncherPersonalization());
            _settingsMessage.ForeColor = SecondaryText;
            SetLocalizedText(_settingsMessage, "Defaults selected. Apply settings to save them.");
        };
        actions.Controls.AddRange([apply, reset]);
        footer.Controls.Add(actions, 0, 0);
        _settingsMessage.AutoSize = true;
        _settingsMessage.ForeColor = SecondaryText;
        _settingsMessage.Margin = new Padding(0, 2, 0, 8);
        footer.Controls.Add(_settingsMessage, 0, 1);
        footer.SizeChanged += (_, _) =>
            _settingsMessage.MaximumSize = new Size(Math.Max(100, footer.ClientSize.Width - 16), 0);
        foreach (var choice in new[] { _languageSetting, _themeSetting, _accentSetting, _startupSetting, _sortSetting })
            choice.SelectedIndexChanged += (_, _) => MarkSettingsChanged();
        _showPathsSetting.CheckedChanged += (_, _) => MarkSettingsChanged();
        _minimizeSetting.CheckedChanged += (_, _) => MarkSettingsChanged();
        _defaultDirectorySetting.TextChanged += (_, _) => MarkSettingsChanged();
        layout.SizeChanged += (_, _) =>
        {
            foreach (var label in layout.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 16), 0);
        };
    }

    private void PopulateSettings(LauncherPersonalization preferences)
    {
        _populatingSettings = true;
        _languageSetting.SelectedItem = preferences.Language;
        _themeSetting.SelectedItem = preferences.Theme;
        _accentSetting.SelectedItem = preferences.AccentColor;
        _startupSetting.SelectedItem = string.Equals(preferences.StartupPage, "Packages", StringComparison.OrdinalIgnoreCase)
            ? "Engines" : preferences.StartupPage;
        _sortSetting.SelectedItem = preferences.ProjectSort;
        _defaultDirectorySetting.Text = preferences.DefaultProjectDirectory;
        _showPathsSetting.Checked = preferences.ShowProjectPaths;
        _minimizeSetting.Checked = preferences.MinimizeOnOpen;
        _populatingSettings = false;
    }

    private void MarkSettingsChanged()
    {
        if (_populatingSettings) return;
        _settingsMessage.ForeColor = SecondaryText;
        SetLocalizedText(_settingsMessage, "Changes not applied. Choose Apply settings to save.");
    }

    private void ShowSettingsPage() => ShowPage(_settingsPage, "Hub settings", _settingsNavigation);

    private void SavePersonalization()
    {
        var preferences = new LauncherPersonalization
        {
            Language = _languageSetting.SelectedItem as string ?? "system",
            Theme = _themeSetting.SelectedItem as string ?? "Dark",
            AccentColor = _accentSetting.SelectedItem as string ?? "Teal",
            StartupPage = _startupSetting.SelectedItem as string ?? "Projects",
            ProjectSort = _sortSetting.SelectedItem as string ?? "Last opened",
            DefaultProjectDirectory = _defaultDirectorySetting.Text.Trim(),
            ShowProjectPaths = _showPathsSetting.Checked,
            MinimizeOnOpen = _minimizeSetting.Checked
        };
        try
        {
            if (preferences.DefaultProjectDirectory.Length != 0)
            {
                if (!Path.IsPathFullyQualified(preferences.DefaultProjectDirectory) ||
                    preferences.DefaultProjectDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    throw new ArgumentException("Choose an absolute folder path, or leave the default project folder blank.");
                preferences.DefaultProjectDirectory = Path.GetFullPath(preferences.DefaultProjectDirectory);
                var pathRoot = Path.GetPathRoot(preferences.DefaultProjectDirectory)!;
                if (preferences.DefaultProjectDirectory[pathRoot.Length..]
                    .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
                    .Any(part => part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new ArgumentException("The default project folder contains invalid characters.");
                if (File.Exists(preferences.DefaultProjectDirectory))
                    throw new ArgumentException("The default project folder points to a file. Choose a folder instead.");
            }
            // Save the candidate before changing the live UI. A failed write leaves active preferences intact.
            var candidate = new LauncherSettingsData
            {
                Format = _settings.Format, Version = _settings.Version,
                LastProjectDirectory = _settings.LastProjectDirectory,
                Projects = _settings.Projects, Personalization = preferences
            };
            _historyStore.Save(candidate);
            _settings = candidate;
            ApplyPersonalization();
            _settingsMessage.ForeColor = SecondaryText;
            SetLocalizedText(_settingsMessage, "Saved. Language, appearance and project options are applied; startup page takes effect next time.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _settingsMessage.ForeColor = ErrorText;
            SetLocalizedText(_settingsMessage, "Could not save settings: {0}", T(exception.Message));
        }
    }

    private void ApplyPersonalization()
    {
        var previous = _palette.Colors;
        _palette = LauncherPalette.From(_settings.Personalization);
        var next = _palette.Colors;
        DrawingColor Map(DrawingColor color, bool foreground = false)
        {
            // Light palettes share accent text and fill colors. Text keeps the text role when returning to dark.
            var index = foreground
                ? Array.FindLastIndex(previous, candidate => candidate.ToArgb() == color.ToArgb())
                : Array.FindIndex(previous, candidate => candidate.ToArgb() == color.ToArgb());
            return index < 0 ? color : next[index];
        }
        void Recolor(Control control)
        {
            control.BackColor = Map(control.BackColor);
            control.ForeColor = Map(control.ForeColor, foreground: true);
            if (control is Button button) button.FlatAppearance.BorderColor = Border;
            foreach (Control child in control.Controls) Recolor(child);
        }
        SuspendLayout();
        Recolor(this);
        ApplyLocalization();
        ApplyUpdateAppearance();
        SetNavigationState(_activeNavigation);
        RefreshProjectList();
        _projectList.Invalidate();
        _packageList.Invalidate();
        _engineSelector.Invalidate();
        ResumeLayout(true);
    }
}
