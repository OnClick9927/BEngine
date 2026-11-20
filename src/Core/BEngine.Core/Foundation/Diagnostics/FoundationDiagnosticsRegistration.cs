using System.Runtime.CompilerServices;
using BEngine.Instrumentation;

namespace BEngine;

internal static class FoundationDiagnosticsRegistration
{
    [ModuleInitializer]
    internal static void Initialize() =>
        DiagnosticsPlatform.SetServiceResolver(static () => RuntimeServices.GetService<IRuntimeDiagnosticsPlatform>());
}
