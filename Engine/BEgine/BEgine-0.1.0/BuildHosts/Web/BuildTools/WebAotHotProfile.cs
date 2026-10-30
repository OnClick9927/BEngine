using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;

// Optional project policy, evaluated against the linked metadata rather than
// runtime method tokens. It only narrows methods already eligible for AOT.
internal sealed class WebAotHotProfile
{
    private readonly HashSet<string> assemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Assembly, string Type)> hotTypes = new();
    private readonly HashSet<string> seenAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Assembly, string Type)> seenTypes = new();

    internal string Path { get; }
    internal string Sha256 { get; }
    internal int AssemblyCount => assemblies.Count;
    internal int HotTypeCount => hotTypes.Count;
    internal int MatchedHotTypeCount => seenTypes.Count;
    internal string[] UnmatchedCoveredAssemblies => assemblies.Except(seenAssemblies, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
    internal string[] UnmatchedTypes => hotTypes.Except(seenTypes).OrderBy(item => item.Assembly, StringComparer.Ordinal)
        .ThenBy(item => item.Type, StringComparer.Ordinal).Select(item => item.Assembly + ":" + item.Type).ToArray();

    private WebAotHotProfile(string path, byte[] bytes)
    {
        Path = System.IO.Path.GetFullPath(path);
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireObject(root, "profile", "Format", "Version", "Assemblies", "HotTypes");
        if (ReadString(root.GetProperty("Format"), "Format") != "BEngine.WebAotProfile" ||
            root.GetProperty("Version").ValueKind != JsonValueKind.Number ||
            !root.GetProperty("Version").TryGetInt32(out var version) || version != 1)
            throw Invalid("Format must be 'BEngine.WebAotProfile' and Version must be 1.");
        var assemblyRecords = root.GetProperty("Assemblies");
        var typeRecords = root.GetProperty("HotTypes");
        if (assemblyRecords.ValueKind != JsonValueKind.Array || assemblyRecords.GetArrayLength() == 0 ||
            typeRecords.ValueKind != JsonValueKind.Array || typeRecords.GetArrayLength() == 0)
            throw Invalid("Assemblies and HotTypes must be non-empty arrays.");
        foreach (var record in assemblyRecords.EnumerateArray())
        {
            var name = ReadString(record, "assembly");
            if (!SimpleAssemblyName(name) || IsBclAssembly(name))
                throw Invalid($"'{name}' is not a selectable non-BCL simple assembly name.");
            if (!assemblies.Add(name)) throw Invalid($"Duplicate assembly '{name}'.");
        }
        foreach (var record in typeRecords.EnumerateArray())
        {
            RequireObject(record, "hot type", "Assembly", "Type");
            var assembly = ReadString(record.GetProperty("Assembly"), "hot type Assembly");
            var type = ReadString(record.GetProperty("Type"), "hot type Type");
            if (!assemblies.TryGetValue(assembly, out var canonicalAssembly))
                throw Invalid($"Hot type '{assembly}:{type}' is not in Assemblies.");
            if (!MetadataTypeName(type))
                throw Invalid("A hot type metadata name cannot contain control characters.");
            if (!hotTypes.Add((canonicalAssembly, type))) throw Invalid($"Duplicate hot type '{assembly}:{type}'.");
        }
    }

    internal static WebAotHotProfile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw Invalid("A hot-profile path is required.");
        try { return new WebAotHotProfile(path, File.ReadAllBytes(path)); }
        catch (JsonException exception) { throw new InvalidDataException("Invalid Web AOT hot profile JSON: " + path, exception); }
    }

    internal bool Covers(string assembly) => !IsBclAssembly(assembly) && assemblies.Contains(assembly);

    internal void ObserveType(string assembly, string type)
    {
        if (!assemblies.TryGetValue(assembly, out var canonicalAssembly)) return;
        seenAssemblies.Add(canonicalAssembly);
        if (hotTypes.Contains((canonicalAssembly, type))) seenTypes.Add((canonicalAssembly, type));
    }

    internal bool SelectsMethod(string assembly, string typeName, MetadataReader metadata,
        TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle)
    {
        if (!Covers(assembly)) return true;
        assemblies.TryGetValue(assembly, out var canonicalAssembly);
        if (hotTypes.Contains((canonicalAssembly!, typeName))) return true;
        var type = metadata.GetTypeDefinition(typeHandle);
        var method = metadata.GetMethodDefinition(methodHandle);
        var name = metadata.GetString(method.Name);
        if ((method.Attributes & MethodAttributes.Virtual) != 0 ||
            (type.Attributes & TypeAttributes.Interface) != 0 ||
            method.GetGenericParameters().Count > 0 ||
            name is "Dispose" or "DisposeAsync" ||
            name.EndsWith(".Dispose", StringComparison.Ordinal) ||
            name.EndsWith(".DisposeAsync", StringComparison.Ordinal)) return true;
        for (var owner = typeHandle; !owner.IsNil; owner = metadata.GetTypeDefinition(owner).GetDeclaringType())
            if (metadata.GetTypeDefinition(owner).GetGenericParameters().Count > 0) return true;
        // Explicit interface implementations normally carry Virtual. Keep the
        // actual MethodImpl slots too, including unusual legal metadata.
        foreach (var implementation in type.GetMethodImplementations())
            if (metadata.GetMethodImplementation(implementation).MethodBody == methodHandle) return true;
        return false;
    }

    internal void VerifyObservedTypes()
    {
        // Normal IL trimming can remove an entire optional module or a formerly
        // sampled type. Report those names, but never make a nonmatch hot.
        if (seenTypes.Count == 0) throw Invalid("No HotTypes match the linked Web metadata.");
    }

    internal static bool IsBclAssembly(string name) => name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) || name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("System.", StringComparison.OrdinalIgnoreCase);

    private static bool SimpleAssemblyName(string name) => !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
        !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name is not ("." or "..") &&
        name.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character) &&
            character is not ('/' or '\\' or ',' or ':' or '=' or '"' or '\'' or ';' or '|' or '*' or '?'));

    // This is an exact metadata key, never a file path, shell argument, XML
    // fragment or reflection type expression. Legal generated identifiers can
    // contain punctuation such as the '|' in an async local-function state
    // machine. Let matching against the actual linked metadata validate names.
    private static bool MetadataTypeName(string name) => name.All(character => !char.IsControl(character));

    private static string ReadString(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } result ||
            result != result.Trim() || result.Any(char.IsControl)) throw Invalid(label + " must be a non-empty trimmed string.");
        return result;
    }

    private static void RequireObject(JsonElement value, string label, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(label + " must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw Invalid(label + " has an unknown or duplicate field '" + property.Name + "'.");
        if (seen.Count != fields.Length) throw Invalid(label + " requires " + string.Join(", ", fields) + ".");
    }

    private static InvalidDataException Invalid(string message) => new("Invalid Web AOT hot profile: " + message);
}
