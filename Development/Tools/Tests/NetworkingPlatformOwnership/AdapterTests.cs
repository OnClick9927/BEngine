using System.Net;
using System.Runtime.InteropServices;
using BEngine.Networking;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

internal static class AdapterTests
{
    internal static void Run(Action<bool,string> check)
    {
        var native = new NativeTransport();
        using var provider = new ServiceCollection().AddBEngineDotNetPlatform()
            .AddSingleton<IPlatformHttpTransport>(native).BuildServiceProvider();
        PlatformRuntime.Initialize(provider);
        var upload = new byte[] { 1, 2, 3 };
        var headers = new Dictionary<string,string> { ["X-Test"] = "same" };
        var sink = new Sink();
        using (var operation = PlatformHttpTransport.Shared.Send(new(new Uri("https://unused.invalid"),
            HttpRequestMethod.Patch, headers, upload, "test/type") { BypassCache = true }, sink))
        {
            check(ReferenceEquals(native.Request!.UploadData,upload) && ReferenceEquals(native.Request.Headers,headers), "HTTP adapter borrows upload and request headers without cloning");
            check(native.Request.Method=="PATCH" && native.Request.BypassCache && native.Request.UploadContentType=="test/type", "HTTP request policy survives neutral adaptation");
            check(operation.Status==NetworkOperationStatus.Running && sink.Response is null, "Injected host callbacks wait for Poll");
            operation.Poll();
            check(ReferenceEquals(sink.Response!.Headers,native.Operation.Headers), "Response headers are not duplicated by the domain adapter");
            check(ReferenceEquals(sink.Array,native.Operation.Data), "Borrowed response block reaches the domain handler without copying");
            check(operation.Status==NetworkOperationStatus.Succeeded && operation.DownloadedBytes==3 && operation.ContentLength==3, "Host completion and progress are preserved");
            operation.Dispose();
            check(native.Operation.Disposed, "Domain disposal releases the host operation");
        }
        using (var operation = PlatformHttpTransport.Shared.Send(new(new Uri("https://unused.invalid"),HttpRequestMethod.Get,headers,null,null),new Sink()))
        {
            operation.Abort();
            check(operation.Status==NetworkOperationStatus.Failed && ReferenceEquals(operation.Exception,native.Operation.Exception), "Abort failure retains the host exception identity");
        }
        // The existing domain injection point must still bypass every host default.
        var domain = new DomainTransport();
        using (var request = new HttpNetworkRequest(new Uri("https://unused.invalid"),HttpRequestMethod.Get,domain))
        {
            var task = request.SendAsync();
            for (var i=0; i<10 && !task.IsCompleted; i++) BEngine.BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(2));
            task.GetAwaiter().GetResult();
            check(domain.Sends==1 && request.IsSuccess, "Custom domain transport injection still overrides host defaults");
        }
        DotNetPlatformServices.Initialize();
    }
    private sealed class Sink : IHttpDownloadHandler
    {
        public HttpTransportResponse? Response; public byte[]? Array;
        public void ReceiveResponse(HttpTransportResponse response)=>Response=response;
        public void ReceiveData(ReadOnlyMemory<byte> data)
        { if(!MemoryMarshal.TryGetArray(data,out var segment)) throw new Exception("Borrowed memory lost its backing array"); Array=segment.Array; }
    }
    private sealed class NativeTransport : IPlatformHttpTransport
    {
        public PlatformHttpRequest? Request; public NativeOperation Operation=null!;
        public IPlatformHttpOperation Send(PlatformHttpRequest request,IPlatformHttpDownloadHandler handler)
        { Request=request; return Operation=new(handler); }
    }
    private sealed class NativeOperation(IPlatformHttpDownloadHandler handler) : IPlatformHttpOperation
    {
        public readonly byte[] Data=[1,2,3];
        public readonly Dictionary<string,string[]> Headers=[];
        public bool Disposed;
        public PlatformNetworkOperationStatus Status {get;private set;}
        public Exception? Exception {get;private set;}
        public PlatformHttpResponse? Response {get;private set;}
        public long DownloadedBytes => Status==PlatformNetworkOperationStatus.Succeeded?3:0;
        public long? ContentLength=>3;
        public void Poll()
        { if(Disposed || Status!=PlatformNetworkOperationStatus.Running)return; Response=new(HttpStatusCode.OK,"OK",Headers);handler.ReceiveResponse(Response);handler.ReceiveData(Data);Status=PlatformNetworkOperationStatus.Succeeded; }
        public void Abort(){Exception=new OperationCanceledException();Status=PlatformNetworkOperationStatus.Failed;}
        public void Dispose()=>Disposed=true;
    }
    private sealed class DomainTransport : IHttpTransport
    {
        public int Sends;
        public IHttpTransportOperation Send(HttpTransportRequest request,IHttpDownloadHandler handler){Sends++;return new DomainOperation(handler);}
    }
    private sealed class DomainOperation(IHttpDownloadHandler handler):IHttpTransportOperation
    {
        public NetworkOperationStatus Status{get;private set;}
        public Exception? Exception=>null;
        public HttpTransportResponse? Response{get;private set;}
        public long DownloadedBytes=>0;public long? ContentLength=>0;
        public void Poll(){if(Status!=NetworkOperationStatus.Running)return;Response=new(HttpStatusCode.NoContent,"No Content",new Dictionary<string,string[]>());handler.ReceiveResponse(Response);Status=NetworkOperationStatus.Succeeded;}
        public void Abort()=>Status=NetworkOperationStatus.Failed;
        public void Dispose(){}
    }
}
