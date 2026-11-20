using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
var random = new System.Random(2047); var snapshots = new List<object>();
var keys = Enumerable.Range(0, 16).Select(i => new RenderBatchKey(new Guid(i, 0, 0, new byte[8]), "shader" + i % 3, "atlas" + i % 4)
    { MaskGroup = i % 2, PayloadType = i % 2 == 0 ? typeof(int) : typeof(string) }).ToArray();
for (var iteration = 0; iteration < 512; iteration++)
{
    var length = random.Next(0, 512); var source = new List<RenderSubmission>();
    for (var i = 0; i < length; i++) source.Add(new(new(SortingLayer.Ui, random.Next(-4, 4), 0,
        RenderTransparency.Transparent, random.Next(0, 4)), keys[random.Next(keys.Length)], i));
    var expected = source.OrderBy(s => s.SortKey).ToArray();
    var actual = RenderBatchBuilder2D.Build(source);
    Check(actual.SelectMany(b => b.Submissions).SequenceEqual(expected), "Stable order changed");
    Check(!actual.Any(b => b.Submissions.Any(s => s.BatchKey != b.BatchKey)), "Mixed keys in a batch");
    snapshots.Add(actual.Select(b => new { b.BatchKey.Material, b.BatchKey.Shader, b.BatchKey.Atlas, b.BatchKey.MaskGroup,
        PayloadType = b.BatchKey.PayloadType?.FullName, Payloads = b.Submissions.Select(s => (int)s.Payload).ToArray() }).ToArray());
    (actual as IDisposable)?.Dispose();
}
var snapshot = JsonSerializer.SerializeToUtf8Bytes(snapshots); File.WriteAllBytes(Path.Combine(output, "BatchSnapshots.json"), snapshot);
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton(new GraphicsUtility(new DrawingPlatform())).BuildServiceProvider();
PlatformRuntime.Initialize(services);
using var scene = new Scene("Batch lifetime", services);
var contributor = new FailingDraw();
RenderContributorRegistry.Register("!batch-lifetime", _ => contributor);
using var device = new RecordingDevice();
bool clearedKeysAfterFailure, clearedKeysAfterSuccess;
using (var renderer = new PortableSceneRenderer(device))
{
    var failed = false;
    try { renderer.Render(scene, RenderCamera.Default, 640, 360); }
    catch (ExpectedFailure) { failed = true; }
    Check(failed, "The failing contribution ran");
    var held = contributor.LastBatch!;
    Check(held is not null && held.Submissions.Count == 0 && held.SubmissionSpan.IsEmpty, "Failed rendering retained borrowed commands");
    clearedKeysAfterFailure = held!.BatchKey == default;
    contributor.Fail = false;
    renderer.Render(scene, RenderCamera.Default, 640, 360);
    Check(renderer.LastRenderStatistics.VisibleSubmissionCount == 1 && renderer.LastRenderStatistics.BatchCount == 1 &&
        contributor.Begins == 2 && contributor.Ends == 2 && contributor.Rendered == 2,
        "Failed rendering did not recover with balanced callbacks");
    clearedKeysAfterSuccess = held.BatchKey == default;
}
RenderContributorRegistry.Unregister("!batch-lifetime"); RenderContributorRegistry.Release(device);
Check(device.ResourcesCreated == device.ResourcesDisposed, "GPU resources leaked");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks,
    Cases = snapshots.Count, StableOrderAndBatchSha256 = Convert.ToHexString(SHA256.HashData(snapshot)),
    ClearedKeysAfterFailure = clearedKeysAfterFailure, ClearedKeysAfterSuccess = clearedKeysAfterSuccess,
    contributor.Begins, contributor.Ends, contributor.Rendered }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("BATCH_PARITY_OK");

sealed class ExpectedFailure : Exception { }
sealed class FailingDraw : IDrawContributor
{
    private static readonly RenderBatchKey Key = new(Guid.Empty, "foreign", "") { PayloadType = typeof(FailingDraw) };
    public string packageId => "batch-lifetime";
    public bool Fail = true;
    public int Begins, Ends, Rendered;
    public RenderBatch? LastBatch;
    public void BeginFrame() => Begins++;
    public void EndFrame() => Ends++;
    public bool CanRender(RenderBatch batch) => batch.BatchKey == Key;
    public long Collect(in RenderCollectionContext context, ICollection<RenderSubmission> submissions, long order)
    { submissions.Add(new(new(SortingLayer.Ui, 0, 0, RenderTransparency.Transparent, order), Key, this)); return order + 1; }
    public void Render(RenderBatch batch, int width, int height, GraphicsRect viewport)
    { LastBatch = batch; Rendered++; if (Fail) throw new ExpectedFailure(); }
    public void Dispose() { }
}
