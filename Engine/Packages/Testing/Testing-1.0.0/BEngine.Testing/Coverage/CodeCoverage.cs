using System.Text;
using System.Text.Json;

namespace BEngine.Testing;

public sealed record CoverageLine(int Line, long Hits);
public sealed record CoverageBranch(int Line, int Block, int Branch, long Hits);
public sealed record CoverageFile(string Path, IReadOnlyList<CoverageLine> Lines)
{
    public IReadOnlyList<CoverageBranch> Branches { get; init; } = [];
    public int Total => Lines.Count;
    public int Covered => Lines.Count(line => line.Hits > 0);
    public int TotalBranches => Branches.Count;
    public int CoveredBranches => Branches.Count(branch => branch.Hits > 0);
}
public sealed record CoverageSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<CoverageFile> Files)
{
    public int TotalLines => Files.Sum(file => file.Total);
    public int CoveredLines => Files.Sum(file => file.Covered);
    public double LineRate => TotalLines == 0 ? 1 : (double)CoveredLines / TotalLines;
    public int TotalBranches => Files.Sum(file => file.TotalBranches);
    public int CoveredBranches => Files.Sum(file => file.CoveredBranches);
    public double BranchRate => TotalBranches == 0 ? 1 : (double)CoveredBranches / TotalBranches;
}

public static class CodeCoverage
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SortedDictionary<int, long>> Files =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SortedDictionary<(int Line, int Block, int Branch), long>> BranchFiles =
        new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterBranch(string file, int line, int block, int outcomes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        if (line < 1 || block < 0 || outcomes < 1) throw new ArgumentOutOfRangeException(nameof(outcomes));
        file = Normalize(file);
        lock (Gate)
        {
            if (!Files.ContainsKey(file)) Files[file] = [];
            if (!BranchFiles.TryGetValue(file, out var branches)) BranchFiles[file] = branches = [];
            for (var branch = 0; branch < outcomes; branch++) branches.TryAdd((line, block, branch), 0);
        }
    }

    public static void HitBranch(string file, int line, int block, int branch)
    {
        if (string.IsNullOrWhiteSpace(file) || line < 1 || block < 0 || branch < 0) return;
        file = Normalize(file);
        lock (Gate)
        {
            if (!Files.ContainsKey(file)) Files[file] = [];
            if (!BranchFiles.TryGetValue(file, out var branches)) BranchFiles[file] = branches = [];
            var key = (line, block, branch);
            branches[key] = branches.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>Records an evaluated Boolean condition and returns it without re-evaluating the expression.</summary>
    public static bool Branch(bool value, string file, int line, int block)
    {
        HitBranch(file, line, block, value ? 0 : 1);
        return value;
    }
    /// <summary>Used inside an existing Boolean/conditional arm to retain C# pattern scope.</summary>
    public static bool MarkBranch(string file, int line, int block, int branch)
    { HitBranch(file, line, block, branch); return true; }

    public static void Register(string file, params int[] executableLines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(executableLines);
        file = Normalize(file);
        lock (Gate)
        {
            if (!Files.TryGetValue(file, out var lines)) Files[file] = lines = [];
            foreach (var line in executableLines.Where(line => line > 0).Distinct())
                lines.TryAdd(line, 0);
        }
    }

    public static void Hit(string file, int line)
    {
        if (string.IsNullOrWhiteSpace(file) || line < 1) return;
        file = Normalize(file);
        lock (Gate)
        {
            if (!Files.TryGetValue(file, out var lines)) Files[file] = lines = [];
            lines[line] = lines.GetValueOrDefault(line) + 1;
        }
    }

    public static void BeginSession()
    {
        lock (Gate)
        {
            foreach (var lines in Files.Values)
                foreach (var line in lines.Keys.ToArray()) lines[line] = 0;
            foreach (var branches in BranchFiles.Values)
                foreach (var branch in branches.Keys.ToArray()) branches[branch] = 0;
        }
    }

    public static CoverageSnapshot Snapshot()
    {
        lock (Gate)
            return new(DateTimeOffset.UtcNow, Files.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CoverageFile(pair.Key,
                    pair.Value.Select(line => new CoverageLine(line.Key, line.Value)).ToArray())
                {
                    Branches = BranchFiles.TryGetValue(pair.Key, out var branches)
                        ? branches.Select(branch => new CoverageBranch(branch.Key.Line, branch.Key.Block,
                            branch.Key.Branch, branch.Value)).ToArray() : []
                })
                .ToArray());
    }

    public static void Merge(CoverageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (Gate)
            foreach (var file in snapshot.Files)
            {
                var path = Normalize(file.Path);
                if (!Files.TryGetValue(path, out var lines)) Files[path] = lines = [];
                foreach (var line in file.Lines) lines[line.Line] = lines.GetValueOrDefault(line.Line) + line.Hits;
                if (!BranchFiles.TryGetValue(path, out var branches)) BranchFiles[path] = branches = [];
                foreach (var branch in file.Branches)
                {
                    var key = (branch.Line, branch.Block, branch.Branch);
                    branches[key] = branches.GetValueOrDefault(key) + branch.Hits;
                }
            }
    }

    public static void WriteJson(string path, CoverageSnapshot? snapshot = null)
    {
        snapshot ??= Snapshot();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void WriteLcov(string path, CoverageSnapshot? snapshot = null)
    {
        snapshot ??= Snapshot();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder();
        foreach (var file in snapshot.Files)
        {
            builder.Append("SF:").AppendLine(file.Path);
            foreach (var line in file.Lines) builder.Append("DA:").Append(line.Line).Append(',').AppendLine(line.Hits.ToString());
            builder.Append("LF:").AppendLine(file.Total.ToString());
            builder.Append("LH:").AppendLine(file.Covered.ToString());
            foreach (var branch in file.Branches)
                builder.Append("BRDA:").Append(branch.Line).Append(',').Append(branch.Block).Append(',')
                    .Append(branch.Branch).Append(',').AppendLine(branch.Hits > 0 ? branch.Hits.ToString() : "-");
            builder.Append("BRF:").AppendLine(file.TotalBranches.ToString());
            builder.Append("BRH:").AppendLine(file.CoveredBranches.ToString());
            builder.AppendLine("end_of_record");
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
