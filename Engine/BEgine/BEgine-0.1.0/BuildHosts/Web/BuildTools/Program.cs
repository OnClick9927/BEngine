using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Format and signature spellings follow dotnet/runtime v10.0.12 profiler/aot.h
// and metadata/debug-helpers.c. Read metadata only; never execute target code.
internal sealed class MonoSignature : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string element, ArrayShape shape) => element+"["+new string(',',shape.Rank-1)+"]";
    public string GetByReferenceType(string element) => element+"&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "*()";
    public string GetGenericInstantiation(string type, ImmutableArray<string> arguments) => type+"<"+string.Join(", ",arguments)+">";
    public string GetGenericMethodParameter(object? context,int index) => ((Context)context!).MethodParameters[index];
    public string GetGenericTypeParameter(object? context,int index) => ((Context)context!).TypeParameters[index];
    public string GetModifiedType(string modifier,string unmodified,bool required)
    {
        // Mono prints custom modifiers before the final by-ref suffix even when
        // metadata puts the modifier outside BYREF (for example an in argument).
        var suffix=" mod"+(required?"req":"opt")+"("+modifier+")";
        return unmodified.EndsWith('&') ? unmodified[..^1]+suffix+"&" : unmodified+suffix;
    }
    public string GetPinnedType(string element) => element;
    public string GetPointerType(string element) => element+"*";
    public string GetPrimitiveType(PrimitiveTypeCode code) => code switch {
        PrimitiveTypeCode.Void=>"void",PrimitiveTypeCode.Boolean=>"bool",PrimitiveTypeCode.Byte=>"byte",PrimitiveTypeCode.SByte=>"sbyte",PrimitiveTypeCode.Char=>"char",
        PrimitiveTypeCode.Int16=>"int16",PrimitiveTypeCode.UInt16=>"uint16",PrimitiveTypeCode.Int32=>"int",PrimitiveTypeCode.UInt32=>"uint",PrimitiveTypeCode.Int64=>"long",PrimitiveTypeCode.UInt64=>"ulong",
        PrimitiveTypeCode.Single=>"single",PrimitiveTypeCode.Double=>"double",PrimitiveTypeCode.IntPtr=>"intptr",PrimitiveTypeCode.UIntPtr=>"uintptr",PrimitiveTypeCode.String=>"string",PrimitiveTypeCode.Object=>"object",PrimitiveTypeCode.TypedReference=>"typedbyref",_=>throw new InvalidDataException("Unsupported primitive "+code)};
    public string GetSZArrayType(string element) => element+"[]";
    public string GetTypeFromDefinition(MetadataReader reader,TypeDefinitionHandle handle,byte kind) => TypeName(reader,handle);
    public string GetTypeFromReference(MetadataReader reader,TypeReferenceHandle handle,byte kind)
    {
        var type=reader.GetTypeReference(handle);var name=reader.GetString(type.Name);
        if(type.ResolutionScope.Kind==HandleKind.TypeReference)return GetTypeFromReference(reader,(TypeReferenceHandle)type.ResolutionScope,kind)+"/"+name;
        var ns=reader.GetString(type.Namespace);return ns.Length==0?name:ns+"."+name;
    }
    public string GetTypeFromSpecification(MetadataReader reader,object? context,TypeSpecificationHandle handle,byte kind) => reader.GetTypeSpecification(handle).DecodeSignature(this,context);
    public static string TypeName(MetadataReader reader,TypeDefinitionHandle handle)
    {
        var type=reader.GetTypeDefinition(handle);var name=reader.GetString(type.Name);var parent=type.GetDeclaringType();
        if(!parent.IsNil)return TypeName(reader,parent)+"/"+name;
        var ns=reader.GetString(type.Namespace);return ns.Length==0?name:ns+"."+name;
    }
    internal sealed record Context(string[] TypeParameters,string[] MethodParameters);
}

internal static class Program
{
    private static bool fullBclAot;
    // Cold BCL features keep their exact IL and the hybrid runtime fallback.
    // Compiling every method of XML, regex, reflection and serialization adds
    // native code even when those features only run during resource loading.
    // Engine/project assemblies retain their existing native selection.
    private static bool IsNativeType(string assembly, string type)
    {
        // Mono 10.0.12 hybrid AOT cannot execute the generic delegate closure
        // used by SortedSet<T>.CopyTo when the caller is interpreted. Keep the
        // complete family together in IL; other collection types stay native.
        // This correctness exception also applies to the broader BCL policy.
        if (assembly == "System.Collections" &&
            (type == "System.Collections.Generic.SortedSet`1" ||
             type.StartsWith("System.Collections.Generic.SortedSet`1/", StringComparison.Ordinal) ||
             type == "System.Collections.Generic.TreeWalkPredicate`1")) return false;
        if (fullBclAot) return true;
        if (assembly is "System.Text.RegularExpressions" or "System.Private.Xml" or "System.Private.Xml.Linq")
        {
            // BXML and style sheets parse and traverse XML/regex during ordinary
            // Player startup. Compile only their retained reader, matching and
            // object-model cores; regex compilation/symbolic engines and the
            // XML schema, DTD, XPath and writer stacks keep their IL fallback.
            // Nested iterator/state types belong to the same selected family.
            var nestedType = type.IndexOf('/');
            var owner = nestedType < 0 ? type : type[..nestedType];
            return assembly switch
            {
                "System.Text.RegularExpressions" => owner is
                    "System.Text.RegularExpressions.Regex" or
                    "System.Text.RegularExpressions.RegexCache" or
                    "System.Text.RegularExpressions.RegexParser" or
                    "System.Text.RegularExpressions.RegexNode" or
                    "System.Text.RegularExpressions.RegexTree" or
                    "System.Text.RegularExpressions.RegexInterpreter" or
                    "System.Text.RegularExpressions.RegexInterpreterCode" or
                    "System.Text.RegularExpressions.RegexInterpreterFactory" or
                    "System.Text.RegularExpressions.RegexRunner" or
                    "System.Text.RegularExpressions.RegexRunnerFactory" or
                    "System.Text.RegularExpressions.RegexFindOptimizations" or
                    "System.Text.RegularExpressions.RegexCharClass" or
                    "System.Text.RegularExpressions.RegexCaseEquivalences" or
                    "System.Text.RegularExpressions.RegexReplacement" or
                    "System.Text.RegularExpressions.Capture" or
                    "System.Text.RegularExpressions.CaptureCollection" or
                    "System.Text.RegularExpressions.Group" or
                    "System.Text.RegularExpressions.GroupCollection" or
                    "System.Text.RegularExpressions.Match" or
                    "System.Text.RegularExpressions.MatchSparse" or
                    "System.Text.RegularExpressions.MatchCollection",
                "System.Private.Xml" => owner is
                    "System.Xml.XmlTextReaderImpl" or
                    "System.Xml.XmlCharType" or
                    "System.Xml.XmlReader" or
                    "System.Xml.XmlReaderSettings" or
                    "System.Xml.XmlNameTable" or
                    "System.Xml.NameTable" or
                    "System.Xml.XmlNamespaceManager",
                "System.Private.Xml.Linq" => owner is
                    "System.Xml.Linq.XDocument" or
                    "System.Xml.Linq.XContainer" or
                    "System.Xml.Linq.XElement" or
                    "System.Xml.Linq.XAttribute" or
                    "System.Xml.Linq.XNode" or
                    "System.Xml.Linq.XObject" or
                    "System.Xml.Linq.XName" or
                    "System.Xml.Linq.XNamespace" or
                    "System.Xml.Linq.XHashtable`1" or
                    "System.Xml.Linq.NamespaceCache" or
                    "System.Xml.Linq.BaseUriAnnotation" or
                    "System.Xml.Linq.LineInfoAnnotation" or
                    "System.Xml.Linq.LineInfoEndElementAnnotation" or
                    "System.Xml.Linq.XText" or
                    "System.Xml.Linq.XCData" or
                    "System.Xml.Linq.XComment" or
                    "System.Xml.Linq.XDeclaration" or
                    "System.Xml.Linq.XProcessingInstruction",
                _ => false
            };
        }
        if (assembly is "System.IO.Compression" or "System.IO.Compression.Brotli")
        {
            // AB and built-in resource archives read ZIP/Deflate during Player
            // startup. Compile the retained reader families, including nested
            // helpers; compression-only families keep their existing IL fallback.
            var separator = type.IndexOf('/');
            var compressionOwner = separator < 0 ? type : type[..separator];
            return compressionOwner is
                "System.IO.Compression.BrotliStream" or
                "System.IO.Compression.Crc32Helper" or
                "System.IO.Compression.DeflateManagedStream" or
                "System.IO.Compression.DeflateStream" or
                "System.IO.Compression.HuffmanTree" or
                "System.IO.Compression.Inflater" or
                "System.IO.Compression.InflaterManaged" or
                "System.IO.Compression.InputBuffer" or
                "System.IO.Compression.OutputWindow" or
                "System.IO.Compression.SubReadStream" or
                "System.IO.Compression.Zip64EndOfCentralDirectoryLocator" or
                "System.IO.Compression.Zip64EndOfCentralDirectoryRecord" or
                "System.IO.Compression.Zip64ExtraField" or
                "System.IO.Compression.ZipArchive" or
                "System.IO.Compression.ZipArchiveEntry" or
                "System.IO.Compression.ZipCentralDirectoryFileHeader" or
                "System.IO.Compression.ZipEndOfCentralDirectoryBlock" or
                "System.IO.Compression.ZipGenericExtraField" or
                "System.IO.Compression.ZipHelper" or
                "System.IO.Compression.ZipLocalFileHeader" or
                "System.IO.Compression.ZLibNative";
        }
        if (assembly == "YamlDotNet")
        {
            // Keep the exact YAML parser and default deserialization families
            // native. Managed images and the fallback for other features stay
            // intact; serializer/emitter and broad BCL reflection are excluded.
            if (type.StartsWith("YamlDotNet.Core.", StringComparison.Ordinal)) return true;
            var separator = type.IndexOf('/');
            var yamlOwner = separator < 0 ? type : type[..separator];
            return yamlOwner is
                "YamlDotNet.Helpers.DefaultFsharpHelper" or
                "YamlDotNet.Helpers.FsharpHelper" or
                "YamlDotNet.PropertyInfoExtensions" or
                "YamlDotNet.ReflectionExtensions" or
                "YamlDotNet.Serialization.BuilderSkeleton`1" or
                "YamlDotNet.Serialization.Converters.GuidConverter" or
                "YamlDotNet.Serialization.Converters.SystemTypeConverter" or
                "YamlDotNet.Serialization.Converters.TimeSpanConverter" or
                "YamlDotNet.Serialization.Converters.UriConverter" or
                "YamlDotNet.Serialization.Deserializer" or
                "YamlDotNet.Serialization.DeserializerBuilder" or
                "YamlDotNet.Serialization.LazyComponentRegistrationList`2" or
                "YamlDotNet.Serialization.LazyComponentRegistrationListExtensions" or
                "YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention" or
                "YamlDotNet.Serialization.NamingConventions.NullNamingConvention" or
                "YamlDotNet.Serialization.NodeDeserializers.ArrayNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.CollectionNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.DictionaryDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.DictionaryNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.EnumerableNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.FsharpListNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.NullNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.ObjectNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.ScalarNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.TypeConverterNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.YamlConvertibleNodeDeserializer" or
                "YamlDotNet.Serialization.NodeDeserializers.YamlSerializableNodeDeserializer" or
                "YamlDotNet.Serialization.NodeTypeResolvers.DefaultContainersNodeTypeResolver" or
                "YamlDotNet.Serialization.NodeTypeResolvers.MappingNodeTypeResolver" or
                "YamlDotNet.Serialization.NodeTypeResolvers.PreventUnknownTagsNodeTypeResolver" or
                "YamlDotNet.Serialization.NodeTypeResolvers.TagNodeTypeResolver" or
                "YamlDotNet.Serialization.NodeTypeResolvers.YamlConvertibleTypeResolver" or
                "YamlDotNet.Serialization.NodeTypeResolvers.YamlSerializableTypeResolver" or
                "YamlDotNet.Serialization.ObjectDescriptor" or
                "YamlDotNet.Serialization.ObjectFactories.DefaultObjectFactory" or
                "YamlDotNet.Serialization.ObjectFactories.ObjectFactoryBase" or
                "YamlDotNet.Serialization.PropertyDescriptor" or
                "YamlDotNet.Serialization.Settings" or
                "YamlDotNet.Serialization.TypeInspectors.CachedTypeInspector" or
                "YamlDotNet.Serialization.TypeInspectors.CompositeTypeInspector" or
                "YamlDotNet.Serialization.TypeInspectors.NamingConventionTypeInspector" or
                "YamlDotNet.Serialization.TypeInspectors.ReadableAndWritablePropertiesTypeInspector" or
                "YamlDotNet.Serialization.TypeInspectors.ReadableFieldsTypeInspector" or
                "YamlDotNet.Serialization.TypeInspectors.ReadablePropertiesTypeInspector" or
                "YamlDotNet.Serialization.TypeInspectors.ReflectionTypeInspector" or
                "YamlDotNet.Serialization.TypeInspectors.TypeInspectorSkeleton" or
                "YamlDotNet.Serialization.TypeInspectors.WritablePropertiesTypeInspector" or
                "YamlDotNet.Serialization.TypeResolvers.StaticTypeResolver" or
                "YamlDotNet.Serialization.Utilities.ReflectionTypeConverter" or
                "YamlDotNet.Serialization.Utilities.SerializerState" or
                "YamlDotNet.Serialization.Utilities.StringExtensions" or
                "YamlDotNet.Serialization.Utilities.TypeConverter" or
                "YamlDotNet.Serialization.Utilities.TypeConverterCache" or
                "YamlDotNet.Serialization.ValueDeserializers.AliasValueDeserializer" or
                "YamlDotNet.Serialization.ValueDeserializers.MaximumRecursionValueDeserializer" or
                "YamlDotNet.Serialization.ValueDeserializers.NodeValueDeserializer" or
                "YamlDotNet.Serialization.YamlAttributeOverrides" or
                "YamlDotNet.Serialization.YamlAttributeOverridesInspector" or
                "YamlDotNet.Serialization.YamlAttributesTypeInspector" or
                "YamlDotNet.Serialization.YamlConverterAttribute" or
                "YamlDotNet.Serialization.YamlFormatter" or
                "YamlDotNet.Serialization.YamlMemberAttribute";
        }
        if (assembly is "SkiaSharp" or "SkiaSharp.HarfBuzz" or "HarfBuzzSharp" or
            "BCnEncoder" or "StbImageSharp" or
            "Silk.NET.Core" or "Silk.NET.Maths" or "Silk.NET.Windowing.Common" or
            "Microsoft.DotNet.PlatformAbstractions" or "Microsoft.Extensions.DependencyModel")
            return false;
        if (assembly == "System.Private.CoreLib")
        {
            // Packaged resource and catalog readers use these binary I/O entry
            // points during startup. Nested asynchronous helpers keep IL fallback.
            if (type is "System.IO.BinaryReader" or "System.IO.BinaryWriter" or
                "System.IO.MemoryStream" or "System.IO.Stream") return true;
            // Archive/catalog identities and binary configuration use Guid and
            // Convert during startup. Keep these small retained families native,
            // including Guid's parsing helpers, without widening reflection.
            if (type == "System.Guid" || type.StartsWith("System.Guid/", StringComparison.Ordinal) ||
                type == "System.Convert") return true;
            // String and collection entry points are already native. Their
            // ordinal comparison/casing helpers also run for catalog paths and
            // resource keys, including non-ASCII keys. Keep the small exact
            // helper families native without widening globalization/reflection.
            if (type is "System.Marvin" or "System.OrdinalComparer" or
                "System.OrdinalCaseSensitiveComparer" or "System.OrdinalIgnoreCaseComparer" or
                "System.Globalization.Ordinal" or "System.Globalization.OrdinalCasing") return true;
            if (type.StartsWith("System.Collections.", StringComparison.Ordinal) ||
                type.StartsWith("System.Threading.", StringComparison.Ordinal) ||
                type.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal) ||
                type.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal) ||
                type.StartsWith("System.Runtime.Intrinsics.", StringComparison.Ordinal) ||
                type.StartsWith("System.Text.", StringComparison.Ordinal) ||
                type.StartsWith("System.Numerics.", StringComparison.Ordinal)) return true;
            foreach (var prefix in new[]
            {
                "System.Array", "System.String", "System.Span`", "System.ReadOnlySpan`",
                "System.Memory`", "System.ReadOnlyMemory`", "System.MemoryExtensions",
                "System.Buffer", "System.BitConverter", "System.Math", "System.Number",
                "System.Int", "System.UInt", "System.Single", "System.Double", "System.Decimal",
                "System.Boolean", "System.Char", "System.Enum", "System.Object", "System.ValueType",
                "System.Tuple", "System.ValueTuple", "System.Nullable", "System.HashCode",
                "System.DateTime", "System.TimeSpan", "System.DateOnly", "System.TimeOnly",
                "System.Globalization.NumberFormatInfo", "System.Globalization.CultureInfo",
                "System.Diagnostics.Stopwatch", "System.Delegate", "System.MulticastDelegate"
            })
                if (type.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }
        if (assembly == "System.Security.Cryptography")
            // Browser SHA-256 uses a managed compression loop. AssetBundle
            // integrity checks process whole bundles, so keep that loop and
            // its small provider path native even with the balanced policy.
            // Other algorithms retain their IL and interpreter fallback.
            return type is "System.Security.Cryptography.SHA256" or
                "System.Security.Cryptography.IncrementalHash" or
                "System.Security.Cryptography.HashProvider" or
                "System.Security.Cryptography.HashProviderDispenser" or
                "System.Security.Cryptography.HashProviderDispenser/OneShotHashProvider" or
                "System.Security.Cryptography.LiteHash" or
                "System.Security.Cryptography.LiteHashProvider" or
                "System.Security.Cryptography.SHAManagedHashProvider" or
                "System.Security.Cryptography.SHAManagedHashProvider/SHAManagedImplementationBase" or
                "System.Security.Cryptography.SHAManagedHashProvider/SHA256ManagedImplementation" or
                "System.Security.Cryptography.SHAManagedHashProvider/SHAUtils";
        if (assembly.StartsWith("System.", StringComparison.Ordinal))
            return assembly is "System.Collections" or "System.Collections.Concurrent" or
                "System.Collections.Immutable" or "System.Collections.NonGeneric" or
                "System.Collections.Specialized" or "System.Linq" or "System.Memory" or
                "System.ObjectModel" or "System.Runtime.Numerics" or "System.Numerics.Vectors" or
                "System.Runtime.CompilerServices.Unsafe" or "System.Runtime.Intrinsics" or
                "System.Runtime.InteropServices.JavaScript" or "System.Runtime.InteropServices" or
                "System.Runtime" or "System.Threading" or "System.Threading.Tasks.Parallel";
        return true;
    }

    private sealed record PrecompiledImage(string Name, string Sha256, string Mvid);
    static int nextId=1;
    static BinaryWriter writer=null!;
    static void Record(byte kind,int id) {writer.Write(kind);writer.Write(id);}
    static void String(string value) {var bytes=Encoding.UTF8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes);}
    static void Main(string[] args)
    {
        if (FrozenScriptStripVerifier.TryRun(args)) return;
        const string coldProfilePrefix = "--cold-methods=";
        var coldProfiles = args.Where(arg => arg.StartsWith(coldProfilePrefix, StringComparison.Ordinal)).ToArray();
        if (coldProfiles.Length > 1) throw new ArgumentException("Specify one Web AOT cold-method policy.");
        var coldProfilePath = coldProfiles.Length == 0 ? null : coldProfiles[0][coldProfilePrefix.Length..];
        args = args.Where(arg => !arg.StartsWith(coldProfilePrefix, StringComparison.Ordinal)).ToArray();
        const string hotProfilePrefix = "--hot-profile=";
        var hotProfiles = args.Where(arg => arg.StartsWith(hotProfilePrefix, StringComparison.Ordinal)).ToArray();
        if (hotProfiles.Length > 1) throw new ArgumentException("Specify one Web AOT hot profile.");
        var hotProfile = hotProfiles.Length == 0 ? null : WebAotHotProfile.Load(hotProfiles[0][hotProfilePrefix.Length..]);
        if (coldProfilePath is not null && hotProfile is not null)
            throw new ArgumentException("WebAotColdMethods.json and the legacy WebAotProfile.json cannot be combined. Use one native selection policy.");
        args = args.Where(arg => !arg.StartsWith(hotProfilePrefix, StringComparison.Ordinal)).ToArray();
        const string policyPrefix = "--bcl-policy=";
        var policies = args.Where(arg => arg.StartsWith(policyPrefix, StringComparison.Ordinal)).ToArray();
        if (policies.Length > 1) throw new ArgumentException("Specify one BCL AOT policy.");
        var policy = policies.Length == 0 ? "balanced" : policies[0][policyPrefix.Length..];
        if (policy is not ("balanced" or "full")) throw new ArgumentException("BCL AOT policy must be 'balanced' or 'full'.");
        fullBclAot = policy == "full";
        args = args.Where(arg => !arg.StartsWith(policyPrefix, StringComparison.Ordinal)).ToArray();
        if(args.Length is < 2 or > 3)throw new ArgumentException("Usage: MetadataAotProfile <assemblies.txt> <output.aotprofile> [precompiled-images.json] [--hot-profile=path]");
        var precompiled = args.Length == 3
            ? (JsonSerializer.Deserialize<PrecompiledImage[]>(File.ReadAllText(args[2])) ??
                throw new InvalidDataException("Empty precompiled script image manifest."))
                .ToDictionary(static image => image.Name, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PrecompiledImage>(StringComparer.OrdinalIgnoreCase);
        var verifiedPrecompiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary=output+".tmp";
        var included=new List<object>();
        using(var file=File.Create(temporary))using(var binary=new BinaryWriter(file))
        {
            writer=binary;writer.Write(Encoding.ASCII.GetBytes("AOTPROFILE"));writer.Write(0x10000);
            foreach(var assemblyPath in File.ReadAllLines(args[0]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                using var input=File.OpenRead(assemblyPath);using var pe=new PEReader(input);
                if(!pe.HasMetadata)continue;var metadata=pe.GetMetadataReader();if(!metadata.IsAssembly)continue;
                var assemblyName=metadata.GetString(metadata.GetAssemblyDefinition().Name);
                if (precompiled.TryGetValue(assemblyName, out var expected))
                {
                    var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
                    var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
                    if (!sha.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase) ||
                        !Guid.TryParse(expected.Mvid, out var expectedMvid) || mvid != expectedMvid)
                        throw new InvalidDataException($"Web precompiled script '{assemblyName}' was rewritten before AOT. " +
                            "The linked image must exactly match the AB/runtime script image.");
                    verifiedPrecompiled.Add(assemblyName);
                }
                // Decoder generic instantiations can exhaust the native compiler's memory.
                // Keep its original IL and the LLVMOnlyInterp fallback available.
                if(assemblyName=="AssetRipper.TextureDecoder")continue;
                var imageId=nextId++;Record(1,imageId);String(assemblyName);String(metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString());
                var signatureProvider=new MonoSignature();var methodCount=0;var interpretedCount=0;var hotProfileInterpretedCount=0;
                foreach(var typeHandle in metadata.TypeDefinitions)
                {
                    var type=metadata.GetTypeDefinition(typeHandle);var name=MonoSignature.TypeName(metadata,typeHandle);
                    hotProfile?.ObserveType(assemblyName, name);
                    if (!IsNativeType(assemblyName, name))
                    {
                        interpretedCount += type.GetMethods().Count(handle => metadata.GetMethodDefinition(handle).RelativeVirtualAddress != 0);
                        continue;
                    }
                    var classId=nextId++;Record(2,classId);writer.Write((byte)0x12);writer.Write(imageId);writer.Write(-1);String(name.Contains('.')?name:"."+name);
                    var typeParameters=type.GetGenericParameters().Select(p=>metadata.GetString(metadata.GetGenericParameter(p).Name)).ToArray();
                    foreach(var methodHandle in type.GetMethods())
                    {
                        var method=metadata.GetMethodDefinition(methodHandle);
                        if(method.RelativeVirtualAddress==0 || (method.Attributes&(MethodAttributes.Abstract|MethodAttributes.PinvokeImpl))!=0)continue;
                        // Keep validated BTEX metadata native without pulling the
                        // optional generic ASTC/ETC CPU decoders into native AOT.
                        if (assemblyName == "BEngine" && name.Contains("TextureImageData", StringComparison.Ordinal) &&
                            (name != "BEngine.Rendering.TextureImageData" || metadata.GetString(method.Name) is not
                                ("get_Signature" or "IsContainer" or "Inspect" or "LevelSize")))
                        { interpretedCount++; continue; }
                        if (hotProfile is not null && !hotProfile.SelectsMethod(assemblyName, name, metadata, typeHandle, methodHandle))
                        { interpretedCount++; hotProfileInterpretedCount++; continue; }
                        var context=new MonoSignature.Context(typeParameters,method.GetGenericParameters().Select(p=>metadata.GetString(metadata.GetGenericParameter(p).Name)).ToArray());
                        var signature=method.DecodeSignature(signatureProvider,context);
                        Record(4,nextId++);writer.Write(classId);writer.Write(-1);writer.Write(signature.ParameterTypes.Length);String(metadata.GetString(method.Name));
                        String(signature.ReturnType+"("+string.Join(",",signature.ParameterTypes)+")");methodCount++;
                    }
                }
                included.Add(new{Assembly=assemblyName,Path=assemblyPath,Mvid=metadata.GetGuid(metadata.GetModuleDefinition().Mvid),Methods=methodCount,InterpretedMethods=interpretedCount,HotProfileCovered=hotProfile?.Covers(assemblyName) == true,HotProfileInterpretedMethods=hotProfileInterpretedCount});
                if (interpretedCount > 0) Console.WriteLine($"BEngine Web AOT selection: {assemblyName}: {methodCount} native profile methods, {interpretedCount} cold IL methods.");
            }
            Record(0,0);
        }
        if (precompiled.Keys.Any(name => !verifiedPrecompiled.Contains(name)))
            throw new InvalidDataException("Web precompiled scripts are absent from the native input: " +
                string.Join(", ", precompiled.Keys.Where(name => !verifiedPrecompiled.Contains(name))));
        hotProfile?.VerifyObservedTypes();
        File.Move(temporary,output,true);
        if (coldProfilePath is not null) WebAotColdMethods.Apply(args[0], output, coldProfilePath);
        File.WriteAllText(output+".json",JsonSerializer.Serialize(new{Format="AOTPROFILE 1.0",BclPolicy=policy,
            HotProfile=hotProfile is null ? null : new {hotProfile.Path,hotProfile.Sha256,CoveredAssemblies=hotProfile.AssemblyCount,
                ConfiguredHotTypes=hotProfile.HotTypeCount,MatchedHotTypes=hotProfile.MatchedHotTypeCount,
                UnmatchedCovered=hotProfile.UnmatchedCoveredAssemblies,hotProfile.UnmatchedTypes},Assemblies=included},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"BEngine Web AOT profile: {included.Count} assemblies, {new FileInfo(output).Length} bytes; unselected IL keeps interpreter fallback.");
    }
}
