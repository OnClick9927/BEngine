using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
#if SCENE
using BEngine.Rendering;
#endif

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", Path.Combine(output, "EditorData"));
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(output, "EditorData"));
var checks = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS|" + name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic;
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
AssetPipelineRuntime.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var serviceAssembly = typeof(ProjectWindowUtil).Assembly;
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
using var attachment = (IDisposable)access.GetMethod("Attach", Statics)!.Invoke(null, [services])!;
Check(typeof(EditorProjectSettings).Assembly == serviceAssembly, "Project settings and asset presentation belong to EditorServices");
Check(!serviceAssembly.GetReferencedAssemblies().Any(a => a.Name is "BEngine.EditorWorkbench.Editor" or "BEngine.IMGUI.Editor" or "BEngine.Rendering2D"), "Shared editor mechanisms have no Workbench, IMGUI or scene-renderer dependency");
var bridge = serviceAssembly.GetType("BEngine.Editor.EditorBridge", true)!;
var hostType = serviceAssembly.GetType("BEngine.Editor.IEditorHost", true)!;
var host = DispatchProxy.Create(hostType, typeof(ProbeHost));
var probe = (ProbeHost)host;
probe.ProjectRoot = Path.Combine(output, "Project"); Directory.CreateDirectory(Path.Combine(probe.ProjectRoot, "Assets"));
bridge.GetMethod("Attach", Statics)!.Invoke(null, [host]);
Check(ProjectWindowUtil.GetActiveFolderPath() == "Assets/Probe", "Asset creation resolves the active host folder");
ProjectWindowUtil.ShowCreatedAsset("Assets\\Probe\\sample.asset.yaml", true);
Check(probe.Revealed == ("Assets/Probe/sample.asset.yaml", true), "Reveal normalizes separators and preserves rename intent");
Throws<ArgumentException>(() => ProjectWindowUtil.ShowCreatedAsset(" "), "Empty asset reveal path is rejected");
var asset = new ProbeAsset { name = "Authoring asset", Value = 37 };
BObject? pinged = null; void OnPing(BObject value) => pinged = value;
EditorPresentationRequests.PingRequested += OnPing;
ProjectWindowUtil.ShowCreatedAsset(asset);
Check(ReferenceEquals(pinged, asset), "Object reveal emits the neutral ping request without an IMGUI renderer");
EditorPresentationRequests.PingRequested -= OnPing;
var assetPath = Path.Combine("Assets", "Created.asset.yaml").Replace('\\', '/');
File.Delete(Path.Combine(probe.ProjectRoot, assetPath));
ProjectWindowUtil.CreateAsset(asset, assetPath);
Check(File.Exists(Path.Combine(probe.ProjectRoot, assetPath)) && probe.Imported == assetPath, "Shared asset API writes data and requests host import");
var yaml = File.ReadAllText(Path.Combine(probe.ProjectRoot, assetPath));
Check(yaml.Contains("37") && yaml.Contains("Authoring asset"), "Created asset preserves payload and name");
var settings = Path.Combine(probe.ProjectRoot, "ProjectSettings.yaml");
File.Delete(settings);
EditorProjectSettings.Initialize(settings, "Fallback Game");
Check(EditorProjectSettings.current.ProductName == "Fallback Game" && !File.Exists(settings), "Missing settings use fallback without eagerly writing a file");
var changes = 0; void Changed() => changes++;
EditorProjectSettings.projectSettingsChanged += Changed;
EditorProjectSettings.current.CompanyName = "Boundary Company";
EditorProjectSettings.current.ProductName = "Boundary Game";
EditorProjectSettings.current.ScriptingDefineSymbols.Add("AUTHORING_TEST");
EditorProjectSettings.current.Tags.Add("AuthoringTag");
EditorProjectSettings.Save();
Check(changes == 1 && Application.companyName == "Boundary Company" && Application.productName == "Boundary Game", "Saving settings applies identities and emits one change");
Check(Path.GetFullPath(settings) == EditorProjectSettings.settingsPath, "Settings path stays normalized");
EditorProjectSettings.Initialize(settings);
Check(EditorProjectSettings.current.ScriptingDefineSymbols.Contains("AUTHORING_TEST") && EditorProjectSettings.current.Tags.Contains("AuthoringTag"), "Settings round trip preserves scripting symbols and tags");
Check(EditorProjectSettings.current.Format == "BEngine.ProjectSettings" && EditorProjectSettings.current.Version == 3, "Settings format remains v3");
EditorProjectSettings.current.ScriptingDefineSymbols.Add("bad-symbol");
Throws<InvalidDataException>(EditorProjectSettings.Save, "Invalid scripting symbols cannot overwrite settings");
Check(changes == 1, "Rejected settings do not notify consumers");
EditorProjectSettings.Initialize(settings);
EditorProjectSettings.current.DefaultScreenWidth = 1;
Throws<InvalidDataException>(EditorProjectSettings.Save, "Invalid screen dimensions remain rejected");
EditorProjectSettings.projectSettingsChanged -= Changed;
EditorProjectSettings.Initialize(settings);
File.WriteAllText(Path.Combine(probe.ProjectRoot, "Invalid.yaml"), "format: [broken");
EditorProjectSettings.Initialize(Path.Combine(probe.ProjectRoot, "Invalid.yaml"), "Recovery");
Check(EditorProjectSettings.current.ProductName == "Recovery", "Malformed settings retain the safe fallback");
foreach (var type in new[] { typeof(ProjectWindowUtil), typeof(EditorProjectSettings) })
    foreach (var suffix in new[] { ", BEngine.EditorWorkbench.Editor", ", BEngine.EditorWorkbench.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null" })
        Check(RuntimeTypeCache.FindType(type.FullName + suffix) == type, "Legacy authoring identity: " + type.Name + suffix);

#if SCENE
var sceneAssembly = typeof(SceneView).Assembly;
Check(sceneAssembly.GetName().Name == "BEngine.Rendering2D.Editor", "Scene tools belong to Rendering2D.Editor");
Check(!sceneAssembly.GetReferencedAssemblies().Any(a => a.Name == "BEngine.EditorWorkbench.Editor"), "Scene tools can run without Workbench");
foreach (var type in new[] { typeof(SceneView), typeof(SceneViewToolContext), typeof(SceneHandleUtility), typeof(ScenePickCandidate), typeof(ScenePickingUtility), typeof(ScenePickingProviderRegistry) })
    Check(RuntimeTypeCache.FindType(type.FullName + ", BEngine.EditorWorkbench.Editor") == type, "Legacy scene-tool identity: " + type.Name);
SceneView.FrameSelected(); SceneView.FrameLastActiveSceneView(); SceneView.RepaintAll();
Check(probe.Frames == 2 && probe.Repaints == 1, "Scene commands target the active host without a default window");
var view = new Rect(15, 30, 640, 360); var camera = RenderCamera.Default;
var context = new SceneViewToolContext(view, camera);
var world = new Vector2(1, 2); var restored = context.GUIToWorld(context.WorldToGUI(world));
Check((world - restored).magnitude < (Fix64)0.0001f, "Tool projection round trips with toolbar offset");
var passes = 0; var cancels = 0;
void Draw(SceneViewToolContext c) { passes++; c.suppressDefaultTools = true; }
void Cancel() => cancels++;
SceneView.duringSceneGui += Draw; SceneView.toolInteractionCancelled += Cancel;
Check(SceneView.DrawTools(view, camera) && passes == 1, "Custom host dispatches scene tools and respects default-tool suppression");
SceneView.CancelToolInteraction(); Check(cancels == 1, "Custom host can cancel a tool interaction");
SceneView.duringSceneGui -= Draw; SceneView.toolInteractionCancelled -= Cancel;
Check(!SceneView.DrawTools(view, camera), "No tool callback leaves default tools available");
using var scene = new Scene("Authoring", services);
var back = scene.CreateGameObject("Back").AddComponent<SpriteRenderer>(); back.size = new Vector2(2, 2); back.orderInLayer = 0;
var front = scene.CreateGameObject("Front").AddComponent<SpriteRenderer>(); front.size = new Vector2(2, 2); front.orderInLayer = 5;
IReadOnlyList<GameObject> Pick() => ScenePickingUtility.PickAll([scene], camera, new Vector2(320, 180), 640, 360);
Check(Pick().SequenceEqual(new[] { front.gameObject, back.gameObject }), "Sprite picking preserves front-to-back ordering");
front.color = new Color(1, 1, 1, 0);
Check(Pick().SequenceEqual(new[] { back.gameObject }), "Transparent sprites remain unpickable"); front.color = Color.white;
SceneVisibilityManager.instance.Hide(front.gameObject);
Check(Pick().SequenceEqual(new[] { back.gameObject }), "Scene visibility applies to picking"); SceneVisibilityManager.instance.ShowAll();
SceneVisibilityManager.instance.DisablePicking(front.gameObject);
Check(Pick().SequenceEqual(new[] { back.gameObject }), "Picking-disabled objects are excluded"); SceneVisibilityManager.instance.EnableAllPicking();
front.enabled = false; Check(Pick().SequenceEqual(new[] { back.gameObject }), "Disabled renderer is excluded"); front.enabled = true;
Check(ScenePickingUtility.PickAll([scene], camera, new Vector2(1, 1), 640, 360).Count == 0, "Pointer outside the sprite has no hit");
var extensionObject = scene.CreateGameObject("Extension");
long Provider(Scene s, RenderCamera c, Vector2 p, int w, int h, ICollection<ScenePickCandidate> hits, long order, Predicate<GameObject>? filter)
{
    hits.Add(new(extensionObject, new RenderOrderKey(SortingLayer.Default, 50, 0, RenderTransparency.Transparent, order)));
    hits.Add(new(extensionObject, new RenderOrderKey(SortingLayer.Default, 49, 0, RenderTransparency.Transparent, order + 1)));
    return order + 2;
}
ScenePickingProviderRegistry.Register("boundary-picking", Provider);
Check(Pick().SequenceEqual(new[] { extensionObject, front.gameObject, back.gameObject }), "Extension hits merge with sprites and deduplicate by object");
SceneVisibilityManager.instance.Hide(extensionObject);
Check(!Pick().Contains(extensionObject), "Extension hits respect the same visibility policy"); SceneVisibilityManager.instance.ShowAll();
using var otherScene = new Scene("Other", services); var foreign = otherScene.CreateGameObject("Foreign");
ScenePickingProviderRegistry.Register("boundary-foreign", (s, c, p, w, h, hits, order, filter) => { hits.Add(new(foreign, default)); return order; });
Check(!Pick().Contains(foreign), "Providers cannot insert objects from another scene");
ScenePickingProviderRegistry.Register("boundary-throws", (s, c, p, w, h, hits, order, filter) => throw new InvalidOperationException("expected provider failure"));
Check(Pick().Count == 3, "A failing picking provider cannot block other contributors");
ScenePickingProviderRegistry.Unregister("boundary-throws"); ScenePickingProviderRegistry.Unregister("boundary-foreign");
Check(ScenePickingProviderRegistry.Unregister("boundary-picking") && !ScenePickingProviderRegistry.Unregister("boundary-picking"), "Picking unregister is idempotent");
back.gameObject.transform.position = new Vector2(3, 4); back.pivot = Vector2.zero; back.useSpritePivot = false;
Check(SceneHandleUtility.GetHandlePosition(back.gameObject) == new Vector2(4, 5), "Handle center follows renderer visual bounds");
Check(SceneHandleUtility.GetHandlePosition(extensionObject) == extensionObject.transform.position, "Object without visuals falls back to transform position");
Check(Tools.HandlePositionProvider == SceneHandleUtility.GetHandlePosition, "Loading Rendering2D.Editor registers the visual handle policy");
var gizmoObject = scene.CreateGameObject("Gizmo"); var gizmo = gizmoObject.AddComponent<ProbeGizmo>();
RuntimeTypeCache.RegisterAssemblies([typeof(ProbeGizmo).Assembly]);
var pass = sceneAssembly.GetType("BEngine.Editor.SceneGizmoPass", true)!;
object Collect(GameObject? selected) => pass.GetMethod("Collect", Statics)!.Invoke(null, [new[] { scene }, selected, 640, 360])!;
int Lines(object list) => ((System.Collections.ICollection)list.GetType().GetProperty("Lines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(list)!).Count;
Check(Lines(Collect(gizmoObject)) == 2 && gizmo.Selected == 1, "Selected gizmos invoke both component callback and attributed drawer");
Check(Lines(Collect(null)) == 1 && gizmo.Normal > 0, "Unselected gizmos use the normal callback and skip selected drawers");
var visibility = sceneAssembly.GetType("BEngine.Editor.SceneGizmoVisibility", true)!;
visibility.GetMethod("SetVisible", Statics)!.Invoke(null, [typeof(ProbeGizmo), false]);
Check(Lines(Collect(null)) == 0, "Per-component gizmo visibility works without default Workbench");
visibility.GetMethod("SetVisible", Statics)!.Invoke(null, [typeof(ProbeGizmo), true]);
var enabled = visibility.GetProperty("Enabled", Statics)!; enabled.SetValue(null, false);
Check(Lines(Collect(null)) == 0, "Global gizmo visibility disables collection"); enabled.SetValue(null, true);
ScenePickingProviderRegistry.Register("boundary-unload", Provider);
RuntimeAssemblyCleanup.UnregisterExtensions(typeof(ProbeGizmo).Assembly);
Check(!Pick().Contains(extensionObject), "Package unload removes owned picking registrations");
var drawers = sceneAssembly.GetType("BEngine.Editor.GizmoDrawerRegistry", true)!;
Check(((Array)drawers.GetField("_registrations", Statics)!.GetValue(null)!).Length == 0 &&
      ((Array)drawers.GetField("_drawableTypes", Statics)!.GetValue(null)!).Length == 0 &&
      ((System.Collections.IDictionary)drawers.GetField("Commands", Statics)!.GetValue(null)!).Count == 0,
      "Unload immediately releases gizmo Type, MethodInfo and delegate caches");
var customHandle = new Func<GameObject, Vector2>(_ => new Vector2(23, 42));
Tools.HandlePositionProvider = customHandle;
RuntimeAssemblyCleanup.UnregisterExtensions(sceneAssembly);
Check(ReferenceEquals(Tools.HandlePositionProvider, customHandle), "Unloading scene authoring preserves a replacement host policy");
Tools.HandlePositionProvider = null;
#endif

bridge.GetMethod("Detach", Statics)!.Invoke(null, [host]);
Check(ProjectWindowUtil.GetActiveFolderPath() is null, "Detaching the host clears active asset folder");
ProjectWindowUtil.ShowCreatedAsset("Assets/Test.asset.yaml");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.EditorWorkbench.Editor" or "BEngine.EditorWorkbench.Host"), "All checks completed without loading a default Workbench assembly");
#if !SCENE
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.IMGUI.Editor" or "BEngine.Graphics" or "BEngine.Rendering2D"), "Asset and settings mechanisms run without graphics or IMGUI");
#endif
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_AUTHORING_BOUNDARY_OK|" + checks.Count);

public class ProbeHost : DispatchProxy
{
    public string ProjectRoot = ""; public string? Imported; public (string, bool) Revealed; public int Frames, Repaints;
    private readonly Dictionary<string, object?> _properties = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("set_")) { _properties[method.Name[4..]] = args![0]; return null; }
        switch (method.Name)
        {
            case "get_ActiveProjectFolderPath": return "Assets/Probe";
            case "get_ProjectRootPath": return ProjectRoot;
            case "get_AssetsRootPath": return Path.Combine(ProjectRoot, "Assets");
            case "RevealProjectAsset": Revealed = ((string)args![0]!, (bool)args[1]!); return null;
            case "ImportAsset": Imported = (string)args![0]!; return null;
            case "FrameSelected": Frames++; return null;
            case "RepaintAllWindows": Repaints++; return null;
        }
        if (method.Name.StartsWith("get_") && _properties.TryGetValue(method.Name[4..], out var value)) return value;
        return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
public sealed class ProbeAsset : BObject { public int Value; }
#if SCENE
public sealed class ProbeGizmo : Component
{
    public int Normal, Selected;
    public override void OnDrawGizmos() { Normal++; Gizmos.DrawLine(Vector2.zero, Vector2.one); }
    public override void OnDrawGizmosSelected() { Selected++; Gizmos.DrawLine(Vector2.zero, Vector2.right); }
    [DrawGizmo(GizmoType.Selected)]
    private static void Drawer(ProbeGizmo target, GizmoType state) => Gizmos.DrawLine(Vector2.zero, Vector2.up);
}
#endif
