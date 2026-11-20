namespace BEngine.Player;

/// <summary>Optional observation callbacks. No logging format or storage policy is imposed on content.</summary>
public sealed class PlayerRuntimeDiagnostics(
    Action<string, string?>? phase = null,
    Action<string, Exception>? failure = null)
{
    public static PlayerRuntimeDiagnostics Default { get; } = new();
    public void Phase(string name, string? detail = null) => phase?.Invoke(name, detail);
    public void Failure(string name, Exception exception)
    {
        if (failure is not null) failure(name, exception);
        else Debug.LogError($"{name}: {exception}");
    }
}
