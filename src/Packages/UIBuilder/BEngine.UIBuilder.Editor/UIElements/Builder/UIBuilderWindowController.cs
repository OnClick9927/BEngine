using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.UIElements.Editor;

public sealed class UIBuilderWindowController :
    EditorWindowController<UIBuilderWindow, UIBuilderWindowModel>
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private static VisualElement? _clipboard;
    private static string _clipboardXml = string.Empty;

    public void SetLibraryTab(bool projectControls)
    {
        if (Model.LibraryProjectControls == projectControls) return;
        Model.LibraryProjectControls = projectControls;
        View.RefreshLibraryTree(); Repaint();
    }

    public void SetPreviewMode(bool enabled)
    {
        if (Model.PreviewMode == enabled) return;
        Model.PreviewDocument?.CancelInteraction();
        Model.PreviewDocument = enabled ? new UIDocument { scaleMode = PanelScaleMode.ConstantPixelSize } : null;
        Model.PreviewDocument?.SetVisualTree(UIBuilderWindowModel.CloneDocument(Model.DocumentRoot));
        Model.Status = enabled ? "Preview" : "Edit";
        GUIUtility.keyboardControl = 0;
        Repaint();
    }

    public void CopySelected()
    {
        _clipboard = UIBuilderWindowModel.CloneDocument(Model.Selected);
        GUIUtility.systemCopyBuffer = _clipboardXml = UxmlSerializer.SerializeElement(_clipboard);
        Model.Status = "Copied element";
        Repaint();
    }

    public void CutSelected()
    {
        if (Model.PreviewMode || Model.Selected.parent is null) return;
        CopySelected();
        DeleteSelected();
    }

    public void Paste()
    {
        if (Model.PreviewMode) return;
        var xml = GUIUtility.systemCopyBuffer;
        if (_clipboard is null || !string.Equals(xml, _clipboardXml, StringComparison.Ordinal)) return;
        AddElement(UIBuilderWindowModel.CloneDocument(_clipboard));
    }

    public bool CanPaste => _clipboard is not null && GUIUtility.systemCopyBuffer == _clipboardXml;

    public UIBuilderWindowController(
        EditorWindowMvcContext context,
        UIBuilderWindowModel model) : this(
            context,
            model,
            EditorServices.GetRequiredService<FileUtility>(),
            EditorServices.GetRequiredService<PathUtility>()) { }

    public UIBuilderWindowController(
        EditorWindowMvcContext context,
        UIBuilderWindowModel model,
        FileUtility fileSystem,
        PathUtility paths) : base(context, model)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public void Select(VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!Model.DocumentRoot.Contains(element))
            throw new ArgumentException("The element does not belong to this UI Builder document.", nameof(element));
        Model.Selected = element;
        Model.Status = $"Selected {element.GetType().Name}";
        View.SyncHierarchySelection();
        Repaint();
    }

    public void Change(string name, Action mutation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(mutation);
        if (Model.PreviewMode) return;
        EditorAssetWritePolicy.EnsureCanWrite(name);
        var before = Model.Capture();
        mutation();
        Model.PushUndo(before);
        Model.MarkChanged();
        Model.Status = name;
        View.RefreshBuilderTrees();
        Repaint();
    }

    public void AddElement(VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Change($"Add {element.GetType().Name}", () =>
        {
            Model.Selected.Add(element);
            Model.Selected = element;
        });
    }

    public void DuplicateSelected()
    {
        if (ReferenceEquals(Model.Selected, Model.DocumentRoot) || Model.Selected.parent is not { } parent) return;
        var copy = UIBuilderWindowModel.CloneDocument(Model.Selected);
        Change($"Duplicate {Model.Selected.GetType().Name}", () =>
        {
            parent.Insert(parent.IndexOf(Model.Selected) + 1, copy);
            Model.Selected = copy;
        });
    }

    public void DeleteSelected()
    {
        if (ReferenceEquals(Model.Selected, Model.DocumentRoot) || Model.Selected.parent is not { } parent) return;
        Change($"Delete {Model.Selected.GetType().Name}", () =>
        {
            Model.Selected.RemoveFromHierarchy();
            Model.Selected = parent;
        });
    }

    public void MoveSelected(int offset)
    {
        if (offset == 0 || Model.Selected.parent is not { } parent) return;
        var current = parent.IndexOf(Model.Selected);
        var next = Math.Clamp(current + offset, 0, parent.Children.Count - 1);
        if (next == current) return;
        Change("Reorder element", () => parent.Insert(next, Model.Selected));
    }

    public void ReparentSelected(VisualElement newParent, int index = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(newParent);
        if (ReferenceEquals(Model.Selected, Model.DocumentRoot) ||
            !Model.DocumentRoot.Contains(newParent) || Model.Selected.Contains(newParent)) return;
        Change("Reparent element", () => newParent.Insert(index, Model.Selected));
    }

    public void SetSelectedClasses(string classes)
    {
        var next = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var current in Model.Selected.classList.ToArray()) Model.Selected.RemoveFromClassList(current);
        foreach (var className in next) Model.Selected.AddToClassList(className);
    }

    public void Undo()
    {
        if (Model.PreviewMode) return;
        if (!Model.TryPopUndo(out var snapshot)) return;
        Model.Restore(snapshot);
        Model.Status = "Undo";
        View.RefreshBuilderTrees();
        Repaint();
    }

    public void Redo()
    {
        if (Model.PreviewMode) return;
        if (!Model.TryPopRedo(out var snapshot)) return;
        Model.Restore(snapshot);
        Model.Status = "Redo";
        View.RefreshBuilderTrees();
        Repaint();
    }

    public void NewDocument()
    {
        if (!ConfirmDiscard()) return;
        SetPreviewMode(false);
        Model.DocumentRoot = UIBuilderWindow.CreateDefaultDocument();
        Model.Selected = Model.DocumentRoot;
        Model.AssetPath = string.Empty;
        Model.Dirty = false;
        Model.Status = "New document";
        Model.ClearHistory();
        Model.SelectedStyleSheetIndex = -1;
        View.RefreshBuilderTrees(expandAll: true);
        Repaint();
    }

    public void OpenDocument()
    {
        if (!ConfirmDiscard()) return;
        var directory = string.IsNullOrWhiteSpace(Model.AssetPath)
            ? Application.dataPath
            : _paths.GetDirectoryName(UIBuilderWindow.ResolveAssetPath(Model.AssetPath)) ?? Application.dataPath;
        var path = EditorUtility.OpenFilePanel("Open UI Document", directory, "bxml");
        if (!string.IsNullOrWhiteSpace(path)) LoadDocument(path, confirmDiscard: false);
    }

    public void LoadDocument(string path) => LoadDocument(path, confirmDiscard: true);

    private void LoadDocument(string path, bool confirmDiscard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (confirmDiscard && !ConfirmDiscard()) return;
        var full = UIBuilderWindow.ResolveAssetPath(path);
        var root = VisualTreeAsset.Load(full).Instantiate();
        SetPreviewMode(false);
        Model.DocumentRoot = root;
        Model.Selected = Model.DocumentRoot;
        Model.AssetPath = UIBuilderWindow.ToProjectPath(full);
        Model.Dirty = false;
        Model.Status = $"Loaded {Model.AssetPath}";
        Model.ClearHistory();
        Model.SelectedStyleSheetIndex = Model.DocumentRoot.styleSheets.Count > 0 ? 0 : -1;
        View.RefreshBuilderTrees(expandAll: true);
        Repaint();
    }

    private bool ConfirmDiscard() => !Model.Dirty || EditorUtility.DisplayDialog(
        "Unsaved UI Document",
        "The current UI document has unsaved changes.",
        "Discard",
        "Cancel");

    public void SaveDocument() => SaveDocumentAs(string.IsNullOrWhiteSpace(Model.AssetPath)
        ? AssetDatabase.GenerateUniqueAssetPath("Assets/UI/New UI.bxml")
        : Model.AssetPath);

    public void SaveDocumentAs()
    {
        var directory = string.IsNullOrWhiteSpace(Model.AssetPath) ? Application.dataPath :
            _paths.GetDirectoryName(UIBuilderWindow.ResolveAssetPath(Model.AssetPath)) ?? Application.dataPath;
        EditorFileDialog.Save("Save UI Document", directory, "UI documents (*.bxml)|*.bxml",
            string.IsNullOrWhiteSpace(Model.AssetPath) ? "New UI.bxml" : _paths.GetFileName(Model.AssetPath),
            path => SaveDocumentAs(path.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase) ? path : path + ".bxml"));
    }

    public void SaveDocumentAs(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = UIBuilderWindow.ResolveAssetPath(path);
        _fileSystem.CreateDirectory(_paths.GetDirectoryName(full)!);
        VisualTreeAsset.Create(Model.DocumentRoot).Save(full);
        Model.AssetPath = UIBuilderWindow.ToProjectPath(full);
        AssetDatabase.ImportAsset(Model.AssetPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        Model.MarkSaved();
        Model.Status = $"Saved {Model.AssetPath}";
        Repaint();
    }

    public void AddExistingStyleSheet()
    {
        var directory = string.IsNullOrWhiteSpace(Model.AssetPath)
            ? Application.dataPath
            : _paths.GetDirectoryName(UIBuilderWindow.ResolveAssetPath(Model.AssetPath)) ?? Application.dataPath;
        EditorFileDialog.Open("Add Style Sheet", directory, "UI styles (*.bss;*.bcss)|*.bss;*.bcss", AttachStyleSheet);
    }

    public void CreateStyleSheet()
    {
        var directory = string.IsNullOrWhiteSpace(Model.AssetPath)
            ? Application.dataPath
            : _paths.GetDirectoryName(UIBuilderWindow.ResolveAssetPath(Model.AssetPath)) ?? Application.dataPath;
        EditorFileDialog.Save("Create Style Sheet", directory, "UI styles (*.bss;*.bcss)|*.bss;*.bcss",
            "New Style Sheet.bss", CreateAndAttachStyleSheet);
    }

    public void AttachStyleSheet(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = UIBuilderWindow.ResolveAssetPath(path);
        if (!IsStyleSheetPath(full))
            throw new InvalidDataException("UI Builder can only attach .bss or .bcss style sheets.");
        if (!_fileSystem.FileExists(full)) throw new FileNotFoundException("Style sheet was not found.", full);
        if (Model.DocumentRoot.styleSheets.Any(sheet => SameStyleSheet(sheet, full)))
        {
            Model.Status = "Style sheet is already attached";
            Repaint();
            return;
        }

        var sheet = StyleSheet.Load(full);
        if (string.IsNullOrWhiteSpace(sheet.name)) sheet.name = _paths.GetFileNameWithoutExtension(full);
        Change("Add style sheet", () =>
        {
            Model.DocumentRoot.styleSheets.Add(sheet);
            Model.DocumentRoot.ApplyStyleSheets();
            Model.SelectedStyleSheetIndex = Model.DocumentRoot.styleSheets.Count - 1;
        });
    }

    public void RemoveSelectedStyleSheet()
    {
        var index = Model.SelectedStyleSheetIndex;
        if (index < 0 || index >= Model.DocumentRoot.styleSheets.Count) return;
        Change("Remove style sheet", () =>
        {
            Model.DocumentRoot.styleSheets.RemoveAt(index);
            Model.DocumentRoot.ApplyStyleSheets();
            Model.SelectedStyleSheetIndex = Math.Min(index, Model.DocumentRoot.styleSheets.Count - 1);
        });
    }

    private void CreateAndAttachStyleSheet(string path)
    {
        var full = IsStyleSheetPath(path)
            ? path
            : path + ".bss";
        _fileSystem.CreateDirectory(_paths.GetDirectoryName(full)!);
        _fileSystem.WriteAllText(full, ".root {\n  flex-direction: column;\n}\n");
        var projectPath = UIBuilderWindow.ToProjectPath(full);
        AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
        AttachStyleSheet(full);
    }

    private bool IsStyleSheetPath(string path) =>
        _paths.GetExtension(path).Equals(".bss", StringComparison.OrdinalIgnoreCase) ||
        _paths.GetExtension(path).Equals(".bcss", StringComparison.OrdinalIgnoreCase);

    private static bool SameStyleSheet(StyleSheet sheet, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(sheet.assetPath)) return false;
        return UIBuilderWindow.ResolveAssetPath(sheet.assetPath)
            .Equals(UIBuilderWindow.ResolveAssetPath(fullPath), StringComparison.OrdinalIgnoreCase);
    }
}
