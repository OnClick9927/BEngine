using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BEngine.Editor;

internal static class UIElementsResourceTypeNames
{
    internal static IEnumerable<string> Read(string path, string text)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".bxml" or ".xml")) yield break;
        using var reader = XmlReader.Create(new StringReader(text),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        foreach (var element in document.Descendants())
        {
            yield return element.Name.LocalName;
            var ns = element.Name.NamespaceName;
            if (ns.StartsWith("clr-namespace:", StringComparison.Ordinal)) ns = ns[14..].Split(';')[0];
            if (ns.Length > 0 && Regex.IsMatch(ns, @"^[A-Za-z_][A-Za-z0-9_.]*$"))
                yield return ns + "." + element.Name.LocalName;
        }
    }
}
