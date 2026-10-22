namespace BEngine.Platform;

public interface IApplicationPaths
{
    string BaseDirectory { get; }
    string CurrentDirectory { get; }
    string TemporaryDirectory { get; }
    string? ProcessPath { get; }
    string GetPersistentDataDirectory(string companyName, string productName);
}
