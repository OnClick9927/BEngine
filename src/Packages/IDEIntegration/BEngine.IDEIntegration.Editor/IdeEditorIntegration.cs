using EditorCapabilities = BEngine.Editor.EditorServices;
using System.ComponentModel;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.IDE.Editor;

internal static class IdeEditorIntegration
{
    private static IdeSession? _session;
    private static IDisposable? _opening;
    private static IDisposable? _fallbackOpening;
    private static BPackageManager? _packages;
    private static bool _queued;
    private static bool _dirty;
    private static long _nextPoll;

    [InitializeOnLoadMethod]
    [DidReloadScripts]
    private static void Initialize()
    {
        if (_queued) return;
        _queued = true;
        EditorApplication.delayCall += Start;
    }

    private static void Start()
    {
        _queued = false;
        if (_session is not null) { _dirty = true; return; }
        var project = EditorInstanceContext.current?.projectRootPath ?? EditorApplication.projectPath;
        if (string.IsNullOrWhiteSpace(project)) return;
        try
        {
            _session = new IdeSession(ProjectWorkspace.Open(project));
            _fallbackOpening = CodeEditorIntegration.Register(new StandardCodeEditor());
            _opening = CodeEditorIntegration.Register(new ProjectCodeEditor());
            _packages = EditorCapabilities.GetService<BPackageManager>();
            if (_packages is not null) _packages.packagesUnloading += OnPackagesUnloading;
            EditorApplication.update += Update;
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorProjectSettings.projectSettingsChanged += MarkDirty;
        }
        catch (Exception exception)
        {
            Stop();
            BEngine.Debug.LogWarning("IDE Integration could not initialize: " + exception.Message);
        }
    }

    private static void Update()
    {
        var now = Environment.TickCount64;
        if (now < _nextPoll || EditorApplication.isCompiling) return;
        _nextPoll = now + 500;
        if (_dirty)
        {
            _dirty = false;
            try { _session?.Synchronize(); }
            catch (Exception exception) { BEngine.Debug.LogWarning("IDE solution synchronization failed: " + exception.Message); }
        }
        try { _session?.PollRequests(); }
        catch (Exception exception) { BEngine.Debug.LogWarning("IDE request could not be handled: " + exception.Message); }
    }

    private static void MarkDirty() => _dirty = true;
    private static void OnCompilationFinished(object _) => MarkDirty();
    private static void OnPackagesUnloading(IReadOnlyList<BPackageDefinition> packages)
    {
        bool __queryResult2707;
        {
            var __querySource2707 = packages;
            ArgumentNullException.ThrowIfNull(__querySource2707, "source");
            __queryResult2707 = false;
            var __querySource2707Type = __querySource2707.GetType();
            if (__querySource2707Type == typeof(BEngine.ProjectSystem.BPackageDefinition[]) || __querySource2707Type == typeof(System.Collections.Generic.List<BEngine.ProjectSystem.BPackageDefinition>))
            {
                System.ReadOnlySpan<BEngine.ProjectSystem.BPackageDefinition> __querySource2707Span = __querySource2707Type == typeof(BEngine.ProjectSystem.BPackageDefinition[]) ? (BEngine.ProjectSystem.BPackageDefinition[])(object)__querySource2707 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.ProjectSystem.BPackageDefinition>)(object)__querySource2707);
                foreach (var __queryItem2707 in __querySource2707Span)
                {
                    if ((__queryItem2707.Document.Id == "com.bengine.ide-integration"))
                    {
                        __queryResult2707 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem2707 in __querySource2707)
                {
                    if ((__queryItem2707.Document.Id == "com.bengine.ide-integration"))
                    {
                        __queryResult2707 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult2707)
            Stop();
    }

    private static void Stop()
    {
        EditorApplication.delayCall -= Start;
        _queued = false;
        EditorApplication.update -= Update;
        EditorApplication.quitting -= Stop;
        AssemblyReloadEvents.beforeAssemblyReload -= Stop;
        CompilationPipeline.compilationFinished -= OnCompilationFinished;
        EditorProjectSettings.projectSettingsChanged -= MarkDirty;
        if (_packages is not null) _packages.packagesUnloading -= OnPackagesUnloading;
        _packages = null;
        _opening?.Dispose();
        _opening = null;
        _fallbackOpening?.Dispose();
        _fallbackOpening = null;
        _session?.Dispose();
        _session = null;
        _dirty = false;
    }

    [MenuItem("Tools/IDE/Sync Solution", false, 30)]
    private static void SyncSolution()
    {
        if (_session is null) Start();
        var solution = _session?.Synchronize();
        if (solution is not null) BEngine.Debug.Log("IDE solution synchronized: " + solution);
    }

    [MenuItem("Tools/IDE/Open Solution", false, 31)]
    private static void OpenSolution()
    {
        SyncSolution();
        if (string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return;
        var solution = Path.Combine(EditorApplication.projectPath, "BEngine.Project.sln");
        new ProjectCodeEditor().Open(new CodeEditorOpenRequest(EditorApplication.projectPath, solution, 0, 0));
    }

    [MenuItem("Tools/IDE/Sync Solution", true)]
    [MenuItem("Tools/IDE/Open Solution", true)]
    private static bool HasProject() => !string.IsNullOrWhiteSpace(EditorApplication.projectPath);
}

internal sealed class ProjectCodeEditor : ICodeEditorIntegration
{
    public bool Open(CodeEditorOpenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath)) return false;
        var editor = EditorPreferences.current.ExternalScriptEditor?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(editor)) editor = FindCode();
        if (string.IsNullOrWhiteSpace(editor)) return false;
        var name = Path.GetFileNameWithoutExtension(editor);
        var code = name.Contains("code", StringComparison.OrdinalIgnoreCase) || name.Contains("codium", StringComparison.OrdinalIgnoreCase);
        var visualStudio = name.Equals("devenv", StringComparison.OrdinalIgnoreCase);
        if (!code && !visualStudio) return false;
        var solution = Path.Combine(request.ProjectPath, "BEngine.Project.sln");
        if (!File.Exists(solution)) IdeSolutionGenerator.Generate(ProjectWorkspace.Open(request.ProjectPath));
        var process = new ProcessHostRequest(editor) { UseShellExecute = true, CreateNoWindow = false };
        process.Arguments.Add(code ? request.ProjectPath : solution);
        if (!request.FilePath.Equals(solution, StringComparison.OrdinalIgnoreCase))
        {
            process.Arguments.Add(code ? "--goto" : "/Edit");
            process.Arguments.Add(code && request.Line > 0 ? $"{request.FilePath}:{request.Line}:{Math.Max(1, request.Column)}" : request.FilePath);
            if (visualStudio && request.Line > 0)
            { process.Arguments.Add("/Command"); process.Arguments.Add("Edit.Goto " + request.Line); }
        }
        try { EditorServices.GetRequiredService<ProcessHostUtility>().StartDetached(process); return true; }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { return false; }
    }

    private static string? FindCode()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            foreach (var name in OperatingSystem.IsWindows() ? new[] { "code.cmd", "code.exe" } : new[] { "code" })
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}
