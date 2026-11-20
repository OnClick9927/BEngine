using System;

namespace BEngine.App.RedDot;

/// <summary>A domain-owned, single-threaded notification tree. Counts are current immediately;
/// listeners are coalesced until Flush or the service's LateUpdate.</summary>
public interface IRedDotService : IGameService
{
    RedDotNode Root { get; }
    RedDotNode EnsurePath(string path);
    bool TryGetNode(string path, out RedDotNode? node);
    long GetCount(string path);
    void SetCount(string path, long count);
    void AddCount(string path, long amount);
    IDisposable Subscribe(string path, Action<RedDotChange> callback, bool notifyImmediately = true);
    bool RemoveBranch(string path);
    void Clear();
    void Flush();
}

public readonly record struct RedDotChange(string Path, long PreviousCount, long Count, long LocalCount);
