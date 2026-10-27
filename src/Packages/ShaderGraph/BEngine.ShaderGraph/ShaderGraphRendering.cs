using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ShaderGraph;

internal static class ShaderGraphRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Package renderer registration.")]
    internal static void Register() => SceneRenderContributor2DRegistry.Register("bengine.shadergraph.2d", device => new ShaderGraphRendering(device));
}

internal sealed class ShaderGraphRendering(IGraphicsDevice device) : ISceneRenderContributor2D
{
    private readonly SceneTextureCache _textures = new(device);
    private readonly Dictionary<(ShaderGraphAsset Graph, string? Preview, int Lights, int Cookies), Pipeline> _pipelines = [];
    private readonly Dictionary<(ShaderGraphAsset Graph, string? Preview, int Lights, int Cookies), (int Hash, string Error)> _failed = [];
    private readonly Dictionary<Scene, LightingShaderContext2D> _lighting = new(ReferenceEqualityComparer.Instance);
    private readonly List<float> _vertices = [];
    private IGraphicsMesh? _mesh;
    private GraphicsMeshUploadCache? _uploads;
    private readonly Dictionary<ShaderGraphAsset, int> _frameHashes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Color> _parameterValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShaderTextureValue> _textureValues = new(StringComparer.Ordinal);
    private IGraphicsTexture2D? _white;
    public string? LastError { get; private set; }
    public string packageId => "com.bengine.shader-graph";
    public bool processesQuads => false;
    public void BeginFrame()
    {
        _frameHashes.Clear();
        _textures.PruneDestroyedRuntimeTextures();
        foreach (var key in _pipelines.Keys.Where(key => key.Graph.isDestroyed).ToArray())
        { Retire(_pipelines[key].Program); _pipelines.Remove(key); }
        foreach (var key in _failed.Keys.Where(key => key.Graph.isDestroyed).ToArray()) _failed.Remove(key);
        foreach (var scene in _lighting.Keys.Where(scene => !scene.isLoaded).ToArray())
        { _lighting[scene].Dispose(); _lighting.Remove(scene); }
        foreach (var context in _lighting.Values) context.BeginFrame();
    }
    public void BeginScene(Scene scene)
    {
        if (!RuntimePackageState.IsEnabled("com.bengine.lighting2d")) return;
        if (!_lighting.TryGetValue(scene, out var context)) _lighting.Add(scene, context = new(device));
        context.BeginScene(scene);
    }
    public void EndFrame() { foreach (var context in _lighting.Values) context.EndFrame(); }
    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder) => submissionOrder;
    public bool CanRender(RenderBatch2D batch) => batch.BatchKey.Shader == "BEngine/ShaderGraph";

    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport)
    {
        if (batch.SubmissionSpan[0].Payload is not SceneQuadPayload2D { Value.EffectData: ShaderGraphRenderer2D first } payload) return;
        if (batch.BatchKey.Material != first.Id)
            Draw(first, payload.Value, camera, width, height, batch: batch);
        else
            foreach (var submission in batch.SubmissionSpan)
                if (submission.Payload is SceneQuadPayload2D { Value.EffectData: ShaderGraphRenderer2D renderer } item)
                    Draw(renderer, item.Value, camera, width, height);
    }

    internal void Draw(ShaderGraphRenderer2D renderer, SceneQuad2D quad, RenderCamera camera, int width, int height,
        string? previewNodeId = null, RenderBatch2D? batch = null)
    {
        if (renderer.ResolveGraph() is not { isDestroyed: false } graph) return;
        LightingShaderParameters2D? lighting = null;
        var lightCapacity = 0;
        if (previewNodeId is null && renderer.receiveLighting && RuntimePackageState.IsEnabled("com.bengine.lighting2d") &&
            graph.surface == ShaderGraphSurface.Lit2D && renderer.gameObject.scene is { } scene)
        {
            if (!_lighting.TryGetValue(scene, out var context)) { BeginScene(scene); context = _lighting[scene]; }
            var output = graph.nodes?.FirstOrDefault(node => node?.kind == ShaderNodeKind.Output);
            var useNormal = output is not null && graph.connections?.Any(edge => edge?.destination == output.id && edge.input == "Normal") == true;
            lighting = context.Prepare(renderer.gameObject, quad, camera, width, height, useNormal);
            if (lighting.Values.Count > 4079) throw new InvalidOperationException("Shader Graph lighting exceeds the 64 KiB uniform budget.");
            lightCapacity = 32;
            while (lightCapacity < lighting.Values.Count) lightCapacity *= 2;
            lightCapacity = Math.Min(lightCapacity, 4079);
        }
        var key = (graph, previewNodeId, lightCapacity, lighting?.CookieTextures.Count ?? 0);
        if (!_frameHashes.TryGetValue(graph, out var hash)) _frameHashes.Add(graph, hash = graph.ContentHash());
        _pipelines.TryGetValue(key, out var pipeline);
        if ((pipeline is null || pipeline.Hash != hash) && (!_failed.TryGetValue(key, out var failedHash) || failedHash.Hash != hash))
        {
            try
            {
                var compiled = ShaderGraphCompiler.CompileSurface(graph, device.Backend, previewNodeId, lightCapacity, lighting?.CookieTextures.Count ?? 0);
                var replacement = new Pipeline(hash, compiled, device.CreateProgram(compiled.Program),
                    compiled.Parameters.Select((_, index) => $"uParameters[{index}]").ToArray(),
                    Enumerable.Range(0, compiled.LightingCapacity).Select(index => $"uLighting[{index}]").ToArray());
                if (pipeline is not null) Retire(pipeline.Program);
                _pipelines[key] = pipeline = replacement;
                _failed.Remove(key);
                LastError = null;
            }
            // Backend shader compilers may expose their own exception types. A bad user function must
            // remain a graph diagnostic, preserving the last working program and the editor session.
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LastError = $"Shader Graph '{graph.name}': {exception.Message}";
                _failed[key] = (hash, LastError);
                Debug.LogError(LastError);
            }
        }
        LastError = _failed.TryGetValue(key, out var failure) && failure.Hash == hash ? failure.Error : null;
        if (pipeline is null) return;
        _mesh ??= device.CreateMesh(new("ShaderGraph.Quad", ReadOnlyMemory<float>.Empty,
            PortableSceneRenderer.TexturedVertexLayout, GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
        _vertices.Clear();
        var projection = camera.CreateViewportTransform(width, height);
        if (batch is null) PortableSceneRenderer.AddTexturedQuad(_vertices, quad, projection);
        else foreach (var submission in batch.SubmissionSpan)
            if (submission.Payload is SceneQuadPayload2D item) PortableSceneRenderer.AddTexturedQuad(_vertices, item.Value, projection);
        _uploads ??= new GraphicsMeshUploadCache(_mesh);
        _uploads.Update(CollectionsMarshal.AsSpan(_vertices));
        var program = pipeline.Program;
        program.Bind(); program.SetFloat("uViewportWidth", width); program.SetFloat("uViewportHeight", height);
        program.SetFloat("uTime", (float)(Time.time * renderer.timeScale + renderer.timeOffset));
        program.SetInt("uTexture0", 0);
        var material = renderer.shaderMaterial is { isDestroyed: false } asset ? asset : null;
        _parameterValues.Clear(); _textureValues.Clear();
        foreach (var value in renderer.parameters) _parameterValues.TryAdd(value.name, value.value);
        foreach (var value in renderer.textures) _textureValues.TryAdd(value.name, value);
        if (material is not null)
        {
            foreach (var value in material.parameters) _parameterValues.TryAdd(value.name, value.value);
            foreach (var value in material.textures) _textureValues.TryAdd(value.name, value);
        }
        for (var index = 0; index < pipeline.Compilation.Parameters.Count; index++)
        {
            var parameter = pipeline.Compilation.Parameters[index];
            var value = _parameterValues.GetValueOrDefault(parameter.Name, parameter.DefaultValue);
            program.SetVector4(pipeline.ParameterUniforms[index], new((float)value.r, (float)value.g, (float)value.b, (float)value.a));
        }
        device.BindTexture(0, quad.IsTextured ? _textures.Resolve(quad.Texture) : White());
        foreach (var parameter in pipeline.Compilation.Textures)
        {
            // A present override with a null texture explicitly selects white, rather than falling through.
            var item = _textureValues.GetValueOrDefault(parameter.Name);
            var texture = item is not null ? item.texture : parameter.DefaultTexture;
            program.SetInt("uTexture" + parameter.Slot, parameter.Slot);
            device.BindTexture(parameter.Slot, texture is { isDestroyed: false } ? _textures.Resolve(texture.assetPath) : White());
        }
        if (pipeline.Compilation.LightingCapacity > 0 && lighting is not null)
        {
            for (var index = 0; index < lighting.Values.Count; index++) program.SetVector4(pipeline.LightingUniforms[index], lighting.Values[index]);
            for (var index = 0; index < pipeline.Compilation.CookieCount; index++)
            {
                var slot = pipeline.Compilation.CookieFirstSlot + index;
                program.SetInt("uTexture" + slot, slot);
                device.BindTexture(slot, _textures.Resolve(lighting.CookieTextures[index]));
            }
        }
        device.Draw(_mesh);
    }

    private IGraphicsTexture2D White() => _white ??= device.CreateTexture2D(
        "ShaderGraph.White", new(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled), [255, 255, 255, 255]);

    private void Retire(IDisposable resource)
    {
        if (device is IGraphicsResourceRetirement retirement) retirement.RetireResource(resource);
        else resource.Dispose();
    }

    public void Dispose()
    {
        foreach (var pipeline in _pipelines.Values) Retire(pipeline.Program);
        _pipelines.Clear(); _failed.Clear(); _frameHashes.Clear(); _uploads?.Dispose();
        if (_mesh is not null) Retire(_mesh);
        if (_white is not null) Retire(_white);
        _textures.Dispose();
        foreach (var context in _lighting.Values) context.Dispose();
        _lighting.Clear();
    }
    private sealed record Pipeline(int Hash, CompiledShaderGraph Compilation, IGraphicsProgram Program, string[] ParameterUniforms, string[] LightingUniforms);
}

/// <summary>Draws an isolated node using the same GPU compiler, uniforms and texture binding as scene rendering.</summary>
public sealed class ShaderGraphPreviewRenderer : IDisposable
{
    private readonly ShaderGraphRendering _rendering;
    private readonly Scene _scene = new("Shader Graph preview");
    private readonly ShaderGraphRenderer2D _renderer;
    private readonly ShaderGraphAsset _background = new()
    {
        nodes = [new() { id = "background", kind = ShaderNodeKind.Color, value = Color.black }, new() { id = "out", kind = ShaderNodeKind.Output }],
        connections = [new() { source = "background", destination = "out", input = "Color" }]
    };
    public string? Error => _rendering.LastError;

    public ShaderGraphPreviewRenderer(IGraphicsDevice device)
    {
        _rendering = new(device);
        _renderer = _scene.CreateGameObject("Preview").AddComponent<ShaderGraphRenderer2D>();
    }

    public void Render(ShaderGraphAsset graph, string? nodeId, int width, int height, Fix64 time,
        ShaderGraphMaterial? material = null, Texture? spriteTexture = null)
    {
        _renderer.shaderMaterial = material;
        _renderer.timeScale = 0; _renderer.timeOffset = time;
        var aspect = (Fix64)width / Math.Max(1, height);
        var quad = SceneQuad2D.Create(Vector2.zero, 0, new(2 * aspect, 2), Color.white,
            spriteTexture?.assetPath ?? string.Empty, new(0, 0, 1, 1), false, false, spriteTexture is { isDestroyed: false });
        _rendering.BeginFrame();
        _rendering.BeginScene(_scene);
        _renderer.graph = _background;
        _rendering.Draw(_renderer, quad, new(Vector2.zero, 0, 1, new(0, 0, 0, 1)), width, height);
        _renderer.graph = graph;
        _rendering.Draw(_renderer, quad, new(Vector2.zero, 0, 1, new(0, 0, 0, 1)), width, height, nodeId);
        _rendering.EndFrame();
    }

    public void Dispose() { _rendering.Dispose(); _scene.Dispose(); BObject.DestroyImmediate(_background); }
}
