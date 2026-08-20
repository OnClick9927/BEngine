using BEngine.Editor.Rendering;
using BEngine.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.Vulkan;
using System.Reflection;

namespace BEngine.ExampleTests.VulkanRendering;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "BEngine Vulkan Rendering Validation",
            ClientSize = new Size(640, 360),
            ShowInTaskbar = false,
            TopMost = true,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(80, 80)
        };
        form.Show();
        try
        {
            Resources.RegisterResourceRoot(Path.Combine(FindRepositoryRoot(), "src", "Core"));
            var factory = new GraphicsDeviceFactory();
            factory.RegisterProvider(new VulkanGraphicsDeviceProvider(
                form.Handle,
                GetModuleHandle(null),
                form.ClientSize.Width,
                form.ClientSize.Height,
                vsync: false));
            using var device = (IGraphicsPresentationDevice)factory.CreateDevice(GraphicsBackendDefaults.Default);
            using var sceneRenderer = new PortableSceneRenderer(device);
            var resolverType = typeof(GUI).Assembly.GetType(
                "BEngine.Editor.EditorGpuCanvasResourceResolver", throwOnError: true)!;
            var resolver = (IGpuCanvasResourceResolver)resolverType
                .GetProperty("Shared", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            using var canvas = new GpuCanvasRenderer(device, resourceResolver: resolver);

            if (args.Contains("--offset-viewport-only", StringComparer.OrdinalIgnoreCase))
            {
                var focusedViewportPixels = VerifyOffsetViewport(form, device, sceneRenderer);
                Console.WriteLine($"VULKAN_OFFSET_VIEWPORT_OK|{device.Capabilities.DeviceName}|" +
                                  $"pixels={focusedViewportPixels}");
                return 0;
            }

            var scene = new Scene("VulkanValidation");
            var sprite = scene.CreateGameObject("Vulkan Sprite");
            sprite.AddComponent<SpriteRenderer>();
            var clip = new GpuCanvasRect(0, 0, 640, 360);
            GpuCanvasCommand[] firstFrame =
            [
                new(GpuCanvasCommandType.SolidRect, new GpuCanvasRect(20, 20, 250, 32), clip,
                    new GpuCanvasColor(45, 48, 52, 235)),
                new(GpuCanvasCommandType.Text, new GpuCanvasRect(28, 24, 230, 24), clip,
                    new GpuCanvasColor(235, 238, 240), "FIRST FRAME TEXT", 14),
                new(GpuCanvasCommandType.SolidRect, new GpuCanvasRect(160, 26, 1, 20), clip,
                    new GpuCanvasColor(240, 240, 240)),
                new(GpuCanvasCommandType.Image, new GpuCanvasRect(280, 20, 24, 24), clip,
                    new GpuCanvasColor(255, 255, 255), "Icons/Assets/AssetScript.png")
            ];

            device.BeginFrame(640, 360);
            device.Clear(GraphicsClearFlags.Color, new System.Numerics.Vector4(0.08f, 0.08f, 0.08f, 1));
            canvas.Render(firstFrame, 640, 360);
            device.Present();
            System.Windows.Forms.Application.DoEvents();

            GpuCanvasCommand[] secondFrame =
            [
                .. firstFrame,
                new(GpuCanvasCommandType.SolidRect, new GpuCanvasRect(20, 80, 250, 32), clip,
                    new GpuCanvasColor(45, 48, 52, 235)),
                new(GpuCanvasCommandType.Text, new GpuCanvasRect(28, 84, 230, 24), clip,
                    new GpuCanvasColor(235, 238, 240), "LATE TEXT VISIBLE", 14)
            ];
            device.BeginFrame(640, 360);
            device.Clear(GraphicsClearFlags.Color, new System.Numerics.Vector4(0.08f, 0.08f, 0.08f, 1));
            canvas.Render(secondFrame, 640, 360);
            device.Present();
            System.Windows.Forms.Application.DoEvents();
            const float scale = 1f;
            var initialPixels = 0;
            var latePixels = 0;
            PollCapture(form, capture =>
            {
                initialPixels = CountBrightPixels(capture, 24, 20, 250, 34, scale);
                latePixels = CountBrightPixels(capture, 24, 80, 250, 34, scale);
                return initialPixels >= 20 && latePixels >= 20;
            });
            if (initialPixels < 20 || latePixels < 20)
                throw new InvalidOperationException(
                    $"Vulkan dynamic GPU text atlas was not visible. Initial={initialPixels}, Late={latePixels}.");

            var offsetViewportPixels = VerifyOffsetViewport(form, device, sceneRenderer);

            if (device.Backend != GraphicsBackend.Vulkan ||
                SystemInfo.graphicsDeviceType != nameof(GraphicsBackend.Vulkan))
                throw new InvalidOperationException("The active graphics device is not Vulkan.");

            Console.WriteLine($"VULKAN_RENDERING_OK|{device.Capabilities.DeviceName}|" +
                              $"{device.Capabilities.ApiVersion}|dynamic-text={latePixels}|" +
                              $"offset-viewport={offsetViewportPixels}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            form.Close();
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private static Bitmap CaptureClient(Form form)
    {
        var size = form.ClientSize;
        var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var origin = form.PointToScreen(Point.Empty);
        graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, size);
        return bitmap;
    }

    private static void PollCapture(Form form, Func<Bitmap, bool> predicate)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(50);
            using var capture = CaptureClient(form);
            if (predicate(capture)) return;
        }
    }

    private static int VerifyOffsetViewport(Form form, IGraphicsPresentationDevice device,
        PortableSceneRenderer sceneRenderer)
    {
        var viewport = new GraphicsRect(320, 170, 220, 120);
        device.BeginFrame(640, 360);
        device.Clear(GraphicsClearFlags.Color, new System.Numerics.Vector4(0.08f, 0.08f, 0.08f, 1));
        sceneRenderer.FillViewport(viewport, new System.Numerics.Vector4(0.12f, 0.72f, 0.38f, 1));
        device.Present();
        System.Windows.Forms.Application.DoEvents();

        const float scale = 1f;
        var pixels = 0;
        PollCapture(form, capture =>
        {
            pixels = CountGreenPixels(capture, viewport, scale);
            return pixels >= viewport.Width * viewport.Height / 2;
        });
        if (pixels < viewport.Width * viewport.Height / 2)
        {
            var failurePath = Path.Combine(FindRepositoryRoot(), "Output", "EditorData", "Logs",
                "VulkanOffsetViewportFailure.png");
            Directory.CreateDirectory(Path.GetDirectoryName(failurePath)!);
            using var failure = CaptureClient(form);
            failure.Save(failurePath, System.Drawing.Imaging.ImageFormat.Png);
            throw new InvalidOperationException(
                $"Vulkan offset viewport was clipped incorrectly. Green={pixels}. Capture={failurePath}");
        }
        return pixels;
    }

    private static int CountBrightPixels(Bitmap bitmap, int x, int y, int width, int height, float scale)
    {
        var left = Math.Clamp((int)(x * scale), 0, bitmap.Width - 1);
        var top = Math.Clamp((int)(y * scale), 0, bitmap.Height - 1);
        var right = Math.Clamp((int)((x + width) * scale), left + 1, bitmap.Width);
        var bottom = Math.Clamp((int)((y + height) * scale), top + 1, bitmap.Height);
        var count = 0;
        for (var row = top; row < bottom; row++)
        for (var column = left; column < right; column++)
        {
            var pixel = bitmap.GetPixel(column, row);
            if (pixel.R > 170 && pixel.G > 170 && pixel.B > 170) count++;
        }
        return count;
    }

    private static int CountGreenPixels(Bitmap bitmap, GraphicsRect rect, float scale)
    {
        var left = Math.Clamp((int)(rect.X * scale), 0, bitmap.Width - 1);
        var top = Math.Clamp((int)(rect.Y * scale), 0, bitmap.Height - 1);
        var right = Math.Clamp((int)((rect.X + rect.Width) * scale), left + 1, bitmap.Width);
        var bottom = Math.Clamp((int)((rect.Y + rect.Height) * scale), top + 1, bitmap.Height);
        var count = 0;
        for (var row = top; row < bottom; row++)
        for (var column = left; column < right; column++)
        {
            var pixel = bitmap.GetPixel(column, row);
            if (pixel.G > 130 && pixel.G > pixel.R * 2 && pixel.G > pixel.B * 1.3) count++;
        }
        return count;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

}
