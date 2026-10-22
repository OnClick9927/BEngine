namespace BEngine.Editor;
public static class AssetProtection
{
    public static event Func<string?, bool>? ProtectedPath;
    public static event Func<string, string?>? AssemblyIdentity;
    public static bool IsProtected(string? path)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(ProtectedPath))
            if (handler(path)) return true;
        return false;
    }

    public static string? GetAssemblyIdentity(string path)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(AssemblyIdentity))
            if (handler(path) is { } identity) return identity;
        return null;
    }
}
