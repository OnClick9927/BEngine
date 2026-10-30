using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Player;

/// <summary>Small scene-independent splash renderer for hosts using the main presentation surface.</summary>
public sealed class PlayerStartupRenderer : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly IGraphicsProgram _program;
    private readonly IGraphicsMesh _mesh;
    private readonly IGraphicsTexture2D _logo;
    private readonly IGraphicsTexture2D _white;
    private readonly int _logoWidth, _logoHeight;
    private readonly NVector4 _background;
    private readonly float[] _vertices = new float[48];

    public PlayerStartupRenderer(IGraphicsDevice device)
    {
        _device = device;
        if (!PngImageCodec.TryDecode(PlayerStartupEnvironment.LogoPng, out _logoWidth, out _logoHeight, out var pixels))
            throw new InvalidDataException("The packaged application Logo is invalid.");
        var color = Convert.ToUInt32(PlayerStartupEnvironment.BackgroundColor.Substring(1, 6), 16);
        _background = new NVector4((color >> 16) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, 1);
        var shaders = GraphicsUtility.Current.ResolveShaderVariant(device.Capabilities, "Shaders/PortableScene/Textured");
        _program = device.CreateProgram(new("BEngine.Startup", shaders.Vertex, shaders.Fragment));
        _mesh = device.CreateMesh(new("BEngine.Startup", ReadOnlyMemory<float>.Empty,
            PortableSceneRenderer.TexturedVertexLayout, GraphicsPrimitiveTopology.TriangleList, GraphicsBufferUsage.Dynamic));
        _logo = device.CreateTexture2D("BEngine.Startup.Logo", new(_logoWidth, _logoHeight,
            GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled), pixels);
        _white = device.CreateTexture2D("BEngine.Startup.Bar", new(1, 1,
            GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled), new byte[] { 255, 255, 255, 255 });
    }

    public void Render(int width, int height, PlayerStartupProgress progress)
    {
        _device.SetViewport(new(0, 0, width, height));
        _device.SetScissor(null);
        _device.Clear(GraphicsClearFlags.Color, _background);
        _device.SetDepthState(GraphicsDepthState.Disabled);
        _device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
        _device.SetRasterizerState(GraphicsRasterizerState.Default);
        _program.Bind();
        _program.SetFloat("uViewportWidth", width);
        _program.SetFloat("uViewportHeight", height);
        _program.SetInt("uTexture", 0);
        var scale = Math.Min(Math.Min(240, width * .48f) / _logoWidth, Math.Min(180, height * .32f) / _logoHeight);
        var w = _logoWidth * scale; var h = _logoHeight * scale;
        var x = (width - w) / 2; var y = (height - h) / 2;
        Draw(_logo, x, y, w, h, new(1, 1, 1, progress.LogoOpacity));
        if (progress.ShowProgressBar)
        {
            var barWidth = Math.Min(240, width * .48f);
            Draw(_white, (width - barWidth) / 2, y + h + 28, barWidth, 5, new(.22f, .25f, .29f, 1));
            Draw(_white, (width - barWidth) / 2, y + h + 28, barWidth * progress.Progress, 5, new(.78f, .83f, .89f, 1));
        }
    }

    private void Draw(IGraphicsTexture2D texture, float x, float y, float w, float h, NVector4 color)
    {
        Span<float> points = stackalloc float[] { x, y, 0, 0, x + w, y, 1, 0, x + w, y + h, 1, 1,
            x, y, 0, 0, x + w, y + h, 1, 1, x, y + h, 0, 1 };
        for (var i = 0; i < 6; i++)
        {
            var v = i * 8; var p = i * 4;
            _vertices[v] = points[p]; _vertices[v + 1] = points[p + 1];
            _vertices[v + 2] = color.X; _vertices[v + 3] = color.Y;
            _vertices[v + 4] = color.Z; _vertices[v + 5] = color.W;
            _vertices[v + 6] = points[p + 2]; _vertices[v + 7] = points[p + 3];
        }
        _mesh.Update(_vertices);
        _device.BindTexture(0, texture);
        _device.Draw(_mesh);
    }

    public void Dispose()
    {
        _white.Dispose(); _logo.Dispose(); _mesh.Dispose(); _program.Dispose();
    }
}
