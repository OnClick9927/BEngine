using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Veldrid;
using Vulkan;
using Vd = Veldrid;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public sealed class VulkanGraphicsDeviceProvider : IGraphicsDeviceProvider
{
    private static readonly object DeviceCreationGate = new();
    private readonly nint _windowHandle;
    private readonly nint _instanceHandle;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _vsync;
    /// <summary>Disable only for render paths which do not use depth or stencil attachments.</summary>
    public static bool useSwapchainDepthStencil { get; set; } = true;
    /// <summary>Optional device-name substring, or "first" for enumeration order.
    /// Null uses BENGINE_VULKAN_DEVICE; the default prefers a compatible high-performance device.</summary>
    public static string? preferredDeviceName { get; set; }

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

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, "Veldrid.Vk.VkGraphicsDevice", "Veldrid")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, "Veldrid.Vk.VkSwapchain", "Veldrid")]
    public IGraphicsDevice CreateDevice()
    {
        if (VulkanStartupMemory.GetRejectionReason() is { } rejection) throw new InvalidOperationException(rejection);
        var support = QuerySupport();
        if (!support.CanCreateDevice) throw new NotSupportedException(support.Reason);
        PixelFormat? depthFormat = useSwapchainDepthStencil ? PixelFormat.D24_UNorm_S8_UInt : null;
        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: depthFormat,
            syncToVerticalBlank: _vsync,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferStandardClipSpaceYDirection: true,
            preferDepthRangeZeroToOne: true);
        var source = SwapchainSource.CreateWin32(_windowHandle, _instanceHandle);
        var swapchain = new SwapchainDescription(source, (uint)_width, (uint)_height,
            depthFormat, _vsync);
        Vd.GraphicsDevice native;
        try
        {
            // The standard output-only Veldrid patch exposes a startup callback.
            // Keep native creation serialized so independent windows cannot
            // replace each other's selector, and always restore an existing hook.
            lock (DeviceCreationGate)
            {
                // The output-only Veldrid patch reads this once per swapchain.
                // An explicit host switch (or diagnostic env=0) keeps the eager
                // wait for comparison; standalone Veldrid users stay opt-out.
                const string deferAcquireSwitch = "BEngine.Vulkan.DeferSwapchainAcquireWait";
                if (!AppContext.TryGetSwitch(deferAcquireSwitch, out _))
                    AppContext.SetSwitch(deferAcquireSwitch,
                        Environment.GetEnvironmentVariable("BENGINE_VULKAN_DEFER_ACQUIRE") != "0");
                var hook = typeof(Vd.GraphicsDevice).Assembly.GetType("Veldrid.Vk.VkGraphicsDevice")?
                    .GetField("BEnginePhysicalDeviceSelector", BindingFlags.Static | BindingFlags.Public);
                var previous = hook?.GetValue(null);
                Func<VkPhysicalDevice[], VkSurfaceKHR, int> select = (devices, surface) =>
                    VulkanPhysicalDeviceSelection.Select(devices, surface, depthFormat is not null,
                        preferredDeviceName ?? Environment.GetEnvironmentVariable("BENGINE_VULKAN_DEVICE"));
                try
                {
                    if (hook is not null)
                        hook.SetValue(null, Delegate.CreateDelegate(hook.FieldType, select.Target, select.Method));
                    else Debug.LogWarning("Vulkan device selection is unavailable in this Veldrid output; using its first-device policy. Rebuild with the standard engine dependency patch.");
                    native = Vd.GraphicsDevice.CreateVulkan(options, swapchain);
                }
                finally { hook?.SetValue(null, previous); }
            }
        }
        catch (VeldridException error) { throw new InvalidOperationException("Vulkan device/swapchain creation failed: " + error.Message, error); }
        try { return new VulkanGraphicsDevice(native); }
        catch { native.Dispose(); throw; }
    }
}
