using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BEngine.Testing.Editor;

public static class WebPlayerTestLauncher
{
    public static async Task<TestRunResult> Run(PlayerTestLaunchRequest request, CancellationToken cancellationToken)
    {
        var executable = Environment.GetEnvironmentVariable("BENGINE_TEST_BROWSER") ?? new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            "/usr/bin/chromium", "/usr/bin/google-chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
        }.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Install Chrome/Edge/Chromium or configure BENGINE_TEST_BROWSER.");
        var index = Directory.GetFiles(request.Build.OutputDirectory, "index.html", SearchOption.AllDirectories).OrderBy(path => path.Length).FirstOrDefault()
            ?? throw new FileNotFoundException("Web build has no index.html.");
        var root = Path.GetDirectoryName(index)!; var token = Guid.NewGuid().ToString("N");
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using var listener = new HttpListener(); var address = $"http://127.0.0.1:{port}/"; listener.Prefixes.Add(address); listener.Start();
        var result = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serving = Serve();
        var profile = Path.Combine(Path.GetTempPath(), "bengine-browser-tests-" + token); Directory.CreateDirectory(profile);
        var query = "?bengineAutoStart=1&bengineRunTests=1&bengineTestToken=" + token + "&bengineTestCoverage=" + (request.CollectCoverage ? "1" : "0") +
            "&bengineTestContentUrl=" + Uri.EscapeDataString(request.ContentUrl);
        if (request.TestIds is not null) query += "&bengineTestIds=" + Uri.EscapeDataString(JsonSerializer.Serialize(request.TestIds));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        // Edge's compatibility relaunch otherwise returns success before the actual browser process starts.
        if (Path.GetFileName(executable).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add("--edge-skip-compat-layer-relaunch");
        foreach (var argument in new[] { "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-background-timer-throttling", "--enable-unsafe-swiftshader", "--user-data-dir=" + profile, address + query }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start test browser.");
        var diagnostics = process.StandardError.ReadToEndAsync();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        try
        {
            var completed = await Task.WhenAny(result.Task, process.WaitForExitAsync(stop.Token)).WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed != result.Task)
            {
                var error = await diagnostics;
                throw new InvalidOperationException($"Test browser exited with code {process.ExitCode} without returning a result.\n" + error[..Math.Min(error.Length, 4096)]);
            }
            return await result.Task;
        }
        finally
        {
            stop.Cancel(); listener.Stop(); if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(); await Task.WhenAll(diagnostics, standardOutput);
            try { await serving; } catch (HttpListenerException) { } catch (ObjectDisposedException) { } catch (OperationCanceledException) { }
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(Path.GetFullPath(profile)) == temporaryRoot && Path.GetFileName(profile) == "bengine-browser-tests-" + token)
                for (var attempt = 0; attempt < 3 && Directory.Exists(profile); attempt++)
                {
                    try { Directory.Delete(profile, recursive: true); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    { await Task.Delay(200); }
                }
        }
        async Task Serve()
        {
            while (!stop.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stop.Token);
                _ = Handle(context);
            }
        }
        async Task Handle(HttpListenerContext context)
        {
            try
            {
                var relative = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath).TrimStart('/');
                if (relative == "__bengine_tests/" + token && context.Request.HttpMethod == "POST")
                {
                    if (context.Request.ContentLength64 is <= 0 or > 16 * 1024 * 1024) { context.Response.StatusCode = 413; return; }
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync(stop.Token); var parsed = JsonSerializer.Deserialize<TestRunResult>(json) ?? throw new InvalidDataException("Empty browser result.");
                    Directory.CreateDirectory(Path.GetDirectoryName(request.ResultPath)!); await File.WriteAllTextAsync(request.ResultPath, json, stop.Token);
                    result.TrySetResult(parsed); return;
                }
                if (context.Request.HttpMethod != "GET") { context.Response.StatusCode = 405; return; }
                var path = Path.GetFullPath(Path.Combine(root, relative.Length == 0 ? "index.html" : relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) { context.Response.StatusCode = 404; return; }
                context.Response.ContentType = Path.GetExtension(path) switch { ".js" or ".mjs" => "text/javascript", ".wasm" => "application/wasm", ".html" => "text/html", ".json" => "application/json", _ => "application/octet-stream" };
                context.Response.Headers.Add("Cross-Origin-Opener-Policy", "same-origin"); context.Response.Headers.Add("Cross-Origin-Embedder-Policy", "require-corp");
                using var file = File.OpenRead(path); context.Response.ContentLength64 = file.Length; await file.CopyToAsync(context.Response.OutputStream, stop.Token);
            }
            catch (Exception exception) { if (!stop.IsCancellationRequested) result.TrySetException(exception); }
            finally { context.Response.Close(); }
        }
    }
}
