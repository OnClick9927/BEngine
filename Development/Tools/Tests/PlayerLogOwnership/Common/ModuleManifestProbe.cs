using System.Reflection;
using BEngine.Player;

public static class ModuleManifestProbe
{
    public static List<string> Run(string root)
    {
        var checks = new List<string>();
        var resolver = typeof(PlayerRuntimeModules).GetMethod("ResolveManifestPath",
            BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException();
        string? Resolve(string basePath, string? processPath) =>
            (string?)resolver.Invoke(null, [basePath, processPath]);
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        var empty = Path.Combine(root, "empty");
        var extracted = Path.Combine(root, "extracted");
        var executable = Path.Combine(root, "executable");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(extracted);
        Directory.CreateDirectory(executable);
        var exe = Path.Combine(executable, "Player.exe");
        var external = Path.Combine(executable, "player.modules.json");
        var primary = Path.Combine(extracted, "player.modules.json");
        Check(Resolve(empty, null) is null, "An absent process capability leaves missing manifests optional");
        Check(Resolve(empty, exe) is null, "Missing manifests do not invent runtime modules");
        File.WriteAllText(external, "[]");
        Check(Resolve(extracted, exe) == external, "Single-file extraction falls back to the executable manifest");
        Check(Resolve(empty, "") is null, "An empty process path is ignored");
        Check(Resolve(empty, "Player.exe") is null, "A relative process identity does not probe the working directory");
        File.WriteAllText(primary, "[]");
        Check(Resolve(extracted, exe) == primary, "An existing base-directory manifest retains precedence");
        Check(Resolve(extracted, null) == primary, "Normal base-directory loading needs no platform process path");
        Check(Resolve(executable, exe) == external, "An unbundled host resolves its original colocated manifest");
        return checks;
    }
}
