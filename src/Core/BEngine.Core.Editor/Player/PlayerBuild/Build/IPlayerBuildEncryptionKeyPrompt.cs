namespace BEngine.Editor.Build;

/// <summary>Optional content-provider UI for selecting a build-time secret. Never persist or log the key.</summary>
public interface IPlayerBuildEncryptionKeyPrompt
{
    /// <summary>Invokes accept only after a key has been selected and validated by the content provider.</summary>
    void RequestKey(Action<string> accept);
}
