using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using System.Security.Cryptography;
using System.Text;
using BEngine.Editor;

namespace BEngine.ProjectSystem.Editor;

internal sealed class CgProjectShaderCompiler : IProjectShaderCompiler
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        var registration = ProjectShaderCompiler.Register(new CgProjectShaderCompiler());
        RuntimeAssemblyCleanup.Register(assembly =>
        {
            if (assembly == typeof(CgProjectShaderCompiler).Assembly) registration.Dispose();
        });
    }

    private const string Schema = "BEngine.ProjectCgShader.v2";

    public ShaderCompilationResult CompileChangedInBackground(
        ProjectWorkspace workspace,
        IEnumerable<string> assetPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetPaths);
        var compiled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var deleted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EditorCapabilities.GetRequiredService<FileUtility>().CreateDirectory(workspace.ShaderArtifactsPath);

        foreach (var input in assetPaths.Where(IsShaderPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assetPath = NormalizeAssetPath(workspace, input);
            try
            {
                var artifact = CompileOne(workspace, assetPath, cancellationToken);
                if (artifact is null) deleted[assetPath] = GetPointerPath(workspace, assetPath);
                else compiled[assetPath] = artifact;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors[assetPath] = exception.Message;
                AppendLog(workspace, assetPath, exception.ToString());
            }
        }

        return new ShaderCompilationResult(compiled, deleted, errors);
    }

    public void ApplyCompilationResult(ShaderCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var artifact in result.CompiledArtifacts)
        {
            var buildId = EditorCapabilities.GetRequiredService<PathUtility>().GetFileNameWithoutExtension(artifact.Value);
            WritePointer(EditorCapabilities.GetRequiredService<PathUtility>().Combine(EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(artifact.Value)!, "current"), buildId);
        }
        foreach (var pointerPath in result.DeletedArtifacts.Values)
        {
            if (EditorCapabilities.GetRequiredService<FileUtility>().FileExists(pointerPath)) EditorCapabilities.GetRequiredService<FileUtility>().DeleteFile(pointerPath);
        }
        if (result.CompiledArtifacts.Count > 0 || result.DeletedAssets.Count > 0)
            BEngine.Rendering.Rhi.DefaultShaderResources.ClearCache();
    }

    public string? ResolveCurrentArtifact(ProjectWorkspace workspace, string assetPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var normalized = NormalizeAssetPath(workspace, assetPath);
        var pointerPath = GetPointerPath(workspace, normalized);
        if (!EditorCapabilities.GetRequiredService<FileUtility>().FileExists(pointerPath)) return null;
        var buildId = EditorCapabilities.GetRequiredService<FileUtility>().ReadAllText(pointerPath).Trim();
        if (buildId.Length == 0) return null;
        var artifact = EditorCapabilities.GetRequiredService<PathUtility>().Combine(EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(pointerPath)!, buildId + ".spv");
        return EditorCapabilities.GetRequiredService<FileUtility>().FileExists(artifact) ? artifact : null;
    }

    public bool IsShaderPath(string path) =>
        path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".cg", StringComparison.OrdinalIgnoreCase);

    private static string? CompileOne(
        ProjectWorkspace workspace,
        string assetPath,
        CancellationToken cancellationToken)
    {
        var sourcePath = workspace.ResolveInside(assetPath);
        var pointerPath = GetPointerPath(workspace, assetPath);
        if (!EditorCapabilities.GetRequiredService<FileUtility>().FileExists(sourcePath)) return null;

        cancellationToken.ThrowIfCancellationRequested();
        var source = EditorCapabilities.GetRequiredService<FileUtility>().ReadAllText(sourcePath);
        var stage = ResolveStage(assetPath, source);
        var entryPoint = ResolveEntryPoint(source);
        var settings = ReadImportSettings(workspace, assetPath, sourcePath);
        var compilerSource = RemoveEnginePragmas(source);
        var buildId = ComputeBuildId(assetPath, source, stage, entryPoint, settings);
        var outputDirectory = EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(pointerPath)!;
        var artifactPath = EditorCapabilities.GetRequiredService<PathUtility>().Combine(outputDirectory, buildId + ".spv");
        EditorCapabilities.GetRequiredService<FileUtility>().CreateDirectory(outputDirectory);
        if (!EditorCapabilities.GetRequiredService<FileUtility>().FileExists(artifactPath))
        {
            var bytes = ShaderCompilerUtility.Current.Compile(
                compilerSource,
                assetPath,
                stage,
                entryPoint,
                settings.StrictCompilation,
                settings.OptimizationLevel);
            cancellationToken.ThrowIfCancellationRequested();
            var temporaryArtifact = artifactPath + $".{Guid.NewGuid():N}.tmp";
            EditorCapabilities.GetRequiredService<FileUtility>().WriteAllBytes(temporaryArtifact, bytes);
            EditorCapabilities.GetRequiredService<FileUtility>().MoveFile(temporaryArtifact, artifactPath, true);
        }

        AppendLog(workspace, assetPath, $"Compiled {stage} -> {artifactPath}");
        return artifactPath;
    }

    private static void WritePointer(string pointerPath, string buildId)
    {
        EditorCapabilities.GetRequiredService<FileUtility>().CreateDirectory(EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(pointerPath)!);
        var temporaryPointer = pointerPath + $".{Guid.NewGuid():N}.tmp";
        EditorCapabilities.GetRequiredService<FileUtility>().WriteAllText(temporaryPointer, buildId);
        EditorCapabilities.GetRequiredService<FileUtility>().MoveFile(temporaryPointer, pointerPath, true);
    }

    private static ShaderCompilationStage ResolveStage(string path, string source)
    {
        var name = path.ToLowerInvariant();
        if (name.EndsWith(".vert.cg") || name.EndsWith(".vertex.cg")) return ShaderCompilationStage.VertexShader;
        if (name.EndsWith(".frag.cg") || name.EndsWith(".fragment.cg")) return ShaderCompilationStage.FragmentShader;
        if (name.EndsWith(".comp.cg") || name.EndsWith(".compute.cg")) return ShaderCompilationStage.ComputeShader;

        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var value = line.Trim();
            const string pragma = "#pragma stage ";
            if (!value.StartsWith(pragma, StringComparison.OrdinalIgnoreCase)) continue;
            var stage = value[pragma.Length..].Trim();
            if (stage.Equals("vertex", StringComparison.OrdinalIgnoreCase)) return ShaderCompilationStage.VertexShader;
            if (stage.Equals("fragment", StringComparison.OrdinalIgnoreCase) ||
                stage.Equals("pixel", StringComparison.OrdinalIgnoreCase)) return ShaderCompilationStage.FragmentShader;
            if (stage.Equals("compute", StringComparison.OrdinalIgnoreCase)) return ShaderCompilationStage.ComputeShader;
            throw new InvalidDataException($"Unsupported shader stage '{stage.ToString()}'.");
        }

        throw new InvalidDataException(
            $"Shader '{path}' must use a stage suffix or declare '#pragma stage vertex|fragment|compute'.");
    }

    private static string ResolveEntryPoint(string source)
    {
        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var value = line.Trim();
            const string pragma = "#pragma entry ";
            if (!value.StartsWith(pragma, StringComparison.OrdinalIgnoreCase)) continue;
            var entryPoint = value[pragma.Length..].Trim().ToString();
            if (entryPoint.Length == 0) throw new InvalidDataException("CG shader entry point cannot be empty.");
            return entryPoint;
        }
        return "main";
    }

    private static string RemoveEnginePragmas(string source)
    {
        var builder = new StringBuilder(source.Length);
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.AsSpan().TrimStart();
            if (trimmed.StartsWith("#pragma stage ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("#pragma entry ", StringComparison.OrdinalIgnoreCase)) continue;
            builder.AppendLine(line);
        }
        return builder.ToString();
    }

    private static ShaderImportSettings ReadImportSettings(
        ProjectWorkspace workspace,
        string assetPath,
        string sourcePath)
    {
        var metaPath = sourcePath + ".meta";
        if (!EditorCapabilities.GetRequiredService<FileUtility>().FileExists(metaPath)) return ShaderImportSettings.Default;
        try
        {
            var values = YamlUtility.Load<AssetMetaDocument>(metaPath).Settings ?? [];
            var strict = values.TryGetValue("strictCompilation", out var strictText) &&
                         bool.TryParse(strictText, out var strictValue)
                ? strictValue
                : ShaderImportSettings.Default.StrictCompilation;
            var optimization = values.TryGetValue("optimizationLevel", out var levelText) &&
                               int.TryParse(levelText,
                                   System.Globalization.NumberStyles.Integer,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out var levelValue)
                ? Math.Clamp(levelValue, 0, 3)
                : ShaderImportSettings.Default.OptimizationLevel;
            return new ShaderImportSettings(strict, optimization);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
            AppendLog(workspace, assetPath,
                $"Could not read shader import settings: {exception.Message}");
            return ShaderImportSettings.Default;
        }
    }

    private static string ComputeBuildId(
        string assetPath,
        string source,
        ShaderCompilationStage stage,
        string entryPoint,
        ShaderImportSettings settings)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Schema);
        Append(hash, assetPath.ToLowerInvariant());
        Append(hash, stage.ToString());
        Append(hash, entryPoint);
        Append(hash, settings.StrictCompilation.ToString());
        Append(hash, settings.OptimizationLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        hash.AppendData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string GetPointerPath(ProjectWorkspace workspace, string assetPath)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(assetPath.ToLowerInvariant())));
        return EditorCapabilities.GetRequiredService<PathUtility>().Combine(workspace.ShaderArtifactsPath, key, "current");
    }

    private static string NormalizeAssetPath(ProjectWorkspace workspace, string path)
    {
        var fullPath = EditorCapabilities.GetRequiredService<PathUtility>().IsPathRooted(path) ? EditorCapabilities.GetRequiredService<PathUtility>().GetFullPath(path) : workspace.ResolveInside(path);
        bool __queryResult11704;
        {
            var __querySource11704 = workspace.AssetRoots;
            ArgumentNullException.ThrowIfNull(__querySource11704, "source");
            __queryResult11704 = false;
            var __querySource11704Type = __querySource11704.GetType();
            if (__querySource11704Type == typeof(string[]) || __querySource11704Type == typeof(System.Collections.Generic.List<string>))
            {
                System.ReadOnlySpan<string> __querySource11704Span = __querySource11704Type == typeof(string[]) ? (string[])(object)__querySource11704 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<string>)(object)__querySource11704);
                foreach (var __queryItem11704 in __querySource11704Span)
                {
                    if ((fullPath.StartsWith(__queryItem11704.TrimEnd(EditorCapabilities.GetRequiredService<PathUtility>().DirectorySeparatorChar) + EditorCapabilities.GetRequiredService<PathUtility>().DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult11704 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem11704 in __querySource11704)
                {
                    if ((fullPath.StartsWith(__queryItem11704.TrimEnd(EditorCapabilities.GetRequiredService<PathUtility>().DirectorySeparatorChar) + EditorCapabilities.GetRequiredService<PathUtility>().DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult11704 = true;
                        break;
                    }
                }
            }
        }
        
        if (!__queryResult11704)
            throw new InvalidDataException($"Shader path is outside Assets: {path}");
        return EditorCapabilities.GetRequiredService<PathUtility>().GetRelativePath(workspace.RootPath, fullPath).Replace('\\', '/');
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static void AppendLog(ProjectWorkspace workspace, string assetPath, string message)
    {
        var path = EditorCapabilities.GetRequiredService<PathUtility>().Combine(EditorInstanceContext.current?.logsPath ?? workspace.LogsPath,
            "ShaderCompilation.log");
        EditorCapabilities.GetRequiredService<FileUtility>().CreateDirectory(EditorCapabilities.GetRequiredService<PathUtility>().GetDirectoryName(path)!);
        EditorCapabilities.GetRequiredService<FileUtility>().AppendAllText(path,
            $"[{EditorCapabilities.GetRequiredService<PlatformUtility>().UtcNow.ToLocalTime():O}] {assetPath}" +
            $"{EditorCapabilities.GetRequiredService<PlatformUtility>().NewLine}{message}{EditorCapabilities.GetRequiredService<PlatformUtility>().NewLine}");
    }

    private readonly record struct ShaderImportSettings(bool StrictCompilation, int OptimizationLevel)
    {
        internal static ShaderImportSettings Default { get; } = new(true, 2);
    }
}
