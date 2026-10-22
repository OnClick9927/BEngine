using System.Globalization;
using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine;

internal sealed partial class SpriteSkinRenderContributor2D
{
    private readonly Dictionary<int, SkinPipeline> _gpuPipelines = [];
    private static readonly GraphicsVertexLayout SkinVertexLayout = new(16 * sizeof(float),
    [
        new(0, 2, 0), new(1, 4, 2 * sizeof(float)), new(2, 2, 6 * sizeof(float)),
        new(3, 4, 8 * sizeof(float)), new(4, 4, 12 * sizeof(float))
    ]);

    private void RenderGpu(SpriteSkinRenderPayload payload, RenderCamera camera, int width, int height, GraphicsRect viewport)
    {
        var bones = payload.Bones!;
        var capacity = 1; while (capacity < bones.Length) capacity *= 2;
        var pipeline = GetGpuPipeline(capacity);
        _vertices.Clear();
        foreach (var index in payload.Triangles)
        {
            var vertex = payload.Vertices[index]; var uv = payload.Uv[index];
            _vertices.Add((float)vertex.x); _vertices.Add((float)vertex.y);
            _vertices.Add((float)payload.Color.r); _vertices.Add((float)payload.Color.g);
            _vertices.Add((float)payload.Color.b); _vertices.Add((float)payload.Color.a);
            _vertices.Add((float)uv.x); _vertices.Add((float)uv.y);
            var weights = payload.Weights[index];
            for (var slot = 0; slot < 4; slot++) _vertices.Add(slot < weights.Length ? weights[slot].BoneIndex : 0);
            for (var slot = 0; slot < 4; slot++) _vertices.Add(slot < weights.Length ? (float)weights[slot].Weight : 0);
        }
        pipeline.Uploads.Update(CollectionsMarshal.AsSpan(_vertices));
        _device.SetViewport(viewport); _device.SetScissor(new(0, 0, viewport.Width, viewport.Height));
        _device.SetDepthState(GraphicsDepthState.Disabled); _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        _device.SetRasterizerState(GraphicsRasterizerState.CullBackFaces);
        pipeline.Program.Bind();
        for (var index = 0; index < bones.Length; index++)
        {
            var bone = bones[index];
            var origin = camera.WorldToViewport(bone.Origin, width, height);
            var x = camera.WorldToViewport(bone.Origin + bone.XAxis, width, height) - origin;
            var y = camera.WorldToViewport(bone.Origin + bone.YAxis, width, height) - origin;
            pipeline.Program.SetVector4(pipeline.UniformNames[index * 2], new NVector4(x.X * 2 / width, y.X * 2 / width, origin.X * 2 / width - 1, 0));
            pipeline.Program.SetVector4(pipeline.UniformNames[index * 2 + 1], new NVector4(-x.Y * 2 / height, -y.Y * 2 / height, 1 - origin.Y * 2 / height, 0));
        }
        pipeline.Program.SetInt("uTexture", 0);
        _device.BindTexture(0, _textures.Resolve(payload.Texture)); _device.Draw(pipeline.Mesh); _device.SetScissor(null);
    }

    private SkinPipeline GetGpuPipeline(int capacity)
    {
        if (_gpuPipelines.TryGetValue(capacity, out var pipeline)) return pipeline;
        var sources = SpriteSkinShaderSources.Create(_device.Backend, capacity);
        var vertex = sources.Vertex.Code; var fragment = sources.Fragment.Code;
        var names = Enumerable.Range(0, capacity * 2).Select(index => "uBones[" + index.ToString(CultureInfo.InvariantCulture) + "]").ToArray();
        var bindings = names.Select((name, index) => new GraphicsUniformBinding(name, 16 + index * 16, 4)).ToArray();
        var program = _device.CreateProgram(new("BEngine.SpriteSkinning2D.Gpu." + capacity,
            new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, vertex), new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, fragment))
        { UniformBufferSizeBytes = 16 + capacity * 32, Uniforms = bindings });
        try
        {
            var mesh = _device.CreateMesh(new("BEngine.SpriteSkinning2D.GpuMesh", ReadOnlyMemory<float>.Empty,
                SkinVertexLayout, GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
            pipeline = new(program, mesh, names, new GraphicsMeshUploadCache(mesh)); _gpuPipelines.Add(capacity, pipeline); return pipeline;
        }
        catch { program.Dispose(); throw; }
    }

    private sealed record SkinPipeline(IGraphicsProgram Program, IGraphicsMesh Mesh, string[] UniformNames, GraphicsMeshUploadCache Uploads);
}
