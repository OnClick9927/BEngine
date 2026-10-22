namespace BEngine;

/// <summary>Timeline-owned playback session for adapters and custom components.</summary>
public interface ITimelineMediaPlayable
{
    bool CanPlayTimelineAsset(BAsset asset);
    void EvaluateTimeline(BAsset asset, Fix64 localTime, bool playing);
    void StopTimeline();
}
