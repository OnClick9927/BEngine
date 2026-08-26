using System.Reflection;
using BEngine.Editor;
using BEngine.PropertyAttributes;
using BEngine.PropertyAttributes.Editor;
using BEngine.ProjectSystem;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.PropertyAttributes;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    [STAThread]
    private static int Main()
    {
        try
        {
            _ = typeof(ExtendedPropertyDrawer).Assembly;
            RegisterEditorResources();
            VerifyPackageDefinition();
            VerifyAttributeSurface();
            VerifyImGuiDrawers();
            Console.WriteLine("PROPERTY_ATTRIBUTES_OK|attributes=25|drawers=conditional,readonly,required,password,range,clamp,progress");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PROPERTY_ATTRIBUTES_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyAttributeSurface()
    {
        var count = typeof(ExtendedPropertyAttribute).Assembly.GetTypes().Count(type =>
            type.IsSealed && typeof(ExtendedPropertyAttribute).IsAssignableFrom(type));
        Assert(count >= 25, $"Expected at least 25 concrete property attributes, found {count}.");
        Assert(typeof(ExtendedPropertyDrawer).GetCustomAttribute<CustomPropertyDrawerAttribute>() is { useForChildren: true },
            "The combined drawer must support every ExtendedPropertyAttribute subtype.");
    }

    private static void VerifyPackageDefinition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "src", "Packages", "PropertyAttributes", "package.yaml");
        var definition = PackageDefinitionLoader.Load(path);
        Assert(definition.Id == "com.bengine.property-attributes", "The package id is invalid.");
        Assert(definition.Runtime?.Assembly == "BEngine.PropertyAttributes", "The runtime assembly is not registered.");
        var editorAssembly = definition.Editor ?? throw new InvalidOperationException("The editor assembly is missing.");
        Assert(editorAssembly.Assembly == "BEngine.PropertyAttributes.Editor",
            "The editor assembly is not registered.");
        Assert(editorAssembly.Dependencies.Any(item =>
                item.PackageId == definition.Id && item.Target == "runtime"),
            "The editor-to-runtime package dependency is missing.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the BEngine repository root from the test directory.");
    }

    private static void VerifyImGuiDrawers()
    {
        var fixture = new AttributeFixture();
        using var serialized = new SerializedObject(fixture);

        var config = Find(serialized, nameof(AttributeFixture.config));
        Assert(EditorGUI.GetPropertyHeight(config) == 0, "ShowIf did not initially hide the property.");

        serialized.FindProperty(nameof(AttributeFixture.advanced))!.boolValue = true;
        Assert(EditorGUI.GetPropertyHeight(config) > 0, "ShowIf did not react to SerializedObject changes.");
        GUI.enabled = true;
        var configCommands = Render(() => EditorGUI.PropertyField(new Rect(0, 0, 420, 80), config));
        Assert(GUI.enabled, "ReadOnly leaked its disabled state into following controls.");
        Assert(configCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.Content.Contains("Configuration is required",
                                                 StringComparison.Ordinal)),
            "Required did not render an error HelpBox.");

        var passwordCommands = Render(() => EditorGUI.PropertyField(new Rect(0, 0, 420, 20),
            Find(serialized, nameof(AttributeFixture.password))));
        Assert(passwordCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                               command.Content == "######") &&
               passwordCommands.All(command => command.Content != fixture.password),
            "Password did not mask the IMGUI text field.");

        var speed = Find(serialized, nameof(AttributeFixture.speed));
        speed.doubleValue = 99;
        var speedCommands = Render(() => EditorGUI.PropertyField(new Rect(0, 0, 420, 20), speed));
        Assert(speedCommands.Count(command => command.Type == GpuCanvasCommandType.SolidRect) >= 2,
            "Range did not render an IMGUI slider.");
        Assert(Math.Abs(serialized.FindProperty(nameof(AttributeFixture.speed))!.doubleValue - 8) < 0.001,
            "Clamp did not constrain a SerializedProperty change.");

        var healthCommands = Render(() => EditorGUI.PropertyField(new Rect(0, 0, 420, 20),
            Find(serialized, nameof(AttributeFixture.health))));
        Assert(healthCommands.Count(command => command.Type == GpuCanvasCommandType.SolidRect) >= 3 &&
               healthCommands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.Content == "Health"),
            "ProgressBar did not render its fill, title, and editable slider.");
    }

    private static SerializedProperty Find(SerializedObject serialized, string name) =>
        serialized.FindProperty(name) ?? throw new InvalidOperationException($"Missing property '{name}'.");

    private static List<GpuCanvasCommand> Render(Action draw)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [new Event(EventType.Repaint), 480, 120, commands]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private static void RegisterEditorResources()
    {
        var root = Directory.GetCurrentDirectory();
        var packageRoot = Path.Combine(root, "src", "Packages", "PropertyAttributes");
        if (Directory.Exists(packageRoot)) EditorResources.RegisterResourceRoot(packageRoot);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class AttributeFixture : ScriptableObject
{
    public bool advanced { get; set; }

    [ShowIf(nameof(advanced))]
    [ReadOnly]
    [Required("Configuration is required.")]
    [Tooltip("Advanced configuration")]
    public string config { get; set; } = string.Empty;

    [Password('#')]
    public string password { get; set; } = "secret";

    [Range(0, 10)]
    [Clamp(2, 8)]
    public Fix64 speed { get; set; } = (Fix64)5;

    [ProgressBar(0, 100, "Health")]
    public Fix64 health { get; set; } = (Fix64)75;
}
