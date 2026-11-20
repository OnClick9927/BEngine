using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.ShaderGraph;
using Microsoft.Extensions.DependencyInjection;

var mode = args[0];
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().BuildServiceProvider();
PlatformRuntime.Initialize(services);
var graph = new ShaderGraphAsset { surface = ShaderGraphSurface.Lit2D };
Check(!typeof(ShaderGraphCompiler).Assembly.GetReferencedAssemblies()
    .Any(assembly => assembly.Name == "BEngine.Lighting2D"), "ShaderGraph assembly references Lighting2D");

if (mode == "without")
{
    Check(!SceneLightingShaderRegistry2D.IsAvailable, "Lighting2D loaded without being requested");
    Check(ShaderGraphCompiler.CompilePreview(graph, GraphicsBackend.OpenGL, null).LightingCapacity == 0,
        "Lit2D graph requires Lighting2D for an unlit preview");
    try
    {
        ShaderGraphCompiler.CompileSurface(graph, GraphicsBackend.OpenGL, null, 32, 0);
        throw new Exception("Lit2D compilation accepted a missing lighting provider");
    }
    catch (InvalidDataException) { }
}
else if (mode == "with")
{
    var lightingAssembly = Assembly.Load("BEngine.Lighting2D");
    RuntimeHelpers.RunModuleConstructor(lightingAssembly.ManifestModule.ModuleHandle);
    Check(SceneLightingShaderRegistry2D.IsAvailable, "Lighting2D did not register its shader provider");
    Resources.RegisterResourceRoot(args[1]);
    Check(ShaderGraphCompiler.CompilePreview(graph, GraphicsBackend.OpenGL, null).LightingCapacity == 32,
        "Lit2D preview did not use the lighting provider");
    RuntimePackageState.SetEnabled("com.bengine.lighting2d", false);
    Check(!SceneLightingShaderRegistry2D.IsAvailable &&
          ShaderGraphCompiler.CompilePreview(graph, GraphicsBackend.OpenGL, null).LightingCapacity == 0,
        "Disabled Lighting2D still affects ShaderGraph");
    RuntimePackageState.SetEnabled("com.bengine.lighting2d", true);
    Check(ShaderGraphCompiler.CompilePreview(graph, GraphicsBackend.OpenGL, null).LightingCapacity == 32,
        "Lit2D preview did not recover after re-enabling Lighting2D");
    RuntimeAssemblyCleanup.UnregisterExtensions(lightingAssembly);
    Check(!SceneLightingShaderRegistry2D.IsAvailable, "Lighting2D provider survived package cleanup");
    Check(ShaderGraphCompiler.CompilePreview(graph, GraphicsBackend.OpenGL, null).LightingCapacity == 0,
        "Lit2D preview did not return to unlit after cleanup");
}
else throw new ArgumentException("Expected without or with mode.");

BObject.DestroyImmediate(graph);
Console.WriteLine($"SHADER_GRAPH_LIGHTING_BOUNDARY_OK|{mode}");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
