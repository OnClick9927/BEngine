using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal sealed class PlainResetComponentProbe : Component
{
    internal const int DefaultValue = 7;

    public int value = DefaultValue;

    public PlainResetComponentProbe() { }
}

internal sealed class OnResetComponentProbe : Component
{
    internal const int ResetValue = 37;

    public int value = 11;
    [NonSerialized] public int onResetCalls;

    public OnResetComponentProbe() { }

    public override void OnReset()
    {
        onResetCalls++;
        value = ResetValue;
    }
}

internal sealed class LegacyResetMonoBehaviourProbe : MonoBehaviour
{
    internal const int ResetValue = 43;

    public int value = 13;
    [NonSerialized] public int resetCalls;
    [NonSerialized] public int validateCalls;

    public LegacyResetMonoBehaviourProbe() { }

    public override void Reset()
    {
        resetCalls++;
        value = ResetValue;
    }

    public override void OnValidate() => validateCalls++;
}

internal abstract class ComponentContextBaseProbe : Component
{
    [NonSerialized] public int inheritedInstanceCalls;
    [NonSerialized] public int multiAttributeCalls;

    [ContextMenu(ComponentExtensionMenuProbes.InheritedInstancePath)]
    private void ExecuteInheritedInstanceCommand() => inheritedInstanceCalls++;

    [ContextMenu(ComponentExtensionMenuProbes.MultiAttributeFirstPath)]
    [ContextMenu(ComponentExtensionMenuProbes.MultiAttributeSecondPath)]
    private void ExecuteMultiAttributeCommand() => multiAttributeCalls++;
}

internal sealed class ComponentContextDerivedProbe : ComponentContextBaseProbe
{
    public ComponentContextDerivedProbe() { }
}

internal static class ComponentExtensionMenuProbes
{
    internal const string InheritedInstancePath = "Testing/Inherited Instance Command";
    internal const string MultiAttributeFirstPath = "Testing/Multi Attribute Command A";
    internal const string MultiAttributeSecondPath = "Testing/Multi Attribute Command B";
    internal const string InheritedStaticPath = "Testing/Inherited Static Component Command";
    internal const string ExternalDisplayPath = "Testing/External Component Command";

    private const string InheritedStaticMenuPath =
        "CONTEXT/ComponentContextBaseProbe/" + InheritedStaticPath;
    private const string ExternalMenuPath = "Component/" + ExternalDisplayPath;

    internal static BObject? ExpectedStaticContext { get; set; }
    internal static BObject? LastStaticContext { get; private set; }
    internal static int StaticContextCalls { get; private set; }
    internal static BObject? ExpectedExternalContext { get; set; }
    internal static BObject? LastExternalContext { get; private set; }
    internal static int ExternalCalls { get; private set; }

    internal static void ResetTracking()
    {
        ExpectedStaticContext = null;
        LastStaticContext = null;
        StaticContextCalls = 0;
        ExpectedExternalContext = null;
        LastExternalContext = null;
        ExternalCalls = 0;
    }

    [MenuItem(InheritedStaticMenuPath, priority: 80)]
    public static void ExecuteInheritedStaticCommand(MenuCommand command)
    {
        StaticContextCalls++;
        LastStaticContext = command.context;
    }

    [MenuItem(InheritedStaticMenuPath, isValidateFunction: true)]
    public static bool ValidateInheritedStaticCommand(MenuCommand command) =>
        ReferenceEquals(command.context, ExpectedStaticContext);

    [MenuItem(ExternalMenuPath, priority: 80)]
    public static void ExecuteExternalComponentCommand(MenuCommand command)
    {
        ExternalCalls++;
        LastExternalContext = command.context;
    }

    [MenuItem(ExternalMenuPath, isValidateFunction: true)]
    public static bool ValidateExternalComponentCommand(MenuCommand command) =>
        ReferenceEquals(command.context, ExpectedExternalContext);
}
