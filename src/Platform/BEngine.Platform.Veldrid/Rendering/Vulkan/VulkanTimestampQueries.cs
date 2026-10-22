using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Veldrid;
using Vulkan;
using static Vulkan.VulkanNative;

namespace BEngine.Rendering.Rhi;

internal sealed unsafe class VulkanTimestampQueries : IDisposable
{
    private const uint Capacity = 1024;
    private readonly ulong[] _values = new ulong[Capacity];
    private readonly VkDevice _device; private readonly float _period; private readonly ulong _mask;
    private readonly PropertyInfo _commandBuffer; private VkQueryPool _pool; private VkCommandBuffer _commands;
    private readonly List<(uint Start, uint End, string Name)> _passes = []; private uint _next;
    private long _frame; private bool _recording; private bool _submitted;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, "Veldrid.Vk.VkCommandList", "Veldrid")]
    internal VulkanTimestampQueries(Veldrid.GraphicsDevice device, CommandList commands)
    {
        var info = device.GetVulkanInfo(); _device = new(info.Device);
        VkPhysicalDeviceProperties properties; vkGetPhysicalDeviceProperties(new(info.PhysicalDevice), out properties); _period = properties.limits.timestampPeriod;
        uint count = 0; vkGetPhysicalDeviceQueueFamilyProperties(new(info.PhysicalDevice), ref count, null);
        var families = new VkQueueFamilyProperties[count];
        fixed (VkQueueFamilyProperties* pointer = families) vkGetPhysicalDeviceQueueFamilyProperties(new(info.PhysicalDevice), ref count, pointer);
        var bits = families[info.GraphicsQueueFamilyIndex].timestampValidBits;
        if (bits == 0 || _period <= 0) throw new NotSupportedException("Vulkan queue has no timestamp support.");
        _mask = bits >= 64 ? ulong.MaxValue : (1UL << (int)bits) - 1;
        // Veldrid 4.9 exposes the native command buffer on its internal Vulkan command list.
        _commandBuffer = commands.GetType().GetProperty("CommandBuffer", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new NotSupportedException("Veldrid command buffer interop is unavailable.");
        var description = new VkQueryPoolCreateInfo { sType = VkStructureType.QueryPoolCreateInfo, queryType = VkQueryType.Timestamp, queryCount = Capacity };
        var result = vkCreateQueryPool(_device, ref description, null, out _pool);
        if (result != VkResult.Success) throw new InvalidOperationException("Vulkan timestamp pool: " + result);
    }
    internal void Begin(CommandList commands, long frame)
    {
        Complete(); _frame = frame; _passes.Clear(); _next = 2; _recording = true;
        _commands = (VkCommandBuffer)_commandBuffer.GetValue(commands)!;
        vkCmdResetQueryPool(_commands, _pool, 0, Capacity);
        vkCmdWriteTimestamp(_commands, VkPipelineStageFlags.TopOfPipe, _pool, 0);
    }
    internal IDisposable? BeginPass(string name)
    {
        if (!_recording || _next + 2 > Capacity) return null;
        var start = _next++; var end = _next++; _passes.Add((start, end, name));
        vkCmdWriteTimestamp(_commands, VkPipelineStageFlags.BottomOfPipe, _pool, start);
        return new Scope(this, end);
    }
    internal void End()
    {
        if (!_recording) return; vkCmdWriteTimestamp(_commands, VkPipelineStageFlags.BottomOfPipe, _pool, 1); _recording = false;
    }
    internal void Submitted() => _submitted = true;
    internal void Complete()
    {
        if (!TryComplete()) throw new InvalidOperationException("Vulkan timestamps are still pending after frame completion.");
    }
    internal bool TryComplete()
    {
        if (!_submitted) return true;
        var values = _values;
        fixed (ulong* pointer = values)
        {
            var result = vkGetQueryPoolResults(_device, _pool, 0, _next, (nuint)(_next * 8), pointer, 8, VkQueryResultFlags._64);
            if (result == VkResult.NotReady) return false;
            if (result != VkResult.Success) throw new InvalidOperationException("Completed Vulkan timestamp queries: " + result);
        }
        double Elapsed(uint start, uint end) => ((values[end] - values[start]) & _mask) * (double)_period / 1_000_000;
        GraphicsFrameTimingService.Report(GraphicsBackend.Vulkan, Elapsed(0, 1), "timestamp-query");
        foreach (var pass in _passes) GraphicsPassTimingService.Report(new(_frame, GraphicsBackend.Vulkan, pass.Name, Elapsed(pass.Start, pass.End)));
        _submitted = false;
        return true;
    }
    public void Dispose() { if (_pool.Handle != 0) vkDestroyQueryPool(_device, _pool, null); _pool = default; }
    private sealed class Scope(VulkanTimestampQueries owner, uint end) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed || !owner._recording) return; _disposed = true; vkCmdWriteTimestamp(owner._commands, VkPipelineStageFlags.BottomOfPipe, owner._pool, end); }
    }
}
