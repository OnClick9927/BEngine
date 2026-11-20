using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace BEngine;

/// <summary>XML documents using the same typed data contract as JSON.</summary>
public static class XmlSerialization
{
    public static string Serialize<T>(T value) => Serialize(value, typeof(T));

    public static string Serialize(object? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new XElement("serialization", new XAttribute("version", 1),
            WriteNode(JsonSerialization.ToNode(value, type), null)).ToString(SaveOptions.DisableFormatting);
    }

    public static T? Deserialize<T>(string xml) => (T?)Deserialize(xml, typeof(T));

    public static object? Deserialize(string xml, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(type);
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 64 * 1024 * 1024
        });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root?.Name != "serialization" || (string?)root.Attribute("version") != "1" ||
            root.Attributes().Count() != 1 || root.Elements().Count() != 1 ||
            root.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value)))
            throw new InvalidDataException("Invalid serialization XML document.");
        return JsonSerialization.FromNode(ReadNode(root.Elements().Single()), type);
    }

    private static XElement WriteNode(JsonNode? node, string? name)
    {
        var element = node switch
        {
            JsonObject => new XElement("object"),
            JsonArray => new XElement("array"),
            null => new XElement("null"),
            _ => WriteScalar(node)
        };
        if (name is not null) element.SetAttributeValue("name", name);
        if (node is JsonObject obj)
            foreach (var member in obj) element.Add(WriteNode(member.Value, member.Key));
        else if (node is JsonArray array)
            foreach (var item in array) element.Add(WriteNode(item, null));
        return element;
    }

    private static XElement WriteScalar(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        var value = document.RootElement;
        return value.ValueKind switch
        {
            JsonValueKind.String => new XElement("string", value.GetString()),
            JsonValueKind.Number => new XElement("number", value.GetRawText()),
            JsonValueKind.True => new XElement("boolean", "true"),
            JsonValueKind.False => new XElement("boolean", "false"),
            _ => throw new InvalidDataException("Unsupported JSON scalar in XML document.")
        };
    }

    private static JsonNode? ReadNode(XElement element)
    {
        if (!string.IsNullOrEmpty(element.Name.NamespaceName))
            throw new InvalidDataException("Namespaced XML nodes are not supported.");
        if (element.Attributes().Any(attribute => attribute.Name != "name"))
            throw new InvalidDataException("Unexpected XML content or attributes.");
        switch (element.Name.LocalName)
        {
            case "object":
            {
                if (element.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value)))
                    throw new InvalidDataException("An XML object cannot contain scalar text.");
                var result = new JsonObject();
                foreach (var member in element.Elements())
                {
                    var name = (string?)member.Attribute("name");
                    if (string.IsNullOrEmpty(name) || result.ContainsKey(name))
                        throw new InvalidDataException("An XML object member has no unique name.");
                    result.Add(name, ReadNode(member));
                }
                return result;
            }
            case "array":
            {
                if (element.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value)))
                    throw new InvalidDataException("An XML array cannot contain scalar text.");
                var result = new JsonArray();
                foreach (var item in element.Elements())
                {
                    if (item.Attribute("name") is not null)
                        throw new InvalidDataException("An XML array item cannot have a member name.");
                    result.Add(ReadNode(item));
                }
                return result;
            }
            case "string":
                if (element.HasElements) throw new InvalidDataException("An XML scalar cannot contain child nodes.");
                return JsonValue.Create(element.Value);
            case "number":
            {
                if (element.HasElements) throw new InvalidDataException("An XML scalar cannot contain child nodes.");
                var value = JsonNode.Parse(element.Value);
                return value is JsonValue && value.GetValueKind() == JsonValueKind.Number
                    ? value : throw new InvalidDataException("Invalid XML number.");
            }
            case "boolean":
                if (element.HasElements) throw new InvalidDataException("An XML scalar cannot contain child nodes.");
                return element.Value switch
            {
                "true" => JsonValue.Create(true),
                "false" => JsonValue.Create(false),
                _ => throw new InvalidDataException("Invalid XML boolean.")
            };
            case "null":
                if (element.HasElements || element.Value.Length != 0)
                    throw new InvalidDataException("An XML null cannot contain data.");
                return null;
            default: throw new InvalidDataException("Unknown serialization XML node.");
        }
    }
}
