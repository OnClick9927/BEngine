using System.Runtime.CompilerServices;

namespace BEngine;

internal static class MainThreadGuard
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Ensure([CallerMemberName] string? operation = null) =>
        EngineThreadContext.AssertMainThread(operation);
}
