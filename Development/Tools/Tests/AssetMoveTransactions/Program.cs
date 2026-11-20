using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().BuildServiceProvider();
using var scope = EditorServiceScope.Attach(services);
var bridge = typeof(AssetDatabase).Assembly.GetType("BEngine.Editor.EditorBridge", true)!;
var hostType = bridge.Assembly.GetType("BEngine.Editor.IEditorHost", true)!;
var host = DispatchProxy.Create(hostType, typeof(MoveHost)); var state = (MoveHost)host;
state.Root = Path.Combine(output, "Project"); Directory.CreateDirectory(state.Full("Assets"));
bridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
string Add(string name, string contents = "original") => state.Add("Assets/" + name, contents);
void Original(string path, Guid id, string contents = "original")
{
    Check(File.ReadAllText(state.Full(path)) == contents, "Original source bytes restored: " + path);
    Check(state.Records[path].Guid == id && File.ReadAllText(state.Full(path) + ".meta") == id.ToString("N"), "GUID and metadata restored: " + path);
}
try
{
    Check(typeof(AssetMoveTransaction).Assembly == typeof(AssetDatabase).Assembly, "Transactions belong to shared editor services");
    var ownerRefs = typeof(AssetMoveTransaction).Assembly.GetReferencedAssemblies();
    Check(!ownerRefs.Any(a => a.Name is "BEngine.TextureAtlas" or "BEngine.Graphics" or "BEngine.IMGUI.Editor"), "The transaction owner has no domain or GUI dependency");
    var a = Add("commit.txt"); var id = state.Records[a].Guid;
    AssetMoveTransaction? retained = null;
    var error = AssetMoveTransaction.Execute(tx => { retained = tx; tx.Move(a, "Assets/committed.txt"); tx.EditSource("Assets/committed.txt", p => File.WriteAllText(p, "new")); });
    Check(error == "" && !File.Exists(state.Full(a)) && File.ReadAllText(state.Full("Assets/committed.txt")) == "new", "Commit moves and edits source");
    Check(state.Records["Assets/committed.txt"].Guid == id && state.Imports.Contains("Assets/committed.txt"), "Commit preserves GUID and imports");
    try { retained!.Exists(a); throw new Exception("Expired scope accepted"); } catch (InvalidOperationException) { Check(true, "A retained scope cannot mutate after commit"); }

    a = Add("child-fail.txt"); var b = Add("companion.txt"); id = state.Records[a].Guid;
    state.FailBefore = b;
    error = AssetMoveTransaction.Execute(tx => { tx.Move(a, "Assets/moved.txt"); tx.Move(b, "Assets/other.txt"); });
    Check(error.Contains("before-move") && !File.Exists(state.Full("Assets/moved.txt")), "Companion failure rolls back its owner"); Original(a, id);
    state.ResetFaults();

    a = Add("refresh-fail.txt"); id = state.Records[a].Guid; state.FailAfter = a;
    error = AssetMoveTransaction.Execute(tx => tx.Move(a, "Assets/refresh-moved.txt"));
    Check(error.Contains("after-move") && !File.Exists(state.Full("Assets/refresh-moved.txt")), "Provider failure after its disk move is journaled"); Original(a, id);
    state.ResetFaults();

    a = Add("import-fail.txt", "\uFEFForiginal\r\n"); id = state.Records[a].Guid; var bytes = File.ReadAllBytes(state.Full(a)); state.FailImport = "Assets/import-moved.txt";
    error = AssetMoveTransaction.Execute(tx => { tx.Move(a, "Assets/import-moved.txt"); tx.EditSource("Assets/import-moved.txt", p => File.WriteAllText(p, "corrupt")); });
    Check(error.Contains("import-failure") && File.ReadAllBytes(state.Full(a)).SequenceEqual(bytes), "Import failure restores exact BOM and newline bytes");
    Check(state.Records[a].Guid == id && state.Imports.Last() == a, "Restored source is imported at its recovered path"); state.ResetFaults();

    a = Add("edit-first.txt"); id = state.Records[a].Guid;
    error = AssetMoveTransaction.Execute(tx => { tx.EditSource(a, p => File.WriteAllText(p, "one")); tx.EditSource(a, p => File.WriteAllText(p, "two")); tx.Move(a, "Assets/edit-final.txt"); throw new IOException("abort"); });
    Check(error == "abort", "Original operation error is retained"); Original(a, id);

    Directory.CreateDirectory(state.Full("Assets/Folder")); a = state.Add("Assets/Folder/item.txt", "original"); id = state.Records[a].Guid;
    error = AssetMoveTransaction.Execute(tx => { tx.EditSource(a, p => File.WriteAllText(p, "changed")); tx.Move("Assets/Folder", "Assets/FolderMoved"); throw new IOException("abort-folder"); });
    Check(error == "abort-folder" && !Directory.Exists(state.Full("Assets/FolderMoved")), "Folder rollback relocates child snapshots"); Original(a, id);

    a = Add("rollback-error.txt"); state.FailBefore = "Assets/stuck.txt"; state.FailImport = "Assets/stuck.txt";
    error = AssetMoveTransaction.Execute(tx => { tx.Move(a, "Assets/stuck.txt"); tx.EditSource("Assets/stuck.txt", p => File.WriteAllText(p, "bad")); });
    Check(error.Contains("Rollback errors:") && error.Contains("before-move"), "Rollback failures are reported with the operation error");
    Check(File.ReadAllText(state.Full("Assets/stuck.txt")) == "original", "Failed move rollback restores bytes at the remaining destination"); state.ResetFaults();

    a = Add("collision.txt"); b = Add("occupied.txt", "keep"); var calls = state.Moves;
    error = AssetMoveTransaction.Execute(tx => tx.Move(a, b));
    Check(error.Contains("Destination already exists") && state.Moves == calls && File.ReadAllText(state.Full(b)) == "keep", "Collision is rejected before provider mutation");
    File.WriteAllText(state.Full("Assets/orphan.txt.meta"), "keep-meta");
    Check(AssetMoveTransaction.Execute(tx => tx.Move(a, "Assets/orphan.txt")).Contains("Destination already exists") && state.Moves == calls, "Orphan destination metadata is protected");
    Check(AssetMoveTransaction.Execute(tx => tx.Move(a, "../outside.txt")).Length > 0 && state.Moves == calls, "Paths escaping Assets are rejected");
    bool Protected(string? p) => p == a; AssetProtection.ProtectedPath += Protected;
    try { Check(AssetMoveTransaction.Execute(tx => tx.Move(a, "Assets/protected.txt")).Contains("protected") && state.Moves == calls, "Package-protected sources cannot be moved"); }
    finally { AssetProtection.ProtectedPath -= Protected; }
    state.Playing = true;
    Check(AssetMoveTransaction.Execute(tx => tx.Move(a, "Assets/play.txt")).Contains("Play Mode") && state.Moves == calls, "Play Mode prevents authoring transactions"); state.Playing = false;
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.Graphics" or "BEngine.IMGUI.Editor" or "BEngine.EditorWorkbench.Editor"), "Shared transactions execute without graphics, GUI or Workbench");
    PipelineChecks.Run(output, Check);
    AtlasChecks.Run(state, Check);
}
finally { bridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]); }
Check(AssetMoveTransaction.Execute(_ => throw new Exception()) == "No editor project is open.", "No-project call does not invoke user operation");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("ASSET_MOVE_TRANSACTIONS_OK|" + checks.Count);

public class MoveHost : DispatchProxy
{
    public string Root = "", FailBefore = "", FailAfter = "", FailImport = "";
    public bool Playing; public int Moves;
    public readonly Dictionary<string, EditorAssetInfo> Records = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Imports = [];
    public string Full(string path) => Path.Combine(Root, path);
    public void ResetFaults() { FailBefore = FailAfter = FailImport = ""; }
    public string Add(string path, string text)
    {
        var id = Guid.NewGuid(); Directory.CreateDirectory(Path.GetDirectoryName(Full(path))!);
        File.WriteAllText(Full(path), text); File.WriteAllText(Full(path) + ".meta", id.ToString("N"));
        Records[path] = new(id, path, Full(path), Full(path), "Text", false, null, 0); return path;
    }
    private string Move(string source, string target)
    {
        Moves++; if (source == FailBefore) return "before-move";
        Directory.CreateDirectory(Path.GetDirectoryName(Full(target))!);
        if (Directory.Exists(Full(source))) Directory.Move(Full(source), Full(target)); else File.Move(Full(source), Full(target));
        if (File.Exists(Full(source) + ".meta")) File.Move(Full(source) + ".meta", Full(target) + ".meta");
        foreach (var key in Records.Keys.Where(k => k == source || k.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var record = Records[key]; Records.Remove(key); var path = target + key[source.Length..];
            Records[path] = record with { AssetPath = path, SourcePath = Full(path), ArtifactPath = Full(path) };
        }
        if (source == FailAfter) { FailAfter = ""; throw new IOException("after-move"); } return "";
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_ProjectRootPath" => Root, "get_AssetsRootPath" => Full("Assets"),
        "get_IsPlayingOrWillChangePlaymode" => Playing, "get_IsPlaying" => Playing,
        "GetAsset" => args![0] is Guid id ? Records.Values.Cast<EditorAssetInfo?>().FirstOrDefault(a => a!.Value.Guid == id) : Records.TryGetValue((string)args[0]!, out var r) ? (EditorAssetInfo?)r : null,
        "FindAssets" => Records.Values.ToArray(), "MoveAsset" => Move((string)args![0]!, (string)args[1]!),
        "ImportAsset" => Import((string)args![0]!),
        _ => method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null
    };
    private object? Import(string path) { Imports.Add(path); if (path == FailImport) { FailImport = ""; throw new IOException("import-failure"); } return null; }
}

static class PipelineChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string output, Action<bool, string> check)
    {
        AssetPipelineRuntime.Initialize();
        RuntimeHelpers.RunModuleConstructor(typeof(BEngine.ProjectSystem.Editor.AssetDatabase).Module.ModuleHandle);
        var root = Path.Combine(output, "RealPipeline"); Directory.CreateDirectory(Path.Combine(root, "Assets"));
        var config = Path.Combine(root, "Project.yaml");
        File.WriteAllText(config, "format: BEngine.Project\nversion: 1\nname: Asset Source Children\nengineVersion: 0.1.0\nassetsDirectory: Assets\n");
        var ownerPath = Path.Combine(root, "Assets/owner.txt"); var childPath = Path.Combine(root, "Assets/child.txt");
        File.WriteAllText(ownerPath, "owner"); File.WriteAllText(childPath, "first");
        var database = new BEngine.ProjectSystem.Editor.AssetDatabase(BEngine.ProjectSystem.ProjectWorkspace.Open(config));
        database.Refresh(); var owner = database.GetRecord("Assets/owner.txt")!; var child = database.GetRecord("Assets/child.txt")!;
        var meta = YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(childPath + ".meta");
        meta.ParentGuid = owner.Guid.ToString("N"); meta.LocalIdentifier = 42; YamlUtility.Save(meta, childPath + ".meta");
        database.ImportAsset(childPath);
        check(database.GetRecord(child.Guid)?.ParentGuid == owner.Guid, "Real incremental child import binds its owner");
        database.ImportAsset(ownerPath);
        check(database.GetRecord(child.Guid) is { } retained && retained.ParentGuid == owner.Guid && retained.LocalIdentifier == 42, "Real incremental owner import retains file-backed child identity");
        check(database.FindAssets("").Count(r => r.Guid == child.Guid) == 1, "Retained source child is indexed once");
        File.WriteAllText(childPath, "updated"); var oldHash = database.GetRecord(child.Guid)!.SourceHash;
        database.ImportAsset(ownerPath);
        check(database.GetRecord(child.Guid)!.SourceHash != oldHash && File.ReadAllText(database.GetRecord(child.Guid)!.ArtifactPath) == "updated", "Owner import observes changed child source and artifact");
        meta.ParentGuid = ""; meta.LocalIdentifier = 0; YamlUtility.Save(meta, childPath + ".meta"); database.ImportAsset(ownerPath);
        check(database.GetRecord("Assets/child.txt") is { ParentGuid: null } restored && restored.Guid == child.Guid, "Owner import observes detached child metadata");
        meta.ParentGuid = owner.Guid.ToString("N"); meta.LocalIdentifier = 42; YamlUtility.Save(meta, childPath + ".meta"); database.ImportAsset(childPath);
        File.Delete(childPath); database.ImportAsset(ownerPath);
        check(database.GetRecord(child.Guid) is null, "Owner import removes vanished source children");
    }
}

static class AtlasChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(MoveHost state, Action<bool, string> check)
    {
        var handler = new TextureAtlasMoveHandler();
        string Atlas(string name, bool child = true, bool generated = false)
        {
            var path = state.Add("Assets/" + name + ".atlas.yaml", "Format: BEngine.TextureAtlas\nTexture: Assets/" + name + ".png\nWidth: 1\nHeight: 1\nSprites: []\n");
            if (child)
            {
                var png = state.Add("Assets/" + name + ".png", "texture"); var entry = state.Records[png];
                state.Records[png] = entry with { ParentGuid = state.Records[path].Guid, LocalIdentifier = 2800000, AssetType = "Texture", SourcePath = generated ? Path.Combine(state.Root, "Library/Artifacts/test.png") : entry.SourcePath };
            }
            return path;
        }
        var old = Atlas("atlas"); var id = state.Records[old].Guid; var childId = state.Records["Assets/atlas.png"].Guid;
        check(handler.Move(old, "Assets/renamed.atlas.yaml") == "", "Legacy atlas and companion move successfully");
        check(TextureAtlas.Load(state.Full("Assets/renamed.atlas.yaml")).Texture == "Assets/renamed.png", "Legacy manifest references the renamed companion");
        check(state.Records["Assets/renamed.atlas.yaml"].Guid == id && state.Records["Assets/renamed.png"].Guid == childId, "Both atlas and companion GUIDs are preserved");
        old = Atlas("generated", generated: true); var pngMoves = state.Moves;
        check(handler.Move(old, "Assets/generated-renamed.atlas.yaml") == "" && state.Moves == pngMoves + 1 && File.Exists(state.Full("Assets/generated.png")), "Library-generated subassets are not moved as source PNG files");
        old = Atlas("empty", child: false);
        check(handler.Move(old, "Assets/empty-renamed.atlas.yaml") == "", "Atlas without a file child still moves");
        old = Atlas("failure"); var original = File.ReadAllBytes(state.Full(old)); state.FailImport = "Assets/failure-renamed.atlas.yaml";
        check(handler.Move(old, "Assets/failure-renamed.atlas.yaml").Contains("import-failure"), "Atlas import failure is reported");
        check(File.ReadAllBytes(state.Full(old)).SequenceEqual(original) && File.Exists(state.Full("Assets/failure.png")) && !File.Exists(state.Full("Assets/failure-renamed.png")), "Failed atlas edit restores original manifest bytes and companion");
        old = Atlas("occupied"); state.Add("Assets/taken.png", "unrelated"); var before = state.Moves;
        check(handler.Move(old, "Assets/taken.atlas.yaml").Contains("already exists") && state.Moves == before, "Atlas companion collision is preflighted before root move");
        old = Atlas("bad"); File.WriteAllText(state.Full(old), "[bad: yaml"); before = state.Moves;
        check(handler.Move(old, "Assets/bad-renamed.atlas.yaml").Length > 0 && state.Moves == before, "Malformed atlas is rejected without mutation");
        old = Atlas("child-error"); state.FailBefore = "Assets/child-error.png";
        check(handler.Move(old, "Assets/child-error-renamed.atlas.yaml").Length > 0 && File.Exists(state.Full(old)) && !File.Exists(state.Full("Assets/child-error-renamed.atlas.yaml")), "Companion move failure restores the atlas source"); state.ResetFaults();
    }
}
