using System.Reflection;

namespace BEngine;

public static class RuntimeLifecycle
{
    private static readonly RuntimeInitializationSession DefaultSession = new();
    private static readonly List<RuntimeInitializationSession> Sessions = [];

    /// <summary>The current initialization session, or the process lifetime fallback for standalone scenes.</summary>
    public static RuntimeInitializationSession CurrentSession => Sessions.Count == 0 ? DefaultSession : Sessions[^1];

    /// <summary>
    /// Begins a fresh runtime initialization lifetime. Hosts should keep this scope until every scene and
    /// code domain in the run has stopped. Initializers execute once per method in this session.
    /// Calls and disposal must occur on the owning engine thread.
    /// </summary>
    public static RuntimeInitializationSession BeginSession()
    {
        var session = new RuntimeInitializationSession();
        Sessions.Add(session);
        return session;
    }

    /// <summary>
    /// Initializes available code through the requested phase in lifecycle order. An assembly filter
    /// initializes only that code domain; shared assemblies already initialized in the session stay intact.
    /// </summary>
    public static void InitializeThrough(RuntimeInitializeLoadType phase, IEnumerable<Assembly>? assemblies = null) =>
        CurrentSession.InitializeThrough(phase, assemblies);

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
        Invoke(sceneLoading, scene, nameof(sceneLoading));
    }

    internal static void CompleteSceneLoad(Scene scene)
    {
        InitializeThrough(RuntimeInitializeLoadType.AfterSceneLoad);
        Invoke(sceneLoaded, scene, nameof(sceneLoaded));
        Invoke(sceneStarted, scene, nameof(sceneStarted));
    }

    internal static void BeginSceneStop(Scene scene) => Invoke(sceneStopping, scene, nameof(sceneStopping));
    internal static void CompleteSceneStop(Scene scene) => Invoke(sceneStopped, scene, nameof(sceneStopped));
    internal static void BeginFrame(Scene scene, Fix64 deltaTime) =>
        Invoke(frameStarted, scene, deltaTime, nameof(frameStarted));
    internal static void CompleteFrame(Scene scene, Fix64 deltaTime) =>
        Invoke(frameEnded, scene, deltaTime, nameof(frameEnded));

    internal static void NotifyInitialization(RuntimeInitializeLoadType phase) =>
        Invoke(initializationPhaseChanged, phase, nameof(initializationPhaseChanged));

    internal static void EndSession(RuntimeInitializationSession session) => Sessions.Remove(session);

    internal static void ForgetAssemblies(IReadOnlySet<Assembly> assemblies)
    {
        DefaultSession.ForgetAssemblies(assemblies);
        foreach (var session in Sessions) session.ForgetAssemblies(assemblies);
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
