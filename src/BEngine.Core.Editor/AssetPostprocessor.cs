using System.Reflection;

namespace BEngine.Editor;

public class AssetImporter : BObject
{
    public string assetPath { get; internal set; } = string.Empty;
    public ulong assetTimeStamp => File.Exists(FullPath)
        ? unchecked((ulong)File.GetLastWriteTimeUtc(FullPath).Ticks)
        : 0;
    public string userData { get; set; } = string.Empty;

    public static AssetImporter? GetAtPath(string path)
    {
        var record = EditorBridge.Host?.GetAsset(path);
        if (record is null) return null;
        return new AssetImporter { name = Path.GetFileName(path), assetPath = record.Value.AssetPath };
    }

    public void SaveAndReimport() => AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

    private string FullPath => EditorBridge.Host is { } host
        ? Path.GetFullPath(Path.Combine(host.ProjectRootPath, assetPath.Replace('/', Path.DirectorySeparatorChar)))
        : string.Empty;
}

public abstract class AssetPostprocessor
{
    public string assetPath { get; internal set; } = string.Empty;
    public AssetImporter? assetImporter { get; internal set; }
    public virtual uint GetVersion() => 0;
    public virtual int GetPostprocessOrder() => 0;
}

internal static class AssetPostprocessorDispatcher
{
    internal static void Notify(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        var postprocessorTypes = TypeCache.GetTypesDerivedFrom<AssetPostprocessor>()
            .Where(type => !type.IsAbstract)
            .OrderBy(type => GetOrder(type))
            .ToArray();

        foreach (var assetPath in importedAssets)
        {
            foreach (var type in postprocessorTypes)
            {
                if (Activator.CreateInstance(type, nonPublic: true) is not AssetPostprocessor processor) continue;
                processor.assetPath = assetPath;
                processor.assetImporter = AssetImporter.GetAtPath(assetPath);
                InvokeInstanceCallback(processor, "OnPostprocessAsset");
            }
        }

        foreach (var type in postprocessorTypes)
        {
            var method = type.GetMethod("OnPostprocessAllAssets", BindingFlags.Static | BindingFlags.Public |
                                                                  BindingFlags.NonPublic);
            if (method is null) continue;
            var parameters = method.GetParameters();
            if (method.ReturnType != typeof(void) || parameters.Length != 4 ||
                parameters.Any(parameter => parameter.ParameterType != typeof(string[])))
            {
                Debug.LogWarning($"{type.FullName}.OnPostprocessAllAssets must be static void with four string arrays.");
                continue;
            }
            Invoke(method, null, [importedAssets, deletedAssets, movedAssets, movedFromAssetPaths]);
        }
    }

    private static int GetOrder(Type type)
    {
        try
        {
            return Activator.CreateInstance(type, nonPublic: true) is AssetPostprocessor processor
                ? processor.GetPostprocessOrder()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void InvokeInstanceCallback(AssetPostprocessor processor, string methodName)
    {
        var method = processor.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public |
                                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (method is null) return;
        if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        {
            Debug.LogWarning($"{processor.GetType().FullName}.{methodName} must be void with no parameters.");
            return;
        }
        Invoke(method, processor, null);
    }

    private static void Invoke(MethodInfo method, object? target, object?[]? arguments)
    {
        try { method.Invoke(target, arguments); }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException : exception;
            Debug.LogError($"Asset postprocessor {method.DeclaringType?.FullName}.{method.Name} failed: " +
                           cause.Message);
        }
    }
}
