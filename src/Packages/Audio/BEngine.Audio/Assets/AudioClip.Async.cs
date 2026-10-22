namespace BEngine.Audio;

public sealed partial class AudioClip
{
    /// <summary>Schedules file/header work and cooperative decoding; completes when data is ready.</summary>
    public static BTask<AudioClip> LoadAsync(string path, AudioClipLoadType loadType = AudioClipLoadType.DecompressOnLoad,
        BCancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var completion = new BTaskCompletionSource<AudioClip>(); AudioClip? clip = null;
        CooperativeWorkScheduler.Main.Schedule(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            clip ??= LoadInternal(path, loadType, true);
            if (clip.loadState == AudioDataLoadState.Loading) return CooperativeWorkStatus.Blocked;
            if (clip.loadState != AudioDataLoadState.Loaded) throw new InvalidDataException(clip.loadError);
            completion.TrySetResult(clip); return CooperativeWorkStatus.Complete;
        }, error => { if (clip is not null) DestroyImmediate(clip); completion.TrySetException(error); }, CooperativeWorkPriority.Background);
        return completion.Task;
    }
}
