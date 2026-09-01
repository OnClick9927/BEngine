using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

[CustomEditor(typeof(AssemblyDefinitionAsset))]
public sealed class AssemblyDefinitionAssetEditor : Editor
{
    private AssemblyDefinitionDocument _document = new();
    private string _references = string.Empty;
    private string _includePlatforms = string.Empty;
    private string _excludePlatforms = string.Empty;
    private string _defineConstraints = string.Empty;
    private string _error = string.Empty;

    protected override void OnEnable() => Reload();

    public override void OnInspectorGUI()
    {
        var asset = (AssemblyDefinitionAsset)target;
        GUILayout.Label(new GUIContent(asset.name, EditorBuiltinIcons.Assets.Assembly, asset.assetPath),
            EditorStyles.boldLabel);
        if (!string.IsNullOrWhiteSpace(asset.importError))
            EditorGUILayout.HelpBox(asset.importError, MessageType.Error);

        EditorGUI.BeginChangeCheck();
        _document.Name = EditorGUILayout.TextField("Name", _document.Name);
        _document.RootNamespace = EditorGUILayout.TextField("Root Namespace", _document.RootNamespace);
        _references = EditorGUILayout.TextField("References", _references);
        _defineConstraints = EditorGUILayout.TextField("Define Constraints", _defineConstraints);
        _includePlatforms = EditorGUILayout.TextField("Include Platforms", _includePlatforms);
        _excludePlatforms = EditorGUILayout.TextField("Exclude Platforms", _excludePlatforms);
        _document.AutoReferenced = EditorGUILayout.Toggle("Auto Referenced", _document.AutoReferenced);
        _document.EditorOnly = EditorGUILayout.Toggle("Editor Only", _document.EditorOnly);
        _document.AllowUnsafeCode = EditorGUILayout.Toggle("Allow Unsafe Code", _document.AllowUnsafeCode);
        if (EditorGUI.EndChangeCheck())
        {
            ReadLists();
            hasUnsavedChanges = true;
            _error = Validate();
        }

        if (!string.IsNullOrWhiteSpace(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(!hasUnsavedChanges);
        if (GUILayout.Button("Revert", GUILayout.Width(80))) Reload();
        EditorGUI.EndDisabledGroup();
        EditorGUI.BeginDisabledGroup(!hasUnsavedChanges || !string.IsNullOrWhiteSpace(_error));
        if (GUILayout.Button("Apply", GUILayout.Width(80))) Apply();
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();
    }

    public override void SaveChanges() => Apply();

    public override void DiscardChanges() => Reload();

    private void Apply()
    {
        ReadLists();
        _error = Validate();
        if (!string.IsNullOrWhiteSpace(_error)) return;
        var asset = (AssemblyDefinitionAsset)target;
        Undo.RegisterAssetFileUndo(asset.assetPath, "Edit Assembly Definition");
        _document.Save(AssetDatabase.ResolveAssetPath(asset.assetPath));
        asset.definition = Clone(_document);
        asset.importError = string.Empty;
        hasUnsavedChanges = false;
        AssetDatabase.ImportAsset(asset.assetPath, ImportAssetOptions.ForceUpdate);
        EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
    }

    private void Reload()
    {
        var asset = (AssemblyDefinitionAsset)target;
        try
        {
            _document = YamlUtility.Load<AssemblyDefinitionDocument>(AssetDatabase.ResolveAssetPath(asset.assetPath));
            asset.definition = Clone(_document);
            asset.importError = string.Empty;
            _error = string.Empty;
        }
        catch (Exception exception)
        {
            _document = Clone(asset.definition);
            _error = exception.Message;
        }
        _references = Join(_document.References);
        _includePlatforms = Join(_document.IncludePlatforms);
        _excludePlatforms = Join(_document.ExcludePlatforms);
        _defineConstraints = Join(_document.DefineConstraints);
        hasUnsavedChanges = false;
    }

    private void ReadLists()
    {
        _document.References = Split(_references);
        _document.IncludePlatforms = Split(_includePlatforms);
        _document.ExcludePlatforms = Split(_excludePlatforms);
        _document.DefineConstraints = Split(_defineConstraints);
    }

    private string Validate()
    {
        try
        {
            _ = _document.ToYaml();
            return string.Empty;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static AssemblyDefinitionDocument Clone(AssemblyDefinitionDocument source) => new()
    {
        Format = source.Format,
        Version = source.Version,
        Name = source.Name,
        RootNamespace = source.RootNamespace,
        References = [.. source.References ?? []],
        IncludePlatforms = [.. source.IncludePlatforms ?? []],
        ExcludePlatforms = [.. source.ExcludePlatforms ?? []],
        DefineConstraints = [.. source.DefineConstraints ?? []],
        AutoReferenced = source.AutoReferenced,
        EditorOnly = source.EditorOnly,
        AllowUnsafeCode = source.AllowUnsafeCode
    };

    private static string Join(IEnumerable<string> values) => string.Join("; ", values);

    private static List<string> Split(string value) => value
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
