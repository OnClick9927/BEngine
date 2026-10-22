using Veldrid;

namespace BEngine.Rendering.Rhi;

/// <summary>Reusable host-visible buffer versions, protected by the device's frame fence.</summary>
internal sealed class VulkanUploadBuffer(VulkanGraphicsDevice device, BufferUsage usage) : IDisposable
{
    private readonly List<DeviceBuffer> _buffers = [];
    private readonly HashSet<DeviceBuffer> _referenced = [];
    private readonly Dictionary<DeviceBuffer, BEngine.Diagnostics.NativeMemoryTracker.Allocation> _allocations = [];
    private readonly Dictionary<DeviceBuffer, long> _lastUse = [];
    private long _frame = -1;
    private int _nextBuffer;
    private DeviceBuffer? _lastReferencedBuffer;
    private long _lastReferencedFrame = -1;

    public DeviceBuffer Upload<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        device.SynchronizeUpload();
        BeginFrame();
        var bytes = Math.Max(16u, checked((uint)System.Runtime.InteropServices.MemoryMarshal.AsBytes(data).Length));
        while (_nextBuffer < _buffers.Count && (_referenced.Contains(_buffers[_nextBuffer]) || _lastUse.GetValueOrDefault(_buffers[_nextBuffer]) > device.CompletedFrameSerial)) _nextBuffer++;
        var index = _nextBuffer++;
        DeviceBuffer buffer;
        if (index == _buffers.Count)
        {
            buffer = device.Factory.CreateBuffer(new BufferDescription(bytes, usage | BufferUsage.Dynamic));
            _buffers.Add(buffer);
            Track(buffer);
        }
        else
        {
            buffer = _buffers[index];
            if (buffer.SizeInBytes < bytes)
            {
                var replacement = device.Factory.CreateBuffer(new BufferDescription(
                    Math.Max(bytes, checked(buffer.SizeInBytes * 2)), usage | BufferUsage.Dynamic));
                _buffers[index] = replacement;
                Track(replacement); Retire(buffer);
                buffer = replacement;
            }
        }
        if (!data.IsEmpty) device.UploadSnapshot(buffer, data);
        _referenced.Add(buffer); _lastUse[buffer] = device.FrameSerial;
        _lastReferencedBuffer = buffer; _lastReferencedFrame = device.FrameSerial;
        return buffer;
    }

    public void MarkReferenced(DeviceBuffer buffer)
    {
        // A mesh or unchanged uniform block can serve many adjacent draws. Its
        // lifetime only needs to be recorded once per recording, not per draw.
        if (ReferenceEquals(_lastReferencedBuffer, buffer) &&
            _lastReferencedFrame == device.FrameSerial && device.IsRecording) return;
        BeginFrame();
        _referenced.Add(buffer); _lastUse[buffer] = device.FrameSerial;
        _lastReferencedBuffer = buffer; _lastReferencedFrame = device.FrameSerial;
    }

    private void BeginFrame()
    {
        if (_frame == device.FrameSerial && device.IsRecording) return;
        // A version is reusable only after its last referencing frame has completed.
        _frame = device.FrameSerial;
        _nextBuffer = 0;
        _referenced.Clear();
        _lastReferencedBuffer = null; _lastReferencedFrame = -1;
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers) Retire(buffer);
        _buffers.Clear();
        _referenced.Clear(); _lastUse.Clear();
        _lastReferencedBuffer = null; _lastReferencedFrame = -1;
    }

    private void Track(DeviceBuffer buffer) => _allocations.Add(buffer, BEngine.Diagnostics.NativeMemoryTracker.Register("GPU.Buffer." + device.Backend, usage.ToString(), buffer.SizeInBytes));
    private void Retire(DeviceBuffer buffer)
    {
        _lastUse.Remove(buffer);
        _allocations.Remove(buffer, out var allocation);
        device.RetireResource(new TrackedBuffer(buffer, allocation));
    }
    private sealed class TrackedBuffer(DeviceBuffer buffer, IDisposable? allocation) : IDisposable
    {
        public void Dispose() { try { buffer.Dispose(); } finally { allocation?.Dispose(); } }
    }
}
