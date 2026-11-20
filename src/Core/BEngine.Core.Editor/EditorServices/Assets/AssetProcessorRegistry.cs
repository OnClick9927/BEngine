using System.Reflection;

namespace BEngine.Editor;

internal static class AssetProcessorRegistry
{
    private static readonly Lock Gate = new();
    private static AssetPostprocessorDescriptor[] _postprocessors = [];
    private static Action<string>[] _create = [];
    private static Func<string[], string[]>[] _save = [];
    private static Func<string, RemoveAssetOptions, AssetDeleteResult>[] _delete = [];
    private static Func<string, string, AssetMoveResult>[] _move = [];
    private static int _generation = -1;

    internal static AssetPostprocessorDescriptor[] Postprocessors
    { get { lock (Gate) { EnsureFresh(); return _postprocessors; } } }

    internal static void Warmup() { lock (Gate) EnsureFresh(); }

    internal static void InvokeCreate(string path)
    { foreach (var callback in GetCallbacks(ref _create)) Invoke(callback, path, "OnWillCreateAsset"); }

    internal static string[] InvokeSave(string[] paths)
    {
        var result = paths;
        foreach (var callback in GetCallbacks(ref _save))
            try { result = callback(result) ?? result; }
            catch (Exception exception) { Log("OnWillSaveAssets", exception); }
        return result;
    }

    internal static AssetDeleteResult InvokeDelete(string path, RemoveAssetOptions options)
    {
        var result = AssetDeleteResult.DidNotDelete;
        foreach (var callback in GetCallbacks(ref _delete))
        {
            try
            {
                var current = callback(path, options);
                if (current == AssetDeleteResult.FailedDelete) return current;
                if (current == AssetDeleteResult.DidDelete) result = current;
            }
            catch (Exception exception) { Log("OnWillDeleteAsset", exception); }
        }
        return result;
    }

    internal static AssetMoveResult InvokeMove(string oldPath, string newPath)
    {
        var result = AssetMoveResult.DidNotMove;
        foreach (var callback in GetCallbacks(ref _move))
        {
            try
            {
                var current = callback(oldPath, newPath);
                if (current == AssetMoveResult.FailedMove) return current;
                if (current == AssetMoveResult.DidMove) result = current;
            }
            catch (Exception exception) { Log("OnWillMoveAsset", exception); }
        }
        return result;
    }

    private static T[] GetCallbacks<T>(ref T[] callbacks)
    { lock (Gate) { EnsureFresh(); return callbacks; } }

    private static void EnsureFresh()
    {
        var generation = RuntimeTypeCache.stats.Generation;
        if (_generation == generation) return;
        _postprocessors = TypeCache.GetTypesDerivedFrom<AssetPostprocessor>()
            .Where(type => !type.IsAbstract)
            .Select(TryCreatePostprocessor)
            .Where(item => item.Factory is not null)
            .OrderBy(item => item.Order).ThenBy(item => item.Type.FullName, StringComparer.Ordinal).ToArray();
        var modificationTypes = TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>()
            .Where(type => !type.IsAbstract).OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (Type: type, Methods: GetDeclaredMethods(type))).ToArray();
        _create = Bind<Action<string>>(modificationTypes, "OnWillCreateAsset");
        _save = Bind<Func<string[], string[]>>(modificationTypes, "OnWillSaveAssets");
        _delete = Bind<Func<string, RemoveAssetOptions, AssetDeleteResult>>(modificationTypes, "OnWillDeleteAsset");
        _move = Bind<Func<string, string, AssetMoveResult>>(modificationTypes, "OnWillMoveAsset");
        _generation = generation;
    }

    private static AssetPostprocessorDescriptor CreatePostprocessor(Type type)
    {
        if (!RuntimeTypeCache.TryCreateInstance(type, out var created) || created is not AssetPostprocessor probe)
            return default;
        AssetPostprocessor Create() =>
            RuntimeTypeCache.TryCreateInstance(type, out var value) && value is AssetPostprocessor processor
                ? processor : throw new InvalidOperationException($"Could not create asset postprocessor {type.FullName}.");
        var methods = GetDeclaredMethods(type);
        var preprocess = BindInstance(type, methods, "OnPreprocessAsset");
        var postprocess = BindInstance(type, methods, "OnPostprocessAsset");
        var postprocessAll = BindStatic<Action<string[], string[], string[], string[]>>(type, methods,
            "OnPostprocessAllAssets");
        return new AssetPostprocessorDescriptor(type, Create, probe.GetPostprocessOrder(), preprocess,
            postprocess, postprocessAll);
    }

    private static AssetPostprocessorDescriptor TryCreatePostprocessor(Type type)
    {
        EditorCallbacks.TryInvoke($"AssetPostprocessor registration {type.FullName}",
            () => CreatePostprocessor(type), default, out var descriptor);
        return descriptor;
    }

    private static Action<AssetPostprocessor>? BindInstance(Type type, MethodInfo[] methods, string methodName)
    {
        var method = Matches4737();
        [global::BEngine.SkipLoopGuard]
        global::System.Reflection.MethodInfo? Matches4737()
        {
            var matchSource4737 = methods;
            ArgumentNullException.ThrowIfNull(matchSource4737, "source");
            global::System.ReadOnlySpan<global::System.Reflection.MethodInfo> matchValues4737 = matchSource4737;
            foreach (var item in matchValues4737)
            {
                if (item.Name == methodName)
                    return item;
            }
        
            return default!;
        }
        if (method is null) return null;
        if (method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        { Debug.LogWarning($"{type.FullName}.{methodName} must be instance void with no parameters."); return null; }
        var target = System.Linq.Expressions.Expression.Parameter(typeof(AssetPostprocessor), "processor");
        return System.Linq.Expressions.Expression.Lambda<Action<AssetPostprocessor>>(
            System.Linq.Expressions.Expression.Call(
                System.Linq.Expressions.Expression.Convert(target, type), method), target).Compile();
    }

    private static T? BindStatic<T>(Type type, MethodInfo[] methods, string methodName) where T : Delegate
    {
        var invoke = typeof(T).GetMethod(nameof(Action.Invoke))!;
        var projectionSource5625 = invoke.GetParameters();
        ArgumentNullException.ThrowIfNull(projectionSource5625, "source");
        var projectionResult5625 = projectionSource5625.Length == 0 ? global::System.Array.Empty<global::System.Type>() : new global::System.Type[projectionSource5625.Length];
        for (var projectionIndex5625 = 0; projectionIndex5625 < projectionSource5625.Length; projectionIndex5625++)
        {
            var projectionItem5625 = projectionSource5625[projectionIndex5625];
            projectionResult5625[projectionIndex5625] = projectionItem5625.ParameterType;
        }
        
        var parameterTypes = projectionResult5625;
        var method = methods.FirstOrDefault(item => item.Name == methodName &&
            item.IsStatic && item.ReturnType == invoke.ReturnType &&
            item.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
        if (method is null) return null;
        return (T)method.CreateDelegate(typeof(T));
    }

    private static MethodInfo[] GetDeclaredMethods(Type type)
    {
        try
        {
            return type.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }
        catch { return []; }
    }

    private static T[] Bind<T>(IEnumerable<(Type Type, MethodInfo[] Methods)> types, string methodName) where T : Delegate =>
        types.Select(entry => TryBindStatic<T>(entry.Type, entry.Methods, methodName)).Where(item => item is not null).ToArray()!;

    private static T? TryBindStatic<T>(Type type, MethodInfo[] methods, string methodName) where T : Delegate
    {
        EditorCallbacks.TryInvoke<T?>($"Asset processor binding {type.FullName}.{methodName}",
            () => BindStatic<T>(type, methods, methodName), null, out var callback);
        return callback;
    }

    private static void Invoke(Action<string> callback, string value, string name)
    { try { callback(value); } catch (Exception exception) { Log(name, exception); } }
    private static void Log(string method, Exception exception) =>
        EditorCallbacks.Report($"Asset modification processor {method}", exception);
}
