using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var inputs=Path.GetFullPath(args[1]);
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception
{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
DotNetPlatformServices.Initialize();RuntimeHelpers.RunModuleConstructor(typeof(TextureAtlas).Module.ModuleHandle);
using var producer=JsonDocument.Parse(File.ReadAllText(Path.Combine(inputs,"Result.json")));
TextureAtlas Decode(byte[] bytes)
{
    if(!RuntimeAssetDecoding.TryDecodeRegistered(bytes,"atlas.yaml",typeof(TextureAtlas),out var result))throw new Exception("No registered atlas decoder");
    return(TextureAtlas)result;
}
foreach(var fixture in producer.RootElement.GetProperty("Fixtures").EnumerateArray())
{
    var name=fixture.GetProperty("Name").GetString()!;var binary=File.ReadAllBytes(Path.Combine(inputs,name+".runtime"));
    var source=File.ReadAllBytes(Path.Combine(inputs,name+".yaml"));
    var plain=Decode(source);var compiled=Decode(binary);
    try
    {
        if(fixture.GetProperty("ExplicitIdentity").GetBoolean())Check(compiled.Id==plain.Id,name+": persistent identity survives compilation");
        Check(compiled.Texture==plain.Texture&&compiled.Width==plain.Width&&compiled.Height==plain.Height&&compiled.Padding==plain.Padding&&compiled.Extrude==plain.Extrude&&compiled.MaxSize==plain.MaxSize,name+": atlas settings survive compilation");
        Check(compiled.Sprites.Count==plain.Sprites.Count&&compiled.Sprites.Zip(plain.Sprites).All(p=>p.First.Name==p.Second.Name&&p.First.Source==p.Second.Source&&p.First.X==p.Second.X&&p.First.Y==p.Second.Y&&p.First.Width==p.Second.Width&&p.First.Height==p.Second.Height&&p.First.PivotX==p.Second.PivotX&&p.First.PivotY==p.Second.PivotY),name+": packed regions and pivots survive compilation");
        YamlUtility.RegisterConverter(new ForeignConverter());
        try
        {
            var replay=Decode(binary);try{Check(replay.Texture==plain.Texture&&replay.Sprites.Count==plain.Sprites.Count,name+": runtime converter changes replay the preserved source");}finally{BObject.DestroyImmediate(replay);}
            var corrupt=binary.ToArray();corrupt[16]^=0x7f;
            Throws<InvalidDataException>(()=>Decode(corrupt),name+": corrupt compiled data is rejected before fallback");
        }
        finally{YamlUtility.UnregisterConverters(Assembly.GetExecutingAssembly());}
        Throws<InvalidDataException>(()=>Decode(binary[..12]),name+": truncated envelopes are rejected");
        Throws<InvalidDataException>(()=>Decode([..binary,0]),name+": unexpected trailing bytes are rejected");
    }
    finally{BObject.DestroyImmediate(plain);BObject.DestroyImmediate(compiled);}
}
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is{}n&&(n=="BEngine.Core.Editor"||n.EndsWith(".Editor")||n=="BEngine.EditorWorkbench")),"Runtime atlas decoding loads no Editor assembly");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("ATLAS_RUNTIME_CONSUMER_OK|"+checks.Count);

sealed class ForeignConverter:IYamlTypeConverter
{
    public bool Accepts(Type type)=>false;
    public object? ReadYaml(IParser parser,Type type,ObjectDeserializer deserializer)=>throw new NotSupportedException();
    public void WriteYaml(IEmitter emitter,object? value,Type type,ObjectSerializer serializer)=>throw new NotSupportedException();
}
