using BEngine.SceneManagement;
using System.Runtime.CompilerServices;

namespace BEngine;

public sealed class SceneRuntime
{
    private static readonly Dictionary<Scene, SceneRuntime> ActiveRuntimes = [];
    private static readonly ConditionalWeakTable<Type, BehaviourCallbacks> CallbackTypes = new();
    private static Scene? _currentScene;

    private readonly HashSet<MonoBehaviour> _awakened = [];
    private readonly HashSet<MonoBehaviour> _enabled = [];
    private readonly HashSet<MonoBehaviour> _started = [];
    private readonly List<MonoBehaviour> _behaviours = [];
    private readonly Dictionary<MonoBehaviour, BehaviourDispatch> _dispatch = [];
    private BehaviourDispatch[] _enabledSnapshot = [];
    private bool _enabledSnapshotDirty = true;
    private ISceneRuntimeSystem[] _systems = [];
    private long _lastTickFrame = -1;
    private bool _stopping;
    private bool _applicationQuitInvoked;
    private Scene[] _singleFrameScenes;
    private FrameSystemCapture? _frameSystemCapture;


    public Scene Scene { get; }
    public bool IsRunning { get; private set; }
    public static Scene? currentScene => _currentScene;

    public SceneRuntime(Scene scene)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _singleFrameScenes = [Scene];
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

            Scene.RuntimeLoadTime = Time.time;
            _lastTickFrame = -1;
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
        using var frame = Time.IsFrameActive ? default : BeginFrame(deltaTime);
        if (_lastTickFrame == Time.frameCount) return;
        _lastTickFrame = Time.frameCount;
        var previousScene = SetCurrentScene(Scene);
        var frameDelta = Time.deltaTime;
        var fixedDelta = Time.FrameFixedDelta;

        try
        {
            RuntimeLifecycle.BeginFrame(Scene, frameDelta);
            foreach (var dispatch in EnabledBehaviours())
                if (!dispatch.Started) EnsureStarted(dispatch);
            foreach (var system in _systems)
            {
                if (!IsRunning) return;
                InvokeSystemBeforeUpdate(system, Scene, frameDelta);
            }
            for (var step = 0; step < Time.FrameFixedSteps && IsRunning; step++)
            {
                Time.BeginFixedStepUnchecked(step);
                foreach (var dispatch in EnabledBehaviours())
                    if ((!dispatch.Started || dispatch.Callbacks.FixedUpdate) && EnsureStarted(dispatch) && dispatch.Callbacks.FixedUpdate)
                        InvokeBehaviourFixedUpdate(dispatch.Behaviour);

                foreach (var system in _systems)
                {
                    if (!IsRunning) return;
                    InvokeSystemFixedUpdate(system, Scene, fixedDelta);
                }

                Time.EndFixedStepUnchecked();
            }

            foreach (var dispatch in EnabledBehaviours())
                if ((!dispatch.Started || dispatch.Callbacks.Update) && EnsureStarted(dispatch) && dispatch.Callbacks.Update)
                    InvokeBehaviourUpdate(dispatch.Behaviour);

            if (!IsRunning) return;

            foreach (var system in _systems)
            {
                if (!IsRunning) return;
                InvokeSystemUpdate(system, Scene, frameDelta);
            }

            foreach (var dispatch in EnabledBehaviours())
                if ((!dispatch.Started || dispatch.Callbacks.LateUpdate) && EnsureStarted(dispatch) && dispatch.Callbacks.LateUpdate)
                    InvokeBehaviourLateUpdate(dispatch.Behaviour);
            if (IsRunning) RuntimeLifecycle.CompleteFrame(Scene, frameDelta);
        }
        finally
        {
            Time.EndFixedStepUnchecked();
            SetCurrentScene(previousScene);
        }
    }

    public void Stop()
    {
        if (!IsRunning || _stopping) return;
        _stopping = true;
        var previousScene = SetCurrentScene(Scene);

        try
        {
            RuntimeLifecycle.BeginSceneStop(Scene);
            foreach (var behaviour in _behaviours.ToArray().Reverse())
                DisableBehaviour(behaviour);
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
            _dispatch.Clear();
            _enabledSnapshot = [];
            _enabledSnapshotDirty = true;
            _systems = [];
            _frameSystemCapture = null;
            RuntimeLifecycle.CompleteSceneStop(Scene);
        }
        finally { _stopping = false; SetCurrentScene(previousScene); }
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
        if (!IsRegisteredBehaviour(behaviour)) return;

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
        if (!IsRegisteredBehaviour(behaviour)) return;

        var shouldEnable = owner.ActiveInHierarchyUnchecked && behaviour.EnabledUnchecked;
        if (shouldEnable && _enabled.Add(behaviour))
        {
            InvalidateEnabledSnapshot(behaviour);
            InvokeBehaviourOnEnable(behaviour);
        }
        else if (!shouldEnable && _enabled.Contains(behaviour))
            DisableBehaviour(behaviour);
    }

    private bool IsRegisteredBehaviour(MonoBehaviour behaviour) =>
        IsRunning && !_stopping && ReferenceEquals(behaviour.GameObjectUnchecked.SceneUnchecked, Scene) &&
        behaviour.GameObjectUnchecked.ComponentsUnchecked.Contains(behaviour);

    private bool EnsureStarted(BehaviourDispatch dispatch)
    {
        var behaviour = dispatch.Behaviour;
        if (!IsBehaviourActive(behaviour)) return false;
        if (_started.Add(behaviour))
        {
            dispatch.Started = true;
            // Start still runs from the phase's captured snapshot. Once a
            // behaviour has started, its empty frame callbacks need no slot in
            // the next phase's plan (renderers still keep all lifecycle state).
            if (!dispatch.Callbacks.HasFrameCallback) _enabledSnapshotDirty = true;
            InvokeBehaviour(behaviour, behaviour.Start, nameof(MonoBehaviour.Start));
        }
        return IsBehaviourActive(behaviour);
    }

    /// <summary>Groups all SceneRuntime ticks belonging to one host Update.</summary>
    public static FrameScope BeginFrame(Fix64 deltaTime)
    {
        Time.BeginFrame(deltaTime);
        return new FrameScope(Time.FrameSequence);
    }

    public static void TickAll(IReadOnlyList<SceneRuntime> runtimes, Fix64 deltaTime)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        using var frame = BeginFrame(deltaTime);
        if (runtimes.Count == 0) return;
        if (runtimes.Count == 1)
        {
            // Capture everything before invoking a frame system. A callback may
            // replace the host's scene list or stop/restart this runtime.
            var runtime = runtimes[0];
            if (runtime is null || !runtime.IsRunning) return;
            var capturedScenes = runtime._singleFrameScenes;
            if (!ReferenceEquals(capturedScenes[0], runtime.Scene))
                capturedScenes = runtime._singleFrameScenes = [runtime.Scene];
            var systems = runtime.CaptureSingleFrameSystems();
            foreach (var system in systems) InvokeFrameSystem(system, capturedScenes, deltaTime, begin: true);
            try { runtime.Tick(deltaTime); }
            finally
            {
                for (var index = systems.Length - 1; index >= 0; index--)
                    InvokeFrameSystem(systems[index], capturedScenes, deltaTime, begin: false);
            }
            return;
        }
        var running = runtimes.Where(runtime => runtime is not null && runtime.IsRunning).ToArray();
        var scenes = running.Select(runtime => runtime.Scene).ToArray();
        var frameSystems = running.SelectMany(runtime => runtime._systems)
            .Select(UnwrapSystem)
            .OfType<IRuntimeFrameSystem>()
            .GroupBy(system => system.frameGroupId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        foreach (var system in frameSystems) InvokeFrameSystem(system, scenes, deltaTime, begin: true);
        try
        {
            foreach (var runtime in running) runtime.Tick(deltaTime);
        }
        finally
        {
            for (var index = frameSystems.Length - 1; index >= 0; index--)
                InvokeFrameSystem(frameSystems[index], scenes, deltaTime, begin: false);
        }
    }

    private IRuntimeFrameSystem[] CaptureSingleFrameSystems()
    {
        var capture = _frameSystemCapture;
        if (capture is null || !ReferenceEquals(capture.Source, _systems))
            _frameSystemCapture = capture = new FrameSystemCapture(_systems);
        bool changed = !capture.Valid;
        capture.Valid = false;
        // frameGroupId is a public getter and may change. Read it exactly once
        // per candidate, as GroupBy did, rather than assuming a constant key.
        // Keep a local capture: a getter can synchronously stop this runtime,
        // clearing its cache without changing this frame's captured systems.
        for (var index = 0; index < capture.Candidates.Length; index++)
        {
            var group = capture.Candidates[index].frameGroupId;
            if (!StringComparer.Ordinal.Equals(capture.GroupIds[index], group)) changed = true;
            capture.GroupIds[index] = group;
        }
        if (!changed) { capture.Valid = true; return capture.Representatives; }
        var representatives = new List<IRuntimeFrameSystem>();
        var groups = new HashSet<string?>(StringComparer.Ordinal);
        for (var index = 0; index < capture.Candidates.Length; index++)
            if (groups.Add(capture.GroupIds[index])) representatives.Add(capture.Candidates[index]);
        // Replace, never rewrite, the array captured by BeginFrame/EndFrame.
        capture.Representatives = representatives.ToArray();
        capture.Valid = true;
        return capture.Representatives;
    }

    private sealed class FrameSystemCapture
    {
        public readonly ISceneRuntimeSystem[] Source;
        public readonly IRuntimeFrameSystem[] Candidates;
        public readonly string?[] GroupIds;
        public IRuntimeFrameSystem[] Representatives = [];
        public bool Valid;
        public FrameSystemCapture(ISceneRuntimeSystem[] source)
        {
            Source = source;
            Candidates = source.Select(UnwrapSystem).OfType<IRuntimeFrameSystem>().ToArray();
            GroupIds = new string?[Candidates.Length];
        }
    }

    private static ISceneRuntimeSystem UnwrapSystem(ISceneRuntimeSystem system) =>
        system is ScopedSceneRuntimeSystem scoped ? scoped.Instance : system;

    private static void InvokeFrameSystem(IRuntimeFrameSystem system, IReadOnlyList<Scene> scenes,
        Fix64 deltaTime, bool begin)
    {
        try
        {
            if (begin) system.BeginFrame(scenes, deltaTime);
            else system.EndFrame(scenes, deltaTime);
        }
        catch (Exception exception)
        {
            Debug.LogError($"{system.GetType().FullName}.{(begin ? "BeginFrame" : "EndFrame")} failed: " +
                           $"{exception.GetType().FullName}: {exception}");
        }
    }

    public readonly struct FrameScope : IDisposable
    {
        private readonly long _sequence;
        internal FrameScope(long sequence) => _sequence = sequence;
        public void Dispose() { if (_sequence != 0 && _sequence == Time.FrameSequence) Time.EndFrame(); }
    }

    private void DisableBehaviour(MonoBehaviour behaviour)
    {
        if (!_enabled.Remove(behaviour)) return;
        InvalidateEnabledSnapshot(behaviour);
        InvokeBehaviourOnDisable(behaviour);
    }

    private void InvalidateEnabledSnapshot(MonoBehaviour behaviour)
    {
        // A previously started behaviour with no frame override is absent
        // from the plan. Enabling/disabling it only affects lifecycle state;
        // unknown and not-yet-started behaviours must still rebuild the plan.
        if (!_dispatch.TryGetValue(behaviour, out var dispatch) || NeedsFrameDispatch(dispatch))
            _enabledSnapshotDirty = true;
    }

    private static bool NeedsFrameDispatch(BehaviourDispatch dispatch) =>
        !dispatch.Started || dispatch.Callbacks.HasFrameCallback;

    private void DestroyBehaviour(MonoBehaviour behaviour)
    {
        DisableBehaviour(behaviour);
        _behaviours.Remove(behaviour);
        _dispatch.Remove(behaviour);
        _started.Remove(behaviour);
        if (_awakened.Remove(behaviour))
            InvokeBehaviour(behaviour, behaviour.OnDestroy, nameof(MonoBehaviour.OnDestroy));
    }

    private void NotifyTransformCallbacks(Transform transform, Transform? previousParent, Transform? newParent)
    {
        foreach (var component in transform.GameObjectUnchecked.ComponentsUnchecked.ToArray())
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
        foreach (var component in parent.GameObjectUnchecked.ComponentsUnchecked.ToArray())
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

    private sealed class BehaviourCallbacks(MonoBehaviour sample)
    {
        // Binding an existing virtual slot identifies the dispatched override,
        // including inherited overrides and hidden same-name methods. Unlike a
        // name-based reflection search, this also works in fully stripped builds.
        public readonly bool FixedUpdate = ((Action)sample.FixedUpdate).Method.DeclaringType != typeof(MonoBehaviour);
        public readonly bool Update = ((Action)sample.Update).Method.DeclaringType != typeof(MonoBehaviour);
        public readonly bool LateUpdate = ((Action)sample.LateUpdate).Method.DeclaringType != typeof(MonoBehaviour);
        public bool HasFrameCallback => FixedUpdate || Update || LateUpdate;
    }

    private sealed class BehaviourDispatch(MonoBehaviour behaviour, BehaviourCallbacks callbacks, bool started)
    {
        public readonly MonoBehaviour Behaviour = behaviour;
        public readonly BehaviourCallbacks Callbacks = callbacks;
        public bool Started = started;
    }

    private BehaviourDispatch DispatchFor(MonoBehaviour behaviour)
    {
        if (_dispatch.TryGetValue(behaviour, out var dispatch)) return dispatch;
        var type = behaviour.GetType();
        if (!CallbackTypes.TryGetValue(type, out var callbacks))
        {
            callbacks = new BehaviourCallbacks(behaviour);
            CallbackTypes.Add(type, callbacks);
        }
        dispatch = new BehaviourDispatch(behaviour, callbacks, _started.Contains(behaviour));
        _dispatch.Add(behaviour, dispatch);
        return dispatch;
    }

    private BehaviourDispatch[] EnabledBehaviours()
    {
        if (!_enabledSnapshotDirty) return _enabledSnapshot;
        var count = 0;
        foreach (var behaviour in _behaviours)
            if (_enabled.Contains(behaviour) &&
                (!_dispatch.TryGetValue(behaviour, out var countDispatch) || NeedsFrameDispatch(countDispatch))) count++;

        // Callbacks can invalidate and rebuild this snapshot while a previous
        // phase is still iterating it. Publish a new array only after filling
        // it, preserving the captured phase's membership and registration order.
        var snapshot = count == 0 ? Array.Empty<BehaviourDispatch>() : new BehaviourDispatch[count];
        var index = 0;
        var needsCompaction = false;
        foreach (var behaviour in _behaviours)
            if (_enabled.Contains(behaviour) &&
                (!_dispatch.TryGetValue(behaviour, out var fillDispatch) || NeedsFrameDispatch(fillDispatch)))
            {
                var dispatch = DispatchFor(behaviour);
                snapshot[index++] = dispatch;
                // A transferred, already-started behaviour first resolves its
                // dispatch here, preserving the original creation order.
                if (!NeedsFrameDispatch(dispatch)) needsCompaction = true;
            }
        _enabledSnapshot = snapshot;
        _enabledSnapshotDirty = needsCompaction;
        return _enabledSnapshot;
    }

    private void RegisterHierarchy(GameObject root, bool allowStart)
    {
        foreach (var component in root.ComponentsUnchecked.ToArray())
        {
            if (component is not MonoBehaviour behaviour) continue;
            if (!_behaviours.Contains(behaviour)) _behaviours.Add(behaviour);
            SynchronizeBehaviour(behaviour, allowStart);
        }
        foreach (var child in root.TransformUnchecked.ChildrenUnchecked.ToArray())
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
            _dispatch.Remove(behaviour);
        }
        _enabledSnapshotDirty = true;
        foreach (var child in root.TransformUnchecked.ChildrenUnchecked)
            PrepareHierarchyForTransfer(child.GameObjectUnchecked);
    }

    private bool IsBehaviourActive(MonoBehaviour behaviour) =>
        IsRunning && !_stopping && _enabled.Contains(behaviour) && behaviour.EnabledUnchecked &&
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
        _dispatch.Clear();
        _enabledSnapshot = [];
        _enabledSnapshotDirty = true;
        _systems = [];
        _frameSystemCapture = null;

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
    private static void InvokeSystemBeforeUpdate(ISceneRuntimeSystem system, Scene scene, Fix64 deltaTime)
    { try { system.BeforeUpdate(scene, deltaTime); } catch (Exception exception) { LogSystemFailure(system, "BeforeUpdate", exception); } }
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
