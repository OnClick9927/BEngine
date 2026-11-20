using System.Security.Cryptography;
using System.Text.Json;
using BEngine.Platform.DotNet;
using BEngine.Player.ProjectSystem;
DotNetPlatformServices.Initialize();
var fixtures=Path.GetFullPath(args[0]);
var rows=JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures,"BaselineResults.json"))).RootElement.EnumerateArray();
var checks=new List<string>();
foreach(var row in rows)
{
    var name=row.GetProperty("File").GetString()!;
    var accepted=row.GetProperty("Accepted").GetBoolean();
    RuntimeMetadataDocument? document=null;Exception? failure=null;
    try {document=RuntimeMetadataSerializer.Deserialize(File.ReadAllBytes(Path.Combine(fixtures,name)));}
    catch(Exception error){failure=error;}
    if(accepted!=(failure is null))throw new Exception("Acceptance changed: "+name,failure);
    if(document is not null)
    {
        var hash=Convert.ToHexString(SHA256.HashData(RuntimeMetadataSerializer.Serialize(document)));
        if(hash!=row.GetProperty("NormalizedSHA256").GetString())throw new Exception("Normalized bytes changed: "+name);
    }
    else if(failure!.GetType().FullName!=row.GetProperty("Error").GetString())
        throw new Exception("Rejection type changed: "+name,failure);
    checks.Add(name);
}
File.WriteAllText(args[1],JsonSerializer.Serialize(new{Passed=true,Count=checks.Count,Checks=checks}));
Console.WriteLine("METADATA_LEGACY_COMPATIBILITY_OK|"+checks.Count);
