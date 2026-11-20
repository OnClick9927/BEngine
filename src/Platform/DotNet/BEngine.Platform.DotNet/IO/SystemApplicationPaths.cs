namespace BEngine.Platform;

public sealed class SystemApplicationPaths : IApplicationPaths
{
    public static SystemApplicationPaths Shared { get; } = new(
        SystemPlatformService.Shared,
        SystemPathService.Shared);

    private readonly IPlatformService _platform;
    private readonly IPathService _paths;

    public SystemApplicationPaths(IPlatformService platform, IPathService paths)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string BaseDirectory => _platform.BaseDirectory;
    public string CurrentDirectory => _platform.CurrentDirectory;
    public string TemporaryDirectory => _platform.TemporaryDirectory;
    public string? ProcessPath => _platform.ProcessPath;

    public string GetPersistentDataDirectory(string companyName, string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        var root = _platform.IsMacOS
            ? _paths.Combine(
                _platform.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support")
            : _platform.IsLinux
                ? ResolveLinuxDataDirectory()
                : _platform.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return _paths.GetFullPath(_paths.Combine(root, companyName, productName));
    }

    private string ResolveLinuxDataDirectory()
    {
        var configured = _platform.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(configured)) return _paths.GetFullPath(configured);
        return _paths.Combine(
            _platform.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share");
    }
}
