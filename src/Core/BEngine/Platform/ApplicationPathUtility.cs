namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IApplicationPaths through dependency injection.</summary>
public sealed class ApplicationPathUtility
{
    private readonly IApplicationPaths _implementation;

    public ApplicationPathUtility(IApplicationPaths implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static ApplicationPathUtility Current => RuntimeServiceAccess.GetRequiredService<ApplicationPathUtility>();

    public string BaseDirectory => _implementation.BaseDirectory;

    public string CurrentDirectory => _implementation.CurrentDirectory;

    public string TemporaryDirectory => _implementation.TemporaryDirectory;

    public string? ProcessPath => _implementation.ProcessPath;

    public string GetPersistentDataDirectory(string companyName, string productName) =>
        _implementation.GetPersistentDataDirectory(companyName, productName);
}
