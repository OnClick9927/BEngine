using BEngine.Rendering.Rhi;
using Veldrid;

namespace BEngine.Platform;

public static class IOSMetalGraphicsDevice
{
    public static IGraphicsPresentationDevice Create(nint viewHandle, int width, int height)
    {
        var options = new GraphicsDeviceOptions(false, PixelFormat.D24_UNorm_S8_UInt, true,
            ResourceBindingModel.Improved, true, true);
        var swapchain = new SwapchainDescription(SwapchainSource.CreateUIView(viewHandle),
            (uint)width, (uint)height, PixelFormat.D24_UNorm_S8_UInt, true);
        return new VulkanGraphicsDevice(
            GraphicsDevice.CreateMetal(options, swapchain), BEngine.Rendering.Rhi.GraphicsBackend.Metal);
    }

}
