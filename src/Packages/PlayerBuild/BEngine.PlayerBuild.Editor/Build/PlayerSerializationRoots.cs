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
            var queue = new Queue<TypeReference>();
            foreach (var type in definitions.Values)
            foreach (var method in type.Methods.Where(static m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not GenericInstanceMethod generic) continue;
                var owner = generic.DeclaringType.FullName;
                if (owner is "BEngine.YamlUtility" or "System.Text.Json.JsonSerializer" or "System.Text.Json.JsonSerializerExtensions")
                    foreach (var argument in generic.GenericArguments) queue.Enqueue(argument);
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (queue.TryDequeue(out var reference))
            {
                if (reference is GenericInstanceType generic)
                    foreach (var argument in generic.GenericArguments) queue.Enqueue(argument);
                reference = reference.GetElementType();
                var key = Key(reference);
                if (!visited.Add(key) || !definitions.TryGetValue(key, out var type)) continue;
                var assembly = root.Elements("assembly").FirstOrDefault(a =>
                    (string?)a.Attribute("fullname") == type.Module.Assembly.Name.Name);
                if (assembly is null)
                {
                    assembly = new XElement("assembly", new XAttribute("fullname", type.Module.Assembly.Name.Name));
                    root.Add(assembly);
                }
                var element = new XElement("type", new XAttribute("fullname", type.FullName),
                    new XAttribute("preserve", "nothing"));
                foreach (var constructor in type.Methods.Where(static m => m.IsConstructor))
                    element.Add(new XElement("method", new XAttribute("name", constructor.Name)));
                foreach (var field in type.Fields.Where(static f => !f.IsStatic))
                {
                    element.Add(new XElement("field", new XAttribute("name", field.Name)));
                    queue.Enqueue(field.FieldType);
                }
                foreach (var property in type.Properties)
                {
                    element.Add(new XElement("property", new XAttribute("name", property.Name)));
                    queue.Enqueue(property.PropertyType);
                }
                if (type.IsEnum)
                    foreach (var field in type.Fields)
                        element.Add(new XElement("field", new XAttribute("name", field.Name)));
                if (type.BaseType is { } parent) queue.Enqueue(parent);
                assembly.Add(element);
            }
        }
        finally { foreach (var assembly in assemblies) assembly.Dispose(); }
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
