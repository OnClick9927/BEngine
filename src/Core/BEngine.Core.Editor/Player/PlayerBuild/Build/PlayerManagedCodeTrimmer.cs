using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;
using BEngine.Platform;
using BEngine.ProjectSystem;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BEngine.Editor.Build;

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
        CancellationToken cancellationToken,
        bool preserveModuleInitializers = false,
        bool webCanonicalPlatform = false)
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
            context.RuntimeManagedCodeAssemblyPaths, preserveModuleInitializers);
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
        WebStaticHostContractFeature.AddLinkerArguments(request.Arguments,
            WebStaticHostContractFeature.IsCanonicalPassEnabled(context, webCanonicalPlatform));
        if (webCanonicalPlatform && WebCanonicalScriptBuild.IsEnabled(context))
        {
            // Only the Web canonical graph is frozen for one known OS. Explicit
            // substitutions let ILLink discard desktop branches without inferring
            // unrelated host/framework constants from the editor's runtime.
            // CoreLib remains skipped and is never rewritten by this partial pass.
            // Parameterized/version predicates retain their normal runtime calls.
            var platformDescriptor = paths.Combine(directory, "web-platform.substitutions.xml");
            WebCanonicalPlatformSubstitutions.Create().Save(platformDescriptor);
            request.Arguments.Add("--substitutions");
            request.Arguments.Add(platformDescriptor);
        }
        if (context.Request.IncludeDebugSymbols) request.Arguments.Add("-b");
        var references = ((((global::System.Collections.Generic.IEnumerable<string>)(inputs.Assemblies.Select(static a => a.AssemblyPath).Concat(AppDomain.CurrentDomain.GetAssemblies().Where(static a => !a.IsDynamic).Select(static a => a.Location).Where(static p => !string.IsNullOrWhiteSpace(p))).Concat((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)).Where(files.FileExists))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems5705, distinctComparer5705) => distinctItems5705.ToHashSet<string>(distinctComparer5705).ToArray()});
        {
            var __projectionSource6228 = inputs.Assemblies;
            ArgumentNullException.ThrowIfNull(__projectionSource6228, "source");
            foreach (var __projectionItem6228 in __projectionSource6228)
            {
                var reference = __projectionItem6228.AssemblyPath;
                request.Arguments.Add("-reference");
                request.Arguments.Add(reference);
            }
        }
        foreach (var search in references.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            request.Arguments.Add("-d"); request.Arguments.Add(search!);
        }
        // Shared host images define the ABI seen by dynamically loaded modules.
        // Rewriting them in this partial pass can remove interface slots and then
        // strip their implementations from a module. The final host link consumes
        // the original images, so keep that same contract throughout this pass.
        foreach (var input in inputs.Assemblies)
        {
            request.Arguments.Add("--action");
            request.Arguments.Add(DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name) ? "skip" : "link");
            request.Arguments.Add(input.Name);
        }
        var result = await ProcessUtility.Current.RunAsync(request, cancellationToken).ConfigureAwait(false);
        report.DependencyPasses++;
        report.LinkerLog = result.StandardOutput + result.StandardError;
        files.WriteAllText(paths.Combine(directory, "linker.log"), report.LinkerLog);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Managed-code trimming failed ({result.ExitCode}).\n{report.LinkerLog}");
        var names = inputs.Assemblies.Where(a => DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(a.Name) ||
                files.FileExists(paths.Combine(output, a.Name + ".dll")))
            .Select(static a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linked = new List<RuntimeManagedCodeReleaseInput>();
        report.Assemblies.Clear();
        foreach (var input in inputs.Assemblies)
        {
            if (DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(input.Name))
            {
                linked.Add(input);
                report.Assemblies.Add(new PlayerStrippingReportEntry
                {
                    Name = input.Name, OriginalBytes = input.AssemblySize,
                    PublishedBytes = input.AssemblySize, Included = true,
                    Reason = "Shared static Host contract; linked only by the final platform build"
                });
                continue;
            }
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
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
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
        IEnumerable<string>? typeDiscoveryAssemblyPaths = null,
        bool preserveModuleInitializers = false)
    {
        var assemblyFiles = assemblyPaths.ToArray();
        var messageNames = GetStringConstants(assemblyFiles);
        var strings = new HashSet<string>(settings.PreservedTypes, StringComparer.Ordinal);
        foreach (var path in assetFiles)
        {
            if (!IsTextAsset(path)) continue;
            CollectAssetTypeNames(path, strings);
        }
        if (preserveModuleInitializers)
            CollectCodeActivatedTypes(assemblyFiles.Concat(typeDiscoveryAssemblyPaths ?? []), strings);
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
            var entryAssembly = moduleAssemblies is null || moduleAssemblies.Contains(assembly.Name.Name) || Query12430();
            bool Query12430()
            {
                var querySource12430 = types;
                ArgumentNullException.ThrowIfNull(querySource12430, "source");
                if (querySource12430.GetType() == typeof(global::Mono.Cecil.TypeDefinition[]) || querySource12430.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.TypeDefinition>))
                {
                    global::System.ReadOnlySpan<global::Mono.Cecil.TypeDefinition> queryValues12430 = querySource12430.GetType() == typeof(global::Mono.Cecil.TypeDefinition[]) ? (global::Mono.Cecil.TypeDefinition[])(object)querySource12430 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.TypeDefinition>)(object)querySource12430);
                    foreach (var t in queryValues12430)
                    {
                        if (strings.Contains(t.FullName.Replace('/', '+')) || strings.Contains(t.Name))
                            return true;
                    }
                }
                else
                {
                    foreach (var t in querySource12430)
                    {
                        if (strings.Contains(t.FullName.Replace('/', '+')) || strings.Contains(t.Name))
                            return true;
                    }
                }
            
                return false;
            }
            foreach (var type in types)
            {
                var name = type.FullName.Replace('/', '+');
                var explicitRoot = settings.PreservedTypes.Contains(name, StringComparer.Ordinal);
                if (explicitRoot) foundExplicit.Add(name);
                var module = entryAssembly && Query14442(type.Interfaces);
                [global::BEngine.SkipLoopGuard]
                bool Query14442(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.InterfaceImplementation> querySource14442)
                {
                    ArgumentNullException.ThrowIfNull(querySource14442, "source");
                    bool Matches14442(global::Mono.Cecil.InterfaceImplementation i) => i.InterfaceType.FullName is "BEngine.Player.HotUpdate.IHotUpdateModule" or "BEngine.ISceneRuntimeSystem" or "BEngine.DependencyInjection.IEngineServiceModule";
                    global::System.ReadOnlySpan<global::Mono.Cecil.InterfaceImplementation> queryValues14442;
                    if (querySource14442.GetType() == typeof(global::Mono.Cecil.InterfaceImplementation[]))
                        queryValues14442 = (global::Mono.Cecil.InterfaceImplementation[])querySource14442;
                    else if (querySource14442.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.InterfaceImplementation>))
                        queryValues14442 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.InterfaceImplementation>)querySource14442);
                    else
                    {
                        foreach (var i in querySource14442)
                            if (Matches14442(i))
                                return true;
                        return false;
                    }
                
                    foreach (var i in queryValues14442)
                        if (Matches14442(i))
                            return true;
                    return false;
                }
                var initializers = entryAssembly ? type.Methods.Where(m =>
                    preserveModuleInitializers && type.Name == "<Module>" && m.IsConstructor ||
                    m.CustomAttributes.Any(static a =>
                        a.AttributeType.FullName == "BEngine.RuntimeInitializeOnLoadMethodAttribute")).ToArray() : [];
                var reflected = strings.Contains(name) || strings.Contains(type.Name);
                if (!explicitRoot && !module && !reflected && initializers.Length == 0) continue;
                var element = new XElement("type", new XAttribute("fullname", type.FullName),
                    new XAttribute("preserve", explicitRoot ? "all" : "nothing"));
                if (!explicitRoot)
                {
                    {
                        var __filteredSource13613 = type.Methods;
                        ArgumentNullException.ThrowIfNull(__filteredSource13613, "source");
                        foreach (var method in __filteredSource13613)
                        {
                            if (!(initializers.Contains(method) || (module || reflected) && (method.IsConstructor || method.IsVirtual || method.IsGetter || method.IsSetter || !method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Count <= 1 && messageNames.Contains(method.Name))))
                                continue;
                            element.Add(new XElement("method", new XAttribute("signature", MethodSignature(method))));
                        }
                    }
                    if (module || reflected)
                        {
                            var __filteredSource14147 = type.Fields;
                            ArgumentNullException.ThrowIfNull(__filteredSource14147, "source");
                            foreach (var field in __filteredSource14147)
                            {
                                if (!(!field.IsStatic && !field.IsInitOnly && !Query16651(field.CustomAttributes) && (field.IsPublic || Query16865(field.CustomAttributes))))
                                    continue;
                                [global::BEngine.SkipLoopGuard]
                                bool Query16651(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttribute> querySource16651)
                                {
                                    ArgumentNullException.ThrowIfNull(querySource16651, "source");
                                    bool Matches16651(global::Mono.Cecil.CustomAttribute a) => a.AttributeType.FullName is "System.NonSerializedAttribute" or "BEngine.SerializationIgnoreAttribute" or "YamlDotNet.Serialization.YamlIgnoreAttribute";
                                    global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttribute> queryValues16651;
                                    if (querySource16651.GetType() == typeof(global::Mono.Cecil.CustomAttribute[]))
                                        queryValues16651 = (global::Mono.Cecil.CustomAttribute[])querySource16651;
                                    else if (querySource16651.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>))
                                        queryValues16651 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>)querySource16651);
                                    else
                                    {
                                        foreach (var a in querySource16651)
                                            if (Matches16651(a))
                                                return true;
                                        return false;
                                    }
                                
                                    foreach (var a in queryValues16651)
                                        if (Matches16651(a))
                                            return true;
                                    return false;
                                }
                                
                                [global::BEngine.SkipLoopGuard]
                                bool Query16865(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttribute> querySource16865)
                                {
                                    ArgumentNullException.ThrowIfNull(querySource16865, "source");
                                    bool Matches16865(global::Mono.Cecil.CustomAttribute a) => a.AttributeType.FullName is "BEngine.SerializeFieldAttribute" or "BEngine.SerializeReferenceAttribute";
                                    global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttribute> queryValues16865;
                                    if (querySource16865.GetType() == typeof(global::Mono.Cecil.CustomAttribute[]))
                                        queryValues16865 = (global::Mono.Cecil.CustomAttribute[])querySource16865;
                                    else if (querySource16865.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>))
                                        queryValues16865 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>)querySource16865);
                                    else
                                    {
                                        foreach (var a in querySource16865)
                                            if (Matches16865(a))
                                                return true;
                                        return false;
                                    }
                                
                                    foreach (var a in queryValues16865)
                                        if (Matches16865(a))
                                            return true;
                                    return false;
                                }
                                element.Add(new XElement("field", new XAttribute("name", field.Name)));
                            }
                        }
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

    private static void CollectCodeActivatedTypes(IEnumerable<string> paths, HashSet<string> names)
    {
        var assemblies = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(AssemblyDefinition.ReadAssembly).ToArray();
        try
        {
            var types = assemblies.SelectMany(static image => EnumerateTypes(image.MainModule.Types))
                .GroupBy(static type => type.FullName, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
            var pending = new Queue<TypeDefinition>();
            var activated = new HashSet<string>(StringComparer.Ordinal);
            {
                var __filteredSource16268 = types.Values;
                ArgumentNullException.ThrowIfNull(__filteredSource16268, "source");
                foreach (var type in __filteredSource16268)
                {
                    if (!(names.Contains(type.FullName.Replace('/', '+')) || names.Contains(type.Name)))
                        continue;
                    Activate(type);
                }
            }
            // These factories execute in a skipped Host assembly. Its reflection
            // over constructors, callbacks and serialized fields is invisible to
            // this partial linker, even when a concrete typeof(T) survives in IL.
            {
                var __filteredSource16711 = types.Values.SelectMany(static type => type.Methods);
                ArgumentNullException.ThrowIfNull(__filteredSource16711, "source");
                foreach (var method in __filteredSource16711)
                {
                    if (!(method.HasBody))
                        continue;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.Code == Code.Ldtoken && instruction.Operand is TypeReference token)
                            Activate(token);
                        if (instruction.Operand is not GenericInstanceMethod generic || !(generic.DeclaringType.FullName == "BEngine.GameObject" && generic.Name == "AddComponent" || generic.DeclaringType.FullName == "BEngine.ScriptableObject" && generic.Name == "CreateInstance"))
                            continue;
                        foreach (var argument in generic.GenericArguments)
                            Activate(argument);
                    }
                }
            }
            while (pending.TryDequeue(out var type))
            {
                if (type.BaseType is { } parent) Activate(parent);
                {
                    var __filteredSource17627 = type.CustomAttributes;
                    ArgumentNullException.ThrowIfNull(__filteredSource17627, "source");
                    foreach (var attribute in __filteredSource17627)
                    {
                        if (!(attribute.AttributeType.FullName == "BEngine.RequireComponentAttribute"))
                            continue;
                        foreach (var argument in attribute.ConstructorArguments)
                            ActivateArgument(argument.Value);
                    }
                }
            }

            void ActivateArgument(object? value)
            {
                if (value is TypeReference reference) Activate(reference);
                else if (value is CustomAttributeArgument[] arguments)
                    foreach (var argument in arguments) ActivateArgument(argument.Value);
            }
            void Activate(TypeReference reference)
            {
                if (!types.TryGetValue(reference.GetElementType().FullName, out var type) ||
                    !IsCreatable(type) || !activated.Add(type.FullName)) return;
                names.Add(type.FullName.Replace('/', '+'));
                pending.Enqueue(type);
            }
            bool IsCreatable(TypeDefinition type)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (visited.Add(type.FullName))
                {
                    if (type.FullName is "BEngine.Component" or "BEngine.ScriptableObject") return true;
                    if (type.BaseType is not { } parent ||
                        !types.TryGetValue(parent.GetElementType().FullName, out type!)) return false;
                }
                return false;
            }
        }
        finally { foreach (var assembly in assemblies) assembly.Dispose(); }
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
            bool __queryResult21577;
            {
                var __querySource21577 = EnumerateTypes(assembly.MainModule.Types);
                ArgumentNullException.ThrowIfNull(__querySource21577, "source");
                __queryResult21577 = false;
                var __querySource21577Type = __querySource21577.GetType();
                if (__querySource21577Type == typeof(Mono.Cecil.TypeDefinition[]) || __querySource21577Type == typeof(System.Collections.Generic.List<Mono.Cecil.TypeDefinition>))
                {
                    System.ReadOnlySpan<Mono.Cecil.TypeDefinition> __querySource21577Span = __querySource21577Type == typeof(Mono.Cecil.TypeDefinition[]) ? (Mono.Cecil.TypeDefinition[])(object)__querySource21577 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<Mono.Cecil.TypeDefinition>)(object)__querySource21577);
                    foreach (var __queryItem21577 in __querySource21577Span)
                    {
                        if ((names.Contains(__queryItem21577.FullName.Replace('/', '+')) || names.Contains(__queryItem21577.Name)))
                        {
                            __queryResult21577 = true;
                            break;
                        }
                    }
                }
                else
                {
                    foreach (var __queryItem21577 in __querySource21577)
                    {
                        if ((names.Contains(__queryItem21577.FullName.Replace('/', '+')) || names.Contains(__queryItem21577.Name)))
                        {
                            __queryResult21577 = true;
                            break;
                        }
                    }
                }
            }
            
            if (__queryResult21577)
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
        foreach (var name in PlayerResourceTextFormats.GetReferencedTypeNames(path, text)) names.Add(name);
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
                {
                    var __filteredSource22584 = type.Fields;
                    ArgumentNullException.ThrowIfNull(__filteredSource22584, "source");
                    foreach (var field in __filteredSource22584)
                    {
                        if (!(!field.IsStatic && !field.IsInitOnly && !Query27786(field.CustomAttributes) && (field.IsPublic || Query28000(field.CustomAttributes))))
                            continue;
                        [global::BEngine.SkipLoopGuard]
                        bool Query27786(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttribute> querySource27786)
                        {
                            ArgumentNullException.ThrowIfNull(querySource27786, "source");
                            bool Matches27786(global::Mono.Cecil.CustomAttribute a) => a.AttributeType.FullName is "System.NonSerializedAttribute" or "BEngine.SerializationIgnoreAttribute" or "YamlDotNet.Serialization.YamlIgnoreAttribute";
                            global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttribute> queryValues27786;
                            if (querySource27786.GetType() == typeof(global::Mono.Cecil.CustomAttribute[]))
                                queryValues27786 = (global::Mono.Cecil.CustomAttribute[])querySource27786;
                            else if (querySource27786.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>))
                                queryValues27786 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>)querySource27786);
                            else
                            {
                                foreach (var a in querySource27786)
                                    if (Matches27786(a))
                                        return true;
                                return false;
                            }
                        
                            foreach (var a in queryValues27786)
                                if (Matches27786(a))
                                    return true;
                            return false;
                        }
                        
                        [global::BEngine.SkipLoopGuard]
                        bool Query28000(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttribute> querySource28000)
                        {
                            ArgumentNullException.ThrowIfNull(querySource28000, "source");
                            bool Matches28000(global::Mono.Cecil.CustomAttribute a) => a.AttributeType.FullName is "BEngine.SerializeFieldAttribute" or "BEngine.SerializeReferenceAttribute";
                            global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttribute> queryValues28000;
                            if (querySource28000.GetType() == typeof(global::Mono.Cecil.CustomAttribute[]))
                                queryValues28000 = (global::Mono.Cecil.CustomAttribute[])querySource28000;
                            else if (querySource28000.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>))
                                queryValues28000 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>)querySource28000);
                            else
                            {
                                foreach (var a in querySource28000)
                                    if (Matches28000(a))
                                        return true;
                                return false;
                            }
                        
                            foreach (var a in queryValues28000)
                                if (Matches28000(a))
                                    return true;
                            return false;
                        }
                        Add(field.FieldType);
                    }
                }
                {
                    var __filteredSource23133 = type.Properties;
                    ArgumentNullException.ThrowIfNull(__filteredSource23133, "source");
                    foreach (var property in __filteredSource23133)
                    {
                        if (!(property.GetMethod is { IsPublic: true } && property.SetMethod is { IsPublic: true } && !Query28650(property.CustomAttributes)))
                            continue;
                        [global::BEngine.SkipLoopGuard]
                        bool Query28650(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttribute> querySource28650)
                        {
                            ArgumentNullException.ThrowIfNull(querySource28650, "source");
                            bool Matches28650(global::Mono.Cecil.CustomAttribute a) => a.AttributeType.FullName is "BEngine.SerializationIgnoreAttribute" or "YamlDotNet.Serialization.YamlIgnoreAttribute";
                            global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttribute> queryValues28650;
                            if (querySource28650.GetType() == typeof(global::Mono.Cecil.CustomAttribute[]))
                                queryValues28650 = (global::Mono.Cecil.CustomAttribute[])querySource28650;
                            else if (querySource28650.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>))
                                queryValues28650 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>)querySource28650);
                            else
                            {
                                foreach (var a in querySource28650)
                                    if (Matches28650(a))
                                        return true;
                                return false;
                            }
                        
                            foreach (var a in queryValues28650)
                                if (Matches28650(a))
                                    return true;
                            return false;
                        }
                        Add(property.PropertyType);
                    }
                }
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
            {
                var __filteredSource24382 = type.Methods;
                ArgumentNullException.ThrowIfNull(__filteredSource24382, "source");
                foreach (var method in __filteredSource24382)
                {
                    if (!(method.HasBody))
                        continue;
                    foreach (var instruction in method.Body.Instructions)
                        if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string { Length: > 0 } value)
                            values.Add(value.Replace('\\', '/'));
                }
            }
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

    internal static bool IsTextAsset(string path) => PlayerResourceTextFormats.IsTextFile(path);

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
