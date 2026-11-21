using System.Diagnostics;
using System.Text;

internal sealed class GitTestRepository
{
    internal static string RemotesRoot = "";
    internal string Root { get; }
    internal string Url => new Uri(Root + Path.DirectorySeparatorChar).AbsoluteUri;
    private HashSet<string> _paths = [];
    internal GitTestRepository(string root)
    {
        Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); Run("init", "--initial-branch=main", "--quiet");
        Run("config", "uploadpack.allowFilter", "true");
        Run("config", "uploadpack.allowAnySHA1InWant", "true");
    }
    internal string Run(params string[] arguments) => Command(null, arguments);
    internal string Command(string? input, params string[] arguments)
        => CommandAt(Root, input, arguments);
    internal static string CommandAt(string root, string? input, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var value in new[] { "-c", "user.name=Hub Tests", "-c", "user.email=hub@example.invalid", "-c", "core.autocrlf=false", "-c", "core.longpaths=true", "-c", "core.hooksPath=" + root + "/disabled-hooks" }) start.ArgumentList.Add(value);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        if (input is not null) process.StandardInput.Write(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new TimeoutException("Git fixture command timed out."); }
        if (process.ExitCode != 0) throw new IOException(errors.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult().TrimEnd('\r', '\n');
    }
    internal string Publish(Dictionary<string, byte[]> files)
    {
        foreach (var old in _paths.Except(files.Keys)) File.Delete(Path.Combine(Root, old));
        foreach (var (path, bytes) in files)
        { var target = Path.Combine(Root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes); }
        _paths = files.Keys.ToHashSet(); Run("add", "--all"); Run("commit", "--quiet", "--allow-empty", "-m", "fixture update");
        var sha = Run("rev-parse", "HEAD");
        foreach (var branch in new[] { "dev", "src", "feature/packages" }) Run("update-ref", "refs/heads/" + branch, sha);
        return sha;
    }
    internal static Dictionary<string, byte[]> Packages(int revision = 1, bool extraVersion = false)
    {
        var files = new Dictionary<string, byte[]> { [".gitattributes"] = Encoding.UTF8.GetBytes("*.yaml text eol=crlf\n*.cs text eol=crlf\n") };
        foreach (var version in extraVersion ? new[] { "1.0.0", "1.1.0", "1.2.0", "9.0.0" } : new[] { "1.0.0", "1.1.0", "9.0.0" })
        {
            var folder = "Published/Animation/Animation-" + version + "/";
            files[folder + "package.yaml"] = Definition("com.vendor.animation", version, version == "1.1.0" ? "com.vendor.helper" : "", version == "9.0.0" ? "9.0.0" : "0.1.0");
            files[folder + "Runtime.cs"] = Encoding.UTF8.GetBytes("// revision " + revision + "\n");
        }
        files["Published/Helper/Helper-1.0.0/package.yaml"] = Definition("com.vendor.helper", "1.0.0");
        return files;
    }
    internal static byte[] Definition(string id, string version, string dependency = "", string minimum = "0.1.0") => Encoding.UTF8.GetBytes(
        $"format: BEngine.Package\nversion: 2\nid: {id}\npackageVersion: {version}\nminimumEngineVersion: {minimum}\ndisplayName: {id}\nruntime:\n  assembly: {id}\n  rootNamespace: Example\n" +
        (dependency.Length == 0 ? "" : $"  dependencies:\n  - packageId: {dependency}\n    target: runtime\n"));
}
