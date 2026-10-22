using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BEngine.Editor;

// Downloaded assemblies are outside the SDK's call graph. Describe the members
// they actually use, with whole-type fallback for opaque reflection/unresolved metadata.
internal static class PlayerHostContractRoots
{
    internal static bool RequiresTextBindings(IEnumerable<string> assemblyPaths, IEnumerable<string?> descriptors)
    {
        foreach (var path in assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            if (TextAssembly(assembly.Name.Name) || assembly.MainModule.AssemblyReferences.Any(reference => TextAssembly(reference.Name)))
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
            // Host-shared packages are also referenced by reloadable modules.
            // Their original facade references remain part of that ABI even when
            // the SDK rewrites the host copy to implementation assemblies.
            var members = assembly.MainModule.GetMemberReferences().ToArray();
            var opaque = members.OfType<MethodReference>().Any(UsesOpaqueReflection);
            var reflectionTokens = Types(assembly.MainModule.Types).SelectMany(static type => type.Methods)
                .Where(static method => method.HasBody).SelectMany(static method => method.Body.Instructions)
                .Where(static instruction => instruction.OpCode.Code == Code.Ldtoken)
                .Select(static instruction => instruction.Operand).OfType<TypeReference>()
                .Select(static type => type.GetElementType().FullName).ToHashSet(StringComparer.Ordinal);
            var literals = opaque ? PlayerManagedCodeTrimmer.GetStringConstants([path]) : [];
            bool ReflectiveType(TypeReference type) => opaque &&
                (reflectionTokens.Contains(type.GetElementType().FullName) ||
                 literals.Contains(type.FullName.Replace('/', '+')) || literals.Contains(type.Name) ||
                 literals.Any(value => value.StartsWith(type.FullName.Replace('/', '+') + ",", StringComparison.Ordinal)));
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
            // Native binding/reflection APIs have metadata contracts beyond IL calls.
            preserveAll |= assemblyName is "SkiaSharp" or "SkiaSharp.HarfBuzz" or "HarfBuzzSharp" ||
                name.StartsWith("System.Reflection.", StringComparison.Ordinal) || resolved is null;
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

    private static void AddMethod(XElement? root, MethodDefinition method) =>
        AddMember(root, "method", method.HasGenericParameters ? "name" : "signature",
            method.HasGenericParameters ? method.Name : PlayerManagedCodeTrimmer.MethodSignature(method));

    private static bool UsesOpaqueReflection(MethodReference method) =>
        method.DeclaringType.FullName is "BEngine.RuntimeTypeCache" or "BEngine.BAssetReferenceLoader" ||
        method.DeclaringType.FullName == "System.Type" && method.Name is
            "GetMethod" or "GetMethods" or "GetProperty" or "GetProperties" or "GetField" or "GetFields" or
            "GetMember" or "GetMembers" or "InvokeMember";

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
