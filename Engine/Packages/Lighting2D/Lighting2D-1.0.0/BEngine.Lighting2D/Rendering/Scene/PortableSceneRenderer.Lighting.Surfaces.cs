using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

internal sealed partial class LightingRenderContributor2D
{
    private readonly Dictionary<Light2D, List<Vector2>> _lightShapes = [];

    private void AppendLightSurface(LightingData data, Light2D light)
    {
        var parameters = new NVector4((float)light.height, (float)light.falloffDistance, -1, 0);
        var rowU = NVector4.Zero; var rowV = NVector4.Zero; var uv = new NVector4(0, 0, 1, 1);
        if (light.lightType == Light2DType.Sprite && light.sprite is { isDestroyed: false } sprite &&
            light.transform.lossyScale.x != 0 && light.transform.lossyScale.y != 0)
        {
            var visual = SpriteSourceRegistry.Resolve(sprite);
            var slot = data.Cookies.IndexOf(visual.Texture);
            if (slot < 0)
            {
                if (data.Cookies.Count == 6) throw new InvalidOperationException("A lit layer supports up to six distinct Sprite light textures; sprites sharing an atlas share one slot.");
                slot = data.Cookies.Count; data.Cookies.Add(visual.Texture);
            }
            parameters.Z = slot;
            var origin = light.transform.InverseTransformPoint(Vector2.zero);
            var x = light.transform.InverseTransformVector(Vector2.right);
            var y = light.transform.InverseTransformVector(Vector2.up);
            rowU = new((float)(x.x / light.spriteSize.x), (float)(y.x / light.spriteSize.x), (float)(origin.x / light.spriteSize.x + visual.Pivot.x), 0);
            rowV = new((float)(-x.y / light.spriteSize.y), (float)(-y.y / light.spriteSize.y), (float)(1 - visual.Pivot.y - origin.y / light.spriteSize.y), 0);
            uv = new((float)visual.Uv.x, (float)visual.Uv.y, (float)visual.Uv.width, (float)visual.Uv.height);
        }
        data.Values.Add(parameters); data.Values.Add(rowU); data.Values.Add(rowV); data.Values.Add(uv);
    }

    private static bool SameNormalBasis(in SceneQuad2D a, in SceneQuad2D b) =>
        a.EffectData is not LightingData { Surface.texture: not null } ||
        a.Rotation == b.Rotation && a.FlipX == b.FlipX && a.FlipY == b.FlipY &&
        (a.Size.x < 0) == (b.Size.x < 0) && (a.Size.y < 0) == (b.Size.y < 0);

    private static void SetNormalSurface(LightingData data, in SceneQuad2D quad)
    {
        if (data.Surface is not { texture: { isDestroyed: false } } surface)
        { data.Values[3] = default; data.Values[4] = default; data.Values[5] = default; return; }
        surface.OnValidate();
        var xSign = (quad.FlipX ? -1 : 1) * (quad.Size.x < 0 ? -1 : 1);
        var ySign = (quad.FlipY ? -1 : 1) * (quad.Size.y < 0 ? -1 : 1);
        var x = Transform.RotateVector(new Vector2(xSign, 0), quad.Rotation);
        var y = Transform.RotateVector(new Vector2(0, ySign), quad.Rotation);
        data.Values[3] = new((float)surface.uvScale.x, (float)surface.uvScale.y, (float)surface.uvOffset.x, (float)surface.uvOffset.y);
        data.Values[4] = new((float)x.x, (float)x.y, (float)y.x, (float)y.y);
        data.Values[5] = new((float)surface.strength, 1, 0, 0);
    }

    private void BindSurfaceTextures(LightingData data)
    {
        var white = ResolveLightingTexture(default);
        _device.BindTexture(1, data.Surface?.texture is { isDestroyed: false } normal ? _textures.Resolve(normal.assetPath) : white);
        for (var index = 0; index < 6; index++)
            _device.BindTexture(index + 2, index < data.Cookies.Count ? _textures.Resolve(data.Cookies[index]) : white);
    }
}
