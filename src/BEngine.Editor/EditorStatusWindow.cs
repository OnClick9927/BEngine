using System.Diagnostics;
using System.Text;
using BEngine.UIElements;
using UiLabel = BEngine.UIElements.Label;

namespace BEngine.Editor;

internal sealed class EditorStatusWindow : EditorWindow
{
    private const int MaximumLogCharacters = 1_000_000;
    private readonly Dictionary<string, string> _logPaths = new(StringComparer.Ordinal);
    private DropdownField _source = null!;
    private SearchField _search = null!;
    private Toggle _follow = null!;
    private UiLabel _summary = null!;
    private TextField _content = null!;
    private string _lastPath = string.Empty;
    private DateTime _lastWriteTimeUtc;
    private long _lastLength = -1;
    private double _nextRefresh;

    [MenuItem("窗口/Editor 状态", false, 240)]
    public static void Open() => GetWindow<EditorStatusWindow>("Editor 状态", true);

    protected override void CreateGUI()
    {
        minSize = new Vector2(720, 420);
        BuildLogPaths();

        var root = rootVisualElement;
        root.style.flexGrow = 1;
        root.style.SetPadding(4);

        var toolbar = new Toolbar();
        _source = new DropdownField("日志", _logPaths.Keys);
        _source.style.width = 280;
        _source.valueChanged += _ => RefreshLog(true);
        toolbar.Add(_source);
        toolbar.Add(new ToolbarButton(() => RefreshLog(true), "刷新"));
        toolbar.Add(new ToolbarButton(ClearSelectedLog, "清空"));
        toolbar.Add(new ToolbarButton(OpenLogDirectory, "打开目录"));
        root.Add(toolbar);

        var filters = new Toolbar();
        _search = new SearchField("搜索");
        _search.style.flexGrow = 1;
        _search.valueChanged += _ => RefreshLog(true);
        filters.Add(_search);
        _follow = new Toggle("跟随末尾");
        _follow.SetValueWithoutNotify(true);
        _follow.valueChanged += value =>
        {
            _content.scrollToEnd = value;
            RefreshLog(true);
        };
        filters.Add(_follow);
        root.Add(filters);

        _summary = new UiLabel();
        _summary.style.SetMargin(5, 2, 5, 4);
        _summary.style.color = UIColor.FromRgb(166, 172, 178);
        root.Add(_summary);

        _content = new TextField
        {
            multiline = true,
            isReadOnly = true,
            scrollToEnd = true
        };
        _content.style.flexGrow = 1;
        _content.style.fontSize = 13;
        root.Add(_content);
        RefreshLog(true);
    }

    protected override void Update()
    {
        if (EditorApplication.timeSinceStartup < _nextRefresh) return;
        _nextRefresh = EditorApplication.timeSinceStartup + 0.75;
        RefreshLog(false);
    }

    private void BuildLogPaths()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Environment.CurrentDirectory;
        var logs = Path.Combine(projectRoot, "Logs");
        _logPaths.Clear();
        _logPaths["Editor.log"] = Path.Combine(logs, "Editor.log");
        _logPaths["运行时脚本编译"] = Path.Combine(logs, "ScriptCompilation.log");
        _logPaths["编辑器脚本编译"] = Path.Combine(logs, "EditorScriptCompilation.log");
        _logPaths["启动构建"] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BEngine", "LauncherBuild.log");
    }

    private void RefreshLog(bool force)
    {
        if (_source is null || _content is null || !_logPaths.TryGetValue(_source.value, out var path)) return;
        var info = new FileInfo(path);
        var writeTime = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        var length = info.Exists ? info.Length : 0;
        if (!force && path == _lastPath && writeTime == _lastWriteTimeUtc && length == _lastLength) return;

        _lastPath = path;
        _lastWriteTimeUtc = writeTime;
        _lastLength = length;
        try
        {
            var text = info.Exists ? ReadSharedText(path) : $"日志文件尚未生成：{path}";
            var search = _search?.value?.Trim() ?? string.Empty;
            if (search.Length > 0)
            {
                text = string.Join(Environment.NewLine, text.Split(['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries).Where(line =>
                    line.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }
            if (text.Length > MaximumLogCharacters)
                text = "... 已省略较早日志 ..." + Environment.NewLine + text[^MaximumLogCharacters..];
            _content.scrollToEnd = _follow?.value ?? true;
            _content.SetValueWithoutNotify(text);
            var lineCount = text.Length == 0 ? 0 : text.Count(character => character == '\n') + 1;
            _summary.text = $"{path}  |  {lineCount} 行  |  {FormatBytes(length)}";
        }
        catch (Exception exception)
        {
            _content.SetValueWithoutNotify($"读取日志失败：{exception.Message}");
            _summary.text = path;
        }
    }

    private void ClearSelectedLog()
    {
        if (!_logPaths.TryGetValue(_source.value, out var path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
            RefreshLog(true);
        }
        catch (Exception exception)
        {
            _content.SetValueWithoutNotify($"清空日志失败：{exception.Message}");
        }
    }

    private void OpenLogDirectory()
    {
        if (!_logPaths.TryGetValue(_source.value, out var path)) return;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
    }

    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };
}
