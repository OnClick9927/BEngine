using BEngine.Rendering.Rhi;
using System.Globalization;
using System.Runtime.InteropServices;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

using Quad = BEngine.Rendering.SceneQuad2D;
using QuadPayload = BEngine.Rendering.SceneQuadPayload2D;

namespace BEngine.Rendering;

internal sealed partial class LightingRenderContributor2D : ISceneRenderContributor2D
{
    private static readonly string[] CookieUniformNames =
        ["uCookie0", "uCookie1", "uCookie2", "uCookie3", "uCookie4", "uCookie5"];
    private readonly IGraphicsDevice _device;
    private readonly SceneTextureCache _textures;
    private IReadOnlyList<Light2D> _lights = Array.Empty<Light2D>();
    private IReadOnlyList<ShadowCaster2D> _shadowCasters = Array.Empty<ShadowCaster2D>();
    private IReadOnlyList<LightingEnvironment2D> _lightingEnvironments = Array.Empty<LightingEnvironment2D>();
    private readonly List<float> _texturedVertices = [];
    private SceneRenderContext2D? _viewContext;
    private IGraphicsProgram? _texturedProgram;
    private IGraphicsMesh? _texturedMesh;
    public string packageId => "com.bengine.lighting2d";
    public LightingRenderContributor2D(IGraphicsDevice device) { _device = device; _textures = new SceneTextureCache(device); }
    public void BeginFrame()
    {
        _textures.PruneDestroyedRuntimeTextures();
        _lightingDataCount = 0;
    }
    public void BeginScene(Scene scene)
    {
        _viewContext = null;
        // QueryComponents returns the scene's cached read-only snapshots;
        // avoid copying them into temporary lists on every rendered scene.
        _lights = scene.QueryComponents<Light2D>();
        _shadowCasters = scene.QueryComponents<ShadowCaster2D>();
        _lightingEnvironments = scene.QueryComponents<LightingEnvironment2D>();
        BeginSceneLighting();
    }
    public void BeginScene(in SceneRenderContext2D context)
    {
        BeginScene(context.Scene);
        _viewContext = context;
    }
    public SceneQuad2D ProcessQuad(SceneQuad2D quad, GameObject target)
    {
        var lighting = GetLighting(target);
        if (lighting is not null && lighting.Values[0].W == 0 &&
            quad.ColorA.Equals(quad.ColorB) && quad.ColorA.Equals(quad.ColorC) && quad.ColorA.Equals(quad.ColorD))
        {
            // Uniform ambient illumination is constant over this surface. Fold
            // the fragment shader's exact multiply/clamp into the vertex tint
            // and retain the standard sprite batch. Gradient vertex colors
            // still use the lit shader, preserving its per-pixel clamp.
            var ambient = lighting.Values[0];
            var color = quad.ColorA;
            color = new Color((Fix64)Math.Clamp((float)color.r * ambient.X, 0, 1),
                (Fix64)Math.Clamp((float)color.g * ambient.Y, 0, 1),
                (Fix64)Math.Clamp((float)color.b * ambient.Z, 0, 1), color.a);
            return quad with { ColorA = color, ColorB = color, ColorC = color, ColorD = color, EffectData = null };
        }
        return quad with { EffectData = lighting };
    }
    public long Collect(Scene scene, RenderCamera camera, int width, int height, ICollection<RenderSubmission2D> submissions, long submissionOrder) => submissionOrder;
    public bool CanRender(RenderBatch2D batch)
    {
        using var submissionBudget = LoopExecutionGuard.AllowIterations(batch.SubmissionSpan.Length);
        foreach (var submission in batch.SubmissionSpan)
            if (submission.Payload is QuadPayload { Value.EffectData: LightingData }) return true;
        return false;
    }
    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport) => DrawLightingBatch(batch, camera, width, height);
    public void EndFrame()
    {
        _viewContext = null; _sceneLighting.Clear(); _shadowOwners.Clear(); RecyclePaths();
        _lights = Array.Empty<Light2D>();
        _shadowCasters = Array.Empty<ShadowCaster2D>();
        _lightingEnvironments = Array.Empty<LightingEnvironment2D>();
        using var dataBudget = LoopExecutionGuard.AllowIterations(_lightingDataCount);
        for (var index = 0; index < _lightingDataCount; index++)
        {
            var data = _lightingData[index];
            data.Surface = null; data.Cookies.Clear(); data.Values.Clear();
        }
        _lightingDataCount = 0;
    }
    public void Dispose() { EndFrame(); DisposeLighting(); _textures.Dispose(); _texturedMesh?.Dispose(); _texturedProgram?.Dispose(); }

    private readonly Dictionary<(ulong Layer, GameObject? Self, NormalSurfaceKey? Surface), LightingData?> _sceneLighting = [];
    private readonly HashSet<GameObject> _shadowOwners = new(ReferenceEqualityComparer.Instance);
    private readonly List<LightingData> _lightingData = [];
    private readonly Dictionary<ShadowCaster2D, List<Vector2>> _worldShadowPaths = [];
    private readonly Stack<List<Vector2>> _pathPool = [];
    private readonly Dictionary<int, LightingPipeline> _lightingPipelines = [];
    private int _lightingDataCount;
    private IGraphicsTexture2D? _lightingWhiteTexture;

    private void BeginSceneLighting()
    {
        _sceneLighting.Clear();
        _shadowOwners.Clear();
        RecyclePaths();
        using var sceneBudget = LoopExecutionGuard.AllowIterations(Math.Max(_lights.Count, _shadowCasters.Count));
        for (var lightIndex = 0; lightIndex < _lights.Count; lightIndex++)
        {
            var light = _lights[lightIndex];
            if (!light.enabled || !light.gameObject.activeInHierarchy) continue;
            light.ValidateShape();
            if (light.lightType != Light2DType.Freeform) continue;
            var points = RentPath(); light.FillWorldShape(points); _lightShapes.Add(light, points);
        }
        for (var casterIndex = 0; casterIndex < _shadowCasters.Count; casterIndex++)
        {
            var caster = _shadowCasters[casterIndex];
            if (caster.enabled && caster.castsShadows && caster.gameObject.activeInHierarchy)
            {
                _shadowOwners.Add(caster.gameObject);
                using var pathBudget = LoopExecutionGuard.AllowIterations(caster.shapePath.Count);
                var path = RentPath(); caster.FillWorldPath(path); _worldShadowPaths.Add(caster, path);
            }
        }
    }

    private List<Vector2> RentPath() => _pathPool.TryPop(out var path) ? path : [];
    private void RecyclePaths()
    {
        using var pathBudget = LoopExecutionGuard.AllowIterations(Math.Max(_lightShapes.Count, _worldShadowPaths.Count));
        foreach (var path in _lightShapes.Values) Recycle(path);
        foreach (var path in _worldShadowPaths.Values) Recycle(path);
        _lightShapes.Clear(); _worldShadowPaths.Clear();
        void Recycle(List<Vector2> path)
        { path.Clear(); if (_pathPool.Count < 256 && path.Capacity <= 4096) _pathPool.Push(path); }
    }

    private LightingData? GetLighting(GameObject target)
    {
        if (_lights.Count == 0 && _lightingEnvironments.Count == 0) return null;
        var surface = target.GetComponent<NormalMap2D>() is { enabled: true, texture: { isDestroyed: false } } receiver ? receiver : null;
        NormalSurfaceKey? surfaceKey = surface is null ? null : new(surface.texture!, surface.strength, surface.uvScale, surface.uvOffset);
        var key = (target.layer, _shadowOwners.Contains(target) ? target : null, surfaceKey);
        if (_sceneLighting.TryGetValue(key, out var cached)) return cached;
        if (_lightingDataCount == _lightingData.Count) _lightingData.Add(new LightingData());
        var data = _lightingData[_lightingDataCount++];
        var values = data.Values;
        values.Clear();
        data.Cookies.Clear(); data.Surface = surface;
        // Ambient/count, viewport-to-world origin/X axis, and Y axis precede variable-sized light records.
        values.Add(default);
        values.Add(default);
        values.Add(default);
        values.Add(default); // Normal UV scale/offset.
        values.Add(default); // Tangent axes.
        values.Add(default); // Normal strength/enabled.
        var ambient = NVector3.Zero;
        var hasLighting = false;
        var lightCount = 0;
        using var sceneBudget = LoopExecutionGuard.AllowIterations(
            Math.Max(_lightingEnvironments.Count, Math.Max(_lights.Count, _shadowCasters.Count)));
        for (var environmentIndex = 0; environmentIndex < _lightingEnvironments.Count; environmentIndex++)
        {
            var environment = _lightingEnvironments[environmentIndex];
            if (!environment.enabled || !environment.gameObject.activeInHierarchy) continue;
            hasLighting = true;
            ambient += Rgb(environment.ambientColor) * (float)environment.ambientIntensity;
        }
        for (var lightIndex = 0; lightIndex < _lights.Count; lightIndex++)
        {
            var light = _lights[lightIndex];
            if (!light.enabled || !light.gameObject.activeInHierarchy || !light.targetLayers.Contains(target.layer)) continue;
            hasLighting = true;
            if (light.intensity <= Fix64.Zero) continue;
            if (light.lightType == Light2DType.Global)
            {
                ambient += Rgb(light.color) * (float)light.intensity;
                continue;
            }
            var position = light.transform.position;
            // Keep hasLighting true even when all local lights are culled: an unlit
            // receiver would otherwise become white instead of preserving darkness.
            if (_viewContext is { } context && light.lightType is Light2DType.Point or Light2DType.Spot)
            {
                var margin = Fix64.Max(Fix64.Zero, context.Camera.Size) * 2 / Math.Max(1, context.Height);
                var diameter = Fix64.Max(Fix64.Zero, light.radius + margin) * 2;
                if (!context.Camera.IsVisible(position, 0, new(diameter, diameter), context.Width, context.Height)) continue;
            }
            lightCount++;
            var direction = Transform.RotateVector(Vector2.right, light.transform.rotation);
            values.Add(new NVector4((float)position.x, (float)position.y, (float)light.radius,
                (float)Fix64.Min(light.innerRadius, light.radius)));
            values.Add(new NVector4(Rgb(light.color), (float)light.intensity));
            values.Add(new NVector4((float)direction.x, (float)direction.y,
                (float)Fix64.Cos(light.outerAngle * Fix64.Half * Fix64.Deg2Rad),
                (float)Fix64.Cos(Fix64.Min(light.innerAngle, light.outerAngle) * Fix64.Half * Fix64.Deg2Rad)));
            var shadowIndex = values.Count;
            values.Add(default);
            AppendLightSurface(data, light);
            var firstShadow = values.Count;
            if (light.castShadows && light.shadowStrength > Fix64.Zero)
                for (var casterIndex = 0; casterIndex < _shadowCasters.Count; casterIndex++)
                {
                    var caster = _shadowCasters[casterIndex];
                    if (!caster.enabled || !caster.castsShadows || !caster.gameObject.activeInHierarchy ||
                        ReferenceEquals(caster.gameObject, target) ||
                        !light.shadowCasterLayers.Contains(caster.gameObject.layer) ||
                        !caster.layerMask.Contains(light.gameObject.layer)) continue;
                    var shadowPath = _worldShadowPaths[caster];
                    if (shadowPath.Count < 2) continue;
                    using var pathBudget = LoopExecutionGuard.AllowIterations(shadowPath.Count);
                    for (var index = 0; index < shadowPath.Count; index++)
                    {
                        var a = shadowPath[index];
                        var b = shadowPath[(index + 1) % shadowPath.Count];
                        values.Add(new NVector4((float)a.x, (float)a.y, (float)b.x, (float)b.y));
                    }
                }
            var shadowCount = values.Count - firstShadow;
            var shapeCount = 0;
            if (_lightShapes.TryGetValue(light, out var points))
            {
                shapeCount = points.Count;
                using var shapeBudget = LoopExecutionGuard.AllowIterations(points.Count);
                for (var index = 0; index < points.Count; index++)
                {
                    var a = points[index]; var b = points[(index + 1) % points.Count];
                    values.Add(new((float)a.x, (float)a.y, (float)b.x, (float)b.y));
                }
            }
            values[shadowIndex] = new NVector4((float)light.lightType, (float)light.shadowStrength, shadowCount, shapeCount);
        }
        values[0] = new NVector4(ambient, lightCount);
        var result = hasLighting ? data : null;
        _sceneLighting.Add(key, result);
        return result;
    }

    private void DrawLightingBatch(RenderBatch2D batch, RenderCamera camera, int width, int height)
    {
        // Preserve transparency order when a material batch mixes lit, unlit, and self-shadow-excluded objects.
        _texturedVertices.Clear();
        var first = default(Quad);
        using var submissionBudget = LoopExecutionGuard.AllowIterations(batch.SubmissionSpan.Length);
        foreach (var submission in batch.SubmissionSpan)
        {
            if (submission.Payload is not QuadPayload payload) continue;
            var quad = payload.Value;
            if (_texturedVertices.Count > 0 &&
                (!ReferenceEquals(first.EffectData, quad.EffectData) || first.IsTextured != quad.IsTextured ||
                 first.Texture != quad.Texture || !SameNormalBasis(first, quad)))
            {
                FlushLightingRun(first, camera, width, height);
                _texturedVertices.Clear();
            }
            if (_texturedVertices.Count == 0) first = quad;
            PortableSceneRenderer.AddTexturedQuad(_texturedVertices, quad, camera, width, height);
        }
        if (_texturedVertices.Count > 0) FlushLightingRun(first, camera, width, height);
    }

    private void FlushLightingRun(Quad first, RenderCamera camera, int width, int height)
    {
        if (first.EffectData is not LightingData data)
        {
            EnsureTexturedPipeline();
            _texturedMesh!.Update(CollectionsMarshal.AsSpan(_texturedVertices));
            _texturedProgram!.Bind();
            _texturedProgram.SetFloat("uViewportWidth", width);
            _texturedProgram.SetFloat("uViewportHeight", height);
            _texturedProgram.SetInt("uTexture", 0);
            _device.BindTexture(0, ResolveLightingTexture(first));
            _device.Draw(_texturedMesh);
            return;
        }
        var pipeline = GetLightingPipeline(data.Values.Count);
        SetLightingView(data, camera, width, height);
        SetNormalSurface(data, first);
        pipeline.Uploads.Update(CollectionsMarshal.AsSpan(_texturedVertices));
        pipeline.Program.Bind();
        pipeline.Program.SetFloat("uViewportWidth", width);
        pipeline.Program.SetFloat("uViewportHeight", height);
        pipeline.Program.SetInt("uTexture", 0);
        pipeline.Program.SetInt("uNormal", 1);
        for (var index = 0; index < CookieUniformNames.Length; index++)
            pipeline.Program.SetInt(CookieUniformNames[index], index + 2);
        pipeline.Program.SetVector4Array("uLighting[0]", CollectionsMarshal.AsSpan(data.Values));
        _device.BindTexture(0, ResolveLightingTexture(first));
        BindSurfaceTextures(data);
        _device.Draw(pipeline.Mesh);
    }

    private static void SetLightingView(LightingData data, RenderCamera camera, int width, int height)
    {
        var unitsPerPixel = (Fix64)(1f / camera.PixelsPerUnit(height));
        var axisX = Numerics.ToNumerics(Transform.RotateVector(new Vector2(unitsPerPixel, 0), camera.Rotation));
        var axisY = Numerics.ToNumerics(Transform.RotateVector(new Vector2(0, -unitsPerPixel), camera.Rotation));
        var centerX = camera.AlignViewportCenter ? Math.Max(1, width) / 2 : Math.Max(1, width) * 0.5f;
        var centerY = camera.AlignViewportCenter ? Math.Max(1, height) / 2 : Math.Max(1, height) * 0.5f;
        var origin = Numerics.ToNumerics(camera.Position) - axisX * centerX - axisY * centerY;
        data.Values[1] = new NVector4(origin.X, origin.Y, axisX.X, axisX.Y);
        data.Values[2] = new NVector4(axisY.X, axisY.Y, 0, 0);
    }

    private IGraphicsTexture2D ResolveLightingTexture(Quad quad) => quad.IsTextured
        ? _textures.Resolve(quad.Texture)
        : _lightingWhiteTexture ??= _device.CreateTexture2D("BEngine.Scene2D.Lighting.White",
            new GraphicsTextureDescription(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled),
            [255, 255, 255, 255]);

    private LightingPipeline GetLightingPipeline(int requiredVectors)
    {
        if (requiredVectors > 4095) throw new InvalidOperationException("2D lighting exceeds the 64 KiB uniform budget; reduce light or shadow outline complexity.");
        var capacity = 32;
        while (capacity < requiredVectors) capacity = checked(capacity * 2);
        capacity = Math.Min(capacity, 4095);
        if (_lightingPipelines.TryGetValue(capacity, out var cached)) return cached;
        _device.Capabilities.Require(GraphicsDeviceFeatures.SampledTextures);
        var sources = LightingShaderSources.Create(_device.Backend, capacity);
        var vertex = sources.Vertex.Code; var fragment = sources.Fragment.Code;
        var bindings = new GraphicsUniformBinding[capacity + 2];
        bindings[0] = new("uViewportWidth", 0, 1);
        bindings[1] = new("uViewportHeight", 4, 1);
        var names = new string[capacity];
        using var uniformBudget = LoopExecutionGuard.AllowIterations(capacity);
        for (var index = 0; index < capacity; index++)
        {
            names[index] = "uLighting[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            bindings[index + 2] = new(names[index], 16 + index * 16, 4);
        }
        var program = _device.CreateProgram(new GraphicsShaderProgramDescription("BEngine.Scene2D.Lighting.Texture",
            new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, vertex),
            new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, fragment))
        {
            UniformBufferSizeBytes = 16 + capacity * 16,
            SampledTextureCount = 8,
            Uniforms = bindings
        });
        try
        {
            var mesh = _device.CreateMesh(new GraphicsMeshDescription("BEngine.Scene2D.Lighting.Triangles",
                ReadOnlyMemory<float>.Empty, PortableSceneRenderer.TexturedVertexLayout, GraphicsPrimitiveTopology.TriangleList,
                GraphicsBufferUsage.Dynamic));
            var pipeline = new LightingPipeline(program, mesh, names, new GraphicsMeshUploadCache(mesh));
            _lightingPipelines.Add(capacity, pipeline);
            return pipeline;
        }
        catch
        {
            program.Dispose();
            throw;
        }
    }

    private void DisposeLighting()
    {
        using var pipelineBudget = LoopExecutionGuard.AllowIterations(_lightingPipelines.Count);
        foreach (var pipeline in _lightingPipelines.Values)
        {
            pipeline.Uploads.Dispose(); pipeline.Mesh.Dispose();
            pipeline.Program.Dispose();
        }
        _lightingPipelines.Clear();
        _lightingWhiteTexture?.Dispose();
        _lightingData.Clear();
        _sceneLighting.Clear();
        _shadowOwners.Clear();
        RecyclePaths();
        _pathPool.Clear();
    }

    private static NVector3 Rgb(Color color) => new((float)color.r, (float)color.g, (float)color.b);

    private sealed class LightingData
    {
        internal readonly List<NVector4> Values = [];
        internal readonly List<string> Cookies = [];
        internal NormalMap2D? Surface;
    }

    private sealed record LightingPipeline(IGraphicsProgram Program, IGraphicsMesh Mesh, string[] UniformNames, GraphicsMeshUploadCache Uploads);
    private readonly record struct NormalSurfaceKey(Texture Texture, Fix64 Strength, Vector2 Scale, Vector2 Offset);
    private void EnsureTexturedPipeline()
    {
        if (_texturedProgram is not null) return;
        _device.Capabilities.Require(GraphicsDeviceFeatures.SampledTextures);
        var (vertex, fragment) = ResolveTexturedShaders(_device);
        _texturedProgram = _device.CreateProgram(new GraphicsShaderProgramDescription(
            "BEngine.Scene2D.Texture", vertex, fragment));
        _texturedMesh = _device.CreateMesh(new GraphicsMeshDescription(
            "BEngine.Scene2D.TexturedTriangles", ReadOnlyMemory<float>.Empty, PortableSceneRenderer.TexturedVertexLayout,
            GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
    }

    private static (GraphicsShaderSource Vertex, GraphicsShaderSource Fragment) ResolveTexturedShaders(
        IGraphicsDevice device)
    {
        if (device.Backend == GraphicsBackend.Vulkan)
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.vulkan.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.vulkan.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Glsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.opengl.vert.glsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.opengl.frag.glsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Hlsl))
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.direct3d.vert.hlsl")),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Hlsl,
                    DefaultShaderResources.Load("Shaders/PortableScene/Textured.direct3d.frag.hlsl")));
        if (device.Capabilities.Supports(GraphicsShaderLanguage.Wgsl))
        {
            var shader = DefaultShaderResources.Load("Shaders/PortableScene/Textured.webgpu.wgsl");
            return (
                new GraphicsShaderSource(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Wgsl, shader, "vs_main"),
                new GraphicsShaderSource(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Wgsl, shader, "fs_main"));
        }
        throw new NotSupportedException($"The 2D scene renderer has no textured shader for {device.Backend}.");
    }

}
