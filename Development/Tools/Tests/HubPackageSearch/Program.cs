using System.Collections;
using System.Reflection;
using System.Text.Json;
using BEngine.Launcher;
using Microsoft.Extensions.DependencyInjection;

internal static class Probe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Field(object instance, string name) => instance.GetType().GetField(name, Members)!.GetValue(instance)!;
    private static object Property(object instance, string name) => instance.GetType().GetProperty(name, Members)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Members)!.Invoke(instance, args);

    [STAThread]
    private static void Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        var editor = Path.Combine(root, "BEgine", "BEgine-0.1.0", "BEngine.Editor.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
        File.WriteAllText(editor, "");
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", root);
        Package("audio", "Audio", "Sound playback and Animation events");
        Package("animation", "Animation", "Animate scene objects");
        Package("ui", "UI Elements", "Interface controls", "audio");
        Package("starter", "Starter Project", "Project template", type: "project");
        var preferences = Path.Combine(root, "Preferences", "Hub");
        Directory.CreateDirectory(preferences);
        File.WriteAllText(Path.Combine(preferences, "LauncherSettings.yaml"),
            "format: BEngine.LauncherSettings\nversion: 2\npersonalization:\n  language: zh-CN\n  startupPage: Engines\n");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var services = new ServiceCollection().AddBEngineLauncher(editor, Path.Combine(root, "Packages")).BuildServiceProvider();
        var formType = typeof(LauncherOptions).Assembly.GetType("BEngine.Launcher.ProjectLauncherForm")!;
        using var form = (Form)services.GetRequiredService(formType);
        form.CreateControl();
        var search = (TextBox)Field(form, "_packageSearch");
        var type = (ComboBox)Field(form, "_packageTypeFilter");
        var graph = (Control)Field(form, "_packageCatalogView");
        graph.Size = new Size(640, 420);
        graph.CreateControl();
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks.Add(message);
        }
        string[] Ids() => ((IEnumerable)Property(Property(graph, "Graph"), "Nodes")).Cast<object>()
            .Select(node => (string)Property(node, "Id")).ToArray();
        bool Visible(string id) => VisibleIn(graph, id);
        Check(Ids().Length == 4, "The real Hub form loads all fixture packages");
        search.Text = "audio";
        Check(Ids().SequenceEqual(["com.example.audio"]), "Typing a package name refreshes the real Hub results");
        search.Text = " ANIMATION ";
        Check(Ids().Contains("com.example.animation"), "Package names ignore case and surrounding whitespace");
        Check((string)Field(form, "_graphSelectedPackage") == "com.example.animation", "An exact package name takes priority over another package's description");
        search.Clear();
        Call(graph, "ZoomBy", 2f);
        for (var i = 0; i < 100; i++) Call(graph, "OnKeyDown", new KeyEventArgs(Keys.Left));
        search.Text = "Audio";
        Check(Ids().SequenceEqual(["com.example.audio"]), "Search still finds the package after zooming and panning");
        Check(Visible("com.example.audio"), "Search returns matching packages to the visible viewport");
        Check((string)Field(form, "_graphSelectedPackage") == "com.example.audio" &&
            ((Label)Field(form, "_packageDetailTitle")).Text == "Audio", "Search selects a match and updates its details");
        Call(graph, "ZoomBy", 2f);
        for (var i = 0; i < 100; i++) Call(graph, "OnKeyDown", new KeyEventArgs(Keys.Left));
        search.Text = "Au";
        Check(Visible("com.example.audio"), "Changing a query reveals results even when the result set is unchanged");
        search.Text = "not-a-package";
        Check(Ids().Length == 0, "An unmatched package name shows no results");
        search.Text = "com.example.animation";
        Check(Ids().SequenceEqual(["com.example.animation"]), "A full package ID also matches");
        search.Text = "Example.audio";
        Check(Ids().SequenceEqual(["com.example.audio"]), "A runtime assembly name also matches");
        search.Text = "UI Elements";
        Check(Ids().Contains("com.example.ui") && Visible("com.example.ui"), "A package with dependencies is visible in its search results");
        Call(form, "EnterPackageFocus", "com.example.ui");
        search.Text = "Audio";
        Check(Field(form, "_graphFocusedPackage") is null && Visible("com.example.audio"), "Searching exits dependency focus and shows the matched package");
        Call(graph, "ZoomBy", 1.5f);
        var previousView = Call(graph, "CaptureView")!;
        Call(form, "RefreshPackageList");
        var currentView = Call(graph, "CaptureView")!;
        Check(Property(previousView, "Zoom").Equals(Property(currentView, "Zoom")) &&
            Property(previousView, "Offset").Equals(Property(currentView, "Offset")), "Refreshing an unchanged search preserves the user's viewport");
        type.SelectedItem = "project";
        Check(Ids().Length == 0, "Package type and name filters combine");
        search.Clear();
        Check(Ids().SequenceEqual(["com.example.starter"]), "Clearing the search retains the project type filter");
        type.SelectedItem = "All";
        Check(Ids().Length == 4, "Clearing filters restores the catalog");
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents();
        Call(form, "ShowEnginesPage"); Application.DoEvents();
        var github = (Button)Field(form, "_githubPackagesButton");
        var engineUpdates = (Button)Field(form, "_engineUpdatesButton");
        var website = github.Parent!.Controls.OfType<Button>().Single(button => button != github && button != engineUpdates);
        Check(github.Visible && github.Enabled && github.Right < website.Left,
            "GitHub package management appears immediately left of the website in the engine page header");
        Check(github.Parent.Parent != (Control)Field(form, "_packagesPage"), "The GitHub entry is no longer in the package search toolbar");
        Check(engineUpdates.Visible && engineUpdates.Right < github.Left, "The engine update entry appears immediately left of GitHub package management");
        Check(((Button)Field(form, "_enginesNavigation")).Parent!.Controls.OfType<Button>().All(button => button.Text != "更新"),
            "The sidebar no longer contains an update tab");
        Call(form, "ShowProjectsPage");
        Check(!github.Visible && !engineUpdates.Visible, "Update and package entries are hidden on the project tab");
        Call(form, "ShowSettingsPage");
        Check(!github.Visible && !engineUpdates.Visible, "Update and package entries are hidden on the settings tab");
        Call(form, "ShowEnginesPage"); Application.DoEvents();
        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(root, "HubPackageHeader.png"));
        }
        form.Hide();
        if (args.Length > 1)
        {
            using var installedServices = new ServiceCollection().AddBEngineLauncher(editor, Path.GetFullPath(args[1])).BuildServiceProvider();
            using var installedForm = (Form)installedServices.GetRequiredService(formType);
            installedForm.CreateControl();
            var installedSearch = (TextBox)Field(installedForm, "_packageSearch");
            var installedGraph = (Control)Field(installedForm, "_packageCatalogView");
            installedGraph.Size = new Size(640, 420);
            var packages = ((IEnumerable)Field(installedForm, "_packages")).Cast<object>().ToArray();
            Check(packages.Length > 0, "The real installation catalog is loaded");
            foreach (var package in packages)
            {
                var id = (string)Property(package, "Id");
                installedSearch.Clear();
                Call(installedGraph, "ZoomBy", 2f);
                for (var i = 0; i < 30; i++) Call(installedGraph, "OnKeyDown", new KeyEventArgs(Keys.Left));
                installedSearch.Text = (string)Property(package, "DisplayName");
                Check((string)Field(installedForm, "_graphSelectedPackage") == id && VisibleIn(installedGraph, id),
                    "Installed package name is selected and visible: " + id);
            }
        }
        File.WriteAllText(Path.Combine(root, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }));
        Console.WriteLine($"HUB_PACKAGE_SEARCH_OK|checks={checks.Count}");

        void Package(string id, string name, string description, string? dependency = null, string type = "package")
        {
            var folder = Path.Combine(root, "Packages", id, "1.0.0");
            Directory.CreateDirectory(folder);
            var yaml = $"format: BEngine.Package\nversion: 2\nid: com.example.{id}\npackageVersion: 1.0.0\nminimumEngineVersion: 0.1.0\ntype: {type}\ndisplayName: {name}\ndescription: {description}\n";
            if (type == "project") yaml += "projectDirectory: Starter\n";
            else yaml += $"runtime:\n  assembly: Example.{id}\n  rootNamespace: Example\n";
            if (dependency is not null) yaml += $"  dependencies:\n    - packageId: com.example.{dependency}\n      target: runtime\n";
            File.WriteAllText(Path.Combine(folder, "package.yaml"), yaml);
        }
    }

    private static bool VisibleIn(Control graph, string id)
    {
        var bounds = (RectangleF)((IDictionary)Field(graph, "_bounds"))[id]!;
        var view = Call(graph, "CaptureView")!;
        var zoom = (float)Property(view, "Zoom");
        var offset = (PointF)Property(view, "Offset");
        return graph.ClientRectangle.Contains(Point.Round(new PointF(
            offset.X + (bounds.Left + bounds.Width / 2) * zoom,
            offset.Y + (bounds.Top + bounds.Height / 2) * zoom)));
    }
}
