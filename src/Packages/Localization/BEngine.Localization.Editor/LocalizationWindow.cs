using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.Localization.Editor;

public sealed class LocalizationWindow : EditorWindow
{
    private LocalizationTable? _table;
    private string _key = string.Empty;
    private string _previewLanguage = "en";
    private bool _pseudo;
    private string _message = string.Empty;
    private string _search = string.Empty;
    private string _cachedSearch = string.Empty;
    private LocalizationTable? _searchedTable;
    private List<LocalizationEntry>? _searchedEntries;
    private int _searchedCount;
    private string[] _matchingKeys = [];
    private string[] _keyOptions = [];
    private readonly EditorScrollView _scrollView = new();
    private Vector2 _scroll;

    [MenuItem("Window/Localization/Tables", false, 410)]
    public static void Open() => GetWindow<LocalizationWindow>("Localization").Show();

    protected override void OnSelectionChange()
    {
        if (Selection.activeObject is LocalizationTable table) _table = table;
        Repaint();
    }

    protected override void OnGUI()
    {
        _table = EditorGUILayout.ObjectField("Table", _table, false);
        if (_table is null) { EditorGUILayout.HelpBox("Create a Localization/Table asset, then select it here. Edit keys, translations and asset references in its Inspector."); return; }
        _scroll = _scrollView.Begin(_scroll);
        try
        {
        GUILayout.Label($"Language: {_table.language} | Entries: {_table.entries.Count}");
        if (GUILayout.Button("Edit Table in Inspector")) Selection.activeObject = _table;
        _search = EditorGUILayout.TextField("Find Key / Text", _search);
        if (_searchedTable != _table || _cachedSearch != _search || _searchedEntries != _table.entries || _searchedCount != _table.entries.Count)
        {
            _matchingKeys = _table.entries.Where(entry => entry.key.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                entry.text?.Contains(_search, StringComparison.OrdinalIgnoreCase) == true).Take(201).Select(entry => entry.key).ToArray();
            _keyOptions = new[] { "Select a key..." }.Concat(_matchingKeys.Take(200)).ToArray();
            _searchedTable = _table; _searchedEntries = _table.entries; _searchedCount = _table.entries.Count; _cachedSearch = _search;
        }
        if (_matchingKeys.Length > 0)
        {
            var selected = Array.IndexOf(_matchingKeys, _key);
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Popup("Matching Keys", selected is >= 0 and < 200 ? selected + 1 : 0, _keyOptions);
            if (EditorGUI.EndChangeCheck() && next > 0) _key = _matchingKeys[next - 1];
            if (_matchingKeys.Length > 200) GUILayout.Label("Showing the first 200 matches. Refine the search.");
        }
        else GUILayout.Label("No matching keys.");
        _key = EditorGUILayout.TextField("Key", _key);
        _previewLanguage = EditorGUILayout.TextField("Preview Language", _previewLanguage);
        _pseudo = EditorGUILayout.Toggle("Pseudo Localization", _pseudo);
        if (GUILayout.Button("Preview Text")) Run(() =>
        {
            using var preview = new LocalizationContext(_table.language) { pseudoLocalization = _pseudo };
            preview.MergeContext(_table);
            preview.SetLanguageAsync(_previewLanguage).GetAwaiter().GetResult();
            _message = preview.GetText(_key);
        });
        if (GUILayout.Button("Validate Table")) Run(() =>
        {
            var tables = Selection.objects.OfType<LocalizationTable>().Append(_table).Distinct().ToArray();
            var issues = LocalizationAuthoring.FindFormatIssues(tables).Concat(LocalizationAuthoring.FindMissingKeys(tables)).ToArray();
            _message = issues.Length == 0 ? $"{tables.Length} table(s) valid, including all format branches."
                : string.Join("\n", issues.Take(100)) + (issues.Length > 100 ? $"\n... {issues.Length} issues total." : "");
            _searchedTable = null;
        });
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (GUILayout.Button("Import CSV Translations")) Run(ImportCsv);
            if (GUILayout.Button("Export CSV Translations")) Run(() => Export("csv", LocalizationCsv.Export([_table])));
            if (GUILayout.Button("Import Excel Translations")) Run(() => ImportCsv(true));
            if (GUILayout.Button("Export Excel Translations")) Run(() => ExportBytes("xlsx", LocalizationWorkbook.Export([_table])));
            if (GUILayout.Button("Generate C# Keys")) Run(() => Export("cs", LocalizationAuthoring.GenerateKeys([_table])));
        }
        EditorGUILayout.HelpBox(_message);
        }
        finally { _scrollView.End(); }
    }

    private void ImportCsv() => ImportCsv(false);
    private void ImportCsv(bool workbook)
    {
        LocalizationAuthoring.EnsureCanWrite(_table);
        var path = EditorUtility.OpenFilePanel("Import localization", "", workbook ? "xlsx" : "csv");
        if (path.Length == 0) return;
        var tables = workbook ? LocalizationWorkbook.Import(FileUtility.Current.ReadAllBytes(path)) : LocalizationCsv.Import(FileUtility.Current.ReadAllText(path));
        try
        {
            var selected = tables.SingleOrDefault(t => LocalizationContext.NormalizeLanguage(t.language).Equals(
                LocalizationContext.NormalizeLanguage(_table!.language), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The file contains no translations for this table's language.");
            LocalizationAuthoring.ApplyTranslations(_table!, selected);
            _message = $"Imported {selected.entries.Count} translations. Save the table to persist them.";
        }
        finally { foreach (var table in tables) BObject.DestroyImmediate(table); }
    }

    private void Export(string extension, string content)
        => ExportBytes(extension, new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(content)).ToArray());

    private void ExportBytes(string extension, byte[] content)
    {
        LocalizationAuthoring.EnsureCanWrite();
        EditorFileDialog.Save("Export localization", "", $"*.{extension}", $"Localization.{extension}", path =>
        {
            LocalizationAuthoring.EnsureCanWrite();
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { FileUtility.Current.WriteAllBytes(temporary, content); FileUtility.Current.MoveFile(temporary, path, true); }
            finally { if (FileUtility.Current.FileExists(temporary)) FileUtility.Current.DeleteFile(temporary); }
            _message = "Exported: " + path;
        });
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception exception) { _message = exception.Message; Debug.LogException(exception); }
    }
}
