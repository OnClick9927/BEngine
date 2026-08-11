using System.Diagnostics;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;
using BEngine.Serialization.Editor;
using BEngine.Serialization.Editor.Documents;
using DrawingColor = System.Drawing.Color;

namespace BEngine.Launcher;

internal sealed class ProjectLauncherForm : Form
{
    private static readonly DrawingColor WindowBackground = DrawingColor.FromArgb(30, 32, 35);
    private static readonly DrawingColor PanelBackground = DrawingColor.FromArgb(39, 42, 46);
    private static readonly DrawingColor FieldBackground = DrawingColor.FromArgb(52, 55, 60);
    private static readonly DrawingColor Accent = DrawingColor.FromArgb(42, 156, 135);
    private static readonly DrawingColor PrimaryText = DrawingColor.FromArgb(236, 238, 240);
    private static readonly DrawingColor SecondaryText = DrawingColor.FromArgb(166, 172, 178);

    private readonly string _editorPath;
    private readonly string _settingsPath;
    private readonly TextBox _directoryField = new();
    private readonly TextBox _nameField = new();
    private readonly Button _createButton = new();
    private readonly Button _openButton = new();
    private readonly Label _statusLabel = new();

    public ProjectLauncherForm(string editorPath)
    {
        _editorPath = editorPath;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BEngine", "LauncherSettings.yaml");
        Text = "BEngine Project Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        ClientSize = new Size(900, 560);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        BackColor = WindowBackground;
        ForeColor = PrimaryText;
        Font = new Font("Segoe UI", 10f);
        BuildInterface();
        UpdateActions();
        RestoreLastProject();
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = DrawingColor.FromArgb(24, 26, 29),
            Padding = new Padding(28, 18, 28, 12)
        };
        var brand = new Label
        {
            Text = "BENGINE",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18f),
            ForeColor = PrimaryText,
            Location = new Point(28, 16)
        };
        var section = new Label
        {
            Text = "Projects",
            AutoSize = true,
            Font = new Font("Segoe UI", 10f),
            ForeColor = SecondaryText,
            Location = new Point(30, 49)
        };
        header.Controls.Add(brand);
        header.Controls.Add(section);
        Controls.Add(header);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBackground,
            Padding = new Padding(34, 30, 34, 28)
        };
        Controls.Add(content);
        content.BringToFront();

        var title = new Label
        {
            Text = "Open or create a project",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15f),
            ForeColor = PrimaryText,
            Location = new Point(34, 31)
        };
        content.Controls.Add(title);

        var directoryLabel = CreateLabel("Working Directory", 34, 92);
        content.Controls.Add(directoryLabel);
        _directoryField.Location = new Point(34, 119);
        _directoryField.Size = new Size(680, 29);
        _directoryField.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        StyleTextField(_directoryField);
        _directoryField.TextChanged += (_, _) => UpdateActions();
        content.Controls.Add(_directoryField);

        var browseButton = CreateButton("Browse...", 726, 117, 106, 32, false);
        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseButton.Click += (_, _) => BrowseDirectory();
        content.Controls.Add(browseButton);

        var nameLabel = CreateLabel("Project Name", 34, 179);
        content.Controls.Add(nameLabel);
        _nameField.Location = new Point(34, 206);
        _nameField.Size = new Size(798, 29);
        _nameField.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        StyleTextField(_nameField);
        _nameField.TextChanged += (_, _) => UpdateActions();
        content.Controls.Add(_nameField);

        var divider = new Panel
        {
            BackColor = DrawingColor.FromArgb(67, 71, 76),
            Location = new Point(34, 276),
            Size = new Size(798, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        content.Controls.Add(divider);

        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(34, 297);
        _statusLabel.Size = new Size(798, 54);
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _statusLabel.ForeColor = SecondaryText;
        content.Controls.Add(_statusLabel);

        _createButton.Text = "Create Project";
        StyleButton(_createButton, true);
        _createButton.Size = new Size(146, 38);
        _createButton.Location = new Point(530, 375);
        _createButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _createButton.Click += (_, _) => CreateProject();
        content.Controls.Add(_createButton);

        _openButton.Text = "Open Project";
        StyleButton(_openButton, false);
        _openButton.Size = new Size(146, 38);
        _openButton.Location = new Point(686, 375);
        _openButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _openButton.Click += (_, _) => OpenProject();
        content.Controls.Add(_openButton);
    }

    private void BrowseDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the BEngine project working directory",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };
        var current = _directoryField.Text.Trim();
        if (Directory.Exists(current)) dialog.SelectedPath = current;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _directoryField.Text = dialog.SelectedPath;
        if (File.Exists(Path.Combine(dialog.SelectedPath, ProjectWorkspace.ProjectFileName)))
        {
            LoadProjectName(dialog.SelectedPath);
        }
        else if (string.IsNullOrWhiteSpace(_nameField.Text))
        {
            _nameField.Text = new DirectoryInfo(dialog.SelectedPath).Name;
        }
    }

    private void CreateProject()
    {
        try
        {
            var workspace = ProjectWorkspaceFactory.Create(
                _directoryField.Text.Trim(), _nameField.Text.Trim());
            LaunchEditor(workspace);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, true);
        }
    }

    private void OpenProject()
    {
        try
        {
            LaunchEditor(ProjectWorkspace.Open(_directoryField.Text.Trim()));
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, true);
        }
    }

    private void LaunchEditor(ProjectWorkspace workspace)
    {
        if (!File.Exists(_editorPath))
        {
            throw new FileNotFoundException("BEngine.Editor.exe was not found. Rebuild BEngine.sln.", _editorPath);
        }

        RememberProject(workspace.RootPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = _editorPath,
            WorkingDirectory = workspace.RootPath,
            UseShellExecute = true,
            ArgumentList = { workspace.ProjectFilePath }
        });
        Close();
    }

    private void RestoreLastProject()
    {
        if (!File.Exists(_settingsPath)) return;

        try
        {
            var settings = new YamlEditorSerializer().LoadLauncherSettings(_settingsPath);
            if (!Directory.Exists(settings.LastProjectDirectory)) return;
            if (!File.Exists(Path.Combine(settings.LastProjectDirectory, ProjectWorkspace.ProjectFileName))) return;

            _directoryField.Text = settings.LastProjectDirectory;
            LoadProjectName(settings.LastProjectDirectory);
            _directoryField.Select(_directoryField.TextLength, 0);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not restore the last project: {exception.Message}", true);
        }
    }

    private void LoadProjectName(string directory)
    {
        try
        {
            _nameField.Text = ProjectWorkspace.Open(directory).Project.Name;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, true);
        }
    }

    private void RememberProject(string directory)
    {
        new YamlEditorSerializer().SaveLauncherSettings(new LauncherSettingsDocument
        {
            LastProjectDirectory = Path.GetFullPath(directory)
        }, _settingsPath);
    }

    private void UpdateActions()
    {
        var directory = _directoryField.Text.Trim();
        var hasDirectory = directory.Length > 0;
        var existingProject = hasDirectory && File.Exists(Path.Combine(directory, ProjectWorkspace.ProjectFileName));
        _openButton.Enabled = existingProject;
        _createButton.Enabled = hasDirectory && !existingProject && !string.IsNullOrWhiteSpace(_nameField.Text);
        SetStatus(existingProject
            ? "Existing BEngine project detected."
            : "The selected directory will be the project root.", false);
    }

    private void SetStatus(string message, bool error)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = error ? DrawingColor.FromArgb(235, 105, 96) : SecondaryText;
    }

    private static Label CreateLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        ForeColor = SecondaryText
    };

    private static Button CreateButton(string text, int x, int y, int width, int height, bool accent)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height) };
        StyleButton(button, accent);
        return button;
    }

    private static void StyleTextField(TextBox field)
    {
        field.BackColor = FieldBackground;
        field.ForeColor = PrimaryText;
        field.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleButton(Button button, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = accent ? 0 : 1;
        button.FlatAppearance.BorderColor = DrawingColor.FromArgb(86, 91, 97);
        button.BackColor = accent ? Accent : FieldBackground;
        button.ForeColor = PrimaryText;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }
}
