using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Hosting;

/// <summary>Resolved presentation inputs; Core never reads deployment files or authoring settings.</summary>
public sealed record HostPresentationOptions(
    HostWindowOptions Window, BuildTargetDescriptor Target, GraphicsBackend PreferredBackend);
