using System.Collections;
using BEngine;
using BEngine.Editor;
using BEngine.Serialization;

namespace BEngine.ExampleTests.ReflectionStartupCache;

internal static class Program
{
    private static int _runtimeInitializationCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeRuntime() => _runtimeInitializationCount++;

    private static int Main()
    {
        RuntimeTypeCache.Warmup();
        var warmed = RuntimeTypeCache.stats;
        Require(warmed.AssemblyCount > 0 && warmed.TypeCount > 0, "Runtime reflection cache is empty.");
        Require(typeof(ReflectionCacheStats).IsValueType, "Reflection cache statistics must remain a value type.");

        for (var index = 0; index < 100; index++)
        {
            RuntimeTypeCache.Warmup();
            _ = RuntimeTypeCache.FindType(typeof(CacheProbe).FullName!);
            _ = TypeCache.GetTypesDerivedFrom<MonoBehaviour>();
            _ = TypeCache.GetMethodsWithAttribute<RuntimeInitializeOnLoadMethodAttribute>();
        }
        var queried = RuntimeTypeCache.stats;
        Require(queried.AssembliesScanned == warmed.AssembliesScanned &&
                queried.TypesScanned == warmed.TypesScanned,
            "A cached reflection query rescanned loaded assemblies.");

        var scene = new Scene("Reflection cache test");
        var gameObject = scene.CreateGameObject("Probe");
        var probe = gameObject.AddComponent<CacheProbe>();
        var serialized = new SerializedObject(probe);
        var counter = serialized.FindProperty(nameof(CacheProbe.counter)) ??
                      throw new InvalidOperationException("Serialized field was not indexed.");
        for (var index = 0; index < 2000; index++)
        {
            counter.intValue = index;
            _ = counter.displayName;
            _ = counter.tooltip;
        }
        Require(RuntimeTypeCache.stats.AssembliesScanned == warmed.AssembliesScanned,
            "SerializedProperty access triggered another assembly scan.");

        using var allocationScope = new AllocationScope();
        var runtime = new SceneRuntime(scene);
        runtime.Start();
        probe.BeginCachedCoroutine();
        Require(_runtimeInitializationCount == 1, "Runtime initializer was not resolved from the startup cache.");
        Require(CacheRuntimeSystem.StartCount == 1, "Runtime system factory was not resolved from the startup cache.");
        for (var frame = 0; frame < 300; frame++) runtime.Tick((Fix64)(1.0 / 60.0));
        var allocated = allocationScope.AllocatedBytes;
        runtime.Stop();

        Require(probe.updateCount == 300, "Cached MonoBehaviour dispatch missed Update calls.");
        Require(CacheRuntimeSystem.UpdateCount == 300, "Cached runtime system dispatch missed Update calls.");
        Require(allocated < 128 * 1024,
            $"Steady-state runtime allocated too much memory: {allocated} bytes for 300 frames.");

        Console.WriteLine($"REFLECTION_STARTUP_CACHE_OK|assemblies={warmed.AssemblyCount}," +
                          $"types={warmed.TypeCount},frames=300,allocated={allocated}");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class CacheProbe : MonoBehaviour
{
    [Tooltip("Cached tooltip")]
    public int counter = 1;
    public int updateCount;
    public override void Update() => updateCount++;
    internal void BeginCachedCoroutine() => StartCoroutine(nameof(CachedCoroutine));
    private IEnumerator CachedCoroutine()
    {
        for (var index = 0; index < 300; index++) yield return null;
    }
}

internal sealed class CacheRuntimeSystem : ISceneRuntimeSystem
{
    internal static int StartCount;
    internal static int UpdateCount;
    public void Start(Scene scene) => StartCount++;
    public void Update(Scene scene, Fix64 deltaTime) => UpdateCount++;
}

internal sealed class AllocationScope : IDisposable
{
    private readonly long _start;
    internal AllocationScope()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _start = GC.GetAllocatedBytesForCurrentThread();
    }
    internal long AllocatedBytes => GC.GetAllocatedBytesForCurrentThread() - _start;
    public void Dispose() { }
}
