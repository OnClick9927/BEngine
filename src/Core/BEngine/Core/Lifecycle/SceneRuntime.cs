using BEngine.SceneManagement;

namespace BEngine;

public sealed class SceneRuntime
{
    private static readonly Dictionary<Scene, SceneRuntime> ActiveRuntimes = [];
    private static Scene? _currentScene;

    private readonly HashSet<MonoBehaviour> _awakened = [];
    private readonly HashSet<MonoBehaviour> _enabled = [];
    private readonly HashSet<MonoBehaviour> _started = [];
    private readonly List<MonoBehaviour> _behaviours = [];
    private MonoBehaviour[] _enabledSnapshot = [];
    private bool _enabledSnapshotDirty = true;
    private ISceneRuntimeSystem[] _systems = [];
    private Fix64 _fixedAccumulator;
    private bool _applicationQuitInvoked;

    public Scene Scene { get; }
    public bool IsRunning { get; private set; }
    public static Scene? currentScene => _currentScene;

    public SceneRuntime(Scene scene)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public void Start()
    {
        if (IsRunning) return;
        Scene.MarkRuntimeOnly();
        var previousScene = SetCurrentScene(Scene);
        IRuntimeSceneManager? sceneManager = null;
        var unregisterSceneOnFailure = false;
        try
        {
            if (ActiveRuntimes.TryGetValue(Scene, out var existing) && !ReferenceEquals(existing, this))
                throw new InvalidOperationException("A Scene can only have one active SceneRuntime.");

            sceneManager = Scene.Services.GetService(typeof(IRuntimeSceneManager)) as IRuntimeSceneManager;
            if (sceneManager is not null)
            {
                unregisterSceneOnFailure = !ContainsScene(sceneManager.LoadedScenes, Scene);
                sceneManager.RegisterScene(Scene, setActive: sceneManager.ActiveScene is null);
            }

            Time.Reset();
            _fixedAccumulator = Fix64.Zero;
            _applicationQuitInvoked = false;
            _systems = RuntimeSystemRegistry.CreateSystems(Scene.Services);
            ActiveRuntimes[Scene] = this;
            IsRunning = true;
            Application.quitting += OnApplicationQuit;
            Application.focusChanged += OnApplicationFocus;
            Application.pauseStateChanged += OnApplicationPause;

            RuntimeLifecycle.BeginScene(Scene);
            foreach (var system in _systems.ToArray())
            {
                if (!IsRunning || !Scene.isCreated) break;
                InvokeSystemStart(system, Scene);
            }
            if (!IsRunning || !Scene.isCreated) return;
            InitializeBehaviours();
            if (!IsRunning || !Scene.isCreated) return;
            RuntimeLifecycle.CompleteSceneLoad(Scene);
        }
        catch
        {
            RollBackFailedStart(sceneManager, unregisterSceneOnFailure);
            throw;
        }
        finally { SetCurrentScene(previousScene); }
    }

    public void Tick(Fix64 deltaTime)
    {
        if (!IsRunning)
            throw new InvalidOperationException("The scene runtime has not been started.");
        var previousScene = SetCurrentScene(Scene);
        var frameDelta = Time.AdvanceFrameUnchecked(deltaTime);
        var fixedDelta = Time.FixedDeltaTimeUnchecked;
        _fixedAccumulator += frameDelta;

        try
        {
            RuntimeLifecycle.BeginFrame(Scene, frameDelta);
            while (_fixedAccumulator >= fixedDelta)
            {
                Time.BeginFixedStepUnchecked();
                foreach (var behaviour in EnabledBehaviours())
                    if (IsBehaviourActive(behaviour)) InvokeBehaviourFixedUpdate(behaviour);

                foreach (var system in _systems)
                    InvokeSystemFixedUpdate(system, Scene, fixedDelta);

                _fixedAccumulator -= fixedDelta;
                Time.EndFixedStepUnchecked();
            }

            foreach (var behaviour in EnabledBehaviours())
                if (IsBehaviourActive(behaviour)) InvokeBehaviourUpdate(behaviour);

            CoroutineScheduler.Tick(Scene);

            foreach (var system in _systems)
                InvokeSystemUpdate(system, Scene, frameDelta);

            foreach (var behaviour in EnabledBehaviours())
                if (IsBehaviourActive(behaviour)) InvokeBehaviourLateUpdate(behaviour);
            RuntimeLifecycle.CompleteFrame(Scene, frameDelta);
        }
        finally
        {
            Time.EndFixedStepUnchecked();
            SetCurrentScene(previousScene);
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        var previousScene = SetCurrentScene(Scene);

        try
        {
            RuntimeLifecycle.BeginSceneStop(Scene);
            for (var index = _behaviours.Count - 1; index >= 0; index--)
                DisableBehaviour(_behaviours[index]);
            for (var index = _systems.Length - 1; index >= 0; index--)
            {
                var system = _systems[index];
                InvokeSystemStop(system, Scene, "Stop");
            }

            Application.quitting -= OnApplicationQuit;
            Application.focusChanged -= OnApplicationFocus;
            Application.pauseStateChanged -= OnApplicationPause;
            if (ActiveRuntimes.TryGetValue(Scene, out var runtime) && ReferenceEquals(runtime, this))
                ActiveRuntimes.Remove(Scene);

            IsRunning = false;
            _awakened.Clear();
            _enabled.Clear();
            _started.Clear();
            _behaviours.Clear();
            _enabledSnapshot = [];
            _enabledSnapshotDirty = true;
            _fixedAccumulator = Fix64.Zero;
            _systems = [];
            CoroutineScheduler.StopScene(Scene);
            RuntimeLifecycle.CompleteSceneStop(Scene);
        }
        finally { SetCurrentScene(previousScene); }
    }

    internal static void NotifyComponentStateChanged(Component component)
    {
        if (component is not MonoBehaviour behaviour || !TryGetRuntime(component, out var runtime)) return;
        if (!runtime._behaviours.Contains(behaviour)) runtime._behaviours.Add(behaviour);
        runtime.SynchronizeBehaviour(behaviour, allowStart: true);
    }

    internal static bool StopRunningScene(Scene scene)
    {
        if (!TryGetRuntime(scene, out var runtime)) return false;
        runtime.Stop();
        return true;
    }

    internal static void NotifyGameObjectAdded(GameObject gameObject)
    {
        if (!TryGetRuntime(gameObject.SceneUnchecked, out var runtime)) return;
        runtime.RegisterHierarchy(gameObject, allowStart: true);
    }

    internal static void NotifyGameObjectMoving(GameObject gameObject, Scene source)
    {
        if (!TryGetRuntime(source, out var runtime)) return;
        runtime.PrepareHierarchyForTransfer(gameObject);
    }

    internal static void NotifyGameObjectMoved(GameObject gameObject, Scene destination)
    {
        if (!TryGetRuntime(destination, out var runtime)) return;
        runtime.RegisterHierarchy(gameObject, allowStart: false);
    }

    internal static void NotifyHierarchyStateChanged(GameObject gameObject)
    {
        if (!TryGetRuntime(gameObject.SceneUnchecked, out var runtime)) return;
        runtime.RegisterHierarchy(gameObject, allowStart: false);
    }

    internal static void NotifyComponentDestroying(Component component)
    {
        if (component is not MonoBehaviour behaviour) return;
        CoroutineScheduler.StopAll(behaviour);
        if (TryGetRuntime(component, out var runtime)) runtime.DestroyBehaviour(behaviour);
        else
        {
            var previousScene = SetCurrentScene(component.GameObjectUnchecked.SceneUnchecked);
            try { InvokeBehaviour(behaviour, behaviour.OnDestroy, nameof(MonoBehaviour.OnDestroy)); }
            finally { SetCurrentScene(previousScene); }
        }
    }

    internal static void NotifyTransformParentChanged(
        Transform transform,
        Transform? previousParent,
        Transform? newParent)
    {
        if (!TryGetRuntime(transform.GameObjectUnchecked.SceneUnchecked, out var runtime)) return;
        runtime.NotifyTransformCallbacks(transform, previousParent, newParent);
        NotifyHierarchyStateChanged(transform.GameObjectUnchecked);
    }

    private void InitializeBehaviours()
    {
        _behaviours.Clear();
        foreach (var behaviour in Scene.QueryComponents<MonoBehaviour>())
            _behaviours.Add(behaviour);
        foreach (var behaviour in _behaviours.ToArray())
            SynchronizeBehaviour(behaviour, allowStart: true);
    }

    private void SynchronizeBehaviour(MonoBehaviour behaviour, bool allowStart)
    {
        var owner = behaviour.GameObjectUnchecked;
        if (!IsRunning || !ReferenceEquals(owner.SceneUnchecked, Scene) || !Scene.Contains(owner)) return;

        if (behaviour.SceneTransferState is { } transferred)
        {
            behaviour.SceneTransferState = null;
            if (transferred.Awakened) _awakened.Add(behaviour);
            if (transferred.Enabled) _enabled.Add(behaviour);
            if (transferred.Started) _started.Add(behaviour);
            _enabledSnapshotDirty = true;
        }

        var active = owner.ActiveInHierarchyUnchecked;
        if (active && _awakened.Add(behaviour))
            InvokeBehaviour(behaviour, behaviour.Awake, nameof(MonoBehaviour.Awake));
        if (!IsRunning || !ReferenceEquals(owner.SceneUnchecked, Scene) || !Scene.Contains(owner)) return;

        var shouldEnable = active && behaviour.EnabledUnchecked;
        if (shouldEnable && _enabled.Add(behaviour))
        {
            _enabledSnapshotDirty = true;
            InvokeBehaviourOnEnable(behaviour);
        }
        else if (!shouldEnable && _enabled.Contains(behaviour))
            DisableBehaviour(behaviour);
        if (!IsRunning || !ReferenceEquals(owner.SceneUnchecked, Scene) || !Scene.Contains(owner)) return;

        if (allowStart && shouldEnable && _awakened.Contains(behaviour) && _started.Add(behaviour))
            InvokeBehaviour(behaviour, behaviour.Start, nameof(MonoBehaviour.Start));
    }

    private void DisableBehaviour(MonoBehaviour behaviour)
    {
        if (!_enabled.Remove(behaviour)) return;
        _enabledSnapshotDirty = true;
        InvokeBehaviourOnDisable(behaviour);
    }

    private void DestroyBehaviour(MonoBehaviour behaviour)
    {
        DisableBehaviour(behaviour);
        _behaviours.Remove(behaviour);
        _started.Remove(behaviour);
        if (_awakened.Remove(behaviour))
            InvokeBehaviour(behaviour, behaviour.OnDestroy, nameof(MonoBehaviour.OnDestroy));
    }

    private void NotifyTransformCallbacks(Transform transform, Transform? previousParent, Transform? newParent)
    {
        foreach (var component in transform.GameObjectUnchecked.ComponentsUnchecked)
        {
            if (component is not MonoBehaviour behaviour) continue;
            InvokeBehaviour(behaviour, behaviour.OnTransformParentChanged,
                nameof(MonoBehaviour.OnTransformParentChanged));
        }
        if (previousParent is not null)
            NotifyTransformChildrenChanged(previousParent);
        if (newParent is not null && !ReferenceEquals(newParent, previousParent))
            NotifyTransformChildrenChanged(newParent);
    }

    private static void NotifyTransformChildrenChanged(Transform parent)
    {
        foreach (var component in parent.GameObjectUnchecked.ComponentsUnchecked)
        {
            if (component is MonoBehaviour behaviour)
                InvokeBehaviour(behaviour, behaviour.OnTransformChildrenChanged,
                    nameof(MonoBehaviour.OnTransformChildrenChanged));
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        foreach (var behaviour in _awakened.ToArray())
            InvokeBehaviour(behaviour, () => behaviour.OnApplicationFocus(focused),
                nameof(MonoBehaviour.OnApplicationFocus));
    }

    private void OnApplicationPause(bool paused)
    {
        foreach (var behaviour in _awakened.ToArray())
            InvokeBehaviour(behaviour, () => behaviour.OnApplicationPause(paused),
                nameof(MonoBehaviour.OnApplicationPause));
    }

    private void OnApplicationQuit()
    {
        if (_applicationQuitInvoked) return;
        _applicationQuitInvoked = true;
        foreach (var behaviour in _awakened.ToArray())
            InvokeBehaviour(behaviour, behaviour.OnApplicationQuit, nameof(MonoBehaviour.OnApplicationQuit));
    }

    private MonoBehaviour[] EnabledBehaviours()
    {
        if (!_enabledSnapshotDirty) return _enabledSnapshot;
        _enabledSnapshot = _behaviours.Where(_enabled.Contains).ToArray();
        _enabledSnapshotDirty = false;
        return _enabledSnapshot;
    }

    private void RegisterHierarchy(GameObject root, bool allowStart)
    {
        foreach (var component in root.ComponentsUnchecked)
        {
            if (component is not MonoBehaviour behaviour) continue;
            if (!_behaviours.Contains(behaviour)) _behaviours.Add(behaviour);
            SynchronizeBehaviour(behaviour, allowStart);
        }
        foreach (var child in root.TransformUnchecked.ChildrenUnchecked)
            RegisterHierarchy(child.GameObjectUnchecked, allowStart);
    }

    private void PrepareHierarchyForTransfer(GameObject root)
    {
        foreach (var component in root.ComponentsUnchecked)
        {
            if (component is not MonoBehaviour behaviour) continue;
            behaviour.SceneTransferState = new SceneRuntimeTransferState(
                _awakened.Remove(behaviour),
                _enabled.Remove(behaviour),
                _started.Remove(behaviour));
            _behaviours.Remove(behaviour);
        }
        _enabledSnapshotDirty = true;
        foreach (var child in root.TransformUnchecked.ChildrenUnchecked)
            PrepareHierarchyForTransfer(child.GameObjectUnchecked);
    }

    private bool IsBehaviourActive(MonoBehaviour behaviour) =>
        _enabled.Contains(behaviour) && behaviour.EnabledUnchecked &&
        ReferenceEquals(behaviour.GameObjectUnchecked.SceneUnchecked, Scene) &&
        behaviour.GameObjectUnchecked.ActiveInHierarchyUnchecked;

    private static bool TryGetRuntime(Component component, out SceneRuntime runtime) =>
        TryGetRuntime(component.GameObjectUnchecked.SceneUnchecked, out runtime);

    private static bool TryGetRuntime(Scene? scene, out SceneRuntime runtime)
    {
        if (scene is not null && ActiveRuntimes.TryGetValue(scene, out var found))
        {
            runtime = found;
            return true;
        }

        runtime = null!;
        return false;
    }

    internal static bool IsRunningScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return ActiveRuntimes.ContainsKey(scene);
    }

    internal static bool IsSceneVisibleInCurrentRuntimeDomain(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_currentScene is not { } current) return true;
        if (current.Services.GetService(typeof(IRuntimeSceneManager)) is not IRuntimeSceneManager sceneManager)
            return ReferenceEquals(scene, current);
        if (!ContainsScene(sceneManager.LoadedScenes, current))
            return ReferenceEquals(scene, current);
        return ContainsScene(sceneManager.LoadedScenes, scene);
    }

    internal static IDisposable EnterSceneContext(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new SceneContextScope(SetCurrentScene(scene));
    }

    private void RollBackFailedStart(IRuntimeSceneManager? sceneManager, bool unregisterScene)
    {
        Application.quitting -= OnApplicationQuit;
        Application.focusChanged -= OnApplicationFocus;
        Application.pauseStateChanged -= OnApplicationPause;
        if (ActiveRuntimes.TryGetValue(Scene, out var runtime) && ReferenceEquals(runtime, this))
            ActiveRuntimes.Remove(Scene);

        for (var index = _systems.Length - 1; index >= 0; index--)
        {
            var system = _systems[index];
            InvokeSystemStop(system, Scene, "Stop after failed start");
        }

        IsRunning = false;
        _awakened.Clear();
        _enabled.Clear();
        _started.Clear();
        _behaviours.Clear();
        _enabledSnapshot = [];
        _enabledSnapshotDirty = true;
        _fixedAccumulator = Fix64.Zero;
        _systems = [];
        CoroutineScheduler.StopScene(Scene);

        if (!unregisterScene || sceneManager is null || !ContainsScene(sceneManager.LoadedScenes, Scene)) return;
        try { sceneManager.UnregisterScene(Scene); }
        catch (Exception exception)
        {
            Debug.LogError($"Rolling back failed SceneRuntime registration failed: {exception.Message}");
        }
    }

    private static bool ContainsScene(IReadOnlyList<Scene> scenes, Scene target) =>
        scenes.Any(scene => ReferenceEquals(scene, target));

    private static void InvokeBehaviour(MonoBehaviour behaviour, Action callback, string callbackName)
    {
        try { callback(); }
        catch (Exception exception)
        {
            Debug.LogError($"{behaviour.GetType().FullName}.{callbackName} failed: {exception.Message}");
        }
    }

    private static Scene? SetCurrentScene(Scene? scene)
    {
        var previous = _currentScene;
        _currentScene = scene;
        return previous;
    }

    private sealed class SceneContextScope(Scene? previousScene) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SetCurrentScene(previousScene);
        }
    }

    private static void InvokeSystemStart(ISceneRuntimeSystem system, Scene scene)
    { try { system.Start(scene); } catch (Exception exception) { LogSystemFailure(system, "Start", exception); } }
    private static void InvokeSystemFixedUpdate(ISceneRuntimeSystem system, Scene scene, Fix64 fixedDeltaTime)
    { try { system.FixedUpdate(scene, fixedDeltaTime); } catch (Exception exception) { LogSystemFailure(system, "FixedUpdate", exception); } }
    private static void InvokeSystemUpdate(ISceneRuntimeSystem system, Scene scene, Fix64 deltaTime)
    { try { system.Update(scene, deltaTime); } catch (Exception exception) { LogSystemFailure(system, "Update", exception); } }
    private static void InvokeSystemStop(ISceneRuntimeSystem system, Scene scene, string callbackName)
    { try { system.Stop(scene); } catch (Exception exception) { LogSystemFailure(system, callbackName, exception); } }

    private static void LogSystemFailure(ISceneRuntimeSystem system, string callbackName, Exception exception) =>
        Debug.LogError($"{system.GetType().FullName}.{callbackName} failed: " +
                       $"{exception.GetType().FullName}: {exception}");

    private static void InvokeBehaviourFixedUpdate(MonoBehaviour behaviour)
    { try { behaviour.FixedUpdate(); } catch (Exception exception) { LogBehaviourFailure(behaviour, "FixedUpdate", exception); } }
    private static void InvokeBehaviourUpdate(MonoBehaviour behaviour)
    { try { behaviour.Update(); } catch (Exception exception) { LogBehaviourFailure(behaviour, "Update", exception); } }
    private static void InvokeBehaviourLateUpdate(MonoBehaviour behaviour)
    { try { behaviour.LateUpdate(); } catch (Exception exception) { LogBehaviourFailure(behaviour, "LateUpdate", exception); } }
    private static void InvokeBehaviourOnEnable(MonoBehaviour behaviour)
    { try { behaviour.OnEnable(); } catch (Exception exception) { LogBehaviourFailure(behaviour, "OnEnable", exception); } }
    private static void InvokeBehaviourOnDisable(MonoBehaviour behaviour)
    { try { behaviour.OnDisable(); } catch (Exception exception) { LogBehaviourFailure(behaviour, "OnDisable", exception); } }

    private static void LogBehaviourFailure(MonoBehaviour behaviour, string callbackName, Exception exception) =>
        Debug.LogError($"{behaviour.GetType().FullName}.{callbackName} failed: " +
                       $"{exception.GetType().FullName}: {exception}");

}
