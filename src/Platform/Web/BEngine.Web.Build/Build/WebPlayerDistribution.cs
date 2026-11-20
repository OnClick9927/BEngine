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
        var modules = new[] { "framework.js", "video.js", "text.js", "platform.js" };
        var sources = BuildIndex515(modules.Append("main.js"), StringComparer.Ordinal);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex515(global::System.Collections.Generic.IEnumerable<string> indexSource515, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer515)
        {
            ArgumentNullException.ThrowIfNull(indexSource515, "source");
            var knownCount = indexSource515.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer515);
            global::System.ReadOnlySpan<string> values;
            if (knownCount && indexSource515 is string[] array)
                values = array;
            else if (knownCount && indexSource515 is global::System.Collections.Generic.List<string> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer515);
                foreach (var item in indexSource515)
                    result.Add(IndexKey515(item), IndexValue515(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer515);
                foreach (var item in values)
                    result.Add(IndexKey515(item), IndexValue515(item));
                return result;
            }
        
            string IndexKey515(string name) => name;
            string IndexValue515(string name) => files.ReadAllText(paths.Combine(webRoot, name));
        }
        var exports = BuildIndex684(modules, StringComparer.Ordinal);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string[]> BuildIndex684(global::System.Collections.Generic.IEnumerable<string> indexSource684, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer684)
        {
            ArgumentNullException.ThrowIfNull(indexSource684, "source");
            var knownCount = indexSource684.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string[]>(indexComparer684);
            global::System.ReadOnlySpan<string> values;
            if (knownCount && indexSource684 is string[] array)
                values = array;
            else if (knownCount && indexSource684 is global::System.Collections.Generic.List<string> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string[]>(capacity, indexComparer684);
                foreach (var item in indexSource684)
                    result.Add(IndexKey684(item), IndexValue684(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string[]>(values.Length, indexComparer684);
                foreach (var item in values)
                    result.Add(IndexKey684(item), IndexValue684(item));
                return result;
            }
        
            string IndexKey684(string name) => name;
            string[] IndexValue684(string name) => GetExports(sources[name]);
        }
        var allExports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            foreach (var binding in exports[module])
                if (!allExports.Add(binding)) throw new InvalidDataException($"Duplicate Web player module binding: {binding}");
            var import = new Regex(@"^import\s*\{(?<bindings>[^}]+)\}\s*from\s*['""]\./" +
                Regex.Escape(module) + @"(?:\?benginev=[0-9a-f]{64})?['""];[ \t]*\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var references = 0;
            foreach (var owner in sources.Keys.ToArray())
            {
                sources[owner] = import.Replace(sources[owner], match =>
                {
                    var bindings = match.Groups["bindings"].Value.Split(',').Select(binding => binding.Trim())
                        .Where(binding => binding.Length > 0).ToArray();
                    if (bindings.Length == 0 || Query1785())
                        throw new InvalidDataException($"Invalid Web player module import from {module} in {owner}.");
                    bool Query1785()
                    {
                        var querySource1785 = bindings;
                        ArgumentNullException.ThrowIfNull(querySource1785, "source");
                        if (querySource1785.GetType() == typeof(string[]) || querySource1785.GetType() == typeof(global::System.Collections.Generic.List<string>))
                        {
                            global::System.ReadOnlySpan<string> queryValues1785 = querySource1785.GetType() == typeof(string[]) ? (string[])(object)querySource1785 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)querySource1785);
                            foreach (var binding in queryValues1785)
                            {
                                if (!exports[module].Contains(binding, StringComparer.Ordinal))
                                    return true;
                            }
                        }
                        else
                        {
                            foreach (var binding in querySource1785)
                            {
                                if (!exports[module].Contains(binding, StringComparer.Ordinal))
                                    return true;
                            }
                        }
                    
                        return false;
                    }
                    references++;
                    return string.Empty;
                });
            }
            if (references == 0) throw new InvalidDataException($"The Web player does not import its {module} module.");
        }
        var main = sources["main.js"];
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
        foreach (var module in modules)
        {
            // The engine-owned modules export named functions. Isolated scopes
            // preserve private identifiers, including modules imported by platform.js.
            var body = ExportFunctions.Replace(sources[module], match => match.Groups["declaration"].Value);
            ValidateNoModuleSyntax(body, module);
            var bindings = string.Join(", ", exports[module]);
            combined.Append("const { ").Append(bindings).Append(" } = (() => {\n")
                .Append(body).Append("\nreturn { ").Append(bindings).Append(" };\n})();\n");
        }

        const string marker = "/* BENGINE_CONTENT_MANIFEST */ null";
        if (main.Split(marker, StringSplitOptions.None).Length != 2)
            throw new InvalidDataException("The Web player has no unique content manifest marker.");
        using var manifest = JsonDocument.Parse(files.ReadAllText(paths.Combine(webRoot, "bengine-content.json")));
        main = main.Replace(marker, JsonSerializer.Serialize(manifest.RootElement), StringComparison.Ordinal);
        main = ReplaceOnce(main, "^export function createStartupDownloads\\(", "function createStartupDownloads(");
        ValidateNoModuleSyntax(main, "main.js");
        combined.Append(main);
        var bytes = Encoding.UTF8.GetBytes(combined.ToString());
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);
        files.WriteAllBytes(paths.Combine(webRoot, "player.bin"), compressed.ToArray());

        // Only engine-owned intermediates are removed. Project content is staged
        // separately and protected from collisions by WebPlayerContentLayout.
        foreach (var name in new[] { "main.js", "framework.js", "video.js", "text.js", "platform.js", "bengine-content.json", "BrotliDecoder.LICENSE.txt", "FreeType.LICENSE.txt", "index.html" })
        {
            if (name != "index.html") files.DeleteFile(paths.Combine(webRoot, name));
            foreach (var extension in new[] { ".gz", ".br" })
            {
                var sidecar = paths.Combine(webRoot, name + extension);
                if (files.FileExists(sidecar)) files.DeleteFile(sidecar);
            }
        }
    }

    private static readonly Regex ExportFunctions = new(
        @"^export[ \t]+(?<declaration>(?:async[ \t]+)?function[ \t]+(?<name>[$A-Z_a-z][$\w]*)[ \t]*\()",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string[] GetExports(string source)
    {
        var names = ExportFunctions.Matches(source).Select(match => match.Groups["name"].Value).ToArray();
        if (names.Length == 0 || ((((global::System.Collections.Generic.IEnumerable<string>)(names)), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems10478, distinctComparer10478) => distinctItems10478.ToHashSet<string>(distinctComparer10478).Count
        }) != names.Length)
            throw new InvalidDataException("The Web player module has no unique named function exports.");
        return names;
    }

    private static void ValidateNoModuleSyntax(string source, string module)
    {
        if (Regex.IsMatch(source, @"^[ \t]*(?:import|export)[ \t{]",
                RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException($"Unsupported Web player module import or export in {module}.");
    }

    private static string ReplaceOnce(string source, string pattern, string replacement)
    {
        var expression = new Regex(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (expression.Matches(source).Count != 1)
            throw new InvalidDataException($"The Web player module does not match its build contract: {pattern}");
        return expression.Replace(source, _ => replacement);
    }
}
