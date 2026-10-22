namespace BEngine.Rendering.Rhi;

/// <summary>Exposes cumulative draw counters recorded by a graphics device.</summary>
public interface IGraphicsDeviceStatistics
{
    GraphicsDrawStatistics DrawStatistics { get; }
}
