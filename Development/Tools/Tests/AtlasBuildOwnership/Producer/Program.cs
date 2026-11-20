using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception
{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
RuntimeHelpers.RunModuleConstructor(typeof(TextureAtlas).Module.ModuleHandle);
RuntimeHelpers.RunModuleConstructor(typeof(TextureAtlasImporter).Module.ModuleHandle);
var rt=typeof(TextureAtlas).Assembly.GetType("BEngine.TextureAtlasRuntimeData")!;
#if LEGACY
const string mode="Legacy";
var oldCompiler=rt.GetMethod("CompileRuntimeBytes",BindingFlags.Static|BindingFlags.NonPublic)!;
byte[] Compile(ReadOnlyMemory<byte> bytes,CancellationToken cancellation=default)
{try{return(byte[])oldCompiler.Invoke(null,[bytes,cancellation])!;}catch(TargetInvocationException e)when(e.InnerException is not null){throw e.InnerException;}}
#else
const string mode="Current";
Check(rt.GetMethod("CompileRuntimeBytes",BindingFlags.Static|BindingFlags.NonPublic) is null,"Runtime contains no atlas publication compiler");
byte[] Compile(ReadOnlyMemory<byte> bytes,CancellationToken cancellation=default)=>TextureAtlasBuildDataCompiler.Compile(bytes,cancellation);
#endif
string Serialize(TextureAtlas atlas)
{
    RuntimeAssetReferences.BindSource(atlas,"atlas.yaml","","",Guid.Parse("dab153ae-d11d-440e-9232-8ad789d8cb54"),"TextureAtlas");
    try{return YamlUtility.Serialize(atlas);}finally{BObject.DestroyImmediate(atlas);}
}
var inputs=new Dictionary<string,byte[]>{
 ["empty"]=Encoding.UTF8.GetBytes(Serialize(new TextureAtlas{Padding=3,MaxSize=256})),
 ["packed"]=Encoding.UTF8.GetBytes(Serialize(new TextureAtlas{Texture="atlas.png",Width=4,Height=4,Padding=2,Extrude=1,Sprites=[new TextureAtlasSprite{Name="Hero",Source="source.png",X=1,Y=1,Width=2,Height=2,PivotX=Fix64.Half,PivotY=Fix64.Half}]})),
 ["defaults"]=Encoding.UTF8.GetBytes("{}\n")
};
if(args.Length>1)inputs["legacy-fixture"]=File.ReadAllBytes(args[1]);
var fixtures=new List<object>();
foreach(var (name,input)in inputs)
{
    var before=input.ToArray();var bytes=Compile(input);
    Check(input.SequenceEqual(before),name+": compiler preserves source bytes");
    Check(bytes.AsSpan().StartsWith("BTAR"u8),name+": ordinary atlas produces the compact envelope");
    Check(Compile(bytes).SequenceEqual(bytes),name+": compiled input is not nested in another envelope");
    Check(Compile(input).SequenceEqual(bytes),name+": repeated compilation is deterministic");
    File.WriteAllBytes(Path.Combine(output,name+".yaml"),input);File.WriteAllBytes(Path.Combine(output,name+".runtime"),bytes);
    fixtures.Add(new{Name=name,SHA256=Convert.ToHexString(SHA256.HashData(bytes)),SourceSHA256=Convert.ToHexString(SHA256.HashData(input)),Bytes=bytes.Length,ExplicitIdentity=name!="defaults"});
    if(name=="legacy-fixture")File.WriteAllBytes(Path.Combine(output,"atlas.runtime"),bytes);
}
using(var cancelled=new CancellationTokenSource())
{cancelled.Cancel();Throws<OperationCanceledException>(()=>Compile(inputs["empty"],cancelled.Token),"Pre-cancelled compilation stops");}
var corrupt=Compile(inputs["packed"]);corrupt[16]^=0x7f;
Throws<InvalidDataException>(()=>Compile(corrupt),"Corrupt compiled input cannot be republished");
YamlUtility.RegisterConverter(new ForeignConverter());
try
{
    Check(Compile(inputs["packed"]).SequenceEqual(inputs["packed"]),"Foreign converters preserve the authored source at publication");
    Throws<InvalidDataException>(()=>Compile(corrupt),"Foreign converters cannot hide a corrupt compiled payload");
}
finally{YamlUtility.UnregisterConverters(Assembly.GetExecutingAssembly());}
var files=RuntimeServices.GetRequiredService<FileUtility>();var paths=RuntimeServices.GetRequiredService<PathUtility>();
var source=Path.Combine(output,"registered.atlas.yaml");File.WriteAllBytes(source,inputs["packed"]);
var sha=Convert.ToHexString(SHA256.HashData(inputs["packed"]));
Check(PlatformAssetBuildPipeline.TryPrepareRuntimeArtifact(source,sha,source,sha,nameof(TextureAtlas),nameof(TextureAtlasImporter),new Dictionary<string,string>(),null,files,paths,CancellationToken.None,out var prepared,out var settings),"The production build pipeline finds the package compiler");
Check(prepared.SequenceEqual(Compile(inputs["packed"]))&&settings["builtRuntimeFormat"]=="atlas-envelope-v1","Registered production compilation preserves bytes and format identity");
RuntimeAssemblyCleanup.UnregisterExtensions(typeof(TextureAtlasImporter).Assembly);
var removed=!PlatformAssetBuildPipeline.TryPrepareRuntimeArtifact(source,sha,source,sha,nameof(TextureAtlas),nameof(TextureAtlasImporter),new Dictionary<string,string>(),null,files,paths,CancellationToken.None,out _,out _);
#if !LEGACY
Check(removed,"Retiring only the Editor assembly removes its publication compiler");
#endif
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BEngine.EditorWorkbench"),"Headless publication never loads the default workbench");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Mode=mode,EditorRetiredCompilerWasRemoved=removed,Fixtures=fixtures,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("ATLAS_BUILD_PRODUCER_OK|"+mode+"|"+checks.Count);

sealed class ForeignConverter:IYamlTypeConverter
{
    public bool Accepts(Type type)=>false;
    public object? ReadYaml(IParser parser,Type type,ObjectDeserializer deserializer)=>throw new NotSupportedException();
    public void WriteYaml(IEmitter emitter,object? value,Type type,ObjectSerializer serializer)=>throw new NotSupportedException();
}
