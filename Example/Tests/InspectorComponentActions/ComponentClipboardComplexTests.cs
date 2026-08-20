using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class ComponentClipboardComplexTests
{
    private const string ProbeTitle = "Component Clipboard Probe (Script)";
    private const string MissingTitle = "Missing Component";

    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        VerifyComplexValuesAreSnapshotAtCopyTime(fixture);
        VerifyMissingComponentClipboardRules(fixture);
    }

    private static void VerifyComplexValuesAreSnapshotAtCopyTime(InspectorWorkspaceFixture fixture)
    {
        var copiedReference = new GameObject("Copied Reference");
        var replacementReference = new GameObject("Replacement Reference");
        var sourceOwner = new GameObject("Complex Clipboard Source");
        var source = sourceOwner.AddComponent<ComponentClipboardProbe>();
        source.numbers = [1, 2, 3];
        source.nestedValues =
        [
            new NestedValue { number = 11, label = "First" },
            new NestedValue { number = 22, label = "Second" }
        ];
        source.referencedObject = copiedReference;
        source.enabled = false;
        var sourceNumbers = source.numbers;
        var sourceValues = source.nestedValues;
        var sourceFirstValue = source.nestedValues[0];

        using (var sourceInspector = new InspectorHarness(sourceOwner, fixture.Workspace))
        {
            GenericMenuCapture.Install();
            ComponentMenuTests.OpenMenu(sourceInspector, ProbeTitle);
            GenericMenuCapture.Invoke("Copy Component");
        }

        source.numbers[0] = 101;
        source.nestedValues[0].number = 111;
        source.nestedValues[0].label = "Changed After Copy";
        source.nestedValues.Add(new NestedValue { number = 33, label = "Added After Copy" });
        source.referencedObject = replacementReference;

        var originalReference = new GameObject("Original Target Reference");
        var targetOwner = new GameObject("Complex Clipboard Target");
        var target = targetOwner.AddComponent<ComponentClipboardProbe>();
        target.numbers = [8, 9];
        target.nestedValues = [new NestedValue { number = 88, label = "Original" }];
        target.referencedObject = originalReference;
        target.enabled = true;

        Undo.ClearAll();
        using var targetInspector = new InspectorHarness(targetOwner, fixture.Workspace);
        GenericMenuCapture.Install();
        ComponentMenuTests.OpenMenu(targetInspector, ProbeTitle);
        TestAssert.Require(MenuEnabled("Paste Component Values"),
            "Paste Component Values was disabled for the same complex component type.");
        GenericMenuCapture.Invoke("Paste Component Values");

        AssertCopiedState(target, copiedReference, "Paste");
        TestAssert.Require(!ReferenceEquals(target.numbers, sourceNumbers) &&
                           !ReferenceEquals(target.nestedValues, sourceValues) &&
                           !ReferenceEquals(target.nestedValues[0], sourceFirstValue),
            "Paste shared a copied array, list, or nested value with the source component.");

        Undo.PerformUndo();
        AssertState(target, [8, 9], [(88, "Original")], originalReference, true, "Undo");
        Undo.PerformRedo();
        AssertCopiedState(target, copiedReference, "Redo");
        TestAssert.Require(!ReferenceEquals(target.numbers, sourceNumbers) &&
                           !ReferenceEquals(target.nestedValues, sourceValues) &&
                           !ReferenceEquals(target.nestedValues[0], sourceFirstValue),
            "Redo restored collections that share mutable state with the clipboard source.");
        Undo.ClearAll();
    }

    private static void VerifyMissingComponentClipboardRules(InspectorWorkspaceFixture fixture)
    {
        const string originalType = "Vendor.Gameplay.MissingProbe, Vendor.Gameplay";
        var copiedFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Answer"] = "42",
            ["OpaqueYaml"] = "{x: 1, nested: [a, b]}",
            ["Empty"] = string.Empty
        };
        var sourceOwner = new GameObject("Missing Clipboard Source");
        var source = CreateMissingComponent(sourceOwner, originalType, copiedFields);
        using (var sourceInspector = new InspectorHarness(sourceOwner, fixture.Workspace))
        {
            GenericMenuCapture.Install();
            ComponentMenuTests.OpenMenu(sourceInspector, MissingTitle);
            TestAssert.Require(!MenuEnabled("Reset"),
                "Reset was enabled for a MissingComponent.");
            GenericMenuCapture.Invoke("Copy Component");
        }
        var changedSourceFields = new Dictionary<string, string>(
            source.serializedFields, StringComparer.Ordinal)
        {
            ["Answer"] = "Changed After Copy",
            ["Later"] = "Must Not Leak"
        };
        SetMissingProperty(source, nameof(MissingComponent.serializedFields), changedSourceFields);

        var targetOwner = new GameObject("Missing Clipboard Target");
        var target = CreateMissingComponent(targetOwner, originalType,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Before"] = "Undo Value" });
        Undo.ClearAll();
        using (var targetInspector = new InspectorHarness(targetOwner, fixture.Workspace))
        {
            GenericMenuCapture.Install();
            ComponentMenuTests.OpenMenu(targetInspector, MissingTitle);
            TestAssert.Require(MenuEnabled("Paste Component Values"),
                "Paste Component Values was disabled for matching MissingComponent originalType values.");
            TestAssert.Require(!MenuEnabled("Reset"),
                "Reset was enabled for a matching MissingComponent.");
            GenericMenuCapture.Invoke("Paste Component Values");
        }
        AssertFields(target.serializedFields, copiedFields, "MissingComponent Paste");
        TestAssert.Require(!ReferenceEquals(target.serializedFields, copiedFields) &&
                           !ReferenceEquals(target.serializedFields, source.serializedFields),
            "MissingComponent Paste reused the source or caller field dictionary.");
        Undo.ClearAll();

        var incompatibleOwner = new GameObject("Different Missing Clipboard Target");
        CreateMissingComponent(incompatibleOwner, "Vendor.Other.MissingProbe, Vendor.Other",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Keep"] = "Unchanged" });
        using var incompatibleInspector = new InspectorHarness(incompatibleOwner, fixture.Workspace);
        GenericMenuCapture.Install();
        ComponentMenuTests.OpenMenu(incompatibleInspector, MissingTitle);
        TestAssert.Require(!MenuEnabled("Paste Component Values"),
            "Paste Component Values was enabled for different MissingComponent originalType values.");
        TestAssert.Require(!MenuEnabled("Reset"),
            "Reset was enabled for an incompatible MissingComponent.");
    }

    private static MissingComponent CreateMissingComponent(GameObject owner, string originalType,
        Dictionary<string, string> fields)
    {
        var component = owner.AddComponent<MissingComponent>();
        SetMissingProperty(component, nameof(MissingComponent.originalType), originalType);
        SetMissingProperty(component, nameof(MissingComponent.serializedFields),
            new Dictionary<string, string>(fields, StringComparer.Ordinal));
        return component;
    }

    private static void SetMissingProperty(MissingComponent component, string name, object value)
    {
        var property = typeof(MissingComponent).GetProperty(name,
                           BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                       throw new MissingMemberException(typeof(MissingComponent).FullName, name);
        property.SetValue(component, value);
    }

    private static bool MenuEnabled(string path) => GenericMenuCapture.Items.Single(item =>
        string.Equals(item.Path, path, StringComparison.Ordinal)).Enabled;

    private static void AssertCopiedState(ComponentClipboardProbe target, BObject reference, string operation) =>
        AssertState(target, [1, 2, 3], [(11, "First"), (22, "Second")], reference, false, operation);

    private static void AssertState(ComponentClipboardProbe target, IReadOnlyList<int> numbers,
        IReadOnlyList<(int Number, string Label)> nestedValues, BObject reference, bool enabled,
        string operation)
    {
        TestAssert.Require(target.numbers.SequenceEqual(numbers),
            $"{operation} did not restore the complete integer array.");
        TestAssert.Require(target.nestedValues.Count == nestedValues.Count &&
                           target.nestedValues.Select(value => (value.number, value.label))
                               .SequenceEqual(nestedValues),
            $"{operation} did not restore the complete nested value list.");
        TestAssert.Require(ReferenceEquals(target.referencedObject, reference),
            $"{operation} did not preserve the expected BObject reference identity.");
        TestAssert.Require(target.enabled == enabled,
            $"{operation} did not restore the component enabled state.");
    }

    private static void AssertFields(IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected, string operation)
    {
        TestAssert.Require(actual.Count == expected.Count && expected.All(pair =>
                actual.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value,
                    StringComparison.Ordinal)),
            $"{operation} did not preserve the MissingComponent field dictionary exactly.");
    }
}
