using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
DotNetPlatformServices.Initialize(); WindowsEditorPlatform.Initialize();
using var host = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
using var scope = EditorServiceScope.Attach(host);
_ = new Texture(1, 1); // Register the real image codec, not a test replacement.
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAnkAAAAASUVORK5CYII=");
var path = Path.Combine(output, "icon.png"); File.WriteAllBytes(path, png);
var legacy = Assembly.LoadFrom(Path.GetFullPath(args[1]));
Check(legacy.GetReferencedAssemblies().Any(a => a.Name == "BEngine.IMGUI.Editor"), "Compatibility consumer was compiled against the original IMGUI identity");
Check((bool)legacy.GetType("LegacyEditorImageConsumer")!.GetMethod("Run")!.Invoke(null, [path])!,
    "An unchanged, precompiled consumer instantiates and implements all three relocated image types");
foreach (var type in new[] { typeof(IGpuCanvasResourceResolver), typeof(CanvasResourceUtility), typeof(FileGpuCanvasResourceResolver) })
    Check(Type.GetType(type.FullName + ", BEngine.IMGUI.Editor", true) == type, "Old assembly-qualified identity resolves: " + type.Name);
var imguiType = typeof(GUI).Assembly.GetType("BEngine.Editor.EditorGpuCanvasResourceResolver", true)!;
var imgui = (IGpuCanvasResourceResolver)Activator.CreateInstance(imguiType, true)!;
var uiAssembly = Assembly.Load("BEngine.UIElements.Editor");
var uiType = uiAssembly.GetType("BEngine.Editor.UIElementsEditorResourceResolver", true)!;
var ui = (IUIRenderResourceResolver)Activator.CreateInstance(uiType, true)!;
Check(imgui.TryResolveTexture(path, out var im) && im.Width == 1, "IMGUI still resolves a real PNG through the shared image source");
Check(ui.TryResolveTexture(path, out var retained) && retained.Width == 1 && retained.Pixels.Span.SequenceEqual(im.Pixels.Span),
    "UIElements receives exactly the same decoded pixels and alpha as IMGUI");
var overrideCalls = 0;
bool Override(string source, out EditorTextureData texture)
{ overrideCalls++; texture = new(2, 1, GraphicsTextureFormat.R8Unorm, new byte[] { 12, 34 }); return true; }
EditorInstrumentation.TextureRequested += Override;
try
{
    Check(imgui.TryResolveTexture("override", out im) && im.Width == 2 && overrideCalls == 1, "IMGUI honors the current instrumentation override exactly once");
    Check(ui.TryResolveTexture("override", out retained) && retained.Width == 2 && overrideCalls == 2 && retained.Pixels.Span.SequenceEqual(im.Pixels.Span),
        "UIElements honors the same override without invoking an IMGUI implementation");
}
finally { EditorInstrumentation.TextureRequested -= Override; }
Check(imgui.TryResolveTexture(path, out _) && ui.TryResolveTexture(path, out _), "Both renderers return to file sources after override removal");
Check(!imgui.TryResolveTexture(Path.Combine(output, "missing.png"), out _) && !ui.TryResolveTexture(Path.Combine(output, "missing.png"), out _),
    "Both adapters preserve the missing-source result");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_IMAGE_WORKFLOW_OK|" + checks.Count);
