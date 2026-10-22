namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsBackendSupport(
    GraphicsBackend Backend,
    GraphicsBackendAvailability Availability,
    string Reason)
{
    public bool CanCreateDevice => Availability == GraphicsBackendAvailability.Available;
}
