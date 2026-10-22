using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BEngine.Testing.Editor;

public static class AndroidPlayerTestLauncher
{
    public static async Task<TestRunResult> Run(PlayerTestLaunchRequest request, CancellationToken cancellationToken)
    {
        var sdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? Environment.GetEnvironmentVariable("ANDROID_HOME")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
        var adb = Environment.GetEnvironmentVariable("BENGINE_TEST_ADB") ?? Path.Combine(sdk, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        var serial = Environment.GetEnvironmentVariable("BENGINE_TEST_ANDROID_SERIAL");
        var package = request.AndroidApplicationIdentifier;
        if (package.Length == 0 || package.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_'))) throw new ArgumentException("Invalid Android application identifier.");
        var apks = Directory.GetFiles(request.Build.OutputDirectory, "*.apk", SearchOption.AllDirectories).OrderByDescending(path => path.Contains("-Signed", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (apks.Length == 0) throw new FileNotFoundException("Android Player tests require an APK build.");
        var port = new Uri(request.ContentUrl).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var remote = "files/bengine-test-" + Guid.NewGuid().ToString("N") + ".json";
        await Adb("install", "-r", apks[0]); await Adb("reverse", "tcp:" + port, "tcp:" + port);
        try
        {
            await Adb("shell", "am", "force-stop", package);
            var resolved = await AdbAllowFailure("shell", "cmd", "package", "resolve-activity", "--brief", package);
            var component = resolved.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault(line => line.StartsWith(package + "/", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Android test launcher activity could not be resolved: " + resolved.Output);
            var launch = new List<string> { "shell", "am", "start", "-n", component,
                "--ez", "bengineAutoStart", "true", "--ez", "bengineRunTests", "true", "--ez", "bengineTestCoverage", request.CollectCoverage ? "true" : "false",
                "--es", "bengineTestResult", remote, "--es", "bengineTestContentUrl", request.ContentUrl };
            if (request.TestIds != null)
            {
                launch.AddRange(["--es", "bengineTestIds64", Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request.TestIds)))]);
            }
            var started = await AdbAllowFailure(launch.ToArray());
            if (started.Code != 0 || started.Output.Contains("Error", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Android test Activity failed to start: " + started.Output);
            var launchedAt = Stopwatch.GetTimestamp();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await AdbAllowFailure("exec-out", "run-as", package, "cat", remote);
                if (result.Code == 0 && result.Output.TrimStart().StartsWith('{'))
                {
                    var parsed = JsonSerializer.Deserialize<TestRunResult>(result.Output) ?? throw new InvalidDataException("Android test result was empty.");
                    Directory.CreateDirectory(Path.GetDirectoryName(request.ResultPath)!); await File.WriteAllTextAsync(request.ResultPath, result.Output, cancellationToken); return parsed;
                }
                if (result.Output.Contains("not debuggable", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Android test APK must enable AndroidDebuggable to retrieve results.");
                if (Stopwatch.GetElapsedTime(launchedAt).TotalSeconds >= 15)
                {
                    var running = await AdbAllowFailure("shell", "pidof", package);
                    if (string.IsNullOrWhiteSpace(running.Output))
                    {
                        var crash = await AdbAllowFailure("logcat", "-d", "-t", "150", "AndroidRuntime:E", "BEngine:E", "*:S");
                        throw new InvalidOperationException("Android test Player exited before writing its result.\n" + crash.Output);
                    }
                }
                await Task.Delay(250, cancellationToken);
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            foreach (var arguments in new[] { new[] { "shell", "am", "force-stop", package }, new[] { "reverse", "--remove", "tcp:" + port }, new[] { "shell", "run-as", package, "rm", "-f", remote } })
                try { await RunAdb(cleanup.Token, arguments); } catch (Exception) { /* Preserve the test result or original failure. */ }
        }
        async Task Adb(params string[] arguments)
        { var result = await AdbAllowFailure(arguments); if (result.Code != 0) throw new InvalidOperationException("adb: " + result.Output); }
        async Task<(int Code, string Output)> AdbAllowFailure(params string[] arguments)
            => await RunAdb(cancellationToken, arguments);
        async Task<(int Code, string Output)> RunAdb(CancellationToken operationToken, params string[] arguments)
        {
            var start = new ProcessStartInfo(adb) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (!string.IsNullOrWhiteSpace(serial)) { start.ArgumentList.Add("-s"); start.ArgumentList.Add(serial); }
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("adb could not be started.");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operationToken); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            return (process.ExitCode, await output + await error);
        }
    }
}
