using System.Reflection;
using BEngine.Editor;
using BEngine.PropertyAttributes;
using BEngine.PropertyAttributes.Editor;
using BEngine.ProjectSystem;
using BEngine.UIElements;

namespace BEngine.ExampleTests.PropertyAttributes;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            _ = typeof(ExtendedPropertyDrawer).Assembly;
            RegisterEditorResources();
            VerifyPackageDefinition();
            VerifyAttributeSurface();
            VerifyRetainedDrawers();
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
        var path = Path.Combine(repositoryRoot, "src", "PropertyAttributes", "package.yaml");
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

    private static void VerifyRetainedDrawers()
    {
        var fixture = new AttributeFixture();
        using var serialized = new SerializedObject(fixture);

        var configField = CreateField(serialized, nameof(AttributeFixture.config));
        var configRoot = configField.Children.Single();
        Assert(configRoot.style.display == DisplayStyle.None, "ShowIf did not initially hide the property.");
        Assert(configField.Q<TextField>() is { enabledInHierarchy: false }, "ReadOnly did not disable the field.");
        Assert(configField.DescendantsAndSelf().OfType<HelpBox>().Any(box => box.messageType == MessageType.Error),
            "Required did not create an error HelpBox.");

        serialized.FindProperty(nameof(AttributeFixture.advanced))!.boolValue = true;
        Assert(configRoot.style.display == DisplayStyle.Flex, "ShowIf did not react to SerializedObject changes.");

        var passwordField = CreateField(serialized, nameof(AttributeFixture.password));
        Assert(passwordField.Q<TextField>() is { isPasswordField: true, maskCharacter: '#' },
            "Password did not configure the retained TextField.");

        var speedField = CreateField(serialized, nameof(AttributeFixture.speed));
        Assert(speedField.Q<Slider>() is not null, "Range did not compose with the extension drawer.");
        serialized.FindProperty(nameof(AttributeFixture.speed))!.doubleValue = 99;
        Assert(Math.Abs(serialized.FindProperty(nameof(AttributeFixture.speed))!.doubleValue - 8) < 0.001,
            "Clamp did not constrain a SerializedProperty change.");

        var healthField = CreateField(serialized, nameof(AttributeFixture.health));
        Assert(healthField.Q<BEngine.UIElements.ProgressBar>() is not null,
            "ProgressBar did not create a retained progress control.");
        Assert(healthField.Q<Slider>() is not null, "Editable ProgressBar did not create its slider.");
    }

    private static PropertyField CreateField(SerializedObject serialized, string name) =>
        new(serialized.FindProperty(name) ?? throw new InvalidOperationException($"Missing property '{name}'."));

    private static void RegisterEditorResources()
    {
        var root = Directory.GetCurrentDirectory();
        var packageRoot = Path.Combine(root, "src", "PropertyAttributes");
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
