using System.Runtime.CompilerServices;
using BEngine.Editor;

namespace BEngine.ExampleTests.EditorFeatureFaultIsolation;

internal static class FaultingMenuCommands
{
    internal const string BadCommandPath = "Fault Isolation/Bad Command";
    internal const string BadValidatorPath = "Fault Isolation/Bad Validator";
    internal const string HealthyCommandPath = "Fault Isolation/Healthy Command";

    internal static int HealthyCommandCalls { get; private set; }
    internal static int ValidatedCommandCalls { get; private set; }

    [MenuItem(BadCommandPath, false, -30_000)]
    private static void BadCommand() => ThrowFromBadMenuCommand();

    [MenuItem(BadValidatorPath, false, -29_999)]
    private static void BadValidatorCommand() => ValidatedCommandCalls++;

    [MenuItem(BadValidatorPath, true)]
    private static bool BadValidator() => ThrowFromBadMenuValidator();

    [MenuItem(HealthyCommandPath, false, -29_998)]
    private static void HealthyCommand() => HealthyCommandCalls++;

    internal static void Reset()
    {
        HealthyCommandCalls = 0;
        ValidatedCommandCalls = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromBadMenuCommand() =>
        throw new InvalidOperationException("MENU_COMMAND_SENTINEL");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ThrowFromBadMenuValidator() =>
        throw new InvalidOperationException("MENU_VALIDATOR_SENTINEL");
}
