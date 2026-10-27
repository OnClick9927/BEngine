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
        if (fullBclAot) return true;
        if (assembly == "YamlDotNet")
            // Scanning, parsing and immutable token/mark/buffer operations run for
            // every character/event in configuration documents. Keep this compact
            // core native; reflection, type inspection and node deserializers
            // retain their existing IL and hybrid interpreter fallback.
            return type.StartsWith("YamlDotNet.Core.", StringComparison.Ordinal);
        if (assembly is "SkiaSharp" or "SkiaSharp.HarfBuzz" or "HarfBuzzSharp" or
            "BCnEncoder" or "StbImageSharp" or
            "Silk.NET.Core" or "Silk.NET.Maths" or "Silk.NET.Windowing.Common" or
            "Microsoft.DotNet.PlatformAbstractions" or "Microsoft.Extensions.DependencyModel")
            return false;
        if (assembly == "System.Private.CoreLib")
        {
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
        const string policyPrefix = "--bcl-policy=";
        var policies = args.Where(arg => arg.StartsWith(policyPrefix, StringComparison.Ordinal)).ToArray();
        if (policies.Length > 1) throw new ArgumentException("Specify one BCL AOT policy.");
        var policy = policies.Length == 0 ? "balanced" : policies[0][policyPrefix.Length..];
        if (policy is not ("balanced" or "full")) throw new ArgumentException("BCL AOT policy must be 'balanced' or 'full'.");
        fullBclAot = policy == "full";
        args = args.Where(arg => !arg.StartsWith(policyPrefix, StringComparison.Ordinal)).ToArray();
        if(args.Length is < 2 or > 3)throw new ArgumentException("Usage: MetadataAotProfile <assemblies.txt> <output.aotprofile> [precompiled-images.json]");
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
                var signatureProvider=new MonoSignature();var methodCount=0;var interpretedCount=0;
                foreach(var typeHandle in metadata.TypeDefinitions)
                {
                    var type=metadata.GetTypeDefinition(typeHandle);var name=MonoSignature.TypeName(metadata,typeHandle);
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
                        var context=new MonoSignature.Context(typeParameters,method.GetGenericParameters().Select(p=>metadata.GetString(metadata.GetGenericParameter(p).Name)).ToArray());
                        var signature=method.DecodeSignature(signatureProvider,context);
                        Record(4,nextId++);writer.Write(classId);writer.Write(-1);writer.Write(signature.ParameterTypes.Length);String(metadata.GetString(method.Name));
                        String(signature.ReturnType+"("+string.Join(",",signature.ParameterTypes)+")");methodCount++;
                    }
                }
                included.Add(new{Assembly=assemblyName,Path=assemblyPath,Mvid=metadata.GetGuid(metadata.GetModuleDefinition().Mvid),Methods=methodCount,InterpretedMethods=interpretedCount});
                if (interpretedCount > 0) Console.WriteLine($"BEngine Web AOT selection: {assemblyName}: {methodCount} native profile methods, {interpretedCount} cold IL methods.");
            }
            Record(0,0);
        }
        if (precompiled.Keys.Any(name => !verifiedPrecompiled.Contains(name)))
            throw new InvalidDataException("Web precompiled scripts are absent from the native input: " +
                string.Join(", ", precompiled.Keys.Where(name => !verifiedPrecompiled.Contains(name))));
        File.Move(temporary,output,true);
        File.WriteAllText(output+".json",JsonSerializer.Serialize(new{Format="AOTPROFILE 1.0",BclPolicy=policy,Assemblies=included},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"BEngine Web AOT profile: {included.Count} assemblies, {new FileInfo(output).Length} bytes; unselected IL keeps interpreter fallback.");
    }
}
