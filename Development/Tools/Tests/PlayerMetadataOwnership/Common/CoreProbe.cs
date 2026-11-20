using System.Text;
using BEngine;
using BEngine.Platform;
using BEngine.Player.Serialization;

public static class MetadataCoreProbe
{
    public static List<string> Run(string root)
    {
        var checks=new List<string>();
        void Check(bool condition,string name) { if(!condition)throw new Exception(name);checks.Add(name); }
        void Reject(Action action,string name)
        {
            try { action(); } catch(InvalidDataException) { checks.Add(name);return; }
            throw new Exception(name);
        }
        Directory.CreateDirectory(root);
        var doc=new PlayerMetadataDocument();
        var empty=PlayerMetadataSerializer.Serialize(doc);
        Check(PlayerMetadataSerializer.Deserialize(empty).ContentSections.Count==0,"Core-only execution metadata needs no content package section");
        doc.Project.Name="Metadata / 引擎";
        doc.ProjectSettings.RunInBackground=true;
        doc.ContentSections.Add(new(101,7,new byte[]{4,3,2,1}));
        doc.ContentSections.Add(new(102,1,new byte[]{9,8,7}));
        var bytes=PlayerMetadataSerializer.Serialize(doc);
        var loaded=PlayerMetadataSerializer.Deserialize(bytes);
        Check(loaded.Project.Name==doc.Project.Name&&loaded.ProjectSettings.RunInBackground,"Core settings preserve Unicode and runtime flags");
        Check(loaded.ContentSections.Count==2&&loaded.ContentSections[0].Id==101&&loaded.ContentSections[0].Version==7&&
            loaded.ContentSections[0].Data.Span.SequenceEqual(new byte[]{4,3,2,1}),"Unknown provider sections retain identity, version, bytes and order");
        Check(PlayerMetadataSerializer.Serialize(loaded).AsSpan().SequenceEqual(bytes),"Core provider sections round-trip deterministically");
        Array.Fill(bytes,(byte)0);
        Check(loaded.ContentSections[0].Data.Span.SequenceEqual(new byte[]{4,3,2,1}),"Deserialized section data does not borrow the caller's mutable buffer");
        var path=Path.Combine(root,"metadata.bmeta");
        PlayerMetadataSerializer.Save(doc,path);
        var file=PlayerMetadataSerializer.Load(path);
        Check(file.ContentSections[1].Data.Span.SequenceEqual(new byte[]{9,8,7}),"File loading preserves owned opaque sections");
        doc.Project.Name="replacement";PlayerMetadataSerializer.Save(doc,path);
        Check(PlayerMetadataSerializer.Load(path).Project.Name=="replacement"&&!Directory.GetFiles(root,"*.tmp-*").Any(),"Atomic save replaces existing metadata without leftover staging files");
        if(OperatingSystem.IsWindows())
        {
            var previous=File.ReadAllBytes(path);
            using(var locked=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                try { PlayerMetadataSerializer.Save(new(),path);throw new Exception("Locked publication unexpectedly succeeded"); }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                { checks.Add("A failed replacement retains the original metadata"); }
            }
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(previous)&&!Directory.GetFiles(root,"*.tmp-*").Any(),"Failed save cleans its staging file and preserves the target bytes");
        }
        var invalid=PlayerMetadataSerializer.Serialize(doc);invalid[^1]^=1;
        Reject(()=>PlayerMetadataSerializer.Deserialize(invalid),"Payload corruption is rejected by the envelope checksum");
        foreach(ushort reserved in new ushort[]{0,1,2,4,5})
        {
            var bad=new PlayerMetadataDocument {ContentSections=[new(reserved,1,ReadOnlyMemory<byte>.Empty)]};
            Reject(()=>PlayerMetadataSerializer.Serialize(bad),"Content cannot replace reserved Core section "+reserved);
        }
        Reject(()=>PlayerMetadataSerializer.Serialize(new(){ContentSections=[new(99,1,default),new(99,2,default)]}),"Duplicate provider identities are rejected");
        Reject(()=>PlayerMetadataSerializer.Serialize(new(){ContentSections=[new(99,0,default)]}),"An unversioned provider section is rejected");
        Reject(()=>PlayerMetadataSerializer.Serialize(new(){ContentSections=[new(99,1,new byte[2*1024*1024+1])]}),"Oversized provider data is rejected");
        var block=new byte[1536*1024];
        Reject(()=>PlayerMetadataSerializer.Serialize(new(){ContentSections=[new(90,1,block),new(91,1,block),new(92,1,block)]}),"Total metadata output is bounded while it is written");
        var fields=new MemoryStream();
        using(var writer=new BinaryWriter(fields,Encoding.UTF8,true))
        {
            PlayerMetadataBinary.WriteString(writer,"prefix / 中文");PlayerMetadataBinary.WriteBoolean(writer,true);
        }
        var fieldBytes=fields.ToArray();
        using(var reader=new BinaryReader(new MemoryStream(fieldBytes,false)))
            Check(PlayerMetadataBinary.ReadString(reader)=="prefix / 中文"&&PlayerMetadataBinary.ReadBoolean(reader),"Field readers support non-exposed seekable buffers");
        var padded=new byte[fieldBytes.Length+13];fieldBytes.CopyTo(padded,7);
        using(var reader=PlayerMetadataBinary.CreateReader(padded.AsMemory(7,fieldBytes.Length)))
            Check(PlayerMetadataBinary.ReadString(reader)=="prefix / 中文"&&PlayerMetadataBinary.ReadBoolean(reader),"Field readers honor an array section's nonzero offset and bounds");
        using(var reader=PlayerMetadataBinary.CreateReader(ReadOnlyMemory<byte>.Empty))
            Check(reader.BaseStream.Length==0,"Empty provider data creates an empty bounded reader");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.AssetBundles" or "BEngine.Graphics" or "BEngine.Application"),"Independent metadata use loads no content, graphics or framework package");
        return checks;
    }
}
