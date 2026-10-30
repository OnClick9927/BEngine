using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using ModuleDefinition = Mono.Cecil.ModuleDefinition;
using TypeReference = Mono.Cecil.TypeReference;

// Build-only native-code policy. The input/output managed images are read-only.
// Each exact exclusion is conditional on both signature and current method body.
// New, changed, ambiguous and unresolved methods retain the baseline selection.
internal static class WebAotColdMethods
{
    private static readonly byte[] ProfileMagic = Encoding.ASCII.GetBytes("AOTPROFILE");
    internal sealed record Entry(string Assembly, string Type, string Name, int GenericArity,
        string Signature, string BodySha256);

    internal static void Apply(string assemblyList, string profilePath, string policyPath)
    {
        var entries = LoadPolicy(policyPath);
        using var resolver = new DefaultAssemblyResolver();
        var modules = new Dictionary<string, ModuleDefinition>(StringComparer.OrdinalIgnoreCase);
        var inputs = File.ReadAllLines(assemblyList).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToArray();
        foreach (var path in inputs)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Web AOT input image is missing.", path);
            resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        }
        try
        {
            foreach (var path in inputs)
            {
                var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
                if (!modules.TryAdd(module.Assembly.Name.Name, module))
                { module.Dispose(); throw Invalid("Duplicate linked assembly name: " + module.Assembly.Name.Name); }
            }
            ApplyCore(modules, profilePath, policyPath, entries);
        }
        finally { foreach (var module in modules.Values) module.Dispose(); }
    }

    private static void ApplyCore(Dictionary<string, ModuleDefinition> modules, string profilePath,
        string policyPath, Entry[] entries)
    {
        var original = File.ReadAllBytes(profilePath);
        var profile = ReadProfile(original);
        var methods = modules.Values.SelectMany(m => m.GetTypes()).SelectMany(t => t.Methods).ToArray();
        var byPortableKey = methods.GroupBy(PortableKey).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var baseline = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
        var profileGroups = new Dictionary<int, string[]>();
        var declared = new Dictionary<string, string>();
        foreach (var image in profile.Images.Values)
            if (!modules.TryGetValue(image.Name, out var module) || module.Mvid != image.Mvid)
                throw Invalid("AOT profile does not match linked PE identity: " + image.Name);
        foreach (var group in modules.Values)
        {
            using var stream = File.OpenRead(group.FileName);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var method in group.GetTypes().SelectMany(t => t.Methods))
            {
                var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(checked((int)method.MetadataToken.RID)));
                var owner = metadata.GetTypeDefinition(definition.GetDeclaringType());
                var context = new MonoSignature.Context(owner.GetGenericParameters().Select(h => metadata.GetString(metadata.GetGenericParameter(h).Name)).ToArray(),
                    definition.GetGenericParameters().Select(h => metadata.GetString(metadata.GetGenericParameter(h).Name)).ToArray());
                var signature = definition.DecodeSignature(new MonoSignature(), context);
                declared[TokenKey(method)] = Normalize(signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")");
            }
        }
        foreach (var item in profile.Methods)
        {
            var module = modules[item.Assembly];
            var owner = module.GetTypes().SingleOrDefault(t => t.FullName == item.Type.TrimStart('.'))
                ?? throw Invalid("AOT profile owner is missing: " + item.Type);
            var matches = owner.Methods.Where(m => m.Name == item.Name && declared[TokenKey(m)] == Normalize(item.Signature)).ToArray();
            if (matches.Length == 0) throw Invalid("AOT profile method is missing: " + item.Type + "::" + item.Name);
            profileGroups.Add(item.Id, matches.Select(TokenKey).ToArray());
            foreach (var match in matches) baseline.TryAdd(TokenKey(match), match);
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var configured = new List<object>();
        foreach (var entry in entries)
        {
            if (!byPortableKey.TryGetValue(PortableKey(entry), out var matches))
            { configured.Add(new { Entry = entry, Status = "Unmatched: retain baseline native" }); continue; }
            if (matches.Length != 1)
            { configured.Add(new { Entry = entry, Status = "Ambiguous: retain baseline native" }); continue; }
            var method = matches[0]; var key = TokenKey(method);
            if (!baseline.ContainsKey(key))
            { configured.Add(new { Entry = entry, Status = "Already outside baseline native set" }); continue; }
            if (!TryBodyHash(method, out var hash) || hash != entry.BodySha256)
            { configured.Add(new { Entry = entry, Status = "Changed or unsupported body: retain baseline native", ActualBodySha256 = hash }); continue; }
            excluded.Add(key); configured.Add(new { Entry = entry, Status = "Exact signature and body matched", CurrentToken = method.MetadataToken.ToInt32() });
        }

        var selected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var queue = new Queue<MethodDefinition>();
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        void Add(MethodDefinition method, string reason)
        {
            if (!modules.TryGetValue(method.Module.Assembly.Name.Name, out var expectedModule) || method.Module.Mvid != expectedModule.Mvid)
            { unresolved.Add("Resolved dependency is outside the actual linked image: " + method.FullName); return; }
            var key = TokenKey(method);
            if (!baseline.TryGetValue(key, out var actual)) return; // Existing native/IL boundary always wins.
            if (Signature(method) != Signature(actual) || method.DeclaringType.FullName != actual.DeclaringType.FullName || method.Name != actual.Name)
            { unresolved.Add("Resolved dependency token does not match actual linked definition: " + method.FullName); return; }
            if (selected.TryGetValue(key, out var reasons)) { reasons.Add(reason); return; }
            selected.Add(key, [reason]); queue.Enqueue(actual);
        }
        foreach (var pair in baseline)
            if (!excluded.Contains(pair.Key)) Add(pair.Value, "Baseline native: no exact unchanged exclusion");
        foreach (var pair in baseline)
            if (Guard(pair.Value)) Add(pair.Value, "Mandatory Core/math/Rendering/FreeType/Host/JS guard");

        // The profile format cannot distinguish some overloaded metadata signatures.
        // Retain the complete collision group whenever any definition is selected.
        var collisionGroups = profileGroups.Values.Where(g => g.Length > 1).ToArray();
        var syntheticArrays = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            foreach (var collision in collisionGroups)
                if (collision.Any(selected.ContainsKey))
                    foreach (var key in collision) Add(baseline[key], "Ambiguous baseline profile signature: retain whole group");
            if (queue.Count == 0) break;
            while (queue.TryDequeue(out var method))
            {
                foreach (var cctor in method.DeclaringType.Methods.Where(m => m.Name == ".cctor"))
                    Add(cctor, "Declaring type initializer");
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code == Code.Calli)
                    { unresolved.Add(method.FullName + " -> indirect unmanaged calli"); continue; }
                    if (instruction.Operand is FieldReference field && instruction.OpCode.Code is Code.Ldsfld or Code.Ldsflda or Code.Stsfld)
                    {
                        try
                        {
                            var owner = field.Resolve()?.DeclaringType;
                            if (owner is null) unresolved.Add(method.FullName + " -> static field owner " + field.FullName);
                            else foreach (var cctor in owner.Methods.Where(m => m.Name == ".cctor")) Add(cctor, "Static field owner initializer");
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        { unresolved.Add(method.FullName + " -> " + field.FullName + " (" + exception.GetType().Name + ")"); }
                    }
                    if (instruction.Operand is not MethodReference reference) continue;
                    if (reference.DeclaringType is ArrayType array && IsSyntheticArrayMethod(reference, array))
                    { syntheticArrays.Add(method.FullName + " -> " + reference.FullName); continue; }
                    try
                    {
                        var definition = reference.Resolve();
                        if (definition is null) unresolved.Add(method.FullName + " -> " + reference.FullName);
                        else Add(definition, "Direct call/delegate dependency within baseline native set");
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    { unresolved.Add(method.FullName + " -> " + reference.FullName + " (" + exception.GetType().Name + ")"); }
                }
            }
        }
        // An incomplete closure is not evidence of cold code. Fall back globally
        // to the exact original native selection rather than guessing dependencies.
        if (unresolved.Count != 0)
            foreach (var method in baseline.Values) Add(method, "Unresolved closure: global baseline native fallback");
        var selectedIds = profileGroups.Where(p => p.Value.Any(selected.ContainsKey)).Select(p => p.Key).ToHashSet();
        var result = selectedIds.Count == profile.Methods.Count ? original : FilterProfile(original, profile, selectedIds);
        var report = new
        {
            Format = "BEngine.WebAotColdMethods.Audit", Version = 1, Policy = Path.GetFullPath(policyPath),
            PolicySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(policyPath))),
            BaselineProfileSha256 = Convert.ToHexStringLower(SHA256.HashData(original)),
            SelectedProfileSha256 = Convert.ToHexStringLower(SHA256.HashData(result)),
            BaselineMethodRecords = profile.Methods.Count, SelectedMethodRecords = selectedIds.Count,
            BaselineDefinitions = baseline.Count, SelectedDefinitions = selected.Count,
            ExcludedDefinitions = baseline.Count - selected.Count, Configured = configured,
            Reenabled = baseline.Where(p => excluded.Contains(p.Key) && selected.ContainsKey(p.Key)).Select(p => new
                { Entry = Describe(p.Value), Reasons = selected[p.Key] }).ToArray(),
            Selected = baseline.Where(p => selected.ContainsKey(p.Key)).Select(p => new
                { Entry = Describe(p.Value), CurrentToken = p.Value.MetadataToken.ToInt32(), Reasons = selected[p.Key] }).ToArray(),
            UnresolvedClosure = unresolved, RuntimeSynthesizedArrayReferences = syntheticArrays,
            GlobalBaselineFallback = unresolved.Count != 0,
            Images = modules.Values.Select(m => new { Name = m.Assembly.Name.Name, Path = m.FileName, m.Mvid,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(m.FileName))) }).ToArray(),
            Contract = "Native subset only; no PE IL/metadata writes. Unknown/new/changed/ambiguous entries retain baseline native. Existing IL compatibility boundaries win. Virtual override dispatch retains IL fallback; actual performance and scenes require validation."
        };
        File.WriteAllText(profilePath + ".cold-methods.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllBytes(profilePath, result);
        Console.WriteLine($"BEngine Web AOT cold-method policy: {profile.Methods.Count} -> {selectedIds.Count} profile records; " +
            $"{baseline.Count - selected.Count} definitions remain interpreted, {unresolved.Count} unresolved closure references.");
    }

    private static bool Guard(MethodDefinition method)
    {
        var assembly = method.Module.Assembly.Name.Name; var type = method.DeclaringType;
        return assembly == "BEngine" && (type.Namespace == "BEngine" || type.Namespace.StartsWith("BEngine.Rendering", StringComparison.Ordinal)) ||
            assembly == "BEngine.UIElements" && (type.FullName.StartsWith("BEngine.UIElements.WebAdvancedFontBackend", StringComparison.Ordinal) || type.FullName.StartsWith("BEngine.UIElements.WebFontNative", StringComparison.Ordinal)) ||
            assembly is "BEngine.Player.WebHost" or "System.Runtime.InteropServices.JavaScript";
    }

    private static bool IsSyntheticArrayMethod(MethodReference method, ArrayType owner)
    {
        if (!method.HasThis || method.GenericParameters.Count != 0 || owner.Rank < 1) return false;
        var count = method.Parameters.Count;
        bool Indices(int length) => method.Parameters.Take(length).All(p => p.ParameterType.MetadataType == MetadataType.Int32);
        return method.Name switch
        {
            ".ctor" => method.ReturnType.MetadataType == MetadataType.Void && (count == owner.Rank || count == owner.Rank * 2) && Indices(count),
            "Get" => count == owner.Rank && Indices(count) && TypeKey(method.ReturnType) == TypeKey(owner.ElementType),
            "Set" => count == owner.Rank + 1 && Indices(count - 1) && method.ReturnType.MetadataType == MetadataType.Void && TypeKey(method.Parameters[^1].ParameterType) == TypeKey(owner.ElementType),
            "Address" => count == owner.Rank && Indices(count) && method.ReturnType is ByReferenceType address && TypeKey(address.ElementType) == TypeKey(owner.ElementType),
            _ => false
        };
    }

    internal static Entry Describe(MethodDefinition method) => new(method.Module.Assembly.Name.Name,
        method.DeclaringType.FullName, method.Name, method.GenericParameters.Count, Signature(method),
        TryBodyHash(method, out var hash) ? hash : ""); // Empty is auditable but cannot form a valid exclusion.
    private static string PortableKey(Entry entry) => Pack(entry.Assembly, entry.Type, entry.Name, entry.GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Signature);
    private static string PortableKey(MethodDefinition method) => Pack(method.Module.Assembly.Name.Name, method.DeclaringType.FullName,
        method.Name, method.GenericParameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Signature(method));
    private static string TokenKey(MethodDefinition method) => method.Module.Assembly.Name.Name + "|" + method.MetadataToken.ToInt32();
    private static string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
    private static string Pack(params string[] values) => string.Concat(values.Select(v => v.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + v));

    // Independent from Mono's display signature: preserve scopes, modifier order,
    // function pointer calling conventions, rank/bounds and positional generics.
    internal static string Signature(MethodReference method) => Pack("method-v1", method.HasThis.ToString(), method.ExplicitThis.ToString(),
        ((int)method.CallingConvention).ToString(System.Globalization.CultureInfo.InvariantCulture), method.GenericParameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TypeKey(method.ReturnType), Pack(method.Parameters.Select(p => TypeKey(p.ParameterType)).ToArray()));
    private static string TypeKey(TypeReference type) => type switch
    {
        Mono.Cecil.GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        GenericInstanceType instance => Pack("generic", TypeKey(instance.ElementType), Pack(instance.GenericArguments.Select(TypeKey).ToArray())),
        ArrayType array => Pack("array", TypeKey(array.ElementType), array.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture), array.IsVector.ToString(),
            Pack(array.Dimensions.Select(d => (d.LowerBound?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "") + ":" + (d.UpperBound?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")).ToArray())),
        ByReferenceType reference => Pack("byref", TypeKey(reference.ElementType)),
        PointerType pointer => Pack("pointer", TypeKey(pointer.ElementType)),
        RequiredModifierType modifier => Pack("modreq", TypeKey(modifier.ModifierType), TypeKey(modifier.ElementType)),
        OptionalModifierType modifier => Pack("modopt", TypeKey(modifier.ModifierType), TypeKey(modifier.ElementType)),
        PinnedType pinned => Pack("pinned", TypeKey(pinned.ElementType)),
        SentinelType sentinel => Pack("sentinel", TypeKey(sentinel.ElementType)),
        FunctionPointerType pointer => Pack("fnptr", pointer.HasThis.ToString(), pointer.ExplicitThis.ToString(), ((int)pointer.CallingConvention).ToString(System.Globalization.CultureInfo.InvariantCulture), TypeKey(pointer.ReturnType), Pack(pointer.Parameters.Select(p => TypeKey(p.ParameterType)).ToArray())),
        _ => Pack("type", type.Scope switch { AssemblyNameReference assembly => assembly.FullName, ModuleDefinition module => module.Assembly.Name.FullName,
            Mono.Cecil.ModuleReference module => "module:" + module.Name, null => "", _ => type.Scope.ToString() ?? "" }, type.FullName)
    };

    internal static bool TryBodyHash(MethodDefinition method, out string hash)
    {
        hash = "";
        if (!method.HasBody) return false;
        try
        {
            var body = method.Body; var indices = body.Instructions.Select((v, i) => (v, i)).ToDictionary(p => p.v, p => p.i);
            string Operand(object? value) => value switch
            {
                null => "null", Instruction target => "branch:" + indices[target],
                Instruction[] targets => Pack("switch", Pack(targets.Select(t => indices[t].ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray())),
                GenericInstanceMethod instance => Pack("methodspec", TypeKey(instance.DeclaringType), instance.Name, Signature(instance.ElementMethod), Pack(instance.GenericArguments.Select(TypeKey).ToArray())),
                MethodReference reference => Pack("method", TypeKey(reference.DeclaringType), reference.Name, Signature(reference)),
                FieldReference field => Pack("field", TypeKey(field.DeclaringType), field.Name, TypeKey(field.FieldType)),
                TypeReference type => TypeKey(type), VariableDefinition variable => "local:" + variable.Index,
                ParameterDefinition parameter => "parameter:" + parameter.Index,
                CallSite site => Pack("callsite", site.HasThis.ToString(), site.ExplicitThis.ToString(), ((int)site.CallingConvention).ToString(), TypeKey(site.ReturnType), Pack(site.Parameters.Select(p => TypeKey(p.ParameterType)).ToArray())),
                string text => Pack("string", text), float number => "f32:" + BitConverter.SingleToInt32Bits(number), double number => "f64:" + BitConverter.DoubleToInt64Bits(number),
                sbyte or byte or short or ushort or int or uint or long or ulong => Pack(value.GetType().FullName!, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!),
                _ => throw Invalid("Unsupported IL operand " + value.GetType().FullName)
            };
            int Index(Instruction? instruction) => instruction is null ? -1 : indices[instruction];
            var semantics = new
            {
                Format = "BEngine.WebAotMethodBody.v1", Signature = Signature(method), Attributes = (int)method.Attributes, Implementation = (int)method.ImplAttributes,
                body.InitLocals, Locals = body.Variables.Select(v => TypeKey(v.VariableType)).ToArray(),
                GenericConstraints = method.GenericParameters.Select(p => new { p.Position, Attributes = (int)p.Attributes, Types = p.Constraints.Select(c => TypeKey(c.ConstraintType)).ToArray() }).ToArray(),
                Instructions = body.Instructions.Select(i => new { Code = (int)i.OpCode.Code, Operand = Operand(i.Operand) }).ToArray(),
                Handlers = body.ExceptionHandlers.Select(h => new { Kind = (int)h.HandlerType, Catch = h.CatchType is null ? "" : TypeKey(h.CatchType),
                    TryStart = Index(h.TryStart), TryEnd = Index(h.TryEnd), HandlerStart = Index(h.HandlerStart), HandlerEnd = Index(h.HandlerEnd), FilterStart = Index(h.FilterStart) }).ToArray()
            };
            hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(semantics))); return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static Entry[] LoadPolicy(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path)); var root = document.RootElement;
        Require(root, "Format", "Version", "Methods");
        if (Text(root.GetProperty("Format")) != "BEngine.WebAotColdMethods" || !root.GetProperty("Version").TryGetInt32(out var version) || version != 1)
            throw Invalid("Expected BEngine.WebAotColdMethods Version 1.");
        var array = root.GetProperty("Methods");
        if (array.ValueKind != JsonValueKind.Array) throw Invalid("Methods must be an array.");
        var result = new List<Entry>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            Require(item, "Assembly", "Type", "Name", "GenericArity", "Signature", "BodySha256");
            if (!item.GetProperty("GenericArity").TryGetInt32(out var arity) || arity < 0) throw Invalid("GenericArity must be nonnegative.");
            var entry = new Entry(Text(item.GetProperty("Assembly")), Text(item.GetProperty("Type")), Text(item.GetProperty("Name")), arity,
                Text(item.GetProperty("Signature")), Text(item.GetProperty("BodySha256")));
            if (entry.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || entry.Assembly.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or ':' or ','))
                throw Invalid("Assembly must be a simple assembly name.");
            if (entry.BodySha256.Length != 64 || entry.BodySha256.Any(c => !char.IsAsciiHexDigit(c)) || entry.BodySha256 != entry.BodySha256.ToLowerInvariant())
                throw Invalid("BodySha256 must be 64 lowercase hexadecimal characters.");
            if (!keys.Add(PortableKey(entry))) throw Invalid("Duplicate exact method exclusion.");
            result.Add(entry);
        }
        return result.ToArray();
    }

    private static void Require(JsonElement item, params string[] expected)
    {
        if (item.ValueKind != JsonValueKind.Object) throw Invalid("Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid("Unknown or duplicate field: " + property.Name);
        if (seen.Count != expected.Length) throw Invalid("Missing required fields.");
    }
    private static string Text(JsonElement element) => element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } value &&
        value == value.Trim() && !value.Any(char.IsControl) ? value : throw Invalid("Expected a nonempty trimmed string without control characters.");
    private static InvalidDataException Invalid(string message) => new("Invalid Web AOT cold-method policy: " + message);

    private sealed record Image(string Name, Guid Mvid);
    private sealed record ProfileMethod(int Id, string Assembly, string Type, string Name, string Signature);
    private sealed record Slice(byte Kind, int Id, int Start, int Length);
    private sealed record Profile(Dictionary<int, Image> Images, List<ProfileMethod> Methods, List<Slice> Records);
    private static Profile ReadProfile(byte[] raw)
    {
        using var stream = new MemoryStream(raw, false); using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (!reader.ReadBytes(ProfileMagic.Length).AsSpan().SequenceEqual(ProfileMagic) || reader.ReadInt32() != 0x10000) throw Invalid("Unexpected SDK profile format.");
        var images = new Dictionary<int, Image>(); var types = new Dictionary<int, (int Image, string Type)>(); var methods = new List<ProfileMethod>(); var records = new List<Slice>(); var ids = new HashSet<int>();
        string ReadString() { var count = reader.ReadInt32(); if (count < 0 || count > stream.Length - stream.Position) throw Invalid("Invalid profile string."); return new UTF8Encoding(false, true).GetString(reader.ReadBytes(count)); }
        while (true)
        {
            var start = checked((int)stream.Position); var kind = reader.ReadByte(); var id = reader.ReadInt32();
            if (kind == 0) { if (id != 0 || stream.Position != stream.Length) throw Invalid("Invalid profile terminator."); records.Add(new Slice(kind, id, start, 5)); break; }
            if (id < 0 || !ids.Add(id)) throw Invalid("Invalid or duplicate profile record.");
            switch (kind)
            {
                case 1:
                    var imageName = ReadString(); var mvid = Guid.Parse(ReadString());
                    if (images.Values.Any(i => string.Equals(i.Name, imageName, StringComparison.OrdinalIgnoreCase))) throw Invalid("Duplicate profile assembly name.");
                    images.Add(id, new Image(imageName, mvid)); break;
                case 2:
                    if (reader.ReadByte() != 0x12) throw Invalid("Unexpected profile type kind.");
                    var image = reader.ReadInt32(); if (reader.ReadInt32() != -1) throw Invalid("Definition profile cannot specialize a generic instance.");
                    if (!images.ContainsKey(image)) throw Invalid("Profile type references a missing image.");
                    types.Add(id, (image, ReadString())); break;
                case 4:
                    var owner = types[reader.ReadInt32()]; if (reader.ReadInt32() != -1) throw Invalid("Definition profile cannot specialize a generic method.");
                    if (reader.ReadInt32() < 0) throw Invalid("Invalid method parameter count.");
                    methods.Add(new ProfileMethod(id, images[owner.Image].Name, owner.Type, ReadString(), ReadString())); break;
                default: throw Invalid("Unexpected record in SDK definition profile: " + kind);
            }
            records.Add(new Slice(kind, id, start, checked((int)stream.Position - start)));
        }
        return new Profile(images, methods, records);
    }
    private static byte[] FilterProfile(byte[] raw, Profile profile, HashSet<int> selected)
    {
        using var stream = new MemoryStream(); stream.Write(raw, 0, ProfileMagic.Length + sizeof(int));
        foreach (var record in profile.Records)
            if (record.Kind != 4 || selected.Contains(record.Id)) stream.Write(raw, record.Start, record.Length);
        var result = stream.ToArray(); _ = ReadProfile(result); return result;
    }
}
