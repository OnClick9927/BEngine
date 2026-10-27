using System.Security.Cryptography;
using System.Text.Json;
using BEngine.Build;
using Mono.Cecil;

namespace BEngine.Editor;

/// <summary>The API actually retained in a published Host; content updates can target this exact contract.</summary>
public static class PlayerHostContract
{
    public sealed class Document
    {
        public string Format { get; set; } = "BEngine.HostContract";
        public int Version { get; set; } = 2;
        public string Target { get; set; } = string.Empty;
        public string[] Backends { get; set; } = [];
        public bool RuntimeShaderCompilation { get; set; }
        public Dictionary<string, Dictionary<string, TypeContract>> Assemblies { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> PrecompiledCodeSha256 { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class TypeContract
    {
        public string[] Methods { get; set; } = [];
        public string[] MethodNames { get; set; } = [];
        public string[] Fields { get; set; } = [];
        public string[] MethodContracts { get; set; } = [];
        public string[] FieldContracts { get; set; } = [];
        public bool Forwarder { get; set; }
    }

    public static string Write(PlayerBuildContext context, IEnumerable<string> assemblyPaths)
    {
        var document = new Document { Target = context.Target.TargetId,
            Backends = context.GraphicsBackends.Select(static b => b.ToString()).ToArray(),
            RuntimeShaderCompilation = context.Request.RuntimeShaderCompilation };
        if (context.Target.Platform == BuildTargetPlatform.Web &&
            context.Request.WebScriptCompilation == WebScriptCompilationMode.AheadOfTime)
        {
            // Native code also binds to its original package metadata. A code
            // update must preserve those dependencies even when they execute IL.
            foreach (var path in context.WebPrecompiledManagedCodeReferencePaths.Concat(context.WebPrecompiledManagedCodeAssemblyPaths)
                         .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                var name = ReadCodeAssemblyName(path);
                if (!document.PrecompiledCodeSha256.TryAdd(name, ComputeCodeSha256(path)))
                    throw new InvalidDataException($"Duplicate Web precompiled code module '{name}'.");
            }
        }
        foreach (var path in assemblyPaths.Order(StringComparer.Ordinal))
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var types = new Dictionary<string, TypeContract>(StringComparer.Ordinal);
            foreach (var type in Types(assembly.MainModule.Types))
                types[type.FullName] = new() { Methods = type.Methods.Select(PlayerManagedCodeTrimmer.MethodSignature).ToArray(),
                    MethodNames = type.Methods.Select(static m => m.Name).Distinct().ToArray(),
                    Fields = type.Fields.Select(static f => f.Name).ToArray(),
                    MethodContracts = type.Methods.Select(MethodContract).ToArray(),
                    FieldContracts = type.Fields.Select(static f => TypeContractName(f.FieldType) + " " + f.Name).ToArray() };
            foreach (var type in assembly.MainModule.ExportedTypes)
                types.TryAdd(type.FullName, new() { Forwarder = true });
            document.Assemblies[assembly.Name.Name] = types;
        }
        var directory = Path.Combine(context.Request.ProjectPath, "Temp", "BuildReports");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, $"host-contract-{context.Target.TargetId}-{Guid.NewGuid():N}.json");
        File.WriteAllText(output, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        return output;
    }

    public static void Validate(string contractPath, PlayerBuildContext context, IEnumerable<string> assemblyPaths)
    {
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(contractPath)) ??
            throw new InvalidDataException("Empty Host contract.");
        if (document.Assemblies is null || document.Backends is null || document.PrecompiledCodeSha256 is null ||
            document.Assemblies.Values.Any(static types =>
                types is null || types.Values.Any(static type => type is null || type.Methods is null || type.MethodNames is null ||
                    type.Fields is null || type.MethodContracts is null || type.FieldContracts is null)))
            throw new InvalidDataException("Malformed Host contract.");
        if (document.Format != "BEngine.HostContract" || document.Version != 2 || document.Target != context.Target.TargetId ||
            !document.Backends.SequenceEqual(context.GraphicsBackends.Select(static b => b.ToString())) ||
            document.RuntimeShaderCompilation != context.Request.RuntimeShaderCompilation)
            throw new InvalidDataException("Host contract format, target, backend order or shader policy differs from this content update.");
        var paths = assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ValidatePrecompiledCode(document.PrecompiledCodeSha256, paths);
        var roots = PlayerHostContractRoots.Build(paths);
        var missing = new List<string>();
        foreach (var assembly in roots.Elements("assembly"))
        {
            var name = (string)assembly.Attribute("fullname")!;
            foreach (var type in assembly.Elements("type"))
            {
                var typeName = (string)type.Attribute("fullname")!;
                if (!document.Assemblies.TryGetValue(name, out var types) || !types.TryGetValue(typeName, out var retained))
                { missing.Add(name + ":" + typeName); continue; }
                if (retained.Forwarder) continue;
                foreach (var method in type.Elements("method"))
                {
                    var signature = (string?)method.Attribute("signature");
                    var methodName = (string?)method.Attribute("name");
                    if (signature is not null ? !retained.Methods.Contains(signature) : !retained.MethodNames.Contains(methodName))
                        missing.Add(name + ":" + typeName + ":" + (signature ?? methodName));
                }
                foreach (var field in type.Elements("field"))
                    if (!retained.Fields.Contains((string)field.Attribute("name")!))
                        missing.Add(name + ":" + typeName + ":" + (string)field.Attribute("name")!);
            }
        }
        // XML intentionally preserves generic overload groups conservatively. Validate
        // direct references with CLR signatures as well, including generic arity and field types.
        var dynamicNames = paths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var resolver = new DefaultAssemblyResolver();
        var platformPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in paths.Concat(PlayerSerializationRoots.HostAssemblyPaths).Concat(platformPaths)
                     .Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
            if (directory is not null) resolver.AddSearchDirectory(directory);
        foreach (var path in paths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
            if (DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(assembly.Name.Name)) continue;
            foreach (var member in assembly.MainModule.GetMemberReferences())
            {
                try
                {
                    var definition = member is MethodReference method ? (IMemberDefinition?)method.GetElementMethod().Resolve() :
                        member is FieldReference field ? field.Resolve() : null;
                    if (definition is null) continue;
                    var owner = definition.DeclaringType;
                    var ownerAssembly = owner.Module.Assembly.Name.Name;
                    if (dynamicNames.Contains(ownerAssembly) && !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(ownerAssembly)) continue;
                    var key = definition is MethodDefinition called ? MethodContract(called) :
                        TypeContractName(((FieldDefinition)definition).FieldType) + " " + definition.Name;
                    if (!document.Assemblies.TryGetValue(ownerAssembly, out var types) || !types.TryGetValue(owner.FullName, out var type) ||
                        !(definition is MethodDefinition ? type.MethodContracts : type.FieldContracts).Contains(key))
                        missing.Add(ownerAssembly + ":" + owner.FullName + ":" + key);
                }
                catch (AssemblyResolutionException) { missing.Add("Unresolved Host contract reference: " + member.FullName); }
                catch (ResolutionException) { missing.Add("Unresolved Host contract reference: " + member.FullName); }
            }
        }
        if (missing.Count > 0)
            throw new InvalidDataException("Content update requires APIs absent from the selected main Host. " +
                "Preserve them in link.xml and rebuild the main package before publishing this update:\n" +
                string.Join("\n", missing.Take(50)));
    }

    private static void ValidatePrecompiledCode(Dictionary<string, string> recordedHashes, IReadOnlyList<string> paths)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, hash) in recordedHashes)
            if (!IsCodeAssemblyName(name) || hash is null || hash.Length != 64 ||
                !hash.All(static character => Uri.IsHexDigit(character)) || !hashes.TryAdd(name, hash))
                throw new InvalidDataException("Malformed Host contract Web precompiled code names or SHA-256 hashes.");
        if (hashes.Count == 0) return;

        // Content partitions may omit built-in modules. Only a module included in this
        // update must match its native image; unchanged assets and symbols remain valid.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var name = ReadCodeAssemblyName(path);
            if (!names.Add(name))
                throw new InvalidDataException($"Duplicate content update code module '{name}'.");
            if (hashes.TryGetValue(name, out var expected) &&
                !ComputeCodeSha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Content update changes Web precompiled code module '{name}'. " +
                    "Rebuild the Web main package before publishing this code update. " +
                    "Asset-only and symbol-only updates can retain the existing main package.");
        }
    }

    private static string ReadCodeAssemblyName(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var name = assembly.Name.Name;
        if (!IsCodeAssemblyName(name) ||
            !name.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Code module filename '{Path.GetFileName(path)}' does not match " +
                $"its metadata assembly name '{name}'. Web precompiled code requires correctly named modules.");
        return name;
    }

    private static bool IsCodeAssemblyName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.Equals(name.Trim(), StringComparison.Ordinal) &&
        !name.Any(static character => char.IsControl(character) || character is '/' or '\\' or ',' or ':' or
            '"' or '<' or '>' or '|' or '?' or '*');

    private static string ComputeCodeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string MethodContract(MethodDefinition method) => TypeContractName(method.ReturnType) + " " + method.Name +
        "``" + method.GenericParameters.Count + "(" + string.Join(',', method.Parameters.Select(static p => TypeContractName(p.ParameterType))) + ")";

    private static string TypeContractName(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        GenericInstanceType generic => TypeContractName(generic.ElementType) + "<" + string.Join(',', generic.GenericArguments.Select(TypeContractName)) + ">",
        ArrayType array => TypeContractName(array.ElementType) + (array.IsVector ? "[]" : array.Rank == 1 ? "[*]" : "[" + new string(',', array.Rank - 1) + "]"),
        ByReferenceType reference => TypeContractName(reference.ElementType) + "&",
        Mono.Cecil.PointerType pointer => TypeContractName(pointer.ElementType) + "*",
        RequiredModifierType modifier => TypeContractName(modifier.ElementType) + " modreq(" + TypeContractName(modifier.ModifierType) + ")",
        OptionalModifierType modifier => TypeContractName(modifier.ElementType) + " modopt(" + TypeContractName(modifier.ModifierType) + ")",
        _ => type.FullName
    };

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
