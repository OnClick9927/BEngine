using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using BEngine.Platform;
using BEngine.ProjectSystem;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BEngine.Editor;

internal static class PlayerManagedCodeTrimmer
{
    // Resource text can contain megabytes of uninterrupted identifiers (encoded data,
    // generated tables). Backtracking retries at every character become quadratic.
    private static readonly Regex QualifiedTypeNames = new(
        @"[A-Za-z_][A-Za-z0-9_+`]*(?:\.[A-Za-z_][A-Za-z0-9_+`]*)+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex TypeFields = new(
        "(?im)(?:[\\\"']?(?:[A-Za-z_][A-Za-z0-9_.]*\\.)?\\$?type(?:Name)?[\\\"']?\\s*[:=]\\s*[\\\"']?|!)([A-Za-z_][A-Za-z0-9_.+`]*)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static async Task<RuntimeManagedCodeReleaseInputSet> TrimAsync(
        PlayerBuildContext context,
        ProjectWorkspace workspace,
        RuntimeManagedCodeReleaseInputSet inputs,
        IEnumerable<string> assetFiles,
        PlayerStrippingSettings settings,
        PlayerStrippingReport report,
        IReadOnlySet<string> moduleAssemblies,
        CancellationToken cancellationToken)
    {
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        context.StrippingWorkDirectory ??= paths.Combine(workspace.RootPath, "Temp", "PlayerStripping", Guid.NewGuid().ToString("N"));
        var directory = paths.Combine(context.StrippingWorkDirectory, "pass-" + Guid.NewGuid().ToString("N"));
        files.CreateDirectory(directory);
        var output = paths.Combine(directory, "linked");
        files.CreateDirectory(output);
        var descriptor = paths.Combine(directory, "roots.xml");
        var root = BuildRoots(inputs.Assemblies.Select(static a => a.AssemblyPath)
            .Concat(PlayerSerializationRoots.HostAssemblyPaths).Append(typeof(Transform).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase), assetFiles, settings, moduleAssemblies,
            context.RuntimeManagedCodeAssemblyPaths);
        var descriptors = PlayerStrippingSettings.GetLinkDescriptors(workspace);
        var availableAssemblies = inputs.Assemblies.Select(static a => a.AssemblyPath)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(static a => !a.IsDynamic).Select(static a => a.Location))
            .Concat((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        PlayerLinkXml.MergeInto(root, descriptors, availableAssemblies);
        report.LinkDescriptors = descriptors.ToList();
        context.ReflectionRootDescriptorPath = descriptor;
        if (!root.Elements().Any()) throw new InvalidDataException("Full trimming found no runtime entry points.");
        new XDocument(root).Save(descriptor);
        var linker = ResolveLinker(workspace);
        var request = new ProcessRunRequest("dotnet")
        {
            WorkingDirectory = directory, CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        BEngine.Tooling.ToolEnvironment.ApplyToDotNetBuild(request.EnvironmentVariables);
        foreach (var argument in new[] { linker, "-x", descriptor, "-out", output,
                     "--action", "skip", "--trim-mode", "skip", "--deterministic", "--skip-unresolved", "false" })
            request.Arguments.Add(argument);
        // Release IL is linked on the editor's OS against its framework reference
        // closure. Inlining implementation constants here would bake that OS into
        // a mobile release (for example IsAndroid=false and IsWindows=true).
        // The target SDK's final host pass can still optimize for its actual OS.
        request.Arguments.Add("--disable-opt");
        request.Arguments.Add("ipconstprop");
        // This is a partial link: skipped host/framework assemblies can construct
        // subtypes which are not visible as instantiated in the release inputs.
        // Folding those type tests to null breaks valid results from their factories.
        // The final closed-world host link retains its normal optimization policy.
        request.Arguments.Add("--disable-opt");
        request.Arguments.Add("unusedtypechecks");
        if (context.Request.IncludeDebugSymbols) request.Arguments.Add("-b");
        var references = inputs.Assemblies.Select(static a => a.AssemblyPath)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(static a => !a.IsDynamic)
                .Select(static a => a.Location).Where(static p => !string.IsNullOrWhiteSpace(p)))
            .Concat((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Where(files.FileExists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var reference in inputs.Assemblies.Select(static a => a.AssemblyPath))
        {
            request.Arguments.Add("-reference"); request.Arguments.Add(reference);
        }
        foreach (var search in references.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            request.Arguments.Add("-d"); request.Arguments.Add(search!);
        }
        // Core is supplied by the static host. Only release inputs are rewritten here;
        // the host's own full ILLink pass processes Core and the framework independently.
        foreach (var input in inputs.Assemblies)
        {
            request.Arguments.Add("--action"); request.Arguments.Add("link"); request.Arguments.Add(input.Name);
        }
        var result = await ProcessUtility.Current.RunAsync(request, cancellationToken).ConfigureAwait(false);
        report.DependencyPasses++;
        report.LinkerLog = result.StandardOutput + result.StandardError;
        files.WriteAllText(paths.Combine(directory, "linker.log"), report.LinkerLog);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Managed-code trimming failed ({result.ExitCode}).\n{report.LinkerLog}");
        var names = inputs.Assemblies.Where(a => files.FileExists(paths.Combine(output, a.Name + ".dll")))
            .Select(static a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linked = new List<RuntimeManagedCodeReleaseInput>();
        report.Assemblies.Clear();
        foreach (var input in inputs.Assemblies)
        {
            var path = paths.Combine(output, input.Name + ".dll");
            var exists = names.Contains(input.Name);
            report.Assemblies.Add(new PlayerStrippingReportEntry
            {
                Name = input.Name, OriginalBytes = input.AssemblySize,
                PublishedBytes = exists ? files.GetFileLength(path) : 0, Included = exists,
                Reason = exists ? "Reachable runtime entry, referenced code or explicit preservation" : "No reachable code"
            });
            if (!exists) continue;
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var dependencies = assembly.MainModule.AssemblyReferences
                .Select(static a => a.Name).Where(names.Contains).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
            using var stream = files.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var symbols = paths.Combine(output, input.Name + ".pdb");
            linked.Add(input with { AssemblyPath = path, AssemblySha256 = hash,
                AssemblySize = files.GetFileLength(path), BuildId = "trim-" + hash[..24],
                SymbolsPath = context.Request.IncludeDebugSymbols && files.FileExists(symbols) ? symbols : null,
                Dependencies = dependencies });
        }
        if (!names.Contains("AOT")) throw new InvalidDataException("Full trimming removed the AOT entry assembly.");
        return new RuntimeManagedCodeReleaseInputSet(linked, inputs.PackageResources, inputs.PackageResourceRoots);
    }

    internal static XElement BuildRoots(IEnumerable<string> assemblyPaths, IEnumerable<string> assetFiles,
        PlayerStrippingSettings settings, IReadOnlySet<string>? moduleAssemblies = null,
        IEnumerable<string>? typeDiscoveryAssemblyPaths = null)
    {
        var assemblyFiles = assemblyPaths.ToArray();
        var messageNames = GetStringConstants(assemblyFiles);
        var strings = new HashSet<string>(settings.PreservedTypes, StringComparer.Ordinal);
        foreach (var path in assetFiles)
        {
            if (!IsTextAsset(path)) continue;
            CollectAssetTypeNames(path, strings);
        }
        // A Resources DTO may live in the game domain and expose package types in
        // its fields/base class. Discover that graph without rooting/loading the
        // game assembly in the startup domain.
        ExpandSerializedTypes(assemblyFiles.Concat(typeDiscoveryAssemblyPaths ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase), strings);
        var root = new XElement("linker");
        root.Add(new XElement("assembly", new XAttribute("fullname", "AOT"),
            new XElement("type", new XAttribute("fullname", "<Module>"), new XAttribute("preserve", "nothing"))));
        var foundExplicit = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in assemblyFiles.OrderBy(static p => p, StringComparer.Ordinal))
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var item = new XElement("assembly", new XAttribute("fullname", assembly.Name.Name));
            var types = EnumerateTypes(assembly.MainModule.Types).ToArray();
            var entryAssembly = moduleAssemblies is null || moduleAssemblies.Contains(assembly.Name.Name) ||
                types.Any(t => strings.Contains(t.FullName.Replace('/', '+')) || strings.Contains(t.Name));
            foreach (var type in types)
            {
                var name = type.FullName.Replace('/', '+');
                var explicitRoot = settings.PreservedTypes.Contains(name, StringComparer.Ordinal);
                if (explicitRoot) foundExplicit.Add(name);
                var module = entryAssembly && type.Interfaces.Any(static i =>
                    i.InterfaceType.FullName is "BEngine.HotUpdate.IHotUpdateModule" or "BEngine.ISceneRuntimeSystem" or
                        "BEngine.DependencyInjection.IEngineServiceModule");
                var initializers = entryAssembly ? type.Methods.Where(static m => m.CustomAttributes.Any(static a =>
                    a.AttributeType.FullName == "BEngine.RuntimeInitializeOnLoadMethodAttribute")).ToArray() : [];
                var reflected = strings.Contains(name) || strings.Contains(type.Name);
                if (!explicitRoot && !module && !reflected && initializers.Length == 0) continue;
                var element = new XElement("type", new XAttribute("fullname", type.FullName),
                    new XAttribute("preserve", explicitRoot ? "all" : "nothing"));
                if (!explicitRoot)
                {
                    foreach (var method in type.Methods.Where(m => initializers.Contains(m) ||
                                 (module || reflected) && (m.IsConstructor || m.IsVirtual || m.IsGetter || m.IsSetter ||
                                     !m.IsStatic && m.ReturnType.FullName == "System.Void" && m.Parameters.Count <= 1 && messageNames.Contains(m.Name))))
                        element.Add(new XElement("method", new XAttribute("signature", MethodSignature(method))));
                    if (module || reflected)
                        foreach (var field in type.Fields.Where(static f => !f.IsStatic && !f.IsInitOnly &&
                                     !f.CustomAttributes.Any(static a => a.AttributeType.FullName is
                                         "System.NonSerializedAttribute" or "YamlDotNet.Serialization.YamlIgnoreAttribute") &&
                                     (f.IsPublic || f.CustomAttributes.Any(static a => a.AttributeType.FullName is
                                         "BEngine.SerializeFieldAttribute" or "BEngine.SerializeReferenceAttribute"))))
                            element.Add(new XElement("field", new XAttribute("name", field.Name)));
                }
                item.Add(element);
            }
            if (item.Elements().Any())
            {
                root.Add(item);
            }
        }
        var missing = settings.PreservedTypes.Except(foundExplicit, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0) throw new InvalidDataException("Preserved types were not found: " + string.Join(", ", missing));
        // Reloadable/AOT inputs are linked before the desktop host. Preserve
        // serializer DTO constructors/accessors here as well: once this pass
        // removes them, the later host descriptor cannot restore the metadata.
        PlayerSerializationRoots.Add(root, assemblyFiles);
        return root;
    }

    internal static string MethodSignature(MethodDefinition method) =>
        method.ReturnType.FullName + " " + method.Name + "(" +
        string.Join(",", method.Parameters.Select(static p => p.ParameterType.FullName)) + ")";

    internal static IReadOnlySet<string> GetAssetReferencedAssemblies(
        IEnumerable<string> assemblyPaths, IEnumerable<string> assetFiles)
    {
        var paths = assemblyPaths.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in assetFiles)
            if (IsTextAsset(path)) CollectAssetTypeNames(path, names);
        if (names.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ExpandSerializedTypes(paths, names);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            if (EnumerateTypes(assembly.MainModule.Types).Any(type =>
                    names.Contains(type.FullName.Replace('/', '+')) || names.Contains(type.Name)))
                result.Add(assembly.Name.Name);
        }
        return result;
    }

    private static void CollectAssetTypeNames(string path, HashSet<string> names)
    {
        var text = File.ReadAllText(path);
        // Qualified names also cover custom data formats. Bare identifiers are only
        // types when they occur in a type field/tag, never arbitrary CSS/text words.
        foreach (Match match in QualifiedTypeNames.Matches(text))
            names.Add(match.Value);
        foreach (Match match in TypeFields.Matches(text))
            names.Add(match.Groups[1].Value);
        if (Path.GetExtension(path).ToLowerInvariant() is not (".bxml" or ".xml")) return;
        using var reader = XmlReader.Create(new StringReader(text),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        foreach (var element in document.Descendants())
        {
            names.Add(element.Name.LocalName);
            var ns = element.Name.NamespaceName;
            if (ns.StartsWith("clr-namespace:", StringComparison.Ordinal)) ns = ns[14..].Split(';')[0];
            if (ns.Length > 0 && Regex.IsMatch(ns, @"^[A-Za-z_][A-Za-z0-9_.]*$"))
                names.Add(ns + "." + element.Name.LocalName);
        }
    }

    private static void ExpandSerializedTypes(IEnumerable<string> paths, HashSet<string> names)
    {
        var assemblies = paths.Select(AssemblyDefinition.ReadAssembly).ToArray();
        try
        {
            var types = assemblies.SelectMany(static a => EnumerateTypes(a.MainModule.Types))
                .GroupBy(static t => t.FullName.Replace('/', '+'), StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
            var pending = new Queue<TypeDefinition>(types.Values.Where(t => names.Contains(t.FullName.Replace('/', '+')) || names.Contains(t.Name)));
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out var type))
            {
                if (!visited.Add(type.FullName)) continue;
                if (type.BaseType is { } parent) Add(parent);
                foreach (var field in type.Fields.Where(static f => !f.IsStatic && !f.IsInitOnly &&
                             !f.CustomAttributes.Any(static a => a.AttributeType.FullName is "System.NonSerializedAttribute" or "YamlDotNet.Serialization.YamlIgnoreAttribute") &&
                             (f.IsPublic || f.CustomAttributes.Any(static a => a.AttributeType.FullName is "BEngine.SerializeFieldAttribute" or "BEngine.SerializeReferenceAttribute"))))
                    Add(field.FieldType);
                foreach (var property in type.Properties.Where(static p => p.GetMethod is { IsPublic: true } && p.SetMethod is { IsPublic: true } &&
                             !p.CustomAttributes.Any(static a => a.AttributeType.FullName == "YamlDotNet.Serialization.YamlIgnoreAttribute")))
                    Add(property.PropertyType);
            }
            void Add(TypeReference reference)
            {
                if (reference is GenericInstanceType generic)
                    foreach (var argument in generic.GenericArguments) Add(argument);
                var name = reference.GetElementType().FullName.Replace('/', '+');
                if (names.Add(name) && types.TryGetValue(name, out var type)) pending.Enqueue(type);
            }
        }
        finally { foreach (var assembly in assemblies) assembly.Dispose(); }
    }

    internal static HashSet<string> GetStringConstants(IEnumerable<string> paths)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (var type in EnumerateTypes(assembly.MainModule.Types))
            foreach (var method in type.Methods.Where(static m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
                if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string { Length: > 0 } value)
                    values.Add(value.Replace('\\', '/'));
        }
        return values;
    }

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in EnumerateTypes(type.NestedTypes)) yield return nested;
        }
    }

    internal static bool IsTextAsset(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".yaml" or ".yml" or ".bxml" or ".bss" or ".bcss" or ".json" or ".xml" or ".txt" or
        ".shader" or ".glsl" or ".glslinc" or ".hlsl" or ".hlslinc";

    private static string ResolveLinker(ProjectWorkspace workspace)
    {
        foreach (var directory in new[] { Path.GetDirectoryName(typeof(PlayerManagedCodeTrimmer).Assembly.Location)!,
                     Path.GetDirectoryName(Path.GetDirectoryName(typeof(PlayerManagedCodeTrimmer).Assembly.Location))!,
                     EditorHostServices.Platform.BaseDirectory })
        {
            var path = Path.Combine(directory, "BuildTools", "ILLink", "illink.dll");
            if (File.Exists(path)) return path;
            path = Path.Combine(directory, "Editor", "BuildTools", "ILLink", "illink.dll");
            if (File.Exists(path)) return path;
        }
        if (Directory.Exists(workspace.PackageCachePath))
        {
            var tool = Directory.EnumerateFiles(workspace.PackageCachePath, "illink.dll", SearchOption.AllDirectories)
                .Where(static p => p.Replace('\\', '/').Contains("/BuildTools/ILLink/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static p => p, StringComparer.Ordinal).FirstOrDefault();
            if (tool is not null) return tool;
        }
        throw new FileNotFoundException("Full stripping requires the exported BuildTools/ILLink tool files. Re-export PlayerBuild.");
    }
}
