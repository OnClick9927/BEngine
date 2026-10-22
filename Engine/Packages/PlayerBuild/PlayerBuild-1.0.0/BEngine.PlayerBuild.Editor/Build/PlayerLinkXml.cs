using System.Xml;
using System.Xml.Linq;

namespace BEngine.Editor;

// User-owned reflection contracts share one descriptor across the AOT and Host
// passes. Keep ILLink's assembly/type/member grammar, with Unity-style discovery.
internal static class PlayerLinkXml
{
    internal static void MergeInto(XElement root, IEnumerable<string> descriptors,
        IEnumerable<string> assemblyPaths)
    {
        var available = assemblyPaths.Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in descriptors.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(EditorHostServices.FileSystem.ReadAllText(path)),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(reader, LoadOptions.SetLineInfo);
                if (document.Root?.Name != "linker") Fail("Expected a <linker> root.");
                foreach (var assembly in document.Root!.Elements())
                {
                    if (assembly.Name != "assembly") Fail("Only <assembly> is allowed below <linker>.");
                    var name = (string?)assembly.Attribute("fullname");
                    if (string.IsNullOrWhiteSpace(name) || name.Contains('*')) Fail("An assembly requires an exact fullname.");
                    var copy = new XElement(assembly);
                    var optional = ReadBoolean(copy, "ignoreIfMissing");
                    copy.Attribute("ignoreIfMissing")?.Remove();
                    if (!available.Contains(name!.Split(',')[0].Trim()))
                    {
                        if (optional) continue;
                        Fail($"Assembly '{name}' was not found; use ignoreIfMissing=\"true\" for an optional assembly.");
                    }
                    // ILLink does not implement this Unity-only assembly option;
                    // reject it instead of silently turning it into a forced root.
                    if (copy.Attribute("ignoreIfUnreferenced") is not null)
                        Fail("ignoreIfUnreferenced is not supported; use required=\"false\" on conditional type roots.");
                    ValidatePreserve(copy);
                    foreach (var type in copy.Elements())
                    {
                        if (type.Name == "namespace") continue;
                        if (type.Name != "type" || string.IsNullOrWhiteSpace((string?)type.Attribute("fullname")))
                            Fail("An assembly child must be a <type fullname=\"...\"> or <namespace>.");
                        // Both CLR notation (+) and Cecil/ILLink notation (/) are accepted.
                        type.SetAttributeValue("fullname", ((string)type.Attribute("fullname")!).Replace('+', '/'));
                        ValidatePreserve(type);
                        foreach (var member in type.Elements())
                        {
                            if (member.Name.LocalName is not ("method" or "field" or "property" or "event") ||
                                string.IsNullOrWhiteSpace((string?)member.Attribute("name")) &&
                                string.IsNullOrWhiteSpace((string?)member.Attribute("signature")))
                                Fail("Members require method/field/property/event and a name or signature.");
                            if (member.Attribute("accessors") is { } accessors &&
                                accessors.Value is not ("all" or "get" or "set" or "get;set"))
                                Fail("Property accessors must be all, get, set or get;set.");
                        }
                    }
                    if (!root.Elements("assembly").Any(existing => XNode.DeepEquals(existing, copy))) root.Add(copy);
                }
            }
            catch (XmlException error) { throw new InvalidDataException($"Invalid link.xml '{path}': {error.Message}", error); }

            void Fail(string message) => throw new InvalidDataException($"Invalid link.xml '{path}': {message}");
            void ValidatePreserve(XElement element)
            {
                if (element.Attribute("preserve") is { } value && value.Value is not ("all" or "nothing" or "fields" or "methods"))
                    Fail("preserve must be all, nothing, fields or methods.");
            }
            bool ReadBoolean(XElement element, string name)
            {
                if (element.Attribute(name) is not { } attribute) return false;
                if (bool.TryParse(attribute.Value, out var result)) return result;
                Fail($"{name} must be true or false."); return false;
            }
        }
    }
}
