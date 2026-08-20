using System.Reflection;

namespace BEngine.Editor;

internal static class AssetProcessorRegistry
{
    private static readonly object Gate = new();
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
            .Where(type => !type.IsAbstract).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        _create = Bind<Action<string>>(modificationTypes, "OnWillCreateAsset");
        _save = Bind<Func<string[], string[]>>(modificationTypes, "OnWillSaveAssets");
        _delete = Bind<Func<string, RemoveAssetOptions, AssetDeleteResult>>(modificationTypes, "OnWillDeleteAsset");
        _move = Bind<Func<string, string, AssetMoveResult>>(modificationTypes, "OnWillMoveAsset");
        _generation = generation;
    }

    private static AssetPostprocessorDescriptor CreatePostprocessor(Type type)
    {
        var objectFactory = RuntimeTypeCache.GetFactory(type);
        if (objectFactory is null) return default;
        Func<AssetPostprocessor> factory = () => (AssetPostprocessor)objectFactory();
        var probe = factory();
        var preprocess = BindInstance(type, "OnPreprocessAsset");
        var postprocess = BindInstance(type, "OnPostprocessAsset");
        var postprocessAll = BindStatic<Action<string[], string[], string[], string[]>>(type,
            "OnPostprocessAllAssets");
        return new AssetPostprocessorDescriptor(type, factory, probe.GetPostprocessOrder(), preprocess,
            postprocess, postprocessAll);
    }

    private static AssetPostprocessorDescriptor TryCreatePostprocessor(Type type)
    {
        EditorFeatureGuard.TryInvoke($"AssetPostprocessor registration {type.FullName}",
            () => CreatePostprocessor(type), default, out var descriptor);
        return descriptor;
    }

    private static Action<AssetPostprocessor>? BindInstance(Type type, string methodName)
    {
        var method = RuntimeTypeCache.GetMethods(type).FirstOrDefault(item => item.Name == methodName);
        if (method is null) return null;
        if (method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        { Debug.LogWarning($"{type.FullName}.{methodName} must be instance void with no parameters."); return null; }
        var target = System.Linq.Expressions.Expression.Parameter(typeof(AssetPostprocessor), "processor");
        return System.Linq.Expressions.Expression.Lambda<Action<AssetPostprocessor>>(
            System.Linq.Expressions.Expression.Call(
                System.Linq.Expressions.Expression.Convert(target, type), method), target).Compile();
    }

    private static T? BindStatic<T>(Type type, string methodName) where T : Delegate
    {
        var invoke = typeof(T).GetMethod(nameof(Action.Invoke))!;
        var parameterTypes = invoke.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var method = RuntimeTypeCache.GetMethods(type).FirstOrDefault(item => item.Name == methodName &&
            item.IsStatic && item.ReturnType == invoke.ReturnType &&
            item.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
        if (method is null) return null;
        return (T)method.CreateDelegate(typeof(T));
    }

    private static T[] Bind<T>(IEnumerable<Type> types, string methodName) where T : Delegate =>
        types.Select(type => TryBindStatic<T>(type, methodName)).Where(item => item is not null).ToArray()!;

    private static T? TryBindStatic<T>(Type type, string methodName) where T : Delegate
    {
        EditorFeatureGuard.TryInvoke<T?>($"Asset processor binding {type.FullName}.{methodName}",
            () => BindStatic<T>(type, methodName), null, out var callback);
        return callback;
    }

    private static void Invoke(Action<string> callback, string value, string name)
    { try { callback(value); } catch (Exception exception) { Log(name, exception); } }
    private static void Log(string method, Exception exception) =>
        EditorFeatureGuard.Report($"Asset modification processor {method}", exception);
}
