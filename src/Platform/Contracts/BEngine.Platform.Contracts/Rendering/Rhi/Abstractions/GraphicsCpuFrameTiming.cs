namespace BEngine.Rendering.Rhi;

/// <summary>Optional, opt-in CPU call boundaries. These are not GPU durations or an attribution of native waits.</summary>
public interface IGraphicsCpuFrameTimingSource
{
    bool CollectCpuFrameTiming { get; set; }
    GraphicsCpuFrameTiming CpuFrameTiming { get; }
}

/// <summary>Stopwatch timestamps from one device frame; a zero boundary was not captured.</summary>
public record struct GraphicsCpuFrameTiming
{
    public long Frame { get; set; }
    public long AcquireStartTicks { get; set; }
    public long AcquireEndTicks { get; set; }
    public long FrameFenceEndTicks { get; set; }
    public long RetireEndTicks { get; set; }
    public long ResizeEndTicks { get; set; }
    public long RecordingEndTicks { get; set; }
    public long PresentStartTicks { get; set; }
    public long SubmitStartTicks { get; set; }
    public long SubmitEndTicks { get; set; }
    public long QueriesEndTicks { get; set; }
    public long SwapStartTicks { get; set; }
    public long SwapEndTicks { get; set; }
    public int RetiredResources { get; set; }
    public int PendingRetirements { get; set; }
}
