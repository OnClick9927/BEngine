using System.Xml.Linq;
using Mono.Cecil;

namespace BEngine.Editor.Build;

// YamlDotNet consumes constructors and members through reflection. Model these
// edges in build tooling rather than putting package-specific rules into Core.
internal static class PlayerSerializationRoots
{
    internal static IEnumerable<string> HostAssemblyPaths => AppDomain.CurrentDomain.GetAssemblies()
        .Where(static a => !a.IsDynamic && DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(a.GetName().Name!))
        .Select(static a => a.Location).Where(File.Exists);

    internal static void Add(XElement root, IEnumerable<string> paths)
    {
        var assemblies = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(AssemblyDefinition.ReadAssembly).ToArray();
        try
        {
            var definitions = assemblies.SelectMany(static a => Types(a.MainModule.Types))
                .GroupBy(static t => Key(t), StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
            var queue = new Queue<(TypeReference Type, SerializerKind Serializer)>();
            foreach (var type in definitions.Values)
            {
                var __filteredSource1219 = type.Methods;
                ArgumentNullException.ThrowIfNull(__filteredSource1219, "source");
                foreach (var method in __filteredSource1219)
                {
                    if (!(method.HasBody))
                        continue;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is not GenericInstanceMethod generic)
                            continue;
                        var owner = generic.DeclaringType.FullName;
                        var serializer = owner switch
                        {
                            "BEngine.YamlUtility" => SerializerKind.Yaml,
                            "System.Text.Json.JsonSerializer" or "System.Text.Json.JsonSerializerExtensions" => SerializerKind.Json,
                            _ => (SerializerKind? )null
                        };
                        if (serializer is { } kind)
                            foreach (var argument in generic.GenericArguments)
                                queue.Enqueue((argument, kind));
                    }
                }
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var elements = new Dictionary<string, XElement>(StringComparer.Ordinal);
            while (queue.TryDequeue(out var item))
            {
                var (reference, serializer) = item;
                // Arrays/pointers can wrap a closed generic collection. Inspect
                // every specification before GetElementType erases its arguments.
                for (var contained = reference; contained is TypeSpecification specification; contained = specification.ElementType)
                    if (contained is GenericInstanceType generic)
                        foreach (var argument in generic.GenericArguments) queue.Enqueue((argument, serializer));
                reference = reference.GetElementType();
                var key = Key(reference);
                // A DTO used by both serializers needs the union of their contracts.
                // In particular, YamlIgnore must not hide an otherwise valid JSON member.
                if (!visited.Add(key + ":" + serializer) || !definitions.TryGetValue(key, out var type)) continue;
                var assembly = Matches3596();
                [global::BEngine.SkipLoopGuard]
                global::System.Xml.Linq.XElement? Matches3596()
                {
                    var matchSource3596 = root.Elements("assembly");
                    ArgumentNullException.ThrowIfNull(matchSource3596, "source");
                    global::System.ReadOnlySpan<global::System.Xml.Linq.XElement> matchValues3596;
                    if (matchSource3596.GetType() == typeof(global::System.Xml.Linq.XElement[]))
                        matchValues3596 = (global::System.Xml.Linq.XElement[])(object)matchSource3596;
                    else if (matchSource3596.GetType() == typeof(global::System.Collections.Generic.List<global::System.Xml.Linq.XElement>))
                        matchValues3596 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Xml.Linq.XElement>)(object)matchSource3596);
                    else
                    {
                        foreach (var a in (global::System.Collections.Generic.IEnumerable<global::System.Xml.Linq.XElement>)matchSource3596)
                        {
                            if ((string? )a.Attribute("fullname") == type.Module.Assembly.Name.Name)
                                return a;
                        }
                
                        return default!;
                    }
                
                    foreach (var a in matchValues3596)
                    {
                        if ((string? )a.Attribute("fullname") == type.Module.Assembly.Name.Name)
                            return a;
                    }
                
                    return default!;
                }
                if (assembly is null)
                {
                    assembly = new XElement("assembly", new XAttribute("fullname", type.Module.Assembly.Name.Name));
                    root.Add(assembly);
                }
                if (!elements.TryGetValue(key, out var element))
                {
                    element = new XElement("type", new XAttribute("fullname", type.FullName),
                        new XAttribute("preserve", "nothing"));
                    // Keep discovered serializer contracts as required roots. A
                    // downloaded module can resolve a Host DTO from a runtime
                    // string without emitting a static type reference, so the
                    // SDK's own call graph cannot safely decide it is unused.
                    elements.Add(key, element);
                    assembly.Add(element);
                }
                {
                    var __filteredSource4217 = type.Methods;
                    ArgumentNullException.ThrowIfNull(__filteredSource4217, "source");
                    foreach (var constructor in __filteredSource4217)
                    {
                        if (!(constructor.IsConstructor))
                            continue;
                        AddMember(element, "method", constructor.Name);
                    }
                }
                {
                    var __filteredSource4378 = type.Fields;
                    ArgumentNullException.ThrowIfNull(__filteredSource4378, "source");
                    foreach (var field in __filteredSource4378)
                    {
                        if (!(SerializableField(field, serializer)))
                            continue;
                        AddMember(element, "field", field.Name);
                        queue.Enqueue((field.FieldType, serializer));
                    }
                }
                {
                    var __filteredSource4645 = type.Properties;
                    ArgumentNullException.ThrowIfNull(__filteredSource4645, "source");
                    foreach (var property in __filteredSource4645)
                    {
                        if (!(SerializableProperty(property, serializer)))
                            continue;
                        AddMember(element, "property", property.Name);
                        queue.Enqueue((property.PropertyType, serializer));
                    }
                }
                if (type.IsEnum)
                    foreach (var field in type.Fields)
                        AddMember(element, "field", field.Name);
                if (type.BaseType is { } parent) queue.Enqueue((parent, serializer));
            }
        }
        finally { foreach (var assembly in assemblies) assembly.Dispose(); }
    }

    private enum SerializerKind { Yaml, Json }

    private static bool SerializableField(FieldDefinition field, SerializerKind serializer) =>
        !field.IsStatic && !Ignored(field, serializer) &&
        (field.IsPublic || ExplicitMember(field, serializer));

    private static bool SerializableProperty(PropertyDefinition property, SerializerKind serializer) =>
        !property.HasParameters &&
        (property.GetMethod ?? property.SetMethod) is { IsStatic: false } &&
        !Ignored(property, serializer) &&
        (property.GetMethod is { IsPublic: true } || property.SetMethod is { IsPublic: true } ||
         ExplicitMember(property, serializer));

    private static bool ExplicitMember(ICustomAttributeProvider member, SerializerKind serializer) =>
        member.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == (serializer == SerializerKind.Yaml
            ? "YamlDotNet.Serialization.YamlMemberAttribute" : "System.Text.Json.Serialization.JsonIncludeAttribute"));

    private static bool Ignored(ICustomAttributeProvider member, SerializerKind serializer)
    {
        if (serializer == SerializerKind.Yaml)
            return member.CustomAttributes.Any(static attribute =>
                attribute.AttributeType.FullName == "YamlDotNet.Serialization.YamlIgnoreAttribute");
        var ignored = Gather9476(member.CustomAttributes);
        [global::BEngine.SkipLoopGuard]
        global::Mono.Cecil.CustomAttribute? Gather9476(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttribute> items9476)
        {
            ArgumentNullException.ThrowIfNull(items9476, "source");
            bool Evaluate9476(global::Mono.Cecil.CustomAttribute attribute) => attribute.AttributeType.FullName == "System.Text.Json.Serialization.JsonIgnoreAttribute";
            global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttribute> values;
            if (items9476.GetType() == typeof(global::Mono.Cecil.CustomAttribute[]))
                values = (global::Mono.Cecil.CustomAttribute[])items9476;
            else if (items9476.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttribute>)items9476);
            else
                return global::System.Linq.Enumerable.FirstOrDefault(items9476, Evaluate9476);
            foreach (var attribute in values)
                if (Evaluate9476(attribute))
                    return attribute;
            return default!;
        }
        // Conditional writing exclusions remain readable, and Never explicitly opts in.
        // JsonIgnore without a Condition uses JsonIgnoreCondition.Always (1).
        return ignored is not null && (!Gather9846(ignored.Properties) || Gather9927(ignored.Properties));
        [global::BEngine.SkipLoopGuard]
        bool Gather9927(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttributeNamedArgument> items9927)
        {
            ArgumentNullException.ThrowIfNull(items9927, "source");
            bool Evaluate9927(global::Mono.Cecil.CustomAttributeNamedArgument value) => value.Name == "Condition" && Convert.ToInt32(value.Argument.Value) == 1;
            global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttributeNamedArgument> values;
            if (items9927.GetType() == typeof(global::Mono.Cecil.CustomAttributeNamedArgument[]))
                values = (global::Mono.Cecil.CustomAttributeNamedArgument[])items9927;
            else if (items9927.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttributeNamedArgument>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttributeNamedArgument>)items9927);
            else
                return global::System.Linq.Enumerable.Any(items9927, Evaluate9927);
            foreach (var value in values)
                if (Evaluate9927(value))
                    return true;
            return false;
        }

        [global::BEngine.SkipLoopGuard]
        bool Gather9846(global::System.Collections.Generic.IEnumerable<global::Mono.Cecil.CustomAttributeNamedArgument> items9846)
        {
            ArgumentNullException.ThrowIfNull(items9846, "source");
            bool Evaluate9846(global::Mono.Cecil.CustomAttributeNamedArgument value) => value.Name == "Condition";
            global::System.ReadOnlySpan<global::Mono.Cecil.CustomAttributeNamedArgument> values;
            if (items9846.GetType() == typeof(global::Mono.Cecil.CustomAttributeNamedArgument[]))
                values = (global::Mono.Cecil.CustomAttributeNamedArgument[])items9846;
            else if (items9846.GetType() == typeof(global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttributeNamedArgument>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::Mono.Cecil.CustomAttributeNamedArgument>)items9846);
            else
                return global::System.Linq.Enumerable.Any(items9846, Evaluate9846);
            foreach (var value in values)
                if (Evaluate9846(value))
                    return true;
            return false;
        }
    }

    private static void AddMember(XElement element, string kind, string name)
    {
        bool __queryResult8550;
        {
            var __querySource8550 = element.Elements(kind);
            ArgumentNullException.ThrowIfNull(__querySource8550, "source");
            __queryResult8550 = false;
            var __querySource8550Type = __querySource8550.GetType();
            if (__querySource8550Type == typeof(System.Xml.Linq.XElement[]) || __querySource8550Type == typeof(System.Collections.Generic.List<System.Xml.Linq.XElement>))
            {
                System.ReadOnlySpan<System.Xml.Linq.XElement> __querySource8550Span = __querySource8550Type == typeof(System.Xml.Linq.XElement[]) ? (System.Xml.Linq.XElement[])(object)__querySource8550 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<System.Xml.Linq.XElement>)(object)__querySource8550);
                foreach (var __queryItem8550 in __querySource8550Span)
                {
                    if (((string? )__queryItem8550.Attribute("name") == name))
                    {
                        __queryResult8550 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem8550 in __querySource8550)
                {
                    if (((string? )__queryItem8550.Attribute("name") == name))
                    {
                        __queryResult8550 = true;
                        break;
                    }
                }
            }
        }
        
        if (!__queryResult8550)
            element.Add(new XElement(kind, new XAttribute("name", name)));
    }

    private static string Key(TypeReference type) =>
        (type is TypeDefinition definition ? definition.Module.Assembly.Name.Name :
            type.Scope is AssemblyNameReference assembly ? assembly.Name : type.Module.Assembly.Name.Name) + ":" + type.FullName;

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
