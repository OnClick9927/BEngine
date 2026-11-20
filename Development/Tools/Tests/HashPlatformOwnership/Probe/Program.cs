using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.AssetBundles;
using BEngine.Networking;
using BEngine.Platform;
using BEngine.Platform.DotNet;

var checks = new List<string>();
var outputs = new SortedDictionary<string, string>();
var assembly = typeof(AssetBundleCatalog).Assembly;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
Exception Unwrap(Exception e) => e is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : e;
void Reject<T>(Action action, string name) where T : Exception
{ try { action(); } catch (Exception e) when (Unwrap(e) is T) { Check(true, name); return; } throw new Exception(name + " did not reject"); }
object? Call(object? target, Type type, string name, params object?[] args) =>
    type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
void Drain(BTask task)
{ for (int i=0; !task.IsCompleted && i<10000; i++) BTaskScheduler.Main.Tick(TimeSpan.Zero); Check(task.IsCompleted, "Operation completes under bounded scheduler pumping"); task.GetAwaiter().GetResult(); }
T DrainValue<T>(BTask<T> task) { Drain(task); return task.GetAwaiter().GetResult(); }
byte[] Data(int count) { var bytes=new byte[count]; new System.Random(1937 + count).NextBytes(bytes); return bytes; }
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

var standaloneFingerprint=AssetBundlePackageDescriptor.ComputeVersion(new AssetBundleCatalog(),[]);
Check(standaloneFingerprint.Length==64,"Fingerprint remains usable before a platform host exists");
DotNetPlatformServices.Initialize();
Check(standaloneFingerprint==AssetBundlePackageDescriptor.ComputeVersion(new AssetBundleCatalog(),[]),"Host selection does not alter fingerprints");
Check(PlatformRuntime.GetRequiredService<IPlatformSha256Service>() is SystemSha256Service, "Windowless host registers SHA service globally");
var lengths = new[] {0,1,55,56,63,64,65,257,65535,65536,65537,1048577};
foreach (var factory in new Func<IPlatformIncrementalSha256>[] {SystemSha256Service.Shared.CreateIncremental, WebSha256Service.Shared.CreateIncremental})
{
    using var digest = factory();
    foreach (var length in lengths)
    {
        var input = Data(length); digest.Reset();
        for (int at=0, step=0; at<input.Length; step++)
        { var count=Math.Min(new[] {1,63,64,8191,65536}[step%5],input.Length-at); digest.Append(input.AsSpan(at,count)); at+=count; }
        var result=Enumerable.Repeat((byte)0x73,48).ToArray(); digest.FinalizeHash(result.AsSpan(7,32));
        Check(result.AsSpan(7,32).SequenceEqual(SHA256.HashData(input)) && result.Take(7).All(b=>b==0x73) && result.Skip(39).All(b=>b==0x73), "SHA padding/chunks/guard bytes " + digest.GetType().Name + ":" + length);
        Reject<InvalidOperationException>(()=>digest.Append([]), "Append after finalization rejected");
        Reject<InvalidOperationException>(()=>digest.FinalizeHash(new byte[32]), "Double finalization rejected");
    }
    digest.Reset(); Reject<ArgumentException>(()=>digest.FinalizeHash(new byte[31]), "Short destination retains valid state");
    var abc="abc"u8.ToArray(); digest.Append(abc); var final=new byte[32]; digest.FinalizeHash(final);
    Check(Convert.ToHexStringLower(final)=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "Published SHA-256 abc vector");
    for(int i=0;i<100;i++){digest.Reset();digest.Append(abc);digest.FinalizeHash(final);}
    var before=GC.GetAllocatedBytesForCurrentThread(); var timer=Stopwatch.StartNew();
    // Stopwatch is intentionally outside the allocation measurement below.
    before=GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<1000;i++){digest.Reset();digest.Append(abc);digest.FinalizeHash(final);}
    var allocated=GC.GetAllocatedBytesForCurrentThread()-before;timer.Stop();
    Check(allocated==0,"1000 warmed digest cycles allocate 0 B: "+digest.GetType().Name);
    outputs["allocation:"+digest.GetType().Name]=allocated.ToString();
    digest.Dispose();digest.Dispose();Reject<ObjectDisposedException>(()=>digest.Reset(),"Reset after disposal rejected");
}
Check(!WebSha256Service.Shared.CanComputeAsync(10) && !SystemSha256Service.Shared.CanComputeAsync(10),"Non-browser host never invokes browser native API");

// Public fingerprints must remain usable before a host provider is selected,
// and retain byte identity across the assembly ownership change.
for(int i=0;i<8;i++)
{
    var catalog=new AssetBundleCatalog();
    catalog.Bundles.Add(new AssetBundleDescriptor { Name="b",Sha256=Hash(Data(i+1)),Size=i+1 });
    catalog.Assets.Add(new AssetBundleAsset {Address=i==7?new string('界',70000)+"😀":"asset"+i,Guid=new Guid("3e49caaa-733b-4d1f-ab09-030d0e7ee3cf"),Bundle="b",Entry="a",AssetType="Bytes",Sha256=Hash(Data(i+4)),Size=i+4,ImporterSettings=new(){{"z","last"},{"a","first😀"}} });
    var fingerprint=AssetBundlePackageDescriptor.ComputeVersion(catalog,["b","B"]);
    Check(fingerprint.Length==64,"Public package fingerprint " + i); outputs["fingerprint:"+i]=fingerprint;
}

// Exercise the actual HTTP ownership/validation implementation through its
// existing internal entry point, with real Networking response contracts.
var handlerType=assembly.GetType("BEngine.AssetBundles.AssetBundleManager+MemoryBundleDownloadHandler",true)!;
foreach(var length in new[]{0,1,65537,1048577})
{
    var input=Data(length);var descriptor=new AssetBundleDescriptor{Name="http",Size=length,Sha256=Hash(input)};
    object Make(AssetBundleDescriptor d,long maximum) => Activator.CreateInstance(handlerType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[d,maximum],null)!;
    using(var handler=(IDisposable)Make(descriptor,2*1024*1024))
    {
        var download=(IHttpDownloadHandler)handler;
        download.ReceiveResponse(new(HttpStatusCode.OK,"OK",new Dictionary<string,string[]>{{"Content-Length",[length.ToString()]}}));
        for(int at=0;at<input.Length;at+=Math.Min(8191,input.Length-at))download.ReceiveData(input.AsMemory(at,Math.Min(8191,input.Length-at)));
        using var stream=(Stream)Call(handler,handlerType,"TakeVerifiedStream")!;using var output=new MemoryStream();stream.CopyTo(output);
        Check(output.ToArray().SequenceEqual(input),"HTTP preserves verified bytes " + length);outputs["http:"+length]=Hash(output.ToArray());
        Reject<InvalidDataException>(()=>Call(handler,handlerType,"TakeVerifiedStream"),"Transferred stream cannot be taken twice");
    }
    if(length>0)
    {
        using var truncated=(IDisposable)Make(descriptor,2*1024*1024);
        ((IHttpDownloadHandler)truncated).ReceiveResponse(new(HttpStatusCode.OK,"OK",new Dictionary<string,string[]>()));
        Reject<InvalidDataException>(()=>Call(truncated,handlerType,"TakeVerifiedStream"),"Truncated HTTP rejects " + length);
    }
    using var wrongLength=(IDisposable)Make(descriptor,2*1024*1024);
    Reject<InvalidDataException>(()=>((IHttpDownloadHandler)wrongLength).ReceiveResponse(new(HttpStatusCode.OK,"OK",new Dictionary<string,string[]>{{"Content-Length",[(length+1).ToString()]}})),"HTTP declared length mismatch rejects");
}

var loadedType=assembly.GetType("BEngine.AssetBundles.LoadedAssetBundle",true)!;
foreach(var length in new[]{0,63,64,65,65537,1048577})
foreach(var compression in new[]{CompressionLevel.NoCompression,CompressionLevel.Fastest})
{
    var input=Data(length);using var zip=new MemoryStream();
    using(var archive=new ZipArchive(zip,ZipArchiveMode.Create,true)){using var entry=archive.CreateEntry("data",compression).Open();entry.Write(input);}
    var bytes=zip.ToArray();
    var descriptor=new AssetBundleDescriptor{Name="bundle",Sha256=Hash(bytes),Size=bytes.Length};
    var asset=new AssetBundleAsset{Address="data",Entry="data",Bundle="bundle",Sha256=Hash(input),Size=input.Length};
    using var source=new MemoryStream(bytes);
    using(var loaded=(IDisposable)Call(null,loadedType,"OpenVerified",source,descriptor,new List<AssetBundleAsset>{asset},new AssetBundleRuntimeOptions())!)
    {
        var result=DrainValue((BTask<byte[]>)Call(loaded,loadedType,"ReadCooperatively",asset,2L*1024*1024,BCancellationToken.None,null)!);
        Check(result.SequenceEqual(input),"Actual cooperative ZIP SHA " + length+":"+compression);outputs["zip:"+length+":"+compression]=Hash(result);
        asset.Sha256=new string('0',64);
        Reject<InvalidDataException>(()=>Drain((BTask<byte[]>)Call(loaded,loadedType,"ReadCooperatively",asset,2L*1024*1024,BCancellationToken.None,null)!),"Corrupt expected asset digest rejects");
    }
    Check(!source.CanRead,"Bundle releases owned ZIP stream");
}

// Fault injection at the neutral service boundary proves that accepted native
// failures cannot turn into managed success and admission pressure stays async.
var verifier=assembly.GetType("BEngine.AssetBundles.AssetBundleHashVerifier");
if(verifier is not null)
{
    var input=Data(257);var expected=Hash(input);
    foreach(var mode in new[]{"success","uppercase","wrong","malformed","failure","status","throw"})
    {
        var service=new FakeHash(mode,expected){Pressure=2};
        var task=((BValueTask)Call(null,verifier,"VerifyAsync",input,expected,BCancellationToken.None,service)!).AsBTask();
        if(mode is "success" or "uppercase")Drain(task);else Reject<Exception>(()=>Drain(task),"Accepted native failure propagates " + mode);
        Check(service.Attempts==3 && service.Lease.Disposals==1 && service.IncrementalCalls==0,"Admission and one lease cleanup " + mode);
    }
    using(var cancel=new BCancellationSource())
    {
        var service=new FakeHash("running",expected);
        var task=((BValueTask)Call(null,verifier,"VerifyAsync",input,expected,cancel.Token,service)!).AsBTask();
        cancel.Cancel();Reject<BOperationCanceledException>(()=>Drain(task),"Cancellation while native work is accepted");
        Check(service.Lease.Disposals==1,"Canceled native lease released once");
    }
    using(var cancel=new BCancellationSource())
    {
        var service=new FakeHash("success",expected){Pressure=999};
        var task=((BValueTask)Call(null,verifier,"VerifyAsync",input,expected,cancel.Token,service)!).AsBTask();
        cancel.Cancel();Reject<BOperationCanceledException>(()=>Drain(task),"Cancellation while waiting for admission");
        Check(service.Lease.Disposals==0,"Unadmitted input has no native lease");
    }
    var invalid=new FakeHash("success",expected);
    Reject<InvalidDataException>(()=>Drain(((BValueTask)Call(null,verifier,"VerifyAsync",input,"invalid",BCancellationToken.None,invalid)!).AsBTask()),"Invalid expected SHA rejects before admission");
    Check(invalid.Attempts==0,"No native input accepted for an invalid catalog hash");
}
var receipt=new{Passed=true,Checks=checks.Count,Details=checks,Outputs=outputs,DomainAssembly=assembly.Location,NewBoundary=verifier is not null};
File.WriteAllText(args[0],JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("HASH_OWNERSHIP_OK|"+checks.Count);

sealed class FakeHash(string mode,string expected) : IPlatformSha256Service
{
    public int Pressure,Attempts,IncrementalCalls;
    public FakeLease Lease {get;}=new(mode,expected);
    public bool CanComputeAsync(long count)=>count>=0;
    public IPlatformSha256Operation? TryBegin(ReadOnlySpan<byte> input){Attempts++;return Attempts<=Pressure?null:Lease;}
    public IPlatformIncrementalSha256 CreateIncremental(){IncrementalCalls++;throw new Exception("Unexpected fallback");}
}
sealed class FakeLease(string mode,string expected) : IPlatformSha256Operation
{
    public int Disposals;
    public PlatformHashOperationStatus Status {get;private set;}
    public string? Result=>mode=="wrong"?new string('0',64):mode=="malformed"?"bad":mode=="uppercase"?expected.ToUpperInvariant():expected;
    public Exception? Exception=>new IOException("Injected native failure");
    public void Poll(){if(mode=="throw")throw new IOException("Injected poll failure");Status=mode=="running"?PlatformHashOperationStatus.Running:mode=="failure"?PlatformHashOperationStatus.Failed:mode=="status"?(PlatformHashOperationStatus)17:PlatformHashOperationStatus.Succeeded;}
    public void Dispose()=>Disposals++;
}
