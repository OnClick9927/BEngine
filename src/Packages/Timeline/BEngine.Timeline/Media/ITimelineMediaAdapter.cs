namespace BEngine;

/// <summary>Extends an existing component with playback owned by Timeline.</summary>
public interface ITimelineMediaAdapter
{
    bool CanAdapt(Component component, BAsset asset);
    ITimelineMediaPlayable CreatePlayback(Component component);
}
