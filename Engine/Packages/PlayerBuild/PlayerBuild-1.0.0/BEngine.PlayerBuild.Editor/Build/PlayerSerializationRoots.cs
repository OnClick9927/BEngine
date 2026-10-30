using System.Xml.Linq;
using Mono.Cecil;

namespace BEngine.Editor;

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
            foreach (var method in type.Methods.Where(static m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not GenericInstanceMethod generic) continue;
                var owner = generic.DeclaringType.FullName;
                var serializer = owner switch
                {
                    "BEngine.YamlUtility" => SerializerKind.Yaml,
                    "System.Text.Json.JsonSerializer" or "System.Text.Json.JsonSerializerExtensions" => SerializerKind.Json,
                    _ => (SerializerKind?)null
                };
                if (serializer is { } kind)
                    foreach (var argument in generic.GenericArguments) queue.Enqueue((argument, kind));
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
                var assembly = root.Elements("assembly").FirstOrDefault(a =>
                    (string?)a.Attribute("fullname") == type.Module.Assembly.Name.Name);
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
                foreach (var constructor in type.Methods.Where(static m => m.IsConstructor))
                    AddMember(element, "method", constructor.Name);
                foreach (var field in type.Fields.Where(field => SerializableField(field, serializer)))
                {
                    AddMember(element, "field", field.Name);
                    queue.Enqueue((field.FieldType, serializer));
                }
                foreach (var property in type.Properties.Where(property => SerializableProperty(property, serializer)))
                {
                    AddMember(element, "property", property.Name);
                    queue.Enqueue((property.PropertyType, serializer));
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
        var ignored = member.CustomAttributes.FirstOrDefault(static attribute =>
            attribute.AttributeType.FullName == "System.Text.Json.Serialization.JsonIgnoreAttribute");
        // Conditional writing exclusions remain readable, and Never explicitly opts in.
        // JsonIgnore without a Condition uses JsonIgnoreCondition.Always (1).
        return ignored is not null && (!ignored.Properties.Any(static value => value.Name == "Condition") ||
            ignored.Properties.Any(static value => value.Name == "Condition" && Convert.ToInt32(value.Argument.Value) == 1));
    }

    private static void AddMember(XElement element, string kind, string name)
    {
        if (!element.Elements(kind).Any(member => (string?)member.Attribute("name") == name))
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
