namespace BEngine.Editor.Build;

/// <summary>Content providers declare static runtime modules for the current build only.</summary>
internal sealed class PlayerBuildHostModules : IDisposable
{
    private static readonly AsyncLocal<PlayerBuildHostModules?> Current = new();
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private readonly PlayerBuildHostModules? _previous;
    private readonly IReadOnlyDictionary<string, string> _anchors;

    private PlayerBuildHostModules(IReadOnlyDictionary<string, string> anchors)
    {
        _previous = Current.Value;
        _anchors = anchors;
        Current.Value = this;
    }

    internal static IReadOnlyDictionary<string, string> Anchors => Current.Value?._anchors ?? Empty;

    internal static PlayerBuildHostModules Enter(IReadOnlyList<Type> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in anchors)
        {
            ArgumentNullException.ThrowIfNull(anchor);
            if (!anchor.IsVisible || anchor.IsGenericType || anchor.ContainsGenericParameters)
                throw new ArgumentException("Runtime host anchors must be public non-generic types.", nameof(anchors));
            names.TryAdd(anchor.Assembly.GetName().Name!, anchor.FullName!.Replace('+', '.'));
        }
        return new PlayerBuildHostModules(names);
    }

    public void Dispose() => Current.Value = _previous;
}
