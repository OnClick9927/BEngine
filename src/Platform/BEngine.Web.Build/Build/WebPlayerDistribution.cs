using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Collapses the engine-owned browser scripts and VFS index into one compressed player.</summary>
internal static class WebPlayerDistribution
{
    internal static void Apply(string webRoot, FileUtility files, PathUtility paths)
    {
        var main = files.ReadAllText(paths.Combine(webRoot, "main.js"));
        var combined = new StringBuilder();
        combined.Append("/*\n").Append(files.ReadAllText(paths.Combine(webRoot, "BrotliDecoder.LICENSE.txt"))
            .Replace("*/", "* /", StringComparison.Ordinal)).Append("\n*/\n");
        var fontLicense = paths.Combine(webRoot, "FreeType.LICENSE.txt");
        if (files.FileExists(fontLicense))
        {
            combined.Append("/* Portions of this software are copyright (c) The FreeType Project (www.freetype.org). All rights reserved.\n")
                .Append(files.ReadAllText(fontLicense).Replace("*/", "* /", StringComparison.Ordinal)).Append("\n*/\n");
            files.DeleteFile(fontLicense);
        }
        foreach (var (file, binding, asynchronous) in new[]
                 {
                     ("framework.js", "prepareFrameworkRuntime", true),
                     ("video.js", "installWebVideo", false),
                     ("text.js", "installWebText", false)
                 })
        {
            // Own modules have one public function. Separate scopes preserve local
            // identifiers without a fragile general-purpose JavaScript rewriter.
            main = ReplaceOnce(main, @"^import \{ " + binding + @" \} from '\./" +
                Regex.Escape(file) + @"(?:\?benginev=[0-9a-f]{64})?';\r?$", string.Empty);
            var body = files.ReadAllText(paths.Combine(webRoot, file));
            var declaration = (asynchronous ? "async " : string.Empty) + "function " + binding;
            body = ReplaceOnce(body, "^export " + declaration + @"\(", declaration + "(");
            combined.Append("const ").Append(binding).Append(" = (() => {\n")
                .Append(body).Append("\nreturn ").Append(binding).Append(";\n})();\n");
        }

        const string marker = "/* BENGINE_CONTENT_MANIFEST */ null";
        if (main.Split(marker, StringSplitOptions.None).Length != 2)
            throw new InvalidDataException("The Web player has no unique content manifest marker.");
        using var manifest = JsonDocument.Parse(files.ReadAllText(paths.Combine(webRoot, "bengine-content.json")));
        main = main.Replace(marker, JsonSerializer.Serialize(manifest.RootElement), StringComparison.Ordinal);
        main = ReplaceOnce(main, "^export function createStartupDownloads\\(", "function createStartupDownloads(");
        combined.Append(main);
        var bytes = Encoding.UTF8.GetBytes(combined.ToString());
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);
        files.WriteAllBytes(paths.Combine(webRoot, "player.bin"), compressed.ToArray());

        // Only engine-owned intermediates are removed. Project content is staged
        // separately and protected from collisions by WebPlayerContentLayout.
        foreach (var name in new[] { "main.js", "framework.js", "video.js", "text.js", "bengine-content.json", "BrotliDecoder.LICENSE.txt", "FreeType.LICENSE.txt", "index.html" })
        {
            if (name != "index.html") files.DeleteFile(paths.Combine(webRoot, name));
            foreach (var extension in new[] { ".gz", ".br" })
            {
                var sidecar = paths.Combine(webRoot, name + extension);
                if (files.FileExists(sidecar)) files.DeleteFile(sidecar);
            }
        }
    }

    private static string ReplaceOnce(string source, string pattern, string replacement)
    {
        var expression = new Regex(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (expression.Matches(source).Count != 1)
            throw new InvalidDataException($"The Web player module does not match its build contract: {pattern}");
        return expression.Replace(source, _ => replacement);
    }
}
