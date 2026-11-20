using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Directory.SetCurrentDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", output);
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize(); AssetPipelineRuntime.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform, TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services])!;
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
PlayerHostServices.ConfigureDataDirectory(Path.Combine(output, "Assets")); Directory.CreateDirectory(Application.dataPath);
var codec = RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>()!;
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws(Action action, string name)
{ try { action(); } catch { Check(true, name); return; } throw new Exception(name); }
string PathFor(string name) => Path.Combine(Application.dataPath, name + ".prefab.yaml");
using var sourceScene = new Scene("sources"); using var scene = new Scene("live");
var source = sourceScene.CreateGameObject("source root");
var child = sourceScene.CreateGameObject("source child"); child.transform.SetParent(source.transform, false);
var value = source.AddComponent<PrefabProbe>(); value.Count = 17; value.Target = child;
value.Graph = new PrefabNode(); value.Graph.Next = value.Graph;
var sourceChildValue = child.AddComponent<PrefabProbe>(); sourceChildValue.Target = value;
var path = PathFor("base"); var prefab = codec.Save(source, path, null);
var root = prefab.Instantiate(scene); var liveValue = root.GetComponent<PrefabProbe>()!;
var liveChild = root.transform.children[0].gameObject;
var external = scene.CreateGameObject("external"); var externalValue = external.AddComponent<PrefabProbe>(); externalValue.Target = liveChild;
var updates = 0; PrefabUtility.prefabInstanceUpdated += OnUpdate;
void OnUpdate(GameObject _) => updates++;
Undo.ClearAll();
try
{
    Check(PrefabUtility.GetOverrides(root).Count == 0, "A fresh instance has no non-default overrides");
    Check(!PrefabUtility.RefreshPrefabInstance(root), "An unchanged source causes no refresh edit");
    var neutral = EditorPrefabRefresh.Prepare(root)!;
    Check(!neutral.HasChanges && !neutral.MetadataOnly, "Neutral preparation reports an unchanged instance");
    Throws(() => neutral.OpenSession(), "An unchanged refresh cannot create an edit session");
    Throws(() => neutral.ApplyMetadata(), "An unchanged refresh cannot apply an unrelated metadata update");
    liveValue.Count = 99;
    var propertyOverride = PrefabUtility.GetOverrides(root).Single(o => ReferenceEquals(o.Target, liveValue) && o.PropertyPath == "Count");
    var beforeUpdates = updates;
    PrefabUtility.RevertOverride(propertyOverride);
    Check(liveValue.Count == 17 && updates > beforeUpdates, "Property revert restores source data and emits workflow notification");
    Check(Undo.canUndo && EditorUtility.IsDirty(root), "Property revert records workflow history and dirty state");
    Undo.PerformUndo(); Check(liveValue.Count == 99, "Property Undo restores the local override");
    Undo.PerformRedo(); Check(liveValue.Count == 17, "Property Redo reapplies the source value");
    Undo.ClearAll();
    liveValue.Count = 38; liveValue.Target = external;
    PrefabUtility.RevertObjectOverride(liveValue);
    Check(liveValue.Count == 17 && ReferenceEquals(liveValue.Target, liveChild), "Object revert maps source references to retained live children");
    Check(ReferenceEquals(liveValue.Graph, liveValue.Graph!.Next), "Object revert retains cyclic managed data");
    Undo.PerformUndo(); Check(liveValue.Count == 38 && ReferenceEquals(liveValue.Target, external), "Object Undo retains external scene references");
    Undo.PerformRedo(); Check(liveValue.Count == 17 && ReferenceEquals(liveValue.Target, liveChild), "Object Redo restores internal references");
    Undo.ClearAll();
    var added = root.AddComponent<PrefabAdded>(); added.Value = 64;
    var addOverride = PrefabUtility.GetOverrides(root).Single(o => o.Kind == PrefabOverrideKind.AddedComponent && ReferenceEquals(o.Target, added));
    PrefabUtility.RevertOverride(addOverride);
    Check(root.GetComponent<PrefabAdded>() is null, "Added-component revert removes the local addition");
    Undo.PerformUndo(); Check(ReferenceEquals(root.GetComponent<PrefabAdded>(), added) && added.Value == 64, "Component Undo preserves original identity and values");
    Undo.PerformRedo(); Check(root.GetComponent<PrefabAdded>() is null, "Component Redo removes the same component");
    Undo.ClearAll();
    var requiredSource = source.AddComponent<PrefabDependent>(); codec.Save(source, path, null);
    Check(PrefabUtility.RefreshPrefabInstance(root), "Refresh discovers newly inherited components");
    Check(root.GetComponent<PrefabDependent>() is not null && root.GetComponent<PrefabRequired>() is not null,
        "Refresh restores required-component dependencies together");
    Check(!Undo.canUndo, "Automated source refresh does not create user Undo history");
    var required = root.GetComponent<PrefabRequired>()!;
    var componentCount = root.components.Count;
    Throws(() => PrefabUtility.RevertOverride(new(root, required, PrefabOverrideKind.AddedComponent, "", Guid.Empty)),
        "Required-component removal is rejected");
    Check(root.components.Count == componentCount && ReferenceEquals(root.GetComponent<PrefabRequired>(), required),
        "A rejected operation rolls back without replacing live components");
    Check(!Undo.canUndo, "A rejected operation records no false history");
    liveValue.Count = 72;
    using (var serialized = new SerializedObject(liveValue))
        PrefabUtility.ApplyPropertyOverride(serialized.FindProperty("Count")!, path);
    Check(codec.Load(path).Instantiate(sourceScene).GetComponent<PrefabProbe>()!.Count == 72, "Property apply persists the selected source value");
    Check(!PrefabUtility.GetOverrides(root).Any(o => ReferenceEquals(o.Target, liveValue) && o.PropertyPath == "Count"),
        "Apply advances the source baseline without retaining a false override");
    Undo.PerformUndo();
    Check(codec.Load(path).Instantiate(sourceScene).GetComponent<PrefabProbe>()!.Count == 17, "Apply Undo restores the source file");
    Undo.PerformRedo();
    Check(codec.Load(path).Instantiate(sourceScene).GetComponent<PrefabProbe>()!.Count == 72, "Apply Redo restores the saved source file");
    Undo.ClearAll();
    var removedChildComponent = liveChild.GetComponent<PrefabProbe>()!;
    var removedChildId = removedChildComponent.Id;
    liveChild.RemoveComponent(removedChildComponent);
    var removedOverride = PrefabUtility.GetOverrides(root).Single(o => o.Kind == PrefabOverrideKind.RemovedComponent && o.SourceId == sourceChildValue.Id);
    PrefabUtility.RevertOverride(removedOverride);
    var rebuiltChildComponent = liveChild.GetComponent<PrefabProbe>()!;
    Check(rebuiltChildComponent.Id == removedChildId && ReferenceEquals(rebuiltChildComponent.Target, liveValue),
        "Removed-component revert restores recorded identity and live reference mapping");
    Undo.PerformUndo(); Check(liveChild.GetComponent<PrefabProbe>() is null, "Removed-component Undo restores the local removal");
    Undo.PerformRedo(); Check(ReferenceEquals(liveChild.GetComponent<PrefabProbe>(), rebuiltChildComponent), "Removed-component Redo retains the reconstructed component identity");
    Undo.ClearAll();
    var appliedComponent = root.AddComponent<PrefabAdded>(); appliedComponent.Value = 213;
    PrefabUtility.ApplyOverride(PrefabUtility.GetOverrides(root).Single(o => o.Kind == PrefabOverrideKind.AddedComponent && ReferenceEquals(o.Target, appliedComponent)), InteractionMode.AutomatedAction);
    Check(codec.Load(path).Instantiate(sourceScene).GetComponent<PrefabAdded>()!.Value == 213, "Applying an added component persists its values through the registered document codec");
    Check(!PrefabUtility.GetOverrides(root).Any(o => o.Kind == PrefabOverrideKind.AddedComponent && ReferenceEquals(o.Target, appliedComponent)),
        "Applying a component establishes source provenance and advances its baseline");
    var appliedChild = scene.CreateGameObject("applied subtree"); appliedChild.transform.SetParent(root.transform, false);
    var appliedGrandchild = scene.CreateGameObject("applied grandchild"); appliedGrandchild.transform.SetParent(appliedChild.transform, false);
    appliedChild.AddComponent<PrefabProbe>().Target = appliedGrandchild;
    appliedGrandchild.AddComponent<PrefabProbe>().Target = appliedChild.GetComponent<PrefabProbe>();
    PrefabUtility.ApplyOverride(PrefabUtility.GetOverrides(root).Single(o => o.Kind == PrefabOverrideKind.AddedGameObject && ReferenceEquals(o.Target, appliedChild)), InteractionMode.AutomatedAction);
    var persistedRoot = codec.Load(path).Instantiate(sourceScene);
    var persistedChild = persistedRoot.transform.children.Single(t => t.gameObject.name == "applied subtree").gameObject;
    Check(ReferenceEquals(persistedChild.GetComponent<PrefabProbe>()!.Target, persistedChild.transform.children[0].gameObject),
        "Applying an added subtree preserves cross-object references in the saved source");
    Check(ReferenceEquals(persistedChild.transform.children[0].gameObject.GetComponent<PrefabProbe>()!.Target, persistedChild.GetComponent<PrefabProbe>()),
        "Applying an added subtree preserves component reference cycles");
    Check(!Undo.canUndo, "Automated apply preserves its no-user-history policy");
    var originalId = liveChild.Id; var originalValue = liveValue;
    liveValue.Count = 91; liveValue.Target = external;
    value.Count = 80; child.name = "renamed upstream";
    var upstream = sourceScene.CreateGameObject("new upstream child"); upstream.transform.SetParent(source.transform, false);
    var upstreamProbe = upstream.AddComponent<PrefabProbe>(); upstreamProbe.Target = child;
    codec.Save(source, path, null);
    Check(PrefabUtility.RefreshPrefabInstance(root), "Changed source data refreshes the live hierarchy");
    Check(ReferenceEquals(root.GetComponent<PrefabProbe>(), originalValue) && liveChild.Id == originalId,
        "Refresh preserves existing live GameObject and component identities");
    Check(liveValue.Count == 91 && ReferenceEquals(liveValue.Target, external), "Refresh preserves local value overrides and external references");
    Check(liveChild.name == "renamed upstream" && root.transform.childCount == 2, "Refresh inherits unmodified values and new children");
    Check(ReferenceEquals(externalValue.Target, liveChild), "Existing external callers still reference the retained live child");
    var inherited = root.transform.children.Single(t => t.gameObject.name == "new upstream child").gameObject;
    Check(ReferenceEquals(inherited.GetComponent<PrefabProbe>()!.Target, liveChild), "New inherited references resolve into retained live objects");
    var inheritedId = inherited.Id;
    Check(!PrefabUtility.RefreshPrefabInstance(root) && inherited.Id == inheritedId, "Repeated unchanged refresh introduces no duplicate identities");
    // Simulate the just-saved Prefab Mode path: metadata changes but live values are already equal to the new source.
    var contents = PrefabUtility.LoadPrefabContents(path);
    try
    {
        var contentProbe = contents.GetComponent<PrefabProbe>()!; contentProbe.Count = 107;
        codec.Save(contents, path, prefab.assetId);
        var prepared = EditorPrefabRefresh.Prepare(contents)!;
        Check(prepared.HasChanges && prepared.MetadataOnly, "Just-saved authoring contents report a provenance-only refresh");
        var contentUpdates = updates;
        Check(PrefabUtility.RefreshPrefabInstance(contents) && updates == contentUpdates + 1, "Metadata-only workflow emits one instance update");
        Check(!EditorUtility.IsDirty(contents) && contentProbe.Count == 107, "Metadata-only refresh leaves just-saved authoring contents clean");
        Check(!PrefabUtility.RefreshPrefabInstance(contents), "A metadata-only refresh settles after one application");
    }
    finally { PrefabUtility.UnloadPrefabContents(contents); }
    // A component constructor fails after additions have begun; the workflow must remove partial additions.
    var badChild = sourceScene.CreateGameObject("fails later"); badChild.transform.SetParent(source.transform, false);
    badChild.AddComponent<PrefabThrowing>(); codec.Save(source, path, null);
    var liveBefore = root.transform.children.Select(t => t.gameObject).ToArray();
    var countBefore = root.components.Count;
    PrefabThrowing.Fail = true;
    try { Throws(() => PrefabUtility.RefreshPrefabInstance(root), "A source constructor failure propagates to the coordinated transaction"); }
    finally { PrefabThrowing.Fail = false; }
    Check(root.transform.children.Select(t => t.gameObject).SequenceEqual(liveBefore) && root.components.Count == countBefore,
        "Failed refresh rolls back partial additions and sibling structure");
    Check(ReferenceEquals(root.GetComponent<PrefabProbe>(), originalValue) && ReferenceEquals(liveValue.Target, external),
        "Failure rollback preserves retained component identity and external reference overrides");
    Check(!Undo.canUndo, "Failed automated refresh creates no user Undo record");
    Check(PrefabUtility.RefreshPrefabInstance(root), "Refresh can retry after the failed component is repaired");
    Check(root.transform.children.Count(t => t.gameObject.name == "fails later") == 1, "Retry inherits exactly one repaired child");
    // Nested instances and a variant use the actual registered source resolver, not a test merge implementation.
    var nestedSource = sourceScene.CreateGameObject("nested"); nestedSource.AddComponent<PrefabAdded>().Value = 3;
    var nestedPath = PathFor("nested"); var nestedAsset = codec.Save(nestedSource, nestedPath, null);
    var nestedA = nestedAsset.Instantiate(sourceScene); nestedA.transform.SetParent(source.transform, false);
    var nestedB = nestedAsset.Instantiate(sourceScene); nestedB.transform.SetParent(source.transform, false);
    codec.Save(source, path, null);
    var variantRoot = codec.Load(path).Instantiate(sourceScene); variantRoot.name = "variant local";
    var variantPath = PathFor("variant"); var variant = PrefabUtility.SaveAsPrefabVariant(variantRoot, variantPath);
    var variantInstance = variant.Instantiate(scene);
    var nestedInstances = variantInstance.transform.children.Where(t => t.gameObject.name == "nested").Select(t => t.gameObject).ToArray();
    Check(nestedInstances.Length == 2 && nestedInstances[0].Id != nestedInstances[1].Id, "Variant retains repeated nested instances with distinct identities");
    nestedInstances[0].GetComponent<PrefabAdded>()!.Value = 11;
    nestedSource.GetComponent<PrefabAdded>()!.Value = 8; codec.Save(nestedSource, nestedPath, null);
    Check(PrefabUtility.RefreshPrefabInstance(variantInstance), "Nested source changes propagate through a variant");
    Check(variantInstance.name == "variant local" && nestedInstances[0].GetComponent<PrefabAdded>()!.Value == 11 &&
          nestedInstances[1].GetComponent<PrefabAdded>()!.Value == 8, "Nested local overrides remain independent while inherited values advance");
    PrefabUtility.RevertPrefabInstance(variantInstance);
    Check(ReferenceEquals(variantInstance.transform.children.Single(t => t.gameObject.Id == nestedInstances[0].Id).gameObject, nestedInstances[0]),
        "Whole-instance revert retains nested live identities");
    Undo.PerformUndo(); Check(nestedInstances[0].GetComponent<PrefabAdded>()!.Value == 11, "Nested hierarchy Undo restores local overrides");
    Undo.PerformRedo(); Check(nestedInstances[0].GetComponent<PrefabAdded>()!.Value == 8, "Nested hierarchy Redo restores source values");
    Undo.ClearAll();
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor"),
        "Prefab workflows require no concrete workbench or IMGUI renderer");
}
finally { PrefabThrowing.Fail = false; PrefabUtility.prefabInstanceUpdated -= OnUpdate; Undo.ClearAll(); }
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PREFAB_WORKFLOW_OK {checks.Count}");
