using System.Collections;

namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class CoreRuntimeSafetyTests
{
    internal static void Run()
    {
        VerifyTimeValidation();
        VerifyTransformParentingInvariants();
        VerifyCanceledWorkDoesNotRunFromTickSnapshots();
        VerifyInstantiateCopiesStateBeforeLifecycleCallbacks();
        VerifyDestroyedObjectsLeaveTheInstanceRegistry();
        VerifySceneDisposeIsReentrantSafe();
        VerifyAssetCacheIdentityAndUnload();
        VerifyConcurrentObjectRegistration();
        VerifySerializationSuppressionIsThreadLocal();
    }

    private static void VerifyTimeValidation()
    {
        var fixedDeltaTime = Time.fixedDeltaTime;
        var maximumDeltaTime = Time.maximumDeltaTime;
        var timeScale = Time.timeScale;
        try
        {
            Expect<ArgumentOutOfRangeException>(() => Time.fixedDeltaTime = Fix64.Zero,
                "Time accepted a zero fixed delta that would make SceneRuntime.Tick loop forever.");
            Expect<ArgumentOutOfRangeException>(() => Time.fixedDeltaTime = -Fix64.One,
                "Time accepted a negative fixed delta.");
            Expect<ArgumentOutOfRangeException>(() => Time.maximumDeltaTime = Fix64.Zero,
                "Time accepted a non-positive maximum delta.");
            Expect<ArgumentOutOfRangeException>(() => Time.timeScale = -Fix64.One,
                "Time accepted a negative time scale.");
        }
        finally
        {
            Time.fixedDeltaTime = fixedDeltaTime;
            Time.maximumDeltaTime = maximumDeltaTime;
            Time.timeScale = timeScale;
        }
    }

    private static void VerifyTransformParentingInvariants()
    {
        using var firstScene = new Scene("Transform invariants");
        using var secondScene = new Scene("Foreign transform invariants");
        var firstParent = firstScene.CreateGameObject("First parent");
        firstParent.transform.localScale = new Vector2(2, 3);
        var secondParent = firstScene.CreateGameObject("Second parent");
        secondParent.transform.localScale = new Vector2(4, 7);
        var child = firstScene.CreateGameObject("Child");
        child.transform.localScale = new Vector2(6, 7);
        child.transform.SetParent(firstParent.transform, false);
        var worldScale = child.transform.lossyScale;

        child.transform.SetParent(secondParent.transform, true);
        Require(child.transform.lossyScale == worldScale,
            "Transform.SetParent(worldPositionStays: true) changed the world scale.");

        var foreignParent = secondScene.CreateGameObject("Foreign parent");
        Expect<InvalidOperationException>(() => child.transform.SetParent(foreignParent.transform),
            "Transform allowed a hierarchy to span two Scenes.");
        Require(ReferenceEquals(child.transform.parent, secondParent.transform),
            "A rejected cross-Scene parent operation changed the existing hierarchy.");
    }

    private static void VerifyCanceledWorkDoesNotRunFromTickSnapshots()
    {
        using var scene = new Scene("Scheduler cancellation");
        var canceler = scene.CreateGameObject("Canceler").AddComponent<SchedulerProbe>();
        var victim = scene.CreateGameObject("Victim").AddComponent<SchedulerProbe>();
        canceler.Peer = victim;

        var runtime = new SceneRuntime(scene);
        runtime.Start();
        try
        {
            canceler.Invoke(nameof(SchedulerProbe.CancelPeerInvoke), Fix64.Zero);
            victim.Invoke(nameof(SchedulerProbe.RecordInvoke), Fix64.Zero);

            canceler.StartCoroutine(canceler.CancelPeerCoroutine());
            victim.ScheduledCoroutine = victim.StartCoroutine(victim.RecordCoroutine());
            runtime.Tick(Time.fixedDeltaTime);

            Require(victim.InvokeCount == 0,
                "CancelInvoke left an invocation in the current tick snapshot.");
            Require(victim.CoroutineStepCount == 0,
                "StopCoroutine left a coroutine in the current tick snapshot.");
        }
        finally
        {
            runtime.Stop();
        }
    }

    private static void VerifyDestroyedObjectsLeaveTheInstanceRegistry()
    {
        using var scene = new Scene("Instance registry lifecycle");
        var gameObject = scene.CreateGameObject("Tracked");
        var component = gameObject.AddComponent<SchedulerProbe>();
        var transformId = gameObject.transform.GetInstanceID();
        var gameObjectId = gameObject.GetInstanceID();
        var componentId = component.GetInstanceID();
        component.DestroyAgain = true;
        var runtime = new SceneRuntime(scene);
        runtime.Start();

        try
        {
            Require(scene.Destroy(gameObject), "The lifecycle test could not destroy its GameObject.");
            Require(BObject.FindObjectFromInstanceID(gameObjectId) is null &&
                    BObject.FindObjectFromInstanceID(transformId) is null &&
                    BObject.FindObjectFromInstanceID(componentId) is null,
                "Destroyed scene objects remained visible through instance-ID lookup.");
            Require(component.DestroyCount == 1,
                "Destroying a running GameObject did not invoke OnDestroy exactly once.");
            BObject.Destroy(gameObject);
            Require(component.DestroyCount == 1,
                "Destroying an already destroyed GameObject invoked OnDestroy again.");

            scene.Add(gameObject);
            Require(ReferenceEquals(BObject.FindObjectFromInstanceID(gameObjectId), gameObject) &&
                    ReferenceEquals(BObject.FindObjectFromInstanceID(componentId), component),
                "Restoring an object did not restore its instance-ID registration.");
            Require(gameObject.RemoveComponent(component) &&
                    BObject.FindObjectFromInstanceID(componentId) is null,
                "A removed Component remained visible through instance-ID lookup.");
            Require(gameObject.RestoreComponent(component, 1) &&
                    ReferenceEquals(BObject.FindObjectFromInstanceID(componentId), component),
                "Restoring a Component did not restore its instance-ID registration.");
        }
        finally
        {
            runtime.Stop();
        }
    }

    private static void VerifyInstantiateCopiesStateBeforeLifecycleCallbacks()
    {
        using var scene = new Scene("Instantiate lifecycle ordering");
        var source = scene.CreateGameObject("Source");
        var sourceProbe = source.AddComponent<CloneLifecycleProbe>();
        sourceProbe.Value = 42;
        sourceProbe.Asset = null;
        var sourceChild = scene.CreateGameObject("Source child");
        sourceChild.transform.SetParent(source.transform, false);
        var sourceChildProbe = sourceChild.AddComponent<CloneLifecycleProbe>();
        sourceChildProbe.Value = 84;
        var runtime = new SceneRuntime(scene);
        runtime.Start();
        try
        {
            var clone = BObject.Instantiate(source) as GameObject ??
                        throw new InvalidOperationException("Instantiate did not return a GameObject clone.");
            var cloneProbe = clone.GetComponent<CloneLifecycleProbe>() ??
                             throw new InvalidOperationException("Instantiate lost the clone probe.");
            Require(cloneProbe.Value == 42 && cloneProbe.ValueObservedByAwake == 42,
                "Instantiate invoked Awake before copying serialized Component state.");
            Require(cloneProbe.Asset is null,
                "Component serialization did not clear an explicitly null asset reference.");
            var cloneChild = clone.transform.GetChild(0).gameObject;
            var cloneChildProbe = cloneChild.GetComponent<CloneLifecycleProbe>() ??
                                  throw new InvalidOperationException("Instantiate lost the child clone probe.");
            Require(ReferenceEquals(cloneChild.scene, scene) &&
                    ReferenceEquals(cloneChildProbe.SceneObservedByAwake, scene) &&
                    cloneChildProbe.ValueObservedByAwake == 84,
                "Instantiate invoked a child lifecycle callback before atomically binding its hierarchy to the Scene.");
        }
        finally
        {
            runtime.Stop();
        }
    }

    private static void VerifySceneDisposeIsReentrantSafe()
    {
        var scene = new Scene("Reentrant dispose");
        var gameObject = scene.CreateGameObject("Dispose owner");
        var probe = gameObject.AddComponent<SchedulerProbe>();
        probe.DisposeSceneAgain = true;
        var sceneId = scene.GetInstanceID();
        var gameObjectId = gameObject.GetInstanceID();

        scene.Dispose();

        Require(!scene.isCreated && probe.DestroyCount == 1 &&
                BObject.FindObjectFromInstanceID(sceneId) is null &&
                BObject.FindObjectFromInstanceID(gameObjectId) is null,
            "Reentrant Scene.Dispose left the Scene or its objects partially registered.");
    }

    private static void VerifyAssetCacheIdentityAndUnload()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BEngineCoreSafety_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Concurrent.txt");
        File.WriteAllText(path, "BEngine cache identity");
        try
        {
            var assets = new TextAsset?[32];
            Parallel.For(0, assets.Length, index => assets[index] = BAsset.Load<TextAsset>(path));
            var first = assets[0] ?? throw new InvalidOperationException("The cache test did not load its asset.");
            Require(assets.All(asset => ReferenceEquals(asset, first)),
                "Concurrent BAsset loads returned multiple instances for one cache key.");

            Resources.UnloadAsset(first);
            Require(BObject.FindObjectFromInstanceID(first.GetInstanceID()) is null,
                "Resources.UnloadAsset left the unloaded asset in the instance registry.");
            var reloaded = BAsset.Load<TextAsset>(path) ??
                           throw new InvalidOperationException("The unloaded asset could not be reloaded.");
            Require(!ReferenceEquals(reloaded, first),
                "Resources.UnloadAsset left the unloaded asset in the BAsset cache.");
            Resources.UnloadAsset(reloaded);

            Expect<ArgumentException>(() => Resources.Load<string>(path),
                "Resources.Load accepted an absolute path outside its resource roots.");
        }
        finally
        {
            BAsset.Invalidate(path);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyConcurrentObjectRegistration()
    {
        var assets = new RegistryProbeAsset[256];
        Parallel.For(0, assets.Length, index => assets[index] = new RegistryProbeAsset());
        Require(assets.Select(asset => asset.GetInstanceID()).Distinct().Count() == assets.Length,
            "Concurrent BObject construction produced duplicate instance IDs.");
        Require(assets.All(asset => ReferenceEquals(
                BObject.FindObjectFromInstanceID(asset.GetInstanceID()), asset)),
            "Concurrent BObject construction corrupted the instance registry.");
        foreach (var asset in assets) BObject.DestroyImmediate(asset);
    }

    private static void VerifySerializationSuppressionIsThreadLocal()
    {
        using var suppressionEntered = new ManualResetEventSlim();
        using var releaseSuppression = new ManualResetEventSlim();
        var suppressingTask = Task.Run(() =>
        {
            using var suppression = SerializationCallbackUtility.SuppressBeforeSerialize();
            suppressionEntered.Set();
            releaseSuppression.Wait();
        });

        suppressionEntered.Wait();
        var receiver = new SerializationCallbackProbe();
        try
        {
            YamlUtility.Serialize(receiver);
            Require(receiver.BeforeSerializeCount == 1,
                "A serialization suppression scope leaked into another thread.");
        }
        finally
        {
            releaseSuppression.Set();
            suppressingTask.GetAwaiter().GetResult();
        }
    }

    private static void Expect<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RegistryProbeAsset : ScriptableObject { }

    private sealed class SchedulerProbe : MonoBehaviour
    {
        public SchedulerProbe() { }

        internal SchedulerProbe? Peer { get; set; }
        internal Coroutine? ScheduledCoroutine { get; set; }
        internal int InvokeCount { get; private set; }
        internal int CoroutineStepCount { get; private set; }
        internal int DestroyCount { get; private set; }
        internal bool DestroyAgain { get; set; }
        internal bool DisposeSceneAgain { get; set; }

        public void CancelPeerInvoke() => Peer!.CancelInvoke(nameof(RecordInvoke));
        public void RecordInvoke() => InvokeCount++;
        public override void OnDestroy()
        {
            DestroyCount++;
            if (DestroyAgain) BObject.Destroy(gameObject);
            if (DisposeSceneAgain) gameObject.scene?.Dispose();
        }

        internal IEnumerator CancelPeerCoroutine()
        {
            Peer!.StopCoroutine(Peer.ScheduledCoroutine!);
            yield return null;
        }

        internal IEnumerator RecordCoroutine()
        {
            CoroutineStepCount++;
            yield return null;
        }
    }

    private sealed class CloneLifecycleProbe : MonoBehaviour
    {
        public CloneLifecycleProbe() { }

        public int Value;
        public TextAsset? Asset = new("Default", "memory:default");
        internal int ValueObservedByAwake { get; private set; }
        internal Scene? SceneObservedByAwake { get; private set; }

        public override void Awake()
        {
            ValueObservedByAwake = Value;
            SceneObservedByAwake = gameObject.scene;
        }
    }

    private sealed class SerializationCallbackProbe : ISerializationCallbackReceiver
    {
        public int BeforeSerializeCount { get; private set; }
        public void OnBeforeSerialize() => BeforeSerializeCount++;
        public void OnAfterDeserialize() { }
    }
}
