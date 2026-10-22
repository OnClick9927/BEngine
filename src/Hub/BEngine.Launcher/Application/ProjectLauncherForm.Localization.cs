namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private HubLocalization _localization = new("system");
    private readonly List<Action> _localizedControls = [];
    private readonly Dictionary<Control, (string Key, object?[] Arguments)> _localizedMessages = [];

    private string T(string key, params object?[] arguments) => _localization.Text(key, arguments);

    private void SetLocalizedText(Control control, string key, params object?[] arguments)
    {
        _localizedMessages[control] = (key, arguments);
        control.Text = T(key, arguments);
    }

    private void BindLocalization(Control control)
    {
        // Bind source strings once; never translate user-entered text or package metadata.
        if (control is Label or ButtonBase && !string.IsNullOrEmpty(control.Text))
        {
            var key = control.Text;
            _localizedControls.Add(() => control.Text = T(key));
        }
        if (control.AccessibleName is { Length: > 0 } accessibleName)
            _localizedControls.Add(() => control.AccessibleName = T(accessibleName));
        if (control is TextBox textBox && textBox.PlaceholderText is { Length: > 0 } placeholder)
            _localizedControls.Add(() => textBox.PlaceholderText = T(placeholder));
        if (control is ListView list)
            foreach (ColumnHeader column in list.Columns)
            {
                var key = column.Text;
                _localizedControls.Add(() => column.Text = T(key));
            }
        if (control.ContextMenuStrip is { } menu)
            foreach (ToolStripItem item in menu.Items)
            {
                var key = item.Text;
                if (!string.IsNullOrEmpty(key)) _localizedControls.Add(() => item.Text = T(key));
            }
        if (control is ComboBox choice && choice.DisplayMember.Length == 0)
        {
            choice.FormattingEnabled = true;
            choice.Format += (_, args) => args.Value = _localization.Option(args.ListItem?.ToString() ?? string.Empty);
        }
        foreach (Control child in control.Controls) BindLocalization(child);
    }

    private void ApplyLocalization()
    {
        _localization = new HubLocalization(_settings.Personalization.Language);
        var populating = _populatingSettings;
        _populatingSettings = true;
        try
        {
            foreach (var update in _localizedControls) update();
            foreach (var (control, message) in _localizedMessages)
                control.Text = T(message.Key, message.Arguments);
            foreach (var choice in new[] { _languageSetting, _themeSetting, _accentSetting, _startupSetting, _sortSetting, _packageTypeFilter, _packageDependencyFilter })
            {
                // Refresh cached native ComboBox display strings without changing the persisted values or selection.
                var selected = choice.SelectedItem;
                var options = choice.Items.Cast<object>().ToArray();
                choice.BeginUpdate();
                choice.Items.Clear();
                choice.Items.AddRange(options);
                choice.SelectedItem = selected;
                choice.EndUpdate();
            }
        }
        finally { _populatingSettings = populating; }
        RefreshPackageList();
        UpdateEngineDetails();
        UpdateCreateActions();
    }
}
