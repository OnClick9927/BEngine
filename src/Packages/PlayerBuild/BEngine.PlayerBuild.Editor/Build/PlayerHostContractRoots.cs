using System.Xml.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BEngine.Editor;

// Downloaded assemblies are outside the SDK's call graph. Describe the members
// they actually use, with whole-type fallback for opaque reflection/unresolved metadata.
internal static class PlayerHostContractRoots
{
    private static readonly Regex NativeTextTypeNames = new(
        @"(?:SkiaSharp|HarfBuzzSharp)\.(?:[A-Za-z_][A-Za-z0-9_`]*\.)*[A-Za-z_][A-Za-z0-9_`]*(?:\+[A-Za-z_][A-Za-z0-9_`]*)*",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static bool RequiresTextBindings(IEnumerable<string> assemblyPaths, IEnumerable<string?> descriptors)
    {
        foreach (var path in assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            // AssemblyRef rows can survive platform/feature trimming after the
            // last use disappears. Native text belongs in the host only when a
            // remaining type contract (or an explicit reflection root) needs it.
            if (TextAssembly(assembly.Name.Name) || assembly.MainModule.GetTypeReferences().Any(reference =>
                    reference.GetElementType().Scope is AssemblyNameReference scope && TextAssembly(scope.Name)))
                return true;
            if (PlayerManagedCodeTrimmer.GetStringConstants([path]).Any(static value =>
                    value.Contains("SkiaSharp", StringComparison.Ordinal) || value.Contains("HarfBuzzSharp", StringComparison.Ordinal)))
                return true;
        }
        return descriptors.Where(static path => path is not null).Any(path =>
            XDocument.Load(path!).Descendants("assembly").Any(element =>
                TextAssembly(((string?)element.Attribute("fullname") ?? string.Empty).Split(',')[0])));
        static bool TextAssembly(string name) => name is "SkiaSharp" or "SkiaSharp.HarfBuzz" or "HarfBuzzSharp";
    }

    internal static XElement Build(IEnumerable<string> assemblyPaths)
    {
        var paths = assemblyPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var dynamicNames = paths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var resolver = new DefaultAssemblyResolver();
        var runtimePaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in paths.Concat(PlayerSerializationRoots.HostAssemblyPaths).Concat(runtimePaths)
                     .Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
            if (directory is not null) resolver.AddSearchDirectory(directory);
        var roots = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
            // The SDK follows the static host's own IL. Only frozen/reloadable
            // modules are outside that call graph and need an explicit ABI root
            // for the host members they use. Scanning the host's unused methods
            // here would retain their entire framework/native dependency graph.
            // Keep every input as a resolver path, and retain calls from an
            // external module into a shared host assembly below.
            if (DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(assembly.Name.Name)) continue;
            // Host-shared packages are also referenced by reloadable modules.
            // Their original facade references remain part of that ABI even when
            // the SDK rewrites the host copy to implementation assemblies.
            var members = assembly.MainModule.GetMemberReferences().ToArray();
            var opaque = members.OfType<MethodReference>().Any(UsesOpaqueReflection);
            var instructions = Types(assembly.MainModule.Types).SelectMany(static type => type.Methods)
                .Where(static method => method.HasBody).SelectMany(static method => method.Body.Instructions)
                .ToArray();
            var reflectionTokens = instructions.Where(static instruction => instruction.OpCode.Code == Code.Ldtoken)
                .Select(static instruction => instruction.Operand).OfType<TypeReference>()
                .Concat(instructions.Select(static instruction => instruction.Operand).OfType<GenericInstanceMethod>()
                    .Where(UsesOpaqueReflection).SelectMany(static method => method.GenericArguments))
                .Select(static type => type.GetElementType().FullName).ToHashSet(StringComparer.Ordinal);
            var literals = opaque ? PlayerManagedCodeTrimmer.GetStringConstants([path]) : [];
            bool ReflectiveType(TypeReference type) => opaque &&
                (reflectionTokens.Contains(type.GetElementType().FullName) ||
                 literals.Contains(type.FullName.Replace('/', '+')) || literals.Contains(type.Name) ||
                 literals.Any(value => value.StartsWith(type.FullName.Replace('/', '+') + ",", StringComparison.Ordinal)));
            // A reloadable module can use Type.GetType with an assembly-qualified
            // literal without emitting any native binding TypeRef. Preserve that
            // precise reflection target, not the whole native binding assembly.
            foreach (var name in literals.SelectMany(value => NativeTextTypeNames.Matches(value).Select(match => match.Value))
                         .Distinct(StringComparer.Ordinal))
            {
                var owner = name.StartsWith("SkiaSharp.HarfBuzz.", StringComparison.Ordinal) ? "SkiaSharp.HarfBuzz" :
                    name.StartsWith("HarfBuzzSharp.", StringComparison.Ordinal) ? "HarfBuzzSharp" : "SkiaSharp";
                try
                {
                    var binding = resolver.Resolve(new AssemblyNameReference(owner, new Version(0, 0)));
                    if (Types(binding.MainModule.Types).FirstOrDefault(type =>
                            type.FullName.Equals(name.Replace('+', '/'), StringComparison.Ordinal)) is { } reflected)
                        AddType(reflected, true);
                }
                catch (AssemblyResolutionException) { }
            }
            foreach (var type in assembly.MainModule.GetTypeReferences()) AddType(type, ReflectiveType(type));
            foreach (var member in members)
            {
                try
                {
                    if (member is MethodReference method && method.GetElementMethod().Resolve() is { } definition)
                    {
                        var root = AddType(definition.DeclaringType, ReflectiveType(definition.DeclaringType));
                        AddMethod(root, definition);
                    }
                    else if (member is FieldReference field && field.Resolve() is { } fieldDefinition)
                        AddMember(AddType(fieldDefinition.DeclaringType, ReflectiveType(fieldDefinition.DeclaringType)), "field", "name", fieldDefinition.Name);
                }
                catch (AssemblyResolutionException) { AddType(member.DeclaringType, true); }
                catch (ResolutionException) { AddType(member.DeclaringType, true); }
            }
        }
        var linker = new XElement("linker");
        foreach (var group in roots.Values.GroupBy(element => (string)element.Attribute("assembly")!)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var assembly = new XElement("assembly", new XAttribute("fullname", group.Key));
            foreach (var element in group.OrderBy(static element => (string)element.Attribute("fullname")!, StringComparer.Ordinal))
            {
                element.Attribute("assembly")!.Remove();
                assembly.Add(element);
            }
            linker.Add(assembly);
        }
        return linker;

        XElement? AddType(TypeReference reference, bool preserveAll)
        {
            reference = reference.GetElementType();
            var assemblyName = reference is TypeDefinition definition ? definition.Module.Assembly.Name.Name :
                reference.Scope is AssemblyNameReference scope ? scope.Name : reference.Module.Assembly.Name.Name;
            if (!ShouldRoot(assemblyName)) return null;
            TypeDefinition? resolved = null;
            try { resolved = reference.Resolve(); }
            catch (AssemblyResolutionException) { preserveAll = true; }
            catch (ResolutionException) { preserveAll = true; }
            if (resolved is not null)
            {
                if (!assemblyName.Equals(resolved.Module.Assembly.Name.Name, StringComparison.Ordinal))
                {
                    // The host linker rewrites its own references to implementation
                    // assemblies. Reloadable DLLs still reference the facade (for
                    // example System.Runtime), so retain that exact type forwarder
                    // as well as the implementation contract below.
                    var facadeKey = assemblyName + ":" + reference.FullName;
                    roots.TryAdd(facadeKey, new XElement("type", new XAttribute("assembly", assemblyName),
                        new XAttribute("fullname", reference.FullName), new XAttribute("preserve", "nothing")));
                }
                assemblyName = resolved.Module.Assembly.Name.Name;
                if (!ShouldRoot(assemblyName)) return null;
            }
            var name = resolved?.FullName ?? reference.FullName;
            // Keep opaque reflection contracts, including native binding types,
            // but let the linker follow direct Skia/HarfBuzz calls normally. A
            // canvas type reference must not root every filter, codec and GPU
            // entry point exposed by that canvas and its native library.
            // Reflection APIs are ordinary call targets too. Their namespace
            // does not imply that downloaded code reflects over every overload
            // on the reflection API itself. Opaque targets were identified above
            // and still retain the complete type, as does unresolved metadata.
            preserveAll |= resolved is null;
            var key = assemblyName + ":" + name;
            if (!roots.TryGetValue(key, out var element))
            {
                element = new XElement("type", new XAttribute("assembly", assemblyName),
                    new XAttribute("fullname", name), new XAttribute("preserve", "nothing"));
                roots.Add(key, element);
                if (resolved is not null)
                {
                    // Construction/accessors/virtual slots are rooted by actual member references.
                    // Reflection and serializer edges are described separately; merely mentioning
                    // a type must not keep all its overloads and properties alive.
                    foreach (var field in resolved.Fields.Where(field => !field.IsStatic || resolved.IsEnum))
                        AddMember(element, "field", "name", field.Name);
                }
            }
            if (preserveAll)
            {
                element.SetAttributeValue("preserve", "all");
                // Also enumerate opaque contracts so later content validation can check
                // reflection/native metadata against the actual trimmed Host.
                if (resolved is not null)
                {
                    foreach (var method in resolved.Methods) AddMethod(element, method);
                    foreach (var field in resolved.Fields) AddMember(element, "field", "name", field.Name);
                }
            }
            return element;
        }
        bool ShouldRoot(string name) => DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(name) || !dynamicNames.Contains(name);
    }

    private static void AddMember(XElement? root, string kind, string attribute, string value)
    {
        if (root is not null && !root.Elements(kind).Any(element => (string?)element.Attribute(attribute) == value))
            root.Add(new XElement(kind, new XAttribute(attribute, value)));
    }

    private static void AddMethod(XElement? root, MethodDefinition method)
    {
        var signature = PlayerManagedCodeTrimmer.MethodSignature(method);
        // ILLink accepts exact signatures for generic methods, but its descriptor
        // syntax omits generic arity. Keep the conservative name root when two
        // overloads share that representation; otherwise retain only the method
        // actually referenced by the downloaded module.
        var nameOnly = method.HasGenericParameters && method.DeclaringType.Methods.Count(candidate =>
            PlayerManagedCodeTrimmer.MethodSignature(candidate) == signature) != 1;
        AddMember(root, "method", nameOnly ? "name" : "signature", nameOnly ? method.Name : signature);
    }

    private static bool UsesOpaqueReflection(MethodReference method) =>
        method.DeclaringType.FullName is "BEngine.RuntimeTypeCache" or "BEngine.BAssetReferenceLoader" ||
        method.DeclaringType.FullName == "System.Activator" && method.Name == "CreateInstance" ||
        method.DeclaringType.FullName == "System.Type" && method.Name is
            "GetMethod" or "GetMethods" or "GetProperty" or "GetProperties" or "GetField" or "GetFields" or
            "GetMember" or "GetMembers" or "GetConstructor" or "GetConstructors" or "GetType" or "InvokeMember";

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
