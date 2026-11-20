using System.Text.Json;
using System.Runtime.CompilerServices;
using BEngine.Rendering.Rhi;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
void Write(GraphicsMeshUploadStream stream, float value, bool retain = true)
{ using var edit = stream.BeginWrite(retain); edit.GetSpan(0, 4).Fill(value); edit.SetLength(4); }
using var mesh = new ProbeMesh();
using var stream = new GraphicsMeshUploadStream(mesh);
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Rendering2D" or "BEngine.UIElements" or "BEngine.IMGUI"),
    "Independent upload producer loads no scene or UI renderer");
Write(stream, 1); Check(stream.Upload() && mesh.Values.AsSpan().SequenceEqual([1f, 1f, 1f, 1f]), "Initial bytes reach the attached mesh");
var count = mesh.Writes;
Check(!stream.Upload() && mesh.Writes == count, "Unmodified CPU stream skips comparison and upload");
Write(stream, 1); Check(!stream.Upload() && mesh.Writes == count, "Rewritten identical bytes skip GPU work");
Write(stream, 2, false); Check(stream.Upload(false), "Continuous stream upload defers its snapshot");
Write(stream, 2); Check(!stream.Upload(), "Stable edit captures the actual preceding deferred upload before modification");
Write(stream, 3, false); stream.Upload(false);
try { using var edit = stream.BeginWrite(false); edit.GetSpan(0, 1)[0] = 99; throw new ProducerFailure(); }
catch (ProducerFailure) { }
Write(stream, 99);
Check(stream.Upload() && mesh.Values.All(x => x == 99), "Interrupted partial writes cannot claim bytes were uploaded");
Write(stream, 4, false); stream.Upload(false);
try { using var edit = stream.BeginWrite(); edit.GetSpan(0, 2).Fill(8); throw new ProducerFailure(); }
catch (ProducerFailure) { }
Write(stream, 4); Check(!stream.Upload(), "Failed stable rewrite preserves only a verified GPU snapshot");
Write(stream, 7); mesh.FailNextWrite = true;
Throws<ProducerFailure>(() => stream.Upload(), "Upload failure propagates after the mesh changed partially");
Check(stream.Upload() && mesh.Values.All(x => x == 7), "Upload retry restores all bytes after a partial driver failure");
var edit1 = stream.BeginWrite();
Throws<InvalidOperationException>(() => stream.Upload(), "Open writes cannot be uploaded");
Throws<InvalidOperationException>(() => stream.BeginWrite(), "Nested producer writes are rejected");
Throws<InvalidOperationException>(() => stream.maximumRetainedBytes = 0, "Snapshot policy cannot change during a borrowed write");
edit1.Dispose(); var edit2 = stream.BeginWrite(); edit1.Dispose();
try { edit1.GetSpan(0, 1); throw new Exception("Stale write accepted"); }
catch (InvalidOperationException) { Check(true, "Completed write cannot mutate a later write"); }
Throws<InvalidOperationException>(() => stream.Upload(), "Disposing an old scope copy cannot close the current write");
edit2.Dispose(); edit2.Dispose();
try { default(GraphicsMeshUploadStream.WriteScope).GetSpan(0, 1); throw new Exception("Default write accepted"); }
catch (InvalidOperationException) { Check(true, "Default write is rejected"); }
using (var edit = stream.BeginWrite()) { edit.SetLength(2); }
stream.Upload(); Check(mesh.Values.AsSpan().SequenceEqual([7f, 7f]), "Shortening uploads only live vertices");
using (var edit = stream.BeginWrite()) { edit.SetLength(4); }
stream.Upload(); Check(mesh.Values.AsSpan().SequenceEqual([7f, 7f, 0f, 0f]), "Length growth clears previously trimmed data");
using (var edit = stream.BeginWrite()) { edit.GetSpan(6, 2).Fill(9); }
stream.Upload(); Check(mesh.Values.AsSpan().SequenceEqual([7f, 7f, 0f, 0f, 0f, 0f, 9f, 9f]), "Sparse write zero-fills its intervening gap");
using (var edit = stream.BeginWrite()) { edit.SetLength(0); }
Check(stream.Upload() && mesh.VertexCount == 0, "Empty active stream can explicitly clear the mesh");
stream.maximumRetainedBytes = 0; Write(stream, 5); stream.Upload();
Check(stream.retainedSnapshotBytes == 0 && !stream.Upload(), "Zero snapshot budget retains CPU revision reuse");
Write(stream, 5); Check(stream.Upload() && stream.retainedSnapshotBytes == 0, "Rewritten zero-budget stream uploads without retaining bytes");
stream.maximumRetainedBytes = 16; Write(stream, 6); stream.Upload(false);
Write(stream, 6); Check(!stream.Upload() && stream.retainedSnapshotBytes <= 16, "Restored budget captures only exact uploaded bytes");
stream.maximumRetainedBytes = 8; Check(stream.retainedSnapshotBytes <= 8, "Shrinking budget releases an oversized snapshot");
stream.maximumRetainedBytes = 16; Write(stream, 6); Check(!stream.Upload(), "Budget changes preserve a valid CPU-to-GPU revision");
mesh.Update([123, 123, 123, 123]); stream.Invalidate();
Check(stream.Upload() && mesh.Values.All(x => x == 6), "Explicit external update invalidation restores the owner stream");
using var mesh2 = new ProbeMesh(); using var stream2 = new GraphicsMeshUploadStream();
Write(stream2, 21); Throws<InvalidOperationException>(() => stream2.Upload(), "Unattached CPU streams reject uploads");
stream2.AttachMesh(mesh2); stream2.AttachMesh(mesh2);
Check(stream2.Upload() && mesh2.Values.All(x => x == 21) && mesh.Values.All(x => x == 6), "Deferred attachment keeps independent producer data isolated");
Throws<InvalidOperationException>(() => stream2.AttachMesh(mesh), "A stream cannot silently replace its owned mesh binding");
using var immutable = new ProbeMesh { Usage = GraphicsBufferUsage.Static };
Throws<ArgumentException>(() => new GraphicsMeshUploadStream(immutable), "Static meshes reject dynamic upload ownership");
Throws<ArgumentNullException>(() => new GraphicsMeshUploadStream(null!), "Null meshes are rejected");
Throws<ArgumentOutOfRangeException>(() => stream.maximumRetainedBytes = -1, "Negative snapshot budgets are rejected");
using (var edit = stream.BeginWrite())
{
    try { edit.GetSpan(-1, 1); throw new Exception("Negative range accepted"); } catch (ArgumentOutOfRangeException) { Check(true, "Negative writable ranges are rejected"); }
    try { edit.GetSpan(int.MaxValue, 1); throw new Exception("Overflow accepted"); } catch (OverflowException) { Check(true, "Writable ranges cannot overflow"); }
}
// The recording mesh reuses its storage so allocation measurements include the producer path.
_ = Measure(stream2, false, 1000);
var movingBytes = Measure(stream2, false, 10000);
_ = Measure(stream2, true, 1000);
var stableBytes = Measure(stream2, true, 10000);
File.WriteAllText(Path.Combine(output, "Allocations.json"), JsonSerializer.Serialize(new { movingBytes, stableBytes }));
Check(movingBytes == 0 && stableBytes == 0, "Warmed continuous and stable rewrites allocate zero bytes");
var disposable = new GraphicsMeshUploadStream(mesh2); var finalEdit = disposable.BeginWrite(); finalEdit.GetSpan(0, 4).Fill(1);
disposable.Dispose(); finalEdit.Dispose(); disposable.Dispose();
Check(disposable.retainedVertexBytes == 0 && disposable.retainedSnapshotBytes == 0 && !mesh2.Disposed,
    "Disposal invalidates borrows and releases CPU state without disposing the caller mesh");
Throws<ObjectDisposedException>(() => disposable.Upload(), "Disposed producers cannot upload");
Throws<ObjectDisposedException>(() => disposable.BeginWrite(), "Disposed producers cannot be edited");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count,
    WarmMovingWrites = 10000, WarmStableWrites = 10000, MovingAllocatedBytes = movingBytes, StableAllocatedBytes = stableBytes, Items = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("MESH_UPLOAD_OWNERSHIP_OK");

[MethodImpl(MethodImplOptions.NoInlining)]
static long Measure(GraphicsMeshUploadStream stream, bool retain, int count)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < count; i++)
    {
        using (var edit = stream.BeginWrite(retain)) { edit.GetSpan(0, 4).Fill(retain ? 9999 : i); edit.SetLength(4); }
        stream.Upload(retain);
    }
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

sealed class ProducerFailure : Exception { }
sealed class ProbeMesh : IGraphicsMesh
{
    private readonly RecordingDevice _device = new();
    public IGraphicsDevice Device => _device;
    public string Label => "Upload probe";
    public GraphicsVertexLayout Layout { get; } = new(8, [new(0, 2, 0)]);
    public GraphicsPrimitiveTopology Topology => GraphicsPrimitiveTopology.TriangleList;
    public GraphicsBufferUsage Usage { get; init; } = GraphicsBufferUsage.Dynamic;
    public int VertexCount { get; private set; }
    public float[] Values = [];
    public int Writes;
    public bool FailNextWrite, Disposed;
    public void Update(ReadOnlySpan<float> vertices)
    {
        Writes++;
        if (Values.Length != vertices.Length) Values = new float[vertices.Length];
        if (FailNextWrite) { FailNextWrite = false; if (Values.Length > 0) Values[0] = -99; throw new ProducerFailure(); }
        vertices.CopyTo(Values); VertexCount = vertices.Length / 2;
    }
    public void Dispose() => Disposed = true;
}
