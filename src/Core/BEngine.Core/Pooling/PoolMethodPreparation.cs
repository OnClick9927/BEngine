using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BEngine;

// Prewarm the measured first-return bodies once for this constructed pool type.
// No object is rented/returned and no factory or client callback is invoked.
internal static class PoolMethodPreparation<T> where T : class
{
    static PoolMethodPreparation()
    {
        if (!OperatingSystem.IsWindows() || !RuntimeFeature.IsDynamicCodeSupported ||
            !RuntimeFeature.IsDynamicCodeCompiled || RuntimeInformation.FrameworkDescription
                .StartsWith("Mono", StringComparison.OrdinalIgnoreCase)) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in new[] { nameof(ObjectPool<T>.Release), "ReturnCore" })
        {
            var method = typeof(ObjectPool<T>).GetMethod(name, flags, null, [typeof(T)], null);
            // A trimmed consumer may legitimately have no return path.
            if (method is not null) RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }
    }

    internal static void Prepare() { }
}
