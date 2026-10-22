using BEngine.Rendering.Rhi;

namespace BEngine.Platform;

/// <summary>Verifies the upload, alpha blending and draw paths on the selected driver.</summary>
public static class DesktopGraphicsProbe
{
    public static void Verify(SilkGraphicsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.MakeCurrent();
        var device = session.Device;
        if (device is not IGraphicsColorReadback readback)
            throw new NotSupportedException("The desktop graphics provider cannot verify rendered pixels.");
        var vulkan = device.Backend == GraphicsBackend.Vulkan;
        var suffix = vulkan ? "vulkan" : "opengl";
        var vertex = DefaultShaderResources.Load($"Shaders/Startup/TextureProbe.{suffix}.vert.glsl");
        var fragment = DefaultShaderResources.Load($"Shaders/Startup/TextureProbe.{suffix}.frag.glsl");
        using var program = device.CreateProgram(new("BEngine.Startup.TextureProbe",
            new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, vertex),
            new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, fragment)));
        using var mesh = device.CreateMesh(new("BEngine.Startup.ProbeMesh", ReadOnlyMemory<float>.Empty,
            new GraphicsVertexLayout(4 * sizeof(float), [new(0, 2, 0), new(1, 2, 2 * sizeof(float))]),
            usage: GraphicsBufferUsage.Dynamic));
        using var texture = device.CreateTexture2D("BEngine.Startup.ProbeTexture",
            new(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled), new byte[] { 255, 0, 0, 255 });
        using var target = device.CreateRenderTarget("BEngine.Startup.ProbeTarget", new(16, 16, GraphicsTextureFormat.Rgba8Unorm, null));
        for (var frame = 0; frame < 3; frame++)
        {
            session.BeginProbeFrame();
            device.SetScissor(null);
            device.Clear(GraphicsClearFlags.Color, new(0, 0, 0, 1));
            GraphicsColorReadbackRequest capture;
            using (device.PushRenderTarget(target))
            {
                device.SetViewport(new(0, 0, 16, 16));
                device.SetScissor(null);
                device.SetDepthState(GraphicsDepthState.Disabled);
                device.SetRasterizerState(GraphicsRasterizerState.Default);
                device.SetBlendMode(GraphicsBlendMode.AlphaBlend);
                device.Clear(GraphicsClearFlags.Color, new(0, 0, 1, 1));
                program.Bind();
                if (frame == 1)
                {
                    // Reuse the previous frame's data before the first update of this frame.
                    device.BindTexture(0, texture);
                    device.Draw(mesh);
                }
                program.SetFloat("uViewportWidth", 16);
                program.SetFloat("uViewportHeight", 16);
                program.SetInt("uTexture", 0);
                texture.Update(new byte[] { 255, 0, 0, 255 });
                device.BindTexture(0, texture);
                mesh.Update(Quad(0, 8));
                device.Draw(mesh);

                if (frame != 1)
                {
                    // All three resources change after a draw that still needs their old data.
                    texture.Update(new byte[] { 0, 255, 0, 128 });
                    program.SetFloat("uViewportWidth", 32);
                    mesh.Update(Quad(16, 32));
                    device.Draw(mesh);
                }
                capture = readback.RequestColorReadback(new(0, 0, 16, 16), 16, 16);
            }
            session.Present();
            using (capture)
            {
                GraphicsColorReadbackImage? pixels = null;
                var deadline = Environment.TickCount64 + 5000;
                while (!capture.TryGetResult(out pixels) && Environment.TickCount64 < deadline)
                    Thread.Sleep(1);
                if (pixels is null) throw new InvalidOperationException("Graphics pixel verification failed: " + capture.Error);
                VerifyPixel(pixels, 2, 8, 255, 0, 0);
                VerifyPixel(pixels, 6, 8, 255, 0, 0);
                VerifyPixel(pixels, 12, 8, 0, 128, 127);
            }
        }
    }

    private static float[] Quad(float left, float right) =>
        [left, 0, 0, 0, right, 0, 1, 0, right, 16, 1, 1,
         left, 0, 0, 0, right, 16, 1, 1, left, 16, 0, 1];

    private static void VerifyPixel(GraphicsColorReadbackImage image, int x, int y, int red, int green, int blue)
    {
        var pixel = image.Pixels.Span.Slice((y * image.Width + x) * 4, 4);
        if (Math.Abs(pixel[0] - red) > 3 || Math.Abs(pixel[1] - green) > 3 || Math.Abs(pixel[2] - blue) > 3)
            throw new InvalidOperationException($"Graphics pixel verification failed at ({x},{y}): " +
                $"expected {red},{green},{blue}; received {pixel[0]},{pixel[1]},{pixel[2]}.");
    }
}
