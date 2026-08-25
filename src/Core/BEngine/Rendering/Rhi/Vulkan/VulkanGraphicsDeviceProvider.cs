using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;
using Vd = Veldrid;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi.Vulkan;

public sealed class VulkanGraphicsDeviceProvider : IGraphicsDeviceProvider
{
    private readonly nint _windowHandle;
    private readonly nint _instanceHandle;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _vsync;

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public VulkanGraphicsDeviceProvider(
        nint windowHandle,
        nint instanceHandle,
        int width,
        int height,
        bool vsync = true)
    {
        if (windowHandle == 0) throw new ArgumentException("A native window handle is required.", nameof(windowHandle));
        _windowHandle = windowHandle;
        _instanceHandle = instanceHandle;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _vsync = vsync;
    }

    public GraphicsBackendSupport QuerySupport() => Vd.GraphicsDevice.IsBackendSupported(Vd.GraphicsBackend.Vulkan)
        ? new GraphicsBackendSupport(Backend, GraphicsBackendAvailability.Available,
            "A Vulkan loader and compatible physical device are available.")
        : new GraphicsBackendSupport(Backend, GraphicsBackendAvailability.Unavailable,
            "No compatible Vulkan loader or physical device was found.");

    public IGraphicsDevice CreateDevice()
    {
        var support = QuerySupport();
        if (!support.CanCreateDevice) throw new NotSupportedException(support.Reason);
        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt,
            syncToVerticalBlank: _vsync,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferStandardClipSpaceYDirection: true,
            preferDepthRangeZeroToOne: true);
        var source = SwapchainSource.CreateWin32(_windowHandle, _instanceHandle);
        var swapchain = new SwapchainDescription(source, (uint)_width, (uint)_height,
            PixelFormat.D24_UNorm_S8_UInt, _vsync);
        return new VulkanGraphicsDevice(Vd.GraphicsDevice.CreateVulkan(options, swapchain));
    }
}
