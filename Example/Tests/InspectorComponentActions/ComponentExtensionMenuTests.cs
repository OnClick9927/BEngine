using System.Collections;
using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class ComponentExtensionMenuTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo HarnessApplication = typeof(InspectorHarness).GetField(
        "_application", Members) ?? throw new MissingFieldException(typeof(InspectorHarness).FullName, "_application");
    private static readonly Type ApplicationType = typeof(EditorWindow).Assembly.GetType(
        "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;

    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        VerifyComponentOnReset(fixture);
        VerifyContextMenuExtensions(fixture);
        VerifyComponentMenuEntryPointConsistency(fixture);
    }

    private static void VerifyComponentOnReset(InspectorWorkspaceFixture fixture)
    {
        Undo.ClearAll();
        var plainOwner = new GameObject("Plain Reset Target");
        var plain = plainOwner.AddComponent<PlainResetComponentProbe>();
        plain.value = 99;
        ResetThroughInspector(plainOwner, plain, fixture);
        TestAssert.Require(plain.value == PlainResetComponentProbe.DefaultValue,
            "Reset did not restore an ordinary Component that uses the base OnReset implementation.");

        Undo.ClearAll();
        var callbackOwner = new GameObject("OnReset Target");
        var callback = callbackOwner.AddComponent<OnResetComponentProbe>();
        callback.value = 99;
        ResetThroughInspector(callbackOwner, callback, fixture);
        TestAssert.Require(callback.value == OnResetComponentProbe.ResetValue && callback.onResetCalls == 1,
            "Inspector Reset did not invoke Component.OnReset exactly once.");

        Undo.ClearAll();
        var factoryOwner = new GameObject("ObjectFactory OnReset Target");
        var factoryCallback = ObjectFactory.AddComponent<OnResetComponentProbe>(factoryOwner);
        TestAssert.Require(factoryCallback.value == OnResetComponentProbe.ResetValue &&
                           factoryCallback.onResetCalls == 1,
            "ObjectFactory.AddComponent did not invoke Component.OnReset exactly once.");

        Undo.ClearAll();
        var legacyOwner = new GameObject("Legacy Reset Target");
        var legacy = legacyOwner.AddComponent<LegacyResetMonoBehaviourProbe>();
        legacy.value = 99;
        ResetThroughInspector(legacyOwner, legacy, fixture);
        TestAssert.Require(legacy.value == LegacyResetMonoBehaviourProbe.ResetValue &&
                           legacy.resetCalls == 1 && legacy.validateCalls == 1,
            "MonoBehaviour.Reset compatibility did not run exactly once through Component.OnReset.");

        Undo.ClearAll();
        var factoryLegacyOwner = new GameObject("ObjectFactory Legacy Reset Target");
        var factoryLegacy = ObjectFactory.AddComponent<LegacyResetMonoBehaviourProbe>(factoryLegacyOwner);
        TestAssert.Require(factoryLegacy.value == LegacyResetMonoBehaviourProbe.ResetValue &&
                           factoryLegacy.resetCalls == 1 && factoryLegacy.validateCalls == 1,
            "ObjectFactory did not preserve legacy MonoBehaviour.Reset and OnValidate behavior.");
        Undo.ClearAll();
    }

    private static void VerifyContextMenuExtensions(InspectorWorkspaceFixture fixture)
    {
        Undo.ClearAll();
        ComponentExtensionMenuProbes.ResetTracking();
        var owner = new GameObject("Component Context Target");
        var component = owner.AddComponent<ComponentContextDerivedProbe>();
        ComponentExtensionMenuProbes.ExpectedStaticContext = component;

        using var inspector = new InspectorHarness(owner, fixture.Workspace);
        InstallDiscoveredMenuRegistry(inspector);
        GenericMenuCapture.Install();
        ComponentMenuTests.OpenMenu(inspector, ComponentTitle(component));
        var items = GenericMenuCapture.Items;

        RequireSingleEnabledAction(items, ComponentExtensionMenuProbes.InheritedInstancePath,
            "A base-class ContextMenu command was not inherited by the derived Component.");
        RequireSingleEnabledAction(items, ComponentExtensionMenuProbes.MultiAttributeFirstPath,
            "The first ContextMenu attribute on a method was not exposed.");
        RequireSingleEnabledAction(items, ComponentExtensionMenuProbes.MultiAttributeSecondPath,
            "The second ContextMenu attribute on a method was not exposed.");
        RequireSingleEnabledAction(items, ComponentExtensionMenuProbes.InheritedStaticPath,
            "A static CONTEXT command declared for a base Component was not inherited.");

        GenericMenuCapture.Invoke(ComponentExtensionMenuProbes.InheritedInstancePath);
        TestAssert.Require(component.inheritedInstanceCalls == 1,
            "The inherited instance ContextMenu command did not execute on the selected Component.");
        GenericMenuCapture.Invoke(ComponentExtensionMenuProbes.MultiAttributeFirstPath);
        GenericMenuCapture.Invoke(ComponentExtensionMenuProbes.MultiAttributeSecondPath);
        TestAssert.Require(component.multiAttributeCalls == 2,
            "Multiple ContextMenu attributes did not execute their shared method once per command.");
        GenericMenuCapture.Invoke(ComponentExtensionMenuProbes.InheritedStaticPath);
        TestAssert.Require(ComponentExtensionMenuProbes.StaticContextCalls == 1 &&
                           ReferenceEquals(ComponentExtensionMenuProbes.LastStaticContext, component),
            "The inherited static CONTEXT command did not receive the exact selected Component.");

        ComponentExtensionMenuProbes.ResetTracking();
        Undo.ClearAll();
    }

    private static void VerifyComponentMenuEntryPointConsistency(InspectorWorkspaceFixture fixture)
    {
        ComponentExtensionMenuProbes.ResetTracking();
        var target = new GameObject("External Component Command Target");
        ComponentExtensionMenuProbes.ExpectedExternalContext = target;
        Selection.activeObject = target;

        try
        {
            using var inspector = new InspectorHarness(target, fixture.Workspace);
            InstallDiscoveredMenuRegistry(inspector);
            GenericMenuCapture.Install();

            var mainItem = ReadMainComponentMenu(inspector).SingleOrDefault(item =>
                item.Label == ComponentExtensionMenuProbes.ExternalDisplayPath);
            TestAssert.Require(mainItem.Label is not null,
                "The top Component menu did not include an external Component/MenuItem command.");

            ShowAddComponentMenu(inspector);
            TestAssert.Require(GenericMenuCapture.IsAdvanced,
                "The Inspector Add Component button did not open as an AdvancedDropdown.");
            var addItems = GenericMenuCapture.Items.Where(item =>
                item.Path == ComponentExtensionMenuProbes.ExternalDisplayPath).ToArray();
            TestAssert.Require(addItems.Length == 1,
                "The Inspector Add Component popup did not include exactly one external Component command.");
            var addItem = addItems[0];
            TestAssert.Require(mainItem.Enabled == addItem.Enabled,
                "The top Component menu and Add Component popup disagreed on command Enabled state.");
            TestAssert.Require(mainItem.Enabled && mainItem.Action is not null && addItem.HasAction,
                "The shared external Component command did not expose an enabled action in both entry points.");

            mainItem.Action!();
            TestAssert.Require(ComponentExtensionMenuProbes.ExternalCalls == 1 &&
                               ReferenceEquals(ComponentExtensionMenuProbes.LastExternalContext, target),
                "The top Component menu action did not execute with the selected GameObject context.");
            GenericMenuCapture.Invoke(ComponentExtensionMenuProbes.ExternalDisplayPath);
            TestAssert.Require(ComponentExtensionMenuProbes.ExternalCalls == 2 &&
                               ReferenceEquals(ComponentExtensionMenuProbes.LastExternalContext, target),
                "The Add Component popup action was not behaviorally consistent with the top menu action.");
        }
        finally
        {
            Selection.activeObject = null;
            ComponentExtensionMenuProbes.ResetTracking();
        }
    }

    private static void ResetThroughInspector(GameObject owner, Component component,
        InspectorWorkspaceFixture fixture)
    {
        using var inspector = new InspectorHarness(owner, fixture.Workspace);
        GenericMenuCapture.Install();
        ComponentMenuTests.OpenMenu(inspector, ComponentTitle(component));
        GenericMenuCapture.Invoke("Reset");
    }

    private static string ComponentTitle(Component component) =>
        ObjectNames.NicifyVariableName(component.GetType().Name) +
        (component is MonoBehaviour ? " (Script)" : string.Empty);

    private static void RequireSingleEnabledAction(IReadOnlyList<MenuItemSnapshot> items, string path,
        string failure)
    {
        var matching = items.Where(item => item.Path == path).ToArray();
        TestAssert.Require(matching.Length == 1 && matching[0].Enabled && matching[0].HasAction, failure);
    }

    private static void InstallDiscoveredMenuRegistry(InspectorHarness inspector)
    {
        var application = HarnessApplication.GetValue(inspector) ??
                          throw new InvalidOperationException("Inspector harness application is unavailable.");
        var registryType = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.MenuItemRegistry", throwOnError: true)!;
        var registry = registryType.GetMethod("Discover", StaticMembers)!.Invoke(null, null) ??
                       throw new InvalidOperationException("MenuItemRegistry.Discover returned null.");
        ApplicationType.GetField("_menuItems", Members)!.SetValue(application, registry);
    }

    private static ReflectedMenuItem[] ReadMainComponentMenu(InspectorHarness inspector)
    {
        var application = HarnessApplication.GetValue(inspector) ??
                          throw new InvalidOperationException("Inspector harness application is unavailable.");
        var raw = ApplicationType.GetMethod("MenuItems", Members)!.Invoke(application, ["Component"])
                  as IEnumerable ?? throw new InvalidOperationException("Component menu enumeration is unavailable.");
        return raw.Cast<object>().Select(ReadMenuItem).ToArray();
    }

    private static void ShowAddComponentMenu(InspectorHarness inspector)
    {
        var application = HarnessApplication.GetValue(inspector) ??
                          throw new InvalidOperationException("Inspector harness application is unavailable.");
        GenericMenuCapture.Reset();
        ApplicationType.GetMethod("ShowAddComponentMenu", Members)!.Invoke(application, null);
        TestAssert.Require(GenericMenuCapture.HasMenu, "ShowAddComponentMenu did not display a GenericMenu.");
    }

    private static ReflectedMenuItem ReadMenuItem(object item)
    {
        var type = item.GetType();
        return new ReflectedMenuItem(
            type.GetProperty("Label", Members)!.GetValue(item) as string,
            (bool)(type.GetProperty("Enabled", Members)!.GetValue(item) ?? false),
            type.GetProperty("Action", Members)!.GetValue(item) as Action);
    }

    private readonly record struct ReflectedMenuItem(string? Label, bool Enabled, Action? Action);
}
