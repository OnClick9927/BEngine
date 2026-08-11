using Silk.NET.OpenGL;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using BEngine.Terrain;
using TerrainComponent = BEngine.Terrain.Terrain;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed class EngineRenderer : IDisposable
{
    private const int MaximumLights = 8;
    private const int MaximumGiEmitters = 8;

    private const string VertexShader = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        uniform mat4 uModel;
        uniform mat4 uViewProjection;
        uniform mat4 uLightViewProjection;
        out vec3 vNormal;
        out vec3 vWorldPosition;
        out vec4 vShadowPosition;
        void main()
        {
            vec4 world = uModel * vec4(aPosition, 1.0);
            vWorldPosition = world.xyz;
            vNormal = mat3(transpose(inverse(uModel))) * aNormal;
            vShadowPosition = uLightViewProjection * world;
            gl_Position = uViewProjection * world;
        }
        """;

    private const string FragmentShader = """
        #version 330 core
        #define MAX_LIGHTS 8
        #define MAX_GI_EMITTERS 8

        struct LightData
        {
            int type;
            vec3 position;
            vec3 direction;
            vec3 color;
            float intensity;
            float range;
            float innerCone;
            float outerCone;
        };

        struct GiEmitter
        {
            vec3 position;
            vec3 color;
            float range;
            float strength;
        };

        in vec3 vNormal;
        in vec3 vWorldPosition;
        in vec4 vShadowPosition;
        uniform vec4 uColor;
        uniform vec3 uEmissionColor;
        uniform float uEmissionIntensity;
        uniform float uMetallic;
        uniform float uSmoothness;
        uniform vec3 uCameraPosition;
        uniform int uLightCount;
        uniform LightData uLights[MAX_LIGHTS];
        uniform vec3 uAmbientSky;
        uniform vec3 uAmbientEquator;
        uniform vec3 uAmbientGround;
        uniform float uAmbientIntensity;
        uniform int uGiEmitterCount;
        uniform GiEmitter uGiEmitters[MAX_GI_EMITTERS];
        uniform float uIndirectIntensity;
        uniform int uReceiveGI;
        uniform sampler2D uShadowMap;
        uniform int uShadowsEnabled;
        uniform int uShadowLightIndex;
        uniform int uSoftShadows;
        uniform int uReceiveShadows;
        uniform float uShadowStrength;
        uniform float uShadowBias;
        out vec4 fragColor;

        float sampleShadow(vec3 normal, vec3 lightDirection)
        {
            if (uShadowsEnabled == 0 || uReceiveShadows == 0) return 0.0;
            vec3 projected = vShadowPosition.xyz / max(vShadowPosition.w, 0.00001);
            projected = projected * 0.5 + 0.5;
            if (projected.z <= 0.0 || projected.z >= 1.0 ||
                projected.x <= 0.0 || projected.x >= 1.0 ||
                projected.y <= 0.0 || projected.y >= 1.0) return 0.0;

            float bias = max(uShadowBias * (1.0 - dot(normal, lightDirection)), uShadowBias * 0.2);
            if (uSoftShadows == 0)
            {
                float depth = texture(uShadowMap, projected.xy).r;
                return (projected.z - bias > depth ? 1.0 : 0.0) * uShadowStrength;
            }
            vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
            float shadow = 0.0;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    float depth = texture(uShadowMap, projected.xy + vec2(x, y) * texel).r;
                    shadow += projected.z - bias > depth ? 1.0 : 0.0;
                }
            }
            return shadow / 9.0 * uShadowStrength;
        }

        vec3 evaluateIndirect(vec3 normal)
        {
            if (uReceiveGI == 0) return vec3(0.0);
            vec3 indirect = vec3(0.0);
            for (int i = 0; i < MAX_GI_EMITTERS; i++)
            {
                if (i >= uGiEmitterCount) break;
                vec3 delta = uGiEmitters[i].position - vWorldPosition;
                float distanceToEmitter = length(delta);
                float range = max(uGiEmitters[i].range, 0.001);
                float attenuation = pow(clamp(1.0 - distanceToEmitter / range, 0.0, 1.0), 2.0);
                vec3 direction = distanceToEmitter > 0.0001 ? delta / distanceToEmitter : normal;
                float facing = 0.25 + max(dot(normal, direction), 0.0) * 0.75;
                indirect += uGiEmitters[i].color * attenuation * facing * uGiEmitters[i].strength;
            }
            return indirect * uIndirectIntensity * 0.22;
        }

        void main()
        {
            vec3 normal = normalize(vNormal);
            vec3 viewDirection = normalize(uCameraPosition - vWorldPosition);
            vec3 albedo = max(uColor.rgb, vec3(0.0));
            vec3 dielectricSpecular = vec3(0.04);
            vec3 specularColor = mix(dielectricSpecular, albedo, clamp(uMetallic, 0.0, 1.0));
            vec3 direct = vec3(0.0);

            for (int i = 0; i < MAX_LIGHTS; i++)
            {
                if (i >= uLightCount) break;
                vec3 lightDirection;
                float attenuation = 1.0;
                if (uLights[i].type == 1)
                {
                    lightDirection = normalize(-uLights[i].direction);
                }
                else
                {
                    vec3 toLight = uLights[i].position - vWorldPosition;
                    float distanceToLight = length(toLight);
                    lightDirection = distanceToLight > 0.0001 ? toLight / distanceToLight : normal;
                    float normalizedDistance = distanceToLight / max(uLights[i].range, 0.001);
                    attenuation = pow(clamp(1.0 - normalizedDistance, 0.0, 1.0), 2.0);
                    attenuation /= 1.0 + distanceToLight * distanceToLight * 0.04;
                    if (uLights[i].type == 0)
                    {
                        float cone = dot(normalize(uLights[i].direction), -lightDirection);
                        attenuation *= smoothstep(uLights[i].outerCone, uLights[i].innerCone, cone);
                    }
                }

                float diffuse = max(dot(normal, lightDirection), 0.0);
                vec3 halfDirection = normalize(lightDirection + viewDirection);
                float shininess = mix(8.0, 256.0, clamp(uSmoothness, 0.0, 1.0));
                float specular = pow(max(dot(normal, halfDirection), 0.0), shininess) *
                    mix(0.08, 1.0, clamp(uSmoothness, 0.0, 1.0));
                vec3 radiance = uLights[i].color * uLights[i].intensity * attenuation;
                float shadow = i == uShadowLightIndex ? sampleShadow(normal, lightDirection) : 0.0;
                direct += (albedo * (1.0 - uMetallic) * diffuse + specularColor * specular) *
                    radiance * (1.0 - shadow);
            }

            float up = dot(normal, vec3(0.0, 1.0, 0.0));
            vec3 ambientColor = up >= 0.0
                ? mix(uAmbientEquator, uAmbientSky, up)
                : mix(uAmbientEquator, uAmbientGround, -up);
            vec3 ambient = albedo * ambientColor * uAmbientIntensity;
            vec3 indirect = albedo * evaluateIndirect(normal);
            vec3 emission = uEmissionColor * uEmissionIntensity;
            fragColor = vec4(max(ambient + indirect + direct + emission, vec3(0.0)), uColor.a);
        }
        """;

    private const string DepthVertexShader = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        uniform mat4 uModel;
        uniform mat4 uLightViewProjection;
        void main()
        {
            gl_Position = uLightViewProjection * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string DepthFragmentShader = """
        #version 330 core
        void main() { }
        """;

    private const string SkyboxVertexShader = """
        #version 330 core
        const vec2 vertices[3] = vec2[3](
            vec2(-1.0, -1.0),
            vec2( 3.0, -1.0),
            vec2(-1.0,  3.0)
        );
        out vec2 vNdc;
        void main()
        {
            vNdc = vertices[gl_VertexID];
            gl_Position = vec4(vNdc, 1.0, 1.0);
        }
        """;

    private const string SkyboxFragmentShader = """
        #version 330 core
        in vec2 vNdc;
        uniform vec3 uCameraForward;
        uniform vec3 uCameraRight;
        uniform vec3 uCameraUp;
        uniform vec3 uSunDirection;
        uniform vec4 uTopColor;
        uniform vec4 uHorizonColor;
        uniform vec4 uGroundColor;
        uniform float uAspect;
        uniform float uTanHalfFov;
        uniform float uExposure;
        uniform float uSunIntensity;
        uniform float uSunSize;
        uniform float uHorizonThickness;
        out vec4 fragColor;
        void main()
        {
            vec3 direction = normalize(
                uCameraForward +
                uCameraRight * (vNdc.x * uAspect * uTanHalfFov) +
                uCameraUp * (vNdc.y * uTanHalfFov));

            float zenith = pow(clamp((direction.y + 0.02) / 1.02, 0.0, 1.0), 0.55);
            vec3 sky = mix(uHorizonColor.rgb, uTopColor.rgb, zenith);
            float groundBlend = smoothstep(0.0, max(0.01, uHorizonThickness), -direction.y);
            vec3 color = mix(sky, uGroundColor.rgb, groundBlend);

            float sunDot = max(dot(direction, normalize(uSunDirection)), 0.0);
            float sunThreshold = mix(0.99998, 0.94, clamp(uSunSize, 0.0, 1.0));
            float sunDisk = smoothstep(sunThreshold, min(1.0, sunThreshold + 0.002), sunDot);
            float sunGlow = pow(sunDot, mix(512.0, 16.0, clamp(uSunSize, 0.0, 1.0))) * 0.22;
            color += (sunDisk + sunGlow) * uSunIntensity * vec3(1.0, 0.88, 0.68);
            fragColor = vec4(color * max(0.0, uExposure), 1.0);
        }
        """;

    private readonly GL _gl;
    private readonly IGraphicsDevice _graphicsDevice;
    private readonly bool _ownsGraphicsDevice;
    private readonly ShaderProgram _shader;
    private readonly ShaderProgram _depthShader;
    private readonly ShaderProgram _skyboxShader;
    private readonly UIElementsRenderer _uiElementsRenderer;
    private readonly uint _skyboxVertexArray;
    private readonly MeshBuffer _cube;
    private readonly MeshBuffer _plane;
    private readonly MeshBuffer _grid;
    private readonly MeshBuffer _cameraGizmo;
    private readonly Dictionary<Guid, TerrainMeshCache> _terrainMeshes = [];
    private uint _shadowFramebuffer;
    private uint _shadowTexture;
    private int _shadowResolution;

    public GraphicsBackend Backend => _graphicsDevice.Backend;
    public GraphicsDeviceCapabilities Capabilities => _graphicsDevice.Capabilities;

    public EngineRenderer(GL gl)
        : this(new OpenGlGraphicsDevice(gl), ownsGraphicsDevice: true)
    {
    }

    public EngineRenderer(IGraphicsDevice graphicsDevice, bool ownsGraphicsDevice = false)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        if (graphicsDevice is not OpenGlGraphicsDevice openGl)
        {
            if (ownsGraphicsDevice) graphicsDevice.Dispose();
            throw new NotSupportedException(
                $"The scene pipeline does not yet implement {graphicsDevice.Backend}. " +
                "VisualElement rendering can already use the portable RHI; scene shaders and materials still require the OpenGL provider.");
        }

        _graphicsDevice = graphicsDevice;
        _ownsGraphicsDevice = ownsGraphicsDevice;
        var gl = openGl.Api;
        _gl = gl;
        _shader = new ShaderProgram(gl, VertexShader, FragmentShader);
        _depthShader = new ShaderProgram(gl, DepthVertexShader, DepthFragmentShader);
        _skyboxShader = new ShaderProgram(gl, SkyboxVertexShader, SkyboxFragmentShader);
        _uiElementsRenderer = new UIElementsRenderer(graphicsDevice);
        _skyboxVertexArray = gl.GenVertexArray();
        _cube = new MeshBuffer(gl, Geometry.Cube);
        _plane = new MeshBuffer(gl, Geometry.Plane);
        _grid = new MeshBuffer(gl, Geometry.CreateGrid(20), (uint)GLEnum.Lines);
        _cameraGizmo = new MeshBuffer(gl, Geometry.CreateCameraGizmo(), (uint)GLEnum.Lines);
        gl.Enable(EnableCap.DepthTest);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public void Render(BEngine.Scene scene, RenderCamera camera, int width, int height,
        bool drawGrid = false, bool drawUi = true, bool drawTerrain = true, bool drawGizmos = false)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        var lighting = ResolveLighting(scene);
        var lights = ResolveLights(scene, lighting.MaximumRealtimeLights);
        var renderItems = ResolveRenderItems(scene);
        var shadow = RenderShadowMap(renderItems, lights, camera, lighting);

        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(camera.ClearColor.X, camera.ClearColor.Y, camera.ClearColor.Z, camera.ClearColor.W);
        var clearMask = camera.ClearFlags switch
        {
            BEngine.CameraClearFlags.Depth => ClearBufferMask.DepthBufferBit,
            BEngine.CameraClearFlags.Nothing => (ClearBufferMask)0,
            _ => ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit
        };
        if (clearMask != 0) _gl.Clear(clearMask);

        var forward = NVector3.Transform(NVector3.UnitZ, camera.Rotation);
        var up = NVector3.Transform(NVector3.UnitY, camera.Rotation);
        var right = NVector3.Transform(NVector3.UnitX, camera.Rotation);
        var sunDirection = lights.FirstOrDefault(light => light.Type == (int)BEngine.LightType.Directional).Direction;
        if (sunDirection.LengthSquared() < 0.0001f)
        {
            sunDirection = NVector3.Normalize(new NVector3(-0.4f, -1f, -0.25f));
        }
        if (camera.ClearFlags == BEngine.CameraClearFlags.Skybox && ResolveSkybox(scene) is { } skybox)
        {
            DrawSkybox(camera, width, height, forward, right, up, sunDirection, skybox);
        }

        var view = NMatrix4x4.CreateLookAt(camera.Position, camera.Position + forward, up);
        var projection = NMatrix4x4.CreatePerspectiveFieldOfView(
            Math.Clamp(camera.FieldOfView, 0.1f, MathF.PI - 0.1f),
            width / (float)height,
            Math.Max(0.001f, camera.NearPlane),
            Math.Max(camera.NearPlane + 0.001f, camera.FarPlane));

        _shader.Use();
        _shader.SetMatrix("uViewProjection", view * projection);
        _shader.SetMatrix("uLightViewProjection", shadow.LightViewProjection);
        _shader.SetVector("uCameraPosition", camera.Position);
        ApplyLightingUniforms(lights, lighting, shadow);

        if (drawGrid)
        {
            _shader.SetMatrix("uModel", NMatrix4x4.Identity);
            _shader.SetVector("uColor", new NVector4(0.23f, 0.26f, 0.29f, 0.72f));
            _shader.SetVector("uEmissionColor", NVector3.Zero);
            _shader.SetFloat("uEmissionIntensity", 0);
            _shader.SetFloat("uMetallic", 0);
            _shader.SetFloat("uSmoothness", 0);
            _shader.SetInt("uReceiveShadows", 0);
            _shader.SetInt("uReceiveGI", 0);
            _shader.SetInt("uGiEmitterCount", 0);
            _grid.Draw();
        }

        foreach (var item in renderItems)
        {
            _shader.SetMatrix("uModel", item.Model);
            _shader.SetVector("uColor", item.Color);
            _shader.SetVector("uEmissionColor", item.EmissionColor);
            _shader.SetFloat("uEmissionIntensity", item.EmissionIntensity);
            _shader.SetFloat("uMetallic", item.Metallic);
            _shader.SetFloat("uSmoothness", item.Smoothness);
            _shader.SetInt("uReceiveShadows", item.Renderer.receiveShadows ? 1 : 0);
            ApplyGiUniforms(item, renderItems, lights, lighting);
            item.Mesh.Draw();
        }

        if (drawGizmos) DrawCameraGizmos(scene);

        if (drawTerrain) DrawTerrains(scene);

        if (drawUi) _uiElementsRenderer.Render(scene, width, height);
    }

    public static RenderCamera ResolveGameCamera(BEngine.Scene scene)
    {
        return TryResolveGameCamera(scene, out var camera) ? camera : RenderCamera.Default;
    }

    public static bool TryResolveGameCamera(BEngine.Scene scene, out RenderCamera camera)
    {
        var cameras = scene.gameObjects
            .Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<BEngine.Camera>())
            .Where(item => item.enabled)
            .ToArray();
        var component = cameras.FirstOrDefault(item => item.isMain) ?? cameras.FirstOrDefault();
        if (component is null)
        {
            camera = default;
            return false;
        }

        camera = RenderCamera.From(component);
        return true;
    }

    public void Dispose()
    {
        ReleaseShadowMap();
        _uiElementsRenderer.Dispose();
        _gl.DeleteVertexArray(_skyboxVertexArray);
        _skyboxShader.Dispose();
        _depthShader.Dispose();
        _grid.Dispose();
        _cameraGizmo.Dispose();
        foreach (var entry in _terrainMeshes.Values) entry.Mesh.Dispose();
        _terrainMeshes.Clear();
        _plane.Dispose();
        _cube.Dispose();
        _shader.Dispose();
        if (_ownsGraphicsDevice) _graphicsDevice.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DrawCameraGizmos(BEngine.Scene scene)
    {
        _shader.SetVector("uColor", new NVector4(1f, 0.72f, 0.12f, 1f));
        _shader.SetVector("uEmissionColor", new NVector3(1f, 0.58f, 0.06f));
        _shader.SetFloat("uEmissionIntensity", 0.85f);
        _shader.SetFloat("uMetallic", 0);
        _shader.SetFloat("uSmoothness", 0);
        _shader.SetInt("uReceiveShadows", 0);
        _shader.SetInt("uReceiveGI", 0);
        _shader.SetInt("uGiEmitterCount", 0);
        foreach (var component in scene.gameObjects
                     .Where(item => item.activeInHierarchy)
                     .SelectMany(item => item.GetComponents<BEngine.Camera>())
                     .Where(item => item.enabled))
        {
            _shader.SetMatrix("uModel", Numerics.WorldMatrix(component.transform));
            _cameraGizmo.Draw();
        }
    }

    private MeshBuffer ResolveMesh(string mesh) => mesh.Equals("Plane", StringComparison.OrdinalIgnoreCase)
        ? _plane
        : _cube;

    private void DrawTerrains(BEngine.Scene scene)
    {
        foreach (var terrain in scene.gameObjects.Where(item => item.activeInHierarchy)
                     .SelectMany(item => item.GetComponents<TerrainComponent>())
                     .Where(item => item.enabled && item.drawHeightmap).OrderBy(item => item.Id))
        {
            var data = terrain.GetTerrainData();
            if (data is null) continue;
            if (!_terrainMeshes.TryGetValue(terrain.Id, out var cached) ||
                !ReferenceEquals(cached.Data, data) || cached.Version != data.version)
            {
                cached?.Mesh.Dispose();
                cached = new TerrainMeshCache(data, data.version,
                    new MeshBuffer(_gl, CreateTerrainVertices(data)));
                _terrainMeshes[terrain.Id] = cached;
            }
            _shader.SetMatrix("uModel", Numerics.WorldMatrix(terrain.transform));
            _shader.SetVector("uColor", Numerics.ToNumerics(terrain.materialColor));
            _shader.SetVector("uEmissionColor", NVector3.Zero);
            _shader.SetFloat("uEmissionIntensity", 0);
            _shader.SetFloat("uMetallic", 0);
            _shader.SetFloat("uSmoothness", 0.18f);
            _shader.SetInt("uReceiveShadows", terrain.receiveShadows ? 1 : 0);
            _shader.SetInt("uReceiveGI", 0);
            _shader.SetInt("uGiEmitterCount", 0);
            cached.Mesh.Draw();
        }
    }

    private static float[] CreateTerrainVertices(TerrainData data)
    {
        var mesh = TerrainMeshGenerator.Generate(data);
        var vertices = new float[mesh.indices.Length * 6];
        for (var index = 0; index < mesh.indices.Length; index++)
        {
            var vertex = mesh.vertices[mesh.indices[index]];
            var offset = index * 6;
            vertices[offset] = (float)vertex.position.x;
            vertices[offset + 1] = (float)vertex.position.y;
            vertices[offset + 2] = (float)vertex.position.z;
            vertices[offset + 3] = (float)vertex.normal.x;
            vertices[offset + 4] = (float)vertex.normal.y;
            vertices[offset + 5] = (float)vertex.normal.z;
        }
        return vertices;
    }

    private RenderItem[] ResolveRenderItems(BEngine.Scene scene) => scene.gameObjects
        .Where(item => item.activeInHierarchy)
        .SelectMany(gameObject => gameObject.GetComponents<BEngine.MeshRenderer>()
            .Where(renderer => renderer.enabled)
            .Select(renderer => new RenderItem(
                renderer,
                ResolveMesh(renderer.mesh),
                Numerics.WorldMatrix(gameObject.transform),
                Numerics.ToNumerics(gameObject.transform.position),
                SafeNormalize(Numerics.ToNumerics(gameObject.transform.up), NVector3.UnitY),
                Numerics.ToNumerics(renderer.color),
                ToRgb(renderer.emissionColor),
                Math.Max(0, (float)renderer.emissionIntensity),
                Math.Clamp((float)renderer.metallic, 0, 1),
                Math.Clamp((float)renderer.smoothness, 0, 1))))
        .ToArray();

    private static LightParameters[] ResolveLights(BEngine.Scene scene, int maximumLights) => scene.gameObjects
        .Where(item => item.activeInHierarchy)
        .SelectMany(item => item.GetComponents<BEngine.Light>())
        .Where(item => item.enabled && item.intensity > BEngine.Fix64.Zero)
        .OrderBy(item => item.type == BEngine.LightType.Directional ? 0 : 1)
        .Take(Math.Clamp(maximumLights, 1, MaximumLights))
        .Select(light => new LightParameters(
            light,
            (int)light.type,
            Numerics.ToNumerics(light.transform.position),
            SafeNormalize(Numerics.ToNumerics(light.transform.forward), NVector3.UnitZ),
            ToRgb(light.color),
            Math.Max(0, (float)light.intensity),
            Math.Max(0.01f, (float)light.range),
            MathF.Cos(Math.Clamp((float)light.innerSpotAngle, 0, 179) * MathF.PI / 360f),
            MathF.Cos(Math.Clamp((float)light.spotAngle, 1, 179) * MathF.PI / 360f),
            Math.Max(0, (float)light.bounceIntensity)))
        .ToArray();

    private static LightingParameters ResolveLighting(BEngine.Scene scene)
    {
        var settings = scene.gameObjects
            .Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<BEngine.LightingSettings>())
            .FirstOrDefault(item => item.enabled);
        return settings is null
            ? new LightingParameters(
                ToRgb(BEngine.RenderSettings.ambientSkyColor),
                ToRgb(BEngine.RenderSettings.ambientEquatorColor),
                ToRgb(BEngine.RenderSettings.ambientGroundColor),
                Math.Max(0, (float)BEngine.RenderSettings.ambientIntensity),
                BEngine.RenderSettings.globalIllumination == BEngine.GlobalIlluminationMode.Realtime,
                Math.Max(0, (float)BEngine.RenderSettings.indirectIntensity),
                8,
                MaximumLights,
                true,
                1024,
                50)
            : new LightingParameters(
                ToRgb(settings.ambientSkyColor),
                ToRgb(settings.ambientEquatorColor),
                ToRgb(settings.ambientGroundColor),
                Math.Max(0, (float)settings.ambientIntensity),
                settings.globalIllumination == BEngine.GlobalIlluminationMode.Realtime,
                Math.Max(0, (float)settings.indirectIntensity),
                Math.Max(0.1f, (float)settings.bounceDistance),
                Math.Clamp(settings.maxRealtimeLights, 1, MaximumLights),
                settings.enableShadows,
                Math.Clamp(settings.shadowResolution, 128, 4096),
                Math.Max(1, (float)settings.shadowDistance));
    }

    private void ApplyLightingUniforms(
        IReadOnlyList<LightParameters> lights,
        LightingParameters lighting,
        ShadowState shadow)
    {
        _shader.SetInt("uLightCount", lights.Count);
        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            var prefix = $"uLights[{index}]";
            _shader.SetInt($"{prefix}.type", light.Type);
            _shader.SetVector($"{prefix}.position", light.Position);
            _shader.SetVector($"{prefix}.direction", light.Direction);
            _shader.SetVector($"{prefix}.color", light.Color);
            _shader.SetFloat($"{prefix}.intensity", light.Intensity);
            _shader.SetFloat($"{prefix}.range", light.Range);
            _shader.SetFloat($"{prefix}.innerCone", Math.Max(light.InnerCone, light.OuterCone));
            _shader.SetFloat($"{prefix}.outerCone", Math.Min(light.InnerCone, light.OuterCone));
        }

        _shader.SetVector("uAmbientSky", lighting.AmbientSky);
        _shader.SetVector("uAmbientEquator", lighting.AmbientEquator);
        _shader.SetVector("uAmbientGround", lighting.AmbientGround);
        _shader.SetFloat("uAmbientIntensity", lighting.AmbientIntensity);
        _shader.SetFloat("uIndirectIntensity", lighting.IndirectIntensity);
        _shader.SetInt("uShadowsEnabled", shadow.Enabled ? 1 : 0);
        _shader.SetInt("uShadowLightIndex", shadow.LightIndex);
        _shader.SetInt("uSoftShadows", shadow.Soft ? 1 : 0);
        _shader.SetFloat("uShadowStrength", shadow.Strength);
        _shader.SetFloat("uShadowBias", shadow.Bias);
        if (!shadow.Enabled) return;
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _shadowTexture);
        _shader.SetInt("uShadowMap", 0);
    }

    private void ApplyGiUniforms(
        RenderItem receiver,
        IReadOnlyList<RenderItem> renderItems,
        IReadOnlyList<LightParameters> lights,
        LightingParameters lighting)
    {
        if (!lighting.EnableRealtimeGi || !receiver.Renderer.receiveGlobalIllumination)
        {
            _shader.SetInt("uReceiveGI", 0);
            _shader.SetInt("uGiEmitterCount", 0);
            return;
        }

        _shader.SetInt("uReceiveGI", 1);
        var emitters = renderItems
            .Where(item => !ReferenceEquals(item.Renderer, receiver.Renderer) &&
                           item.Renderer.contributeGlobalIllumination)
            .Select(item => CreateGiEmitter(item, lights, lighting))
            .Where(item => NVector3.DistanceSquared(item.Position, receiver.Position) <= item.Range * item.Range)
            .OrderBy(item => NVector3.DistanceSquared(item.Position, receiver.Position))
            .Take(MaximumGiEmitters)
            .ToArray();
        _shader.SetInt("uGiEmitterCount", emitters.Length);
        for (var index = 0; index < emitters.Length; index++)
        {
            var emitter = emitters[index];
            var prefix = $"uGiEmitters[{index}]";
            _shader.SetVector($"{prefix}.position", emitter.Position);
            _shader.SetVector($"{prefix}.color", emitter.Color);
            _shader.SetFloat($"{prefix}.range", emitter.Range);
            _shader.SetFloat($"{prefix}.strength", emitter.Strength);
        }
    }

    private static GiEmitterParameters CreateGiEmitter(
        RenderItem item,
        IReadOnlyList<LightParameters> lights,
        LightingParameters lighting)
    {
        var illumination = lighting.AmbientIntensity * 0.35f;
        foreach (var light in lights)
        {
            var attenuation = 1f;
            var lightDirection = -light.Direction;
            if (light.Type != (int)BEngine.LightType.Directional)
            {
                var delta = light.Position - item.Position;
                var distance = delta.Length();
                lightDirection = distance > 0.0001f ? delta / distance : item.Up;
                attenuation = MathF.Pow(Math.Clamp(1f - distance / light.Range, 0, 1), 2);
                if (light.Type == (int)BEngine.LightType.Spot)
                {
                    var cone = NVector3.Dot(light.Direction, -lightDirection);
                    attenuation *= SmoothStep(light.OuterCone, light.InnerCone, cone);
                }
            }
            illumination += Math.Max(0, NVector3.Dot(item.Up, lightDirection)) *
                            light.Intensity * light.BounceIntensity * attenuation;
        }

        var reflected = new NVector3(item.Color.X, item.Color.Y, item.Color.Z) * illumination;
        reflected += item.EmissionColor * item.EmissionIntensity;
        return new GiEmitterParameters(
            item.Position,
            reflected,
            lighting.BounceDistance,
            Math.Max(0, (float)item.Renderer.giContribution));
    }

    private ShadowState RenderShadowMap(
        IReadOnlyList<RenderItem> renderItems,
        IReadOnlyList<LightParameters> lights,
        RenderCamera camera,
        LightingParameters lighting)
    {
        var lightIndex = -1;
        for (var index = 0; index < lights.Count; index++)
        {
            if (lights[index].Type == (int)BEngine.LightType.Directional &&
                lights[index].Source.shadows != BEngine.LightShadows.None)
            {
                lightIndex = index;
                break;
            }
        }
        if (!lighting.EnableShadows || lightIndex < 0)
        {
            return new ShadowState(false, NMatrix4x4.Identity, -1, false, 0, 0);
        }

        var light = lights[lightIndex];
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var previousFramebuffer);
        EnsureShadowMap(lighting.ShadowResolution);
        var cameraForward = SafeNormalize(NVector3.Transform(NVector3.UnitZ, camera.Rotation), NVector3.UnitZ);
        var center = camera.Position + cameraForward * Math.Min(lighting.ShadowDistance * 0.35f, 20f);
        var lightPosition = center - light.Direction * lighting.ShadowDistance;
        var lightUp = Math.Abs(NVector3.Dot(light.Direction, NVector3.UnitY)) > 0.95f
            ? NVector3.UnitZ
            : NVector3.UnitY;
        var lightView = NMatrix4x4.CreateLookAt(lightPosition, center, lightUp);
        var lightProjection = NMatrix4x4.CreateOrthographic(
            lighting.ShadowDistance,
            lighting.ShadowDistance,
            0.1f,
            lighting.ShadowDistance * 2.5f);
        var lightViewProjection = lightView * lightProjection;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);
        _gl.Viewport(0, 0, (uint)_shadowResolution, (uint)_shadowResolution);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(2f, 4f);
        _depthShader.Use();
        _depthShader.SetMatrix("uLightViewProjection", lightViewProjection);
        foreach (var item in renderItems.Where(item => item.Renderer.castShadows))
        {
            _depthShader.SetMatrix("uModel", item.Model);
            item.Mesh.Draw();
        }
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);

        return new ShadowState(
            true,
            lightViewProjection,
            lightIndex,
            light.Source.shadows == BEngine.LightShadows.Soft,
            Math.Clamp((float)light.Source.shadowStrength, 0, 1),
            Math.Clamp((float)light.Source.shadowBias, 0.00001f, 0.1f));
    }

    private unsafe void EnsureShadowMap(int resolution)
    {
        if (_shadowFramebuffer != 0 && _shadowResolution == resolution) return;
        ReleaseShadowMap();
        _shadowResolution = resolution;
        _shadowFramebuffer = _gl.GenFramebuffer();
        _shadowTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _shadowTexture);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.DepthComponent24,
            (uint)resolution,
            (uint)resolution,
            0,
            PixelFormat.DepthComponent,
            PixelType.Float,
            null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D,
            _shadowTexture,
            0);
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);
        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("Could not create the directional light shadow map.");
        }
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void ReleaseShadowMap()
    {
        if (_shadowTexture != 0) _gl.DeleteTexture(_shadowTexture);
        if (_shadowFramebuffer != 0) _gl.DeleteFramebuffer(_shadowFramebuffer);
        _shadowTexture = 0;
        _shadowFramebuffer = 0;
        _shadowResolution = 0;
    }

    private void DrawSkybox(
        RenderCamera camera,
        int width,
        int height,
        NVector3 forward,
        NVector3 right,
        NVector3 up,
        NVector3 lightDirection,
        SkyboxParameters skybox)
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _skyboxShader.Use();
        _skyboxShader.SetVector("uCameraForward", SafeNormalize(forward, NVector3.UnitZ));
        _skyboxShader.SetVector("uCameraRight", SafeNormalize(right, NVector3.UnitX));
        _skyboxShader.SetVector("uCameraUp", SafeNormalize(up, NVector3.UnitY));
        _skyboxShader.SetVector("uSunDirection", SafeNormalize(-lightDirection, NVector3.UnitY));
        _skyboxShader.SetVector("uTopColor", skybox.TopColor);
        _skyboxShader.SetVector("uHorizonColor", skybox.HorizonColor);
        _skyboxShader.SetVector("uGroundColor", skybox.GroundColor);
        _skyboxShader.SetFloat("uAspect", width / (float)height);
        _skyboxShader.SetFloat("uTanHalfFov", MathF.Tan(camera.FieldOfView * 0.5f));
        _skyboxShader.SetFloat("uExposure", skybox.Exposure);
        _skyboxShader.SetFloat("uSunIntensity", skybox.SunIntensity);
        _skyboxShader.SetFloat("uSunSize", skybox.SunSize);
        _skyboxShader.SetFloat("uHorizonThickness", skybox.HorizonThickness);
        _gl.BindVertexArray(_skyboxVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
    }

    private static SkyboxParameters? ResolveSkybox(BEngine.Scene scene)
    {
        var component = scene.gameObjects
            .Where(item => item.activeInHierarchy)
            .SelectMany(item => item.GetComponents<BEngine.Skybox>())
            .FirstOrDefault(item => item.enabled);
        if (component is not null)
        {
            return new SkyboxParameters(
                Numerics.ToNumerics(component.topColor),
                Numerics.ToNumerics(component.horizonColor),
                Numerics.ToNumerics(component.groundColor),
                (float)component.exposure,
                (float)component.sunIntensity,
                (float)component.sunSize,
                (float)component.horizonThickness);
        }

        var material = BEngine.RenderSettings.skybox;
        if (material is null) return null;
        return new SkyboxParameters(
            Numerics.ToNumerics(material.GetColor("_TopColor", new BEngine.Color(
                BEngine.Fix64.Parse("0.12"), BEngine.Fix64.Parse("0.32"), BEngine.Fix64.Parse("0.68"), 1))),
            Numerics.ToNumerics(material.GetColor("_HorizonColor", new BEngine.Color(
                BEngine.Fix64.Parse("0.72"), BEngine.Fix64.Parse("0.82"), BEngine.Fix64.Parse("0.92"), 1))),
            Numerics.ToNumerics(material.GetColor("_GroundColor", new BEngine.Color(
                BEngine.Fix64.Parse("0.10"), BEngine.Fix64.Parse("0.12"), BEngine.Fix64.Parse("0.16"), 1))),
            (float)material.GetFloat("_Exposure", BEngine.Fix64.One),
            (float)material.GetFloat("_SunIntensity", BEngine.Fix64.Parse("1.25")),
            (float)material.GetFloat("_SunSize", BEngine.Fix64.Parse("0.04")),
            (float)material.GetFloat("_HorizonThickness", BEngine.Fix64.Parse("0.32")));
    }

    private static NVector3 ToRgb(BEngine.Color color) => new((float)color.r, (float)color.g, (float)color.b);

    private static NVector3 SafeNormalize(NVector3 value, NVector3 fallback) =>
        value.LengthSquared() < 0.000001f ? fallback : NVector3.Normalize(value);

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if (Math.Abs(edge1 - edge0) < 0.000001f) return value >= edge1 ? 1 : 0;
        var amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return amount * amount * (3 - 2 * amount);
    }

    private readonly record struct RenderItem(
        BEngine.MeshRenderer Renderer,
        MeshBuffer Mesh,
        NMatrix4x4 Model,
        NVector3 Position,
        NVector3 Up,
        NVector4 Color,
        NVector3 EmissionColor,
        float EmissionIntensity,
        float Metallic,
        float Smoothness);

    private sealed record TerrainMeshCache(TerrainData Data, int Version, MeshBuffer Mesh);

    private readonly record struct LightParameters(
        BEngine.Light Source,
        int Type,
        NVector3 Position,
        NVector3 Direction,
        NVector3 Color,
        float Intensity,
        float Range,
        float InnerCone,
        float OuterCone,
        float BounceIntensity);

    private readonly record struct LightingParameters(
        NVector3 AmbientSky,
        NVector3 AmbientEquator,
        NVector3 AmbientGround,
        float AmbientIntensity,
        bool EnableRealtimeGi,
        float IndirectIntensity,
        float BounceDistance,
        int MaximumRealtimeLights,
        bool EnableShadows,
        int ShadowResolution,
        float ShadowDistance);

    private readonly record struct GiEmitterParameters(
        NVector3 Position,
        NVector3 Color,
        float Range,
        float Strength);

    private readonly record struct ShadowState(
        bool Enabled,
        NMatrix4x4 LightViewProjection,
        int LightIndex,
        bool Soft,
        float Strength,
        float Bias);

    private readonly record struct SkyboxParameters(
        NVector4 TopColor,
        NVector4 HorizonColor,
        NVector4 GroundColor,
        float Exposure,
        float SunIntensity,
        float SunSize,
        float HorizonThickness);
}
