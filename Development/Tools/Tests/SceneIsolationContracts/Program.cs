using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

new Probe(args[0], Path.GetFullPath(args[1])).Run();

sealed class Probe(string mode, string output)
{
    readonly List<string> checks = [];
    long clockAllocation;
    void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    void Throws<T>(Action call, string name) where T : Exception
    { try { call(); } catch (T) { Check(true, name); return; } throw new InvalidOperationException(name); }
    static object? Call(Type type, string name, object? owner, params object?[] args)
    {
        try { return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.Invoke(owner, args); }
        catch (TargetInvocationException e) when (e.InnerException is { } inner)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw(); throw; }
    }
    static T Read<T>(object owner, string name) => (T)owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(owner)!;
    public void Run()
    {
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
        DotNetPlatformServices.Initialize();
        using var services = new ServiceCollection().AddBEngineDotNetPlatform().BuildServiceProvider();
        PlatformRuntime.Initialize(services);
        RuntimeTypeCache.RegisterAssemblies([typeof(Probe).Assembly]);
        Check(!typeof(BObject).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == typeof(Probe).Assembly.GetName().Name), "Independent consumer has no Core private-access grant");
        switch (mode)
        {
            case "Clock": Clock(); break;
            case "Core": Core(services); break;
            case "Graphics": Graphics(services); break;
            case "Reload": Reload(services); break;
            case "Workbench": Workbench(services); break;
            default: throw new ArgumentException(mode);
        }
        if (mode is "Core" or "Clock" or "Reload")
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor" or "BEngine.Application"), "Mechanism works without graphics, UI, workbench or application packages");
        File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks, ClockAllocationBytes = clockAllocation }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SCENE_ISOLATION_{mode.ToUpperInvariant()}_OK {checks.Count}");
    }
    void Clock()
    {
        var initial = Time.CaptureState();
        try
        {
            Time.fixedDeltaTime = (Fix64)1 / 10; Time.maximumDeltaTime = 1; Time.timeScale = 1;
            using (SceneRuntime.BeginFrame((Fix64)3 / 100)) { }
            var state = Time.CaptureState(); var time = Time.time; var unscaled = Time.unscaledTime;
            var frame = Time.frameCount; var fixedTime = Time.fixedTime; var delta = Time.deltaTime;
            using (SceneRuntime.BeginFrame((Fix64)6 / 100)) { }
            var expectedTime = Time.time; var expectedFixed = Time.fixedTime;
            Time.RestoreState(state);
            Check(Time.time == time && Time.unscaledTime == unscaled && Time.fixedTime == fixedTime && Time.frameCount == frame && Time.deltaTime == delta, "Restore recovers all public clock values");
            using (SceneRuntime.BeginFrame((Fix64)6 / 100)) { }
            Check(Time.time == expectedTime && Time.fixedTime == expectedFixed, "Restore recovers pending fixed-step remainder, not just visible properties");
            Time.timeScale = 3; Time.fixedDeltaTime = (Fix64)1 / 60; Time.maximumDeltaTime = (Fix64)1 / 5;
            Time.RestoreState(state);
            Check(Time.timeScale == 1 && Time.fixedDeltaTime == (Fix64)1 / 10 && Time.maximumDeltaTime == 1, "Restore recovers simulation settings");
            Throws<ArgumentException>(() => Time.RestoreState(default), "Default clock state is rejected");
            using (SceneRuntime.BeginFrame(0))
            {
                Throws<InvalidOperationException>(() => Time.CaptureState(), "Capture inside an active frame is rejected");
                Throws<InvalidOperationException>(() => Time.RestoreState(state), "Restore inside an active frame is rejected");
                Check(Time.isFrameActive, "Rejected restore does not terminate the active frame");
            }
            for (var warm = 0; warm < 2; warm++)
                for (var i = 0; i < 1000; i++) Time.RestoreState(Time.CaptureState());
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) Time.RestoreState(Time.CaptureState());
            clockAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(clockAllocation == 0, "Ten thousand warmed clock capture/restore pairs allocate zero managed bytes");
            Check(typeof(Time).GetProperty(nameof(Time.time))!.SetMethod?.IsPublic != true && typeof(Time).GetProperty(nameof(Time.frameCount))!.SetMethod?.IsPublic != true, "Read-only simulation properties did not gain public setters");
            using var scene = new Scene("Clock epoch"); var count = scene.CreateGameObject("counter").AddComponent<FrameCounter>();
            var runtime = new SceneRuntime(scene); runtime.Start(); var snapshot = Time.CaptureState();
            runtime.Tick((Fix64)1 / 60); var first = count.Updates; Time.RestoreState(snapshot); runtime.Tick((Fix64)1 / 60);
            Check(first == 1 && count.Updates == 2, "Restoring frame count does not rewind the runtime dispatch epoch"); runtime.Stop();
        }
        finally { Time.RestoreState(initial); }
    }
    void Core(IServiceProvider services)
    {
        Check(typeof(EditorSceneIsolation).Assembly == typeof(EditorObjectSnapshot).Assembly, "Scene isolation belongs to Core Editor");
        Throws<ArgumentNullException>(() => EditorSceneIsolation.CloneScenes(null!, services), "Null scene collection is rejected");
        Throws<ArgumentNullException>(() => EditorSceneIsolation.CloneScenes([], null!), "Null services are rejected");
        Check(EditorSceneIsolation.CloneScenes([], services).Count == 0, "An empty scene set is supported");
        using var a = new Scene("A", services); using var b = new Scene("B", services); using var unopened = new Scene("Selected scene asset", services);
        var root = a.CreateGameObject("root"); var first = a.CreateGameObject("first"); var last = a.CreateGameObject("last");
        first.transform.SetParent(root.transform, false); last.transform.SetParent(root.transform, false); last.transform.SetSiblingIndex(0);
        root.transform.localPosition = new(3, 7); first.SetActive(false); root.layer = 8;
        var other = b.CreateGameObject("other"); var component = root.AddComponent<IsolationComponent>(); component.enabled = false;
        component.Other = other; var node = new ManagedNode { Value = 17 }; node.Next = node;
        component.Nodes = [node, node]; var asset = new IsolationAsset { Number = 23 }; asset.Borrowed = asset;
        var otherComponent = other.AddComponent<IsolationComponent>(); otherComponent.Asset = asset; otherComponent.Nodes = [node];
        var extra = new IsolationAsset { Number = 41 }; component.Asset = asset;
        var prefab = PrefabDocumentSnapshot.Capture(first).CreateAsset("Assets/first.prefab");
        var instance = prefab.Instantiate(a); component.Prefab = prefab;
        var ids = PrefabDocumentSnapshot.CaptureIdMap(instance);
        IsolationComponent.Before = 0; IsolationComponent.After.Clear();
        var map = EditorSceneIsolation.CloneScenes([a, b, a], services, [extra, unopened, other, null]);
        var ma = (Scene)map[a]; var mb = (Scene)map[b]; var mirrored = (IsolationComponent)map[component];
        try
        {
            Check(map.Keys.OfType<Scene>().Count() == 2 && !map.ContainsKey(unopened), "Duplicate scene roots collapse and selected unopened scenes stay outside the mirror");
            Check(!ReferenceEquals(ma, a) && ma.Id == a.Id && ma.name == a.name && ma.path == a.path && ma.isLoaded == a.isLoaded, "Mirrored scene preserves serialized identity, path and load state");
            Check(map.All(p => !ReferenceEquals(p.Key, p.Value) && p.Key.GetInstanceID() != p.Value.GetInstanceID()), "Every returned mirror is a different registered runtime instance");
            Check(map.Where(p => p.Key is Scene or GameObject or Component).All(p => p.Key.Id == p.Value.Id), "Scene object identities remain stable");
            Check(ReferenceEquals(mirrored.Other, map[other]), "Cross-scene object references bind to the corresponding scene mirror");
            Check(mirrored.Nodes[0].Value == 17 && !ReferenceEquals(mirrored.Nodes[0], node) && ReferenceEquals(mirrored.Nodes[0], mirrored.Nodes[1]) && ReferenceEquals(mirrored.Nodes[0], mirrored.Nodes[0].Next), "Managed aliases and cycles survive isolation without retaining mutable source nodes");
            var otherMirror = (IsolationComponent)map[otherComponent];
            Check(ReferenceEquals(otherMirror.Asset, mirrored.Asset) && ReferenceEquals(otherMirror.Nodes[0], mirrored.Nodes[0]), "Shared assets and managed nodes use one map across scene boundaries");
            Check(!mirrored.enabled && ((GameObject)map[root]).layer == 8 && !((GameObject)map[first]).activeSelf, "Enabled, active and layer state are preserved");
            Check(((GameObject)map[root]).transform.localPosition == new Vector2(3, 7), "In-memory transform values are preserved");
            Check(((GameObject)map[root]).transform.children.Select(t => t.gameObject.name).SequenceEqual(new[] { "last", "first" }), "Child ordering is restored");
            Check(ids.OrderBy(x => x.Key).SequenceEqual(PrefabDocumentSnapshot.CaptureIdMap((GameObject)map[instance]).OrderBy(x => x.Key)), "Prefab source and instance identity maps survive isolation");
            var copy = (IsolationAsset)map[asset];
            Check(ReferenceEquals(copy.Borrowed, copy) && copy.Number == 23, "Private asset payloads map self references");
            Check(RuntimeAssetReferences.IsTransient(copy) && RuntimeAssetReferences.IsTransient(ma) && !RuntimeAssetReferences.IsTransient(asset), "Mirrors become transient while their sources remain persistent");
            Check(map.ContainsKey(extra) && !ReferenceEquals(map[extra], extra), "An additional asset root is isolated");
            Check(((PrefabAsset)map[prefab]).assetId == prefab.assetId && ((PrefabAsset)map[prefab]).objectCount == prefab.objectCount, "Prefab asset data is cloned without losing its resource identity");
            Check(IsolationComponent.Before == 0 && IsolationComponent.After.Count == 0, "Construction suppresses user serialization callbacks");
            EditorSceneIsolation.InvokeAfterDeserialize(map.Values.Concat(map.Values), ma);
            Check(IsolationComponent.After.Count == 2 && IsolationComponent.After.Contains(ma) && IsolationComponent.After.Contains(mb), "Deferred callbacks run once in each owning scene even with duplicate values");
            Check(SceneRuntime.currentScene is null, "Callback scene context is restored afterwards");
            copy.Number = -1; mirrored.Nodes[0].Value = -2;
            Check(asset.Number == 23 && node.Value == 17, "Editing mirrors does not mutate source assets or managed state");
        }
        finally { Release(map); BObject.Destroy(prefab); BObject.Destroy(extra); BObject.Destroy(asset); }
        Check(map.Values.All(v => v.isDestroyed) && a.isCreated && b.isCreated, "Releasing isolated graphs destroys mirrors and keeps source scenes alive");
        EditorSceneIsolation.ReleaseAssets(map.Values); Check(true, "Repeated asset release is harmless");
        using var outside = new Scene("Outside", services); var outsideObject = outside.CreateGameObject("not included");
        component.Asset = null; component.Prefab = null;
        component.Other = outsideObject;
        var beforeCount = BObject.FindObjectsByType<Scene>().Length;
        Throws<InvalidDataException>(() => EditorSceneIsolation.CloneScenes([a], services), "References to excluded scene objects fail instead of borrowing editable objects");
        Check(BObject.FindObjectsByType<Scene>().Length == beforeCount, "Failed mirror construction leaves no registered scene behind");
        component.Other = null; component.Asset = null; component.Prefab = null;
        var failing = new FailingAsset(); FailingAsset.Created.Clear();
        var sceneCount = BObject.FindObjectsByType<Scene>().Length;
        try
        {
            Throws<InvalidDataException>(() => EditorSceneIsolation.CloneScenes([a], services, [failing]), "A private-payload failure aborts graph construction");
            Check(FailingAsset.Created.Count == 1 && FailingAsset.Created[0].isDestroyed && !failing.isDestroyed, "A failed private payload releases its new asset and preserves the source");
            Check(BObject.FindObjectsByType<Scene>().Length == sceneCount, "Payload failure releases all newly cloned scenes");
        }
        finally { BObject.Destroy(failing); }
        IsolationComponent.Before = 0; _ = SceneAssetDocuments.Serialize(a);
        Check(IsolationComponent.Before > 0, "Failure releases callback-suppression scopes");
        using var disposed = new Scene("disposed", services); disposed.Dispose();
        Throws<ObjectDisposedException>(() => EditorSceneIsolation.CloneScenes([disposed], services), "Disposed scenes are rejected");
    }
    void Graphics(IServiceProvider services)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Material).Module.ModuleHandle);
        RuntimeHelpers.RunModuleConstructor(typeof(Sprite).Module.ModuleHandle);
        using var scene = new Scene("Graphics", services); var go = scene.CreateGameObject("graphics");
        var component = go.AddComponent<GraphicsComponent>();
        var texture = new Texture(2, 2); texture.SetPixel(0, 0, Color.red);
        var sprite = Sprite.Create(texture, new Vector2((Fix64)1 / 3, (Fix64)2 / 3));
        var shader = (Shader)Activator.CreateInstance(typeof(Shader), BindingFlags.Instance | BindingFlags.NonPublic,
            null, ["BEngine/IsolationProbe", "// nonempty shader source fixture"], null)!;
        var cachedShader = Shader.Find("BEngine/IsolationCached"); var material = new Material(shader);
        material.SetTexture("_MainTex", texture); material.SetColor("_Tint", Color.green); material.EnableKeyword("TEST");
        component.Sprite = sprite; component.Material = material; component.Texture = texture; component.Shader = shader;
        var map = EditorSceneIsolation.CloneScenes([scene], services, [cachedShader]); var copy = (GraphicsComponent)map[component];
        try
        {
            Check(!ReferenceEquals(copy.Sprite, sprite) && copy.Sprite!.pivot == sprite.pivot, "Sprite members are cloned through their package implementation");
            Check(ReferenceEquals(copy.Sprite!.texture, copy.Texture) && ReferenceEquals(copy.Material!.GetTexture("_MainTex"), copy.Texture), "Sprite and Material private references share the same isolated texture");
            Check(copy.Texture!.GetPixel(0, 0).Equals(Color.red) && copy.Texture.width == 2 && copy.Texture.height == 2, "Texture private pixel payload and dimensions are retained");
            Check(copy.Material!.GetColor("_Tint").Equals(Color.green) && copy.Material.IsKeywordEnabled("TEST"), "Material properties and keywords survive cloning");
            Check(ReferenceEquals(copy.Material.shader, copy.Shader) && !ReferenceEquals(copy.Shader, shader) && copy.Shader!.shaderName == shader.shaderName && copy.Shader.sourceCode == shader.sourceCode, "Shader construction and private state belong to Graphics and are isolated");
            copy.Texture.SetPixel(0, 0, Color.blue);
            Check(texture.GetPixel(0, 0).Equals(Color.red), "Mutable pixel storage is detached");
            Check(!copy.Texture.assetPath.Equals(texture.assetPath, StringComparison.Ordinal), "In-memory texture addressing points at the mirror instance");
        }
        finally { Release(map); BObject.Destroy(material); BObject.Destroy(sprite); BObject.Destroy(texture); }
        Check(!shader.isDestroyed && !cachedShader.isDestroyed && ReferenceEquals(Shader.Find(cachedShader.shaderName), cachedShader), "Mirror release does not destroy private or cached source shaders");
        BObject.Destroy(shader);
        RuntimeObjectCodecs.Register(new InvalidCodec()); var invalid = new InvalidObject(); InvalidCodec.Source = invalid;
        var wrapped = new WrappedAsset { Value = invalid };
        try { Throws<InvalidDataException>(() => EditorSceneIsolation.CloneScenes([], services, [wrapped]), "A codec returning a source object is rejected");
            Check(!invalid.isDestroyed && !wrapped.isDestroyed, "Rejecting an invalid provider does not destroy borrowed sources"); }
        finally { RuntimeObjectCodecs.UnregisterAssembly(typeof(Probe).Assembly); BObject.Destroy(invalid); BObject.Destroy(wrapped); }
    }
    void Reload(IServiceProvider services)
    {
        const string source = """
            using System; using BEngine;
            namespace ReloadFixture;
            public sealed class Asset : BAsset {
                private int _value; private BObject? _reference;
                public void Set(int value, BObject reference) { _value=value; _reference=reference; }
                public int Read()=>_value; public BObject? Reference()=>_reference;
                protected override object CaptureClonePayload()=>new object?[]{_value,_reference};
                protected override void RestoreClonePayload(object? value, Func<BObject,BObject> map) {
                    var data=(object?[])value!; _value=(int)data[0]!; _reference=map((BObject)data[1]!);
                }
            }
            public sealed class Node { public int Number; public Node? Next; }
            public sealed class Owner : Component { public Asset? Value; public Node[] Nodes=[]; }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(BObject).Assembly.Location).Distinct().Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("SceneIsolationReloadFixture", [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream(); var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        var bytes = stream.ToArray(); var oldContext = new SharedContext(); var newContext = new SharedContext();
        var before = oldContext.LoadFromStream(new MemoryStream(bytes)); var after = newContext.LoadFromStream(new MemoryStream(bytes));
        RuntimeTypeCache.RegisterAssemblies([before, after]);
        using var scene = new Scene("Reload", services); var go = scene.CreateGameObject("owner");
        var owner = go.AddComponent(before.GetType("ReloadFixture.Owner", true)!);
        var sourceAsset = (BAsset)Activator.CreateInstance(before.GetType("ReloadFixture.Asset", true)!)!;
        sourceAsset.GetType().GetMethod("Set")!.Invoke(sourceAsset, [71, sourceAsset]); owner.GetType().GetField("Value")!.SetValue(owner, sourceAsset);
        var nodeType = before.GetType("ReloadFixture.Node", true)!; var node = Activator.CreateInstance(nodeType)!;
        nodeType.GetField("Number")!.SetValue(node, 93); nodeType.GetField("Next")!.SetValue(node, node);
        var nodes = Array.CreateInstance(nodeType, 2); nodes.SetValue(node, 0); nodes.SetValue(node, 1); owner.GetType().GetField("Nodes")!.SetValue(owner, nodes);
        using (RuntimeTypeCache.PreferAssemblies([after]))
        {
            var map = EditorSceneIsolation.CloneScenes([scene], services);
            try
            {
                var copy = map[owner]; var asset = map[sourceAsset]; var copiedNodes = (Array)copy.GetType().GetField("Nodes")!.GetValue(copy)!;
                Check(copy.GetType().Assembly == after && asset.GetType().Assembly == after, "Preferred reload assembly supplies component and asset instances");
                Check((int)asset.GetType().GetMethod("Read")!.Invoke(asset, null)! == 71 && ReferenceEquals(asset.GetType().GetMethod("Reference")!.Invoke(asset, null), asset), "Private CLR payload migrates across collectible assembly versions");
                Check(ReferenceEquals(copy.GetType().GetField("Value")!.GetValue(copy), asset), "Reloaded component points to the reloaded asset");
                var newNode = copiedNodes.GetValue(0)!;
                Check(newNode.GetType().Assembly == after && ReferenceEquals(newNode, copiedNodes.GetValue(1)) && ReferenceEquals(newNode, newNode.GetType().GetField("Next")!.GetValue(newNode)), "Typed managed arrays rebind while preserving aliases and cycles");
            }
            finally { Release(map); }
        }
        BObject.Destroy(sourceAsset); RuntimeTypeCache.UnregisterAssemblies([before, after]); oldContext.Unload(); newContext.Unload();
    }
    void Workbench(IServiceProvider services)
    {
        DotNetEditorPlatform.Initialize();
        using var editorServices = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
        using var editorScope = EditorServiceScope.Attach(editorServices);
        services = editorServices;
        var assembly = Assembly.Load("BEngine.EditorWorkbench.Editor"); var type = assembly.GetType("BEngine.Editor.EditorPlayModeSession", true)!;
        var stateType = assembly.GetType("BEngine.Editor.EditorRuntimeStateSnapshot", true)!;
        using var scene = new Scene("Workbench", services); var root = scene.CreateGameObject("selected");
        var entryType = Assembly.Load("BEngine.Core.Editor").GetType("BEngine.Editor.EditorOpenScene", true)!;
        var entries = Array.CreateInstance(entryType, 1);
        entries.SetValue(Activator.CreateInstance(entryType, [scene, Path.Combine(output, "source.scene"), "Assets/source.scene", true]), 0);
        var state = Call(stateType, "Capture", null);
        var session = Call(type, "Create", null, services, entries, scene, "source.scene", null, null, null, root, null, null,
            Array.Empty<BObject>(), true, false, new BObject[] { root }, root, null, null, null, null, state)!;
        var runtimeScene = Read<Scene>(session, "RuntimeScene"); var selected = Read<GameObject>(session, "RuntimeSelected");
        Check(!ReferenceEquals(runtimeScene, scene) && ReferenceEquals(Call(type, "ToRuntime", session, root), selected) && ReferenceEquals(Call(type, "ToEdit", session, selected), root), "Workbench selection mapping retains both edit and runtime identities");
        Check(Read<bool>(session, "EditDirty") && Read<string>(session, "EditScenePath") == "source.scene", "Workbench retains dirty and path policy");
        var runtimeEntries = Read<Array>(session, "RuntimeOpenScenes");
        var reload = Call(type, "PrepareRuntimeReload", session, services, runtimeEntries, runtimeScene, null, null, null, selected, null, new BObject[] { selected }, selected, Array.Empty<BObject>())!;
        var reloadType = reload.GetType();
        Call(type, "CommitRuntimeReload", session, reload); var reloaded = Read<Scene>(session, "RuntimeScene");
        Check(!ReferenceEquals(reloaded, runtimeScene) && ReferenceEquals(Call(type, "ToEdit", session, Read<GameObject>(session, "RuntimeSelected")), root), "Committed reload updates workbench mappings");
        Call(type, "RollbackRuntimeReload", session, reload); ((IDisposable)reload).Dispose();
        Check(ReferenceEquals(Read<Scene>(session, "RuntimeScene"), runtimeScene) && !reloaded.isCreated && runtimeScene.isCreated, "Reload rollback restores previous mappings and disposes replacements");
        var committed = Call(type, "PrepareRuntimeReload", session, services, runtimeEntries, runtimeScene, null, null, null, selected, null, new BObject[] { selected }, selected, Array.Empty<BObject>())!;
        Call(type, "CommitRuntimeReload", session, committed); Call(type, "CompleteRuntimeReload", session, committed); ((IDisposable)committed).Dispose();
        var final = Read<Scene>(session, "RuntimeScene");
        Check(final.isCreated && ReferenceEquals(Read<GameObject>(session, "RuntimeSelected").scene, final), "Successful reload transfers lifetime to the active workbench session");
        runtimeScene.Dispose(); final.Dispose(); Call(type, "ReleaseRuntimeAssets", session);
        Check(scene.isCreated && root.name == "selected", "Stopping the runtime mirrors leaves the edit scene unchanged");
        var originalTime = Time.CaptureState(); using (SceneRuntime.BeginFrame((Fix64)1 / 60)) { }
        Call(stateType, "Restore", state); Check(!Time.isFrameActive, "Workbench runtime-state restoration uses the Core clock snapshot"); Time.RestoreState(originalTime);
    }
    static void Release(Dictionary<BObject, BObject> map)
    {
        foreach (var scene in map.Values.OfType<Scene>().Distinct<Scene>(ReferenceEqualityComparer.Instance)) if (scene.isCreated) scene.Dispose();
        EditorSceneIsolation.ReleaseAssets(map.Values);
    }
}
public sealed class IsolationComponent : Component, ISerializationCallbackReceiver
{
    public BObject? Other; public BAsset? Asset; public PrefabAsset? Prefab; public ManagedNode[] Nodes = [];
    public static int Before; public static readonly List<Scene?> After = [];
    public void OnBeforeSerialize() => Before++;
    public void OnAfterDeserialize() => After.Add(SceneRuntime.currentScene);
}
public sealed class ManagedNode { public int Value; public ManagedNode? Next; }
public sealed class IsolationAsset : BAsset
{
    public int Number; public BObject? Borrowed;
    protected override object CaptureClonePayload() => new object?[] { Number, Borrowed };
    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> map)
    { var data = (object?[])payload!; Number = (int)data[0]!; Borrowed = data[1] is BObject obj ? map(obj) : null; }
}
public sealed class FrameCounter : MonoBehaviour { public int Updates; public override void Update() => Updates++; }
public sealed class GraphicsComponent : Component { public Sprite? Sprite; public Material? Material; public Texture? Texture; public Shader? Shader; }
public sealed class InvalidObject : BObject;
public sealed class InvalidCodec : RuntimeObjectCodec
{
    public static InvalidObject? Source;
    public override Type ObjectType => typeof(InvalidObject);
    public override BObject CreateCloneInstance() => Source!;
    public override string WriteReference(BObject value) => "invalid";
    public override BObject? ReadReference(string reference, IReadOnlyDictionary<string, string>? fields = null) => Source;
}
public sealed class WrappedAsset : BAsset { public BObject? Value; }
public sealed class FailingAsset : BAsset
{
    public static readonly List<FailingAsset> Created = [];
    public FailingAsset() => Created.Add(this);
    protected override object CaptureClonePayload() => 7;
    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> map) => throw new InvalidDataException("Injected private-payload failure");
}
sealed class SharedContext() : AssemblyLoadContext(isCollectible: true)
{ protected override Assembly? Load(AssemblyName name) => Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name); }
