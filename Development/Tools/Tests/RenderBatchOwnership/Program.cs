using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
var assembly = typeof(RenderBatchBuffer).Assembly;
Check(assembly == typeof(RenderBatch).Assembly, "Graphics owns command ranges and their reusable lifetime");
Check(!assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName == Assembly.GetExecutingAssembly().GetName().Name),
    "The independent producer uses no friend access");
var key = new RenderBatchKey(Guid.NewGuid(), "custom", "atlas");
RenderSubmission Command(RenderBatchKey batchKey, object payload, int order = 0) =>
    new(new(SortingLayer.Ui, order, 0, RenderTransparency.Transparent, order), batchKey, payload);
var first = new object(); var second = new object();
var source = new List<RenderSubmission> { Command(key, first, 5), Command(key, second, 1) };
using var batches = new RenderBatchBuffer();
Check(batches.BuildOrdered(source) == 1 && batches.Count == 1, "Adjacent equal keys share a batch");
var retained = batches[0];
Check(ReferenceEquals(retained.Submissions[0].Payload, first) && ReferenceEquals(retained.Submissions[1].Payload, second),
    "Batching preserves producer order and payload identity without sorting");
Check(retained.SubmissionSpan.SequenceEqual(source.ToArray()), "The span and list views expose the same commands");
Check(batches.ToArray().Single() == retained, "Enumerable and indexed batch views agree");
Throws<ArgumentOutOfRangeException>(() => _ = batches[-1], "Negative batch indices fail");
Throws<ArgumentOutOfRangeException>(() => _ = batches[1], "Past-end batch indices fail");
Throws<ArgumentOutOfRangeException>(() => _ = retained.Submissions[-1], "Negative submission indices fail");
Throws<ArgumentOutOfRangeException>(() => _ = retained.Submissions[2], "Past-end submission indices fail");
Throws<ArgumentNullException>(() => batches.BuildOrdered(null!), "Null sources are rejected");
Check(batches.Count == 1 && retained.Submissions.Count == 2, "Invalid input leaves the previous build intact");
var alternatives = new[] {
    key with { Material = Guid.NewGuid() }, key with { Shader = "Custom" }, key with { Atlas = "other" },
    key with { MaskGroup = 1 }, key with { PayloadType = typeof(string) }
};
foreach (var alternative in alternatives)
{
    source.Clear(); source.Add(Command(key, first)); source.Add(Command(alternative, second)); source.Add(Command(key, first));
    Check(batches.BuildOrdered(source) == 3 && batches.Select(b => b.Submissions.Count).SequenceEqual(new[] { 1, 1, 1 }),
        "Complete keys separate adjacent ranges and nonadjacent matches: " + alternative);
}
source.Clear(); for (var i = 0; i < 256; i++) source.Add(Command(i % 2 == 0 ? key : alternatives[0], first));
Check(batches.BuildOrdered(source) == 256, "The buffer grows for fragmented command streams");
var oldTail = batches[255]; var oldHead = batches[0];
source = [Command(key, second)];
Check(batches.BuildOrdered(source) == 1 && ReferenceEquals(batches[0], oldHead), "Smaller builds reuse existing batch objects");
Check(oldTail.Submissions.Count == 0 && oldTail.SubmissionSpan.IsEmpty && oldTail.BatchKey == default,
    "Unused pooled tails release old commands and complete batch keys");
batches.Clear(); batches.Clear();
Check(batches.Count == 0 && oldHead.Submissions.Count == 0 && oldHead.SubmissionSpan.IsEmpty && oldHead.BatchKey == default,
    "Clear is repeatable and invalidates borrowed batch views and keys");
Check(batches.BuildOrdered([]) == 0, "An empty stream creates no batches");
Check(batches.BuildOrdered(source) == 1 && ReferenceEquals(batches[0], oldHead), "Clear retains reusable empty batch objects");
var random = new System.Random(1371); var randomValid = true;
for (var iteration = 0; iteration < 250; iteration++)
{
    source.Clear(); var expected = new List<(RenderBatchKey Key, int Start, int Count)>();
    var length = random.Next(1, 512);
    for (var i = 0; i < length; i++)
    {
        var current = random.Next(4) == 0 ? alternatives[random.Next(alternatives.Length)] : key;
        source.Add(Command(current, i, random.Next(-20, 20)));
        if (expected.Count != 0 && expected[^1].Key == current)
        { var last = expected[^1]; expected[^1] = (last.Key, last.Start, last.Count + 1); }
        else expected.Add((current, i, 1));
    }
    batches.BuildOrdered(source);
    randomValid &= batches.Count == expected.Count;
    for (var i = 0; i < batches.Count; i++)
    {
        var actual = batches[i]; var range = expected[i];
        randomValid &= actual.BatchKey == range.Key && actual.Submissions.Count == range.Count &&
            actual.SubmissionSpan.SequenceEqual(source.ToArray().AsSpan(range.Start, range.Count));
    }
}
Check(randomValid, "250 reproducible mixed-key streams match independent contiguous range expectations");
foreach (var release in new[] { "clear", "rebuild", "dispose" })
{
    var owner = new RenderBatchBuffer(); var references = BorrowCollectible(owner);
    for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    Check(references.All(r => r.IsAlive), "An active borrow retains source, payload, key strings, and type: " + release);
    if (release == "clear") owner.Clear(); else if (release == "rebuild") owner.BuildOrdered([]); else owner.Dispose();
    for (var i = 0; i < 12 && references.Any(r => r.IsAlive); i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    Check(references.All(r => !r.IsAlive), "Borrow release allows source, payload, strings, and collectible package type to unload: " + release);
    GC.KeepAlive(owner); owner.Dispose();
}
source.Clear(); for (var i = 0; i < 1024; i++) source.Add(Command(i % 4 == 0 ? key : alternatives[0], first));
for (var i = 0; i < 100; i++) { batches.BuildOrdered(source); batches.Clear(); }
var warmBytes = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
warmBytes = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < 10000; i++) { batches.BuildOrdered(source); batches.Clear(); }
warmBytes = GC.GetAllocatedBytesForCurrentThread() - warmBytes; clock.Stop();
Check(warmBytes == 0, "10000 warmed fragmented builds and clears allocate zero bytes");
var disposedView = oldHead; batches.BuildOrdered(source); batches.Dispose(); batches.Dispose();
Check(batches.Count == 0 && disposedView.BatchKey == default && disposedView.SubmissionSpan.IsEmpty,
    "Dispose is repeatable and clears pooled borrows");
Throws<ObjectDisposedException>(() => batches.BuildOrdered(source), "Disposed buffers reject new builds");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Rendering2D" or "BEngine.UIElements" or "BEngine.IMGUI.Editor"),
    "Arbitrary command producers work without scene or UI renderers");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks,
    WarmBuilds = 10000, CommandsPerBuild = 1024, WarmAllocatedBytes = warmBytes, WarmMilliseconds = clock.Elapsed.TotalMilliseconds }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("RENDER_BATCH_OWNERSHIP_OK|" + checks.Count);

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference[] BorrowCollectible(RenderBatchBuffer owner)
{
    var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("TransientBatchPayload"), AssemblyBuilderAccess.RunAndCollect);
    var type = assembly.DefineDynamicModule("Payload").DefineType("Payload", TypeAttributes.Public).CreateType()!;
    var payload = Activator.CreateInstance(type)!;
    var shader = new string('s', 31); var atlas = new string('a', 37);
    var source = new List<RenderSubmission> { new(default, new(Guid.Empty, shader, atlas) { PayloadType = type }, payload) };
    owner.BuildOrdered(source);
    return [new(source), new(payload), new(shader), new(atlas), new(type)];
}
