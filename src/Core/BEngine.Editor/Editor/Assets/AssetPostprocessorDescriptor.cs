using System.Reflection;

namespace BEngine.Editor;

internal readonly record struct AssetPostprocessorDescriptor(
    Type Type,
    Func<AssetPostprocessor> Factory,
    int Order,
    Action<AssetPostprocessor>? Preprocess,
    Action<AssetPostprocessor>? Postprocess,
    Action<string[], string[], string[], string[]>? PostprocessAll);
