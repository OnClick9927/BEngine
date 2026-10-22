using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

/// <summary>Lighting uniforms for a custom 2D surface. Consume before preparing the next surface or ending the frame.</summary>
public sealed record LightingShaderParameters2D(IReadOnlyList<NVector4> Values, IReadOnlyList<string> CookieTextures);

/// <summary>Shares the standard light, outline, shadow and layer evaluation with custom shader pipelines.</summary>
public sealed class LightingShaderContext2D : IDisposable
{
    private readonly LightingRenderContributor2D _lighting;
    public LightingShaderContext2D(IGraphicsDevice device) => _lighting = new(device);
    public void BeginFrame() => _lighting.BeginFrame();
    public void BeginScene(Scene scene) => _lighting.BeginScene(scene);
    public void EndFrame() => _lighting.EndFrame();
    public LightingShaderParameters2D Prepare(GameObject target, SceneQuad2D quad, RenderCamera camera, int width, int height, bool useGraphNormal) =>
        _lighting.PrepareShaderSurface(target, quad, camera, width, height, useGraphNormal);
    public static string ShaderFunctions => DefaultShaderResources.Load("Shaders/PortableScene/Lighting.evaluation.glslinc");
    public void Dispose() => _lighting.Dispose();
}

internal sealed partial class LightingRenderContributor2D
{
    private readonly LightingData _unlitSurface = new();
    internal LightingShaderParameters2D PrepareShaderSurface(GameObject target, SceneQuad2D quad, RenderCamera camera,
        int width, int height, bool useGraphNormal)
    {
        var data = GetLighting(target);
        if (data is null)
        {
            data = _unlitSurface; data.Values.Clear(); data.Values.Add(new(1, 1, 1, 0));
            for (var i = 1; i < 6; i++) data.Values.Add(default);
        }
        SetLightingView(data, camera, width, height);
        var x = Transform.RotateVector(new Vector2(quad.FlipX ^ (quad.Size.x < 0) ? -1 : 1, 0), quad.Rotation);
        var y = Transform.RotateVector(new Vector2(0, quad.FlipY ^ (quad.Size.y < 0) ? -1 : 1), quad.Rotation);
        data.Values[3] = default;
        data.Values[4] = new((float)x.x, (float)x.y, (float)y.x, (float)y.y);
        data.Values[5] = new(1, useGraphNormal ? 1 : 0, 0, 0);
        return new(data.Values, data.Cookies);
    }
}
