using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BEngine.Editor.Build;

// Downloaded assemblies are outside the SDK's call graph. Describe the members
// they actually use, with whole-type fallback for opaque reflection/unresolved metadata.
internal static class PlayerHostContractRoots
{
    internal static bool RequiresTextBindings(IEnumerable<string> assemblyPaths, IEnumerable<string?> descriptors)
    {
        foreach (var path in assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            // Stale AssemblyRef rows alone must not retain native capabilities.
            if (PlatformTextBuildPolicy.IsBindingAssembly(assembly.Name.Name) ||
                assembly.MainModule.GetTypeReferences().Any(static reference =>
                    reference.GetElementType().Scope is AssemblyNameReference scope &&
                    PlatformTextBuildPolicy.IsBindingAssembly(scope.Name)) ||
                PlayerManagedCodeTrimmer.GetStringConstants([path]).Any(PlatformTextBuildPolicy.ReferencesBindings))
                return true;
        }
        return descriptors.Where(static path => path is not null).Any(static path =>
            XDocument.Load(path!).Descendants("assembly").Any(static element =>
                PlatformTextBuildPolicy.IsBindingAssembly(((string?)element.Attribute("fullname") ?? string.Empty)
                    .Split(',')[0])));
    }

    internal static XElement Build(IEnumerable<string> assemblyPaths)
    {
        var paths = ((((global::System.Collections.Generic.IEnumerable<string>)(assemblyPaths.Where(File.Exists))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems3098, distinctComparer3098) => distinctItems3098.ToHashSet<string>(distinctComparer3098).ToArray()});
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
            bool ReflectiveType(TypeReference type)
            {
                bool Query5308()
                {
                    var querySource5308 = literals;
                    ArgumentNullException.ThrowIfNull(querySource5308, "source");
                    if (querySource5308.GetType() == typeof(string[]) || querySource5308.GetType() == typeof(global::System.Collections.Generic.List<string>))
                    {
                        global::System.ReadOnlySpan<string> queryValues5308 = querySource5308.GetType() == typeof(string[]) ? (string[])(object)querySource5308 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)querySource5308);
                        foreach (var value in queryValues5308)
                        {
                            if (value.StartsWith(type.FullName.Replace('/', '+') + ",", StringComparison.Ordinal))
                                return true;
                        }
                    }
                    else
                    {
                        foreach (var value in querySource5308)
                        {
                            if (value.StartsWith(type.FullName.Replace('/', '+') + ",", StringComparison.Ordinal))
                                return true;
                        }
                    }
                
                    return false;
                }
                return opaque && (reflectionTokens.Contains(type.GetElementType().FullName) || literals.Contains(type.FullName.Replace('/', '+')) || literals.Contains(type.Name) || Query5308());
            }
            // A reloadable module can use Type.GetType with an assembly-qualified
            // literal without emitting any native binding TypeRef. Preserve that
            // precise reflection target, not the whole native binding assembly.
            foreach (var (owner, name) in literals.SelectMany(PlatformTextBuildPolicy.FindReflectedTypes).Distinct())
            {
                try
                {
                    var binding = resolver.Resolve(new AssemblyNameReference(owner, new Version(0, 0)));
                    if (Matches8466()is { } reflected)
                        AddType(reflected, true);
                    [global::BEngine.SkipLoopGuard]
                    global::Mono.Cecil.TypeDefinition? Matches8466()
                    {
                        var matchSource8466 = Types(binding.MainModule.Types);
                        ArgumentNullException.ThrowIfNull(matchSource8466, "source");
                        global::System.ReadOnlySpan<global::Mono.Cecil.TypeDefinition> matchValues8466;
                        if (matchSource8466.GetType() == typeof(global::Mono.Cecil.TypeDefinition[]))
                            matchValues8466 = (global::Mono.Cecil.TypeDefinition[])(object)matchSource8466;
                        else if (matchSource8466.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.TypeDefinition>))
                            matchValues8466 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.TypeDefinition>)(object)matchSource8466);
                        else
                        {
                            foreach (var type in (global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.TypeDefinition>)matchSource8466)
                            {
                                if (type.FullName.Equals(name.Replace('+', '/'), StringComparison.Ordinal))
                                    return type;
                            }
                    
                            return default!;
                        }
                    
                        foreach (var type in matchValues8466)
                        {
                            if (type.FullName.Equals(name.Replace('+', '/'), StringComparison.Ordinal))
                                return type;
                        }
                    
                        return default!;
                    }
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
            // Closed calls from downloaded code are outside the SDK call graph.
            // A new() constraint requires the actual argument's public default
            // constructor even when the generic method constructs by reflection
            // (or only needs the constraint to satisfy runtime verification).
            {
                var __typedSource7947 = instructions.Select(static instruction => instruction.Operand);
                ArgumentNullException.ThrowIfNull(__typedSource7947, "source");
                foreach (var __typedItem7947 in __typedSource7947)
                {
                    if (__typedItem7947 is not GenericInstanceMethod method)
                        continue;
                    try
                    {
                        var definition = method.GetElementMethod().Resolve();
                        if (definition is null)
                            continue;
                        for (var index = 0; index < definition.GenericParameters.Count; index++)
                        {
                            if (!definition.GenericParameters[index].HasDefaultConstructorConstraint || method.GenericArguments[index].Resolve()is not { } argument)
                                continue;
                            var constructor = Matches11109();
                            [global::BEngine.SkipLoopGuard]
                            global::Mono.Cecil.MethodDefinition? Matches11109()
                            {
                                var matchSource11109 = argument.Methods;
                                ArgumentNullException.ThrowIfNull(matchSource11109, "source");
                                global::System.ReadOnlySpan<global::Mono.Cecil.MethodDefinition> matchValues11109;
                                if (matchSource11109.GetType() == typeof(global::Mono.Cecil.MethodDefinition[]))
                                    matchValues11109 = (global::Mono.Cecil.MethodDefinition[])(object)matchSource11109;
                                else if (matchSource11109.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.MethodDefinition>))
                                    matchValues11109 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.MethodDefinition>)(object)matchSource11109);
                                else
                                {
                                    foreach (var candidate in (global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.MethodDefinition>)matchSource11109)
                                    {
                                        if (candidate.IsConstructor && !candidate.IsStatic && candidate.IsPublic && candidate.Parameters.Count == 0)
                                            return candidate;
                                    }
                            
                                    return default!;
                                }
                            
                                foreach (var candidate in matchValues11109)
                                {
                                    if (candidate.IsConstructor && !candidate.IsStatic && candidate.IsPublic && candidate.Parameters.Count == 0)
                                        return candidate;
                                }
                            
                                return default!;
                            }
                            if (constructor is not null)
                                AddMethod(AddType(argument, false), constructor);
                        }
                    }
                    catch (AssemblyResolutionException)
                    {
                    }
                    catch (ResolutionException)
                    {
                    }
                }
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
            // but let the linker follow direct native binding calls normally. A
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
                    {
                        var __filteredSource12900 = resolved.Fields;
                        ArgumentNullException.ThrowIfNull(__filteredSource12900, "source");
                        foreach (var field in __filteredSource12900)
                        {
                            if (!(!field.IsStatic || resolved.IsEnum))
                                continue;
                            AddMember(element, "field", "name", field.Name);
                        }
                    }
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
        if (root is not null && !Query14681())
            root.Add(new XElement(kind, new XAttribute(attribute, value)));
        bool Query14681()
        {
            var querySource14681 = root.Elements(kind);
            ArgumentNullException.ThrowIfNull(querySource14681, "source");
            if (querySource14681.GetType() == typeof(global::System.Xml.Linq.XElement[]) || querySource14681.GetType() == typeof(global::System.Collections.Generic.List<global::System.Xml.Linq.XElement>))
            {
                global::System.ReadOnlySpan<global::System.Xml.Linq.XElement> queryValues14681 = querySource14681.GetType() == typeof(global::System.Xml.Linq.XElement[]) ? (global::System.Xml.Linq.XElement[])(object)querySource14681 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Xml.Linq.XElement>)(object)querySource14681);
                foreach (var element in queryValues14681)
                {
                    if ((string? )element.Attribute(attribute) == value)
                        return true;
                }
            }
            else
            {
                foreach (var element in querySource14681)
                {
                    if ((string? )element.Attribute(attribute) == value)
                        return true;
                }
            }
        
            return false;
        }
    }

    private static void AddMethod(XElement? root, MethodDefinition method)
    {
        var signature = PlayerManagedCodeTrimmer.MethodSignature(method);
        // ILLink accepts exact signatures for generic methods, but its descriptor
        // syntax omits generic arity. Keep the conservative name root when two
        // overloads share that representation; otherwise retain only the method
        // actually referenced by the downloaded module.
        var nameOnly = method.HasGenericParameters && Query15369() != 1;
        int Query15369()
        {
            var querySource15369 = method.DeclaringType.Methods;
            ArgumentNullException.ThrowIfNull(querySource15369, "source");
            int matches = 0;
            if (querySource15369.GetType() == typeof(global::Mono.Cecil.MethodDefinition[]) || querySource15369.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.MethodDefinition>))
            {
                global::System.ReadOnlySpan<global::Mono.Cecil.MethodDefinition> queryValues15369 = querySource15369.GetType() == typeof(global::Mono.Cecil.MethodDefinition[]) ? (global::Mono.Cecil.MethodDefinition[])(object)querySource15369 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.MethodDefinition>)(object)querySource15369);
                foreach (var candidate in queryValues15369)
                {
                    if (PlayerManagedCodeTrimmer.MethodSignature(candidate) == signature)
                        checked
                        {
                            matches++;
                        }
                }
            }
            else
            {
                foreach (var candidate in querySource15369)
                {
                    if (PlayerManagedCodeTrimmer.MethodSignature(candidate) == signature)
                        checked
                        {
                            matches++;
                        }
                }
            }
        
            return matches;
        }
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
