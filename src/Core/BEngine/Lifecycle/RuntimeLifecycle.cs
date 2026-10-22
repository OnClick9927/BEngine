namespace BEngine;

public static class RuntimeLifecycle
{
    public static event Action<RuntimeInitializeLoadType>? initializationPhaseChanged;
    public static event Action<Scene>? sceneLoading;
    public static event Action<Scene>? sceneLoaded;
    public static event Action<Scene>? sceneStarted;
    public static event Action<Scene>? sceneStopping;
    public static event Action<Scene>? sceneStopped;
    public static event Action<Scene, Fix64>? frameStarted;
    public static event Action<Scene, Fix64>? frameEnded;

    internal static void BeginScene(Scene scene)
    {
        InvokeInitialization(RuntimeInitializeLoadType.SubsystemRegistration);
        InvokeInitialization(RuntimeInitializeLoadType.AfterAssembliesLoaded);
        InvokeInitialization(RuntimeInitializeLoadType.BeforeSplashScreen);
        InvokeInitialization(RuntimeInitializeLoadType.BeforeSceneLoad);
        Invoke(sceneLoading, scene, nameof(sceneLoading));
    }

    internal static void CompleteSceneLoad(Scene scene)
    {
        InvokeInitialization(RuntimeInitializeLoadType.AfterSceneLoad);
        Invoke(sceneLoaded, scene, nameof(sceneLoaded));
        Invoke(sceneStarted, scene, nameof(sceneStarted));
    }

    internal static void BeginSceneStop(Scene scene) => Invoke(sceneStopping, scene, nameof(sceneStopping));
    internal static void CompleteSceneStop(Scene scene) => Invoke(sceneStopped, scene, nameof(sceneStopped));
    internal static void BeginFrame(Scene scene, Fix64 deltaTime) =>
        Invoke(frameStarted, scene, deltaTime, nameof(frameStarted));
    internal static void CompleteFrame(Scene scene, Fix64 deltaTime) =>
        Invoke(frameEnded, scene, deltaTime, nameof(frameEnded));

    private static void InvokeInitialization(RuntimeInitializeLoadType phase)
    {
        foreach (var initializer in RuntimeTypeCache.GetRuntimeInitializers(phase))
        {
            try { initializer.Callback(); }
            catch (Exception exception)
            { Debug.LogError($"runtime initialization {phase} callback {initializer.Description} failed: " +
                             exception.Message); }
        }

        Invoke(initializationPhaseChanged, phase, nameof(initializationPhaseChanged));
    }

    internal static void Invoke(Action? callback, string callbackName)
    {
        if (callback is null) return;
        try { callback(); }
        catch (Exception exception) { Debug.LogError($"{callbackName} callback failed: {exception.Message}"); }
    }

    internal static void Invoke<T>(Action<T>? callback, T argument, string callbackName)
    {
        if (callback is null) return;
        try { callback(argument); }
        catch (Exception exception) { Debug.LogError($"{callbackName} callback failed: {exception.Message}"); }
    }

    internal static void Invoke<T1, T2>(Action<T1, T2>? callback, T1 first, T2 second, string callbackName)
    {
        if (callback is null) return;
        try { callback(first, second); }
        catch (Exception exception) { Debug.LogError($"{callbackName} callback failed: {exception.Message}"); }
    }
}
