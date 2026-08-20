namespace BEngine.ExampleTests.CodexCliDiscovery;

internal sealed class CodexCliFixture : IDisposable
{
    public string Root { get; }
    public string ProjectRoot { get; }
    public string BinRoot { get; }

    public CodexCliFixture(string name)
    {
        Root = Path.Combine(Path.GetTempPath(), "BEngine", "CodexCliDiscovery", $"{name}-{Guid.NewGuid():N}");
        ProjectRoot = Path.Combine(Root, "Project");
        BinRoot = Path.Combine(Root, "bin");
        Directory.CreateDirectory(ProjectRoot);
        Directory.CreateDirectory(BinRoot);
    }

    public void CopyHostAs(string fileName, string hostExecutable)
    {
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            File.Copy(source, Path.Combine(BinRoot, Path.GetFileName(source)), overwrite: true);
        }
        File.Copy(hostExecutable, Path.Combine(BinRoot, fileName), overwrite: true);
    }

    public void WriteCommandShim(string fileName, string hostExecutable)
    {
        var escapedExecutable = hostExecutable.Replace("%", "%%", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(BinRoot, fileName), $"@echo off{Environment.NewLine}\"{escapedExecutable}\" %*{Environment.NewLine}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A failed assertion can leave the child process winding down briefly.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
