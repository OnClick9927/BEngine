using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BEngine.Editor;

/// <summary>Freezes exact runtime images for native mobile execution, independently of content formats.</summary>
internal static class NativePrecompiledScriptBuild
{
    internal static string Write(PlatformBuildContext context, string artifactsDirectory)
    {
        var directory = Path.Combine(artifactsDirectory, "native-runtime-images");
        Directory.CreateDirectory(directory);
        var images = new List<(string Name, string Path, string Hash, string Mvid)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in context.PrecompiledManagedCodeAssemblyPaths.Order(StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(input);
            using var reader = new PEReader(new MemoryStream(bytes, writable: false));
            if (!reader.HasMetadata) throw new InvalidDataException($"Native runtime image '{input}' has no metadata.");
            var metadata = reader.GetMetadataReader();
            if (!metadata.IsAssembly) throw new InvalidDataException($"Native runtime image '{input}' is not an assembly.");
            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_.-]*$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
                !name.Equals(Path.GetFileNameWithoutExtension(input), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Invalid native runtime image identity '{name}'.");
            var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            if (mvid == Guid.Empty) throw new InvalidDataException($"Native runtime image '{name}' has no MVID.");
            if (!names.Add(name)) throw new InvalidDataException($"Duplicate native runtime image '{name}'.");
            var destination = Path.Combine(directory, name + ".dll");
            File.WriteAllBytes(destination, bytes);
            images.Add((name, destination, Convert.ToHexStringLower(SHA256.HashData(bytes)),
                mvid.ToString()));
        }
        var bootstrap = new StringBuilder("// Generated native runtime identity registration.\n" +
            "internal static class BEngineNativeRuntimeBootstrap\n{\n" +
            "    [System.Runtime.CompilerServices.ModuleInitializer]\n" +
            "    [System.Diagnostics.CodeAnalysis.SuppressMessage(\"Usage\", \"CA2255\")]\n" +
            "    internal static void Initialize()\n    {\n");
        var registration = context.Hooks.PrecompiledRegistrationStatement ??
            throw new InvalidOperationException("Native runtime images require a runtime identity registration hook.");
        foreach (var image in images)
            bootstrap.Append("        ").Append(registration(new(image.Name, image.Hash, image.Mvid, Shared: true))).Append('\n');
        bootstrap.Append("    }\n}\n");
        var bootstrapPath = Path.Combine(directory, "NativeRuntimeBootstrap.cs");
        File.WriteAllText(bootstrapPath, bootstrap.ToString());
        var descriptorPath = Path.Combine(directory, "copy-images.xml");
        new XDocument(new XElement("linker", images.Select(image => new XElement("assembly",
            new XAttribute("fullname", image.Name), new XAttribute("preserve", "all"),
            new XAttribute("action", "copy"))))).Save(descriptorPath);
        File.WriteAllText(Path.Combine(directory, "images.json"), JsonSerializer.Serialize(images.Select(image =>
            new { image.Name, AssemblyPath = image.Path, Sha256 = image.Hash, image.Mvid }),
            new JsonSerializerOptions { WriteIndented = true }));
        var propsPath = Path.Combine(directory, "images.props");
        new XDocument(new XElement("Project", new XElement("ItemGroup",
            images.Select(image => new XElement("Reference", new XAttribute("Include", image.Name),
                new XElement("HintPath", image.Path), new XElement("Private", "true"))),
            new XElement("TrimmerRootDescriptor", new XAttribute("Include", descriptorPath)),
            new XElement("Compile", new XAttribute("Include", bootstrapPath))))).Save(propsPath);
        return propsPath;
    }
}
