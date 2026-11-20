using System.Runtime.InteropServices;
using System.Text.Json;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Rendering.Rhi;
using Veldrid.SPIRV;

var binaries = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}
NativeLibrary.SetDllImportResolver(typeof(SpirvCompilation).Assembly, (name, _, _) =>
    name.Contains("veldrid-spirv", StringComparison.Ordinal) ? NativeLibrary.Load(Path.Combine(binaries, "libveldrid-spirv.dll")) : 0);
DotNetEditorPlatform.Initialize();
Check(typeof(Veldrid.ShaderStages).Assembly.GetName().Version == new Version(4, 9, 0, 0), "Independent editor tool uses reviewed Veldrid 4.9");
var vertex = new ShaderBuildSource(ShaderBuildStage.Vertex, ShaderBuildLanguage.Glsl,
    "#version 450\nlayout(location=0) in vec2 position; void main() { gl_Position=vec4(position,0,1); }");
var fragment = new ShaderBuildSource(ShaderBuildStage.Fragment, ShaderBuildLanguage.Glsl,
    "#version 450\nlayout(location=0) out vec4 color; void main() { color=vec4(1); }");
foreach (var source in new[] { vertex, fragment })
{
    var bytes = ShaderBuildPipeline.Compile(source, GraphicsBackend.Vulkan);
    Check(bytes.Length > 20 && BitConverter.ToUInt32(bytes) == 0x07230203, "Native SPIR-V compilation: " + source.Stage);
}
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
Throws<OperationCanceledException>(() => ShaderBuildPipeline.Compile(vertex, GraphicsBackend.Vulkan, cancelled.Token), "Cancelled shader build remains cancelled");
Throws<InvalidDataException>(() => ShaderBuildPipeline.Compile(vertex with { EntryPoint = "unsupported" }, GraphicsBackend.Vulkan), "Unsupported compiler input remains rejected");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_BOOTSTRAP_SHADER_OK " + checks.Count);
