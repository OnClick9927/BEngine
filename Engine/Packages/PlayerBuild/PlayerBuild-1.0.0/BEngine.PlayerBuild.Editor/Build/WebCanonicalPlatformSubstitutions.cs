using System.Xml.Linq;

namespace BEngine.Editor;

/// <summary>Explicit target facts for the otherwise platform-neutral partial linker.</summary>
internal static class WebCanonicalPlatformSubstitutions
{
    internal static XDocument Create()
    {
        var type = new XElement("type", new XAttribute("fullname", "System.OperatingSystem"));
        foreach (var name in new[] { "IsBrowser", "IsWindows", "IsLinux", "IsMacOS", "IsAndroid", "IsIOS",
                     "IsTvOS", "IsWatchOS", "IsFreeBSD", "IsMacCatalyst", "IsWasi" })
            type.Add(new XElement("method",
                new XAttribute("signature", "System.Boolean " + name + "()"),
                new XAttribute("body", "stub"),
                new XAttribute("value", name == "IsBrowser" ? "true" : "false")));
        return new XDocument(new XElement("linker", new XElement("assembly",
            new XAttribute("fullname", "System.Private.CoreLib"), type)));
    }
}
