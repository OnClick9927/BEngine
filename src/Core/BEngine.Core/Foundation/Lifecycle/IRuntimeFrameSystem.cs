namespace BEngine;

/// <summary>
/// Optional lifecycle for runtime systems that combine work across every scene ticked by one host frame.
/// One representative is invoked for each <see cref="frameGroupId"/>.
/// </summary>
public interface IRuntimeFrameSystem
{
    string frameGroupId { get; }
    void BeginFrame(IReadOnlyList<Scene> scenes, Fix64 deltaTime) { }
    void EndFrame(IReadOnlyList<Scene> scenes, Fix64 deltaTime) { }
}
