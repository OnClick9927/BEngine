using BEngine.Platform;

namespace BEngine.Editor;

public sealed class TextureAtlasWindowController :
    EditorWindowController<TextureAtlasWindow, TextureAtlasWindowModel>
{
    public TextureAtlasWindowController(
        EditorWindowMvcContext context,
        TextureAtlasWindowModel model) : base(context, model) { }

    public void HandleSelection(BObject? selectedObject)
    {
        var selectedAssetLocation = AssetDatabase.GetAssetPath(selectedObject);
        if (selectedAssetLocation.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase) &&
            !selectedAssetLocation.Equals(Model.AssetPath, StringComparison.OrdinalIgnoreCase))
        {
            Model.AssetPath = selectedAssetLocation;
            LoadAtlas();
        }
        else if (selectedObject is Sprite sprite)
        {
            Model.PendingSource = sprite;
        }
    }

    public void SetAssetPath(string path) => Model.AssetPath = path ?? string.Empty;

    public void SetPendingSource(Sprite? sprite) => Model.PendingSource = sprite;

    public void SetPacking(int maxSize, int padding, int extrude)
    {
        maxSize = Math.Clamp(maxSize, 32, 16384);
        padding = Math.Clamp(padding, 0, 64);
        extrude = Math.Clamp(extrude, 0, padding);
        if (maxSize == Model.Atlas.MaxSize && padding == Model.Atlas.Padding &&
            extrude == Model.Atlas.Extrude) return;
        Model.Atlas.MaxSize = maxSize;
        Model.Atlas.Padding = padding;
        Model.Atlas.Extrude = extrude;
        Model.IsDirty = true;
    }

    public void AddPendingSource()
    {
        if (Model.PendingSource is not { } sprite) return;
        AddSource(sprite);
        Model.PendingSource = null;
    }

    public void AddSelected()
    {
        var sprites = Selection.objects.OfType<Sprite>().ToList();
        foreach (var texture in Selection.objects.OfType<Texture>())
            if (AssetDatabase.LoadAssetAtPath<Sprite>(texture.assetPath) is { } sprite)
                sprites.Add(sprite);
        foreach (var sprite in sprites) AddSource(sprite);
        Repaint();
    }

    public void AddSource(Sprite sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        if (ContainsSource(sprite)) return;
        Model.Atlas.Sources = [.. Model.Atlas.Sources, sprite];
        Model.IsDirty = true;
    }

    public void ReplaceSource(int index, Sprite sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        if ((uint)index >= (uint)Model.Atlas.Sources.Length || ContainsSource(sprite, index)) return;
        Model.Atlas.Sources[index] = sprite;
        Model.IsDirty = true;
    }

    public void RemoveSource(int index)
    {
        if ((uint)index >= (uint)Model.Atlas.Sources.Length) return;
        Model.Atlas.Sources = Model.Atlas.Sources.Where((_, current) => current != index).ToArray();
        if (Model.Atlas.Sources.Length == 0) Model.Atlas.Sprites.Clear();
        Model.IsDirty = true;
    }

    public void NewAtlas()
    {
        Model.Atlas = new TextureAtlas();
        Model.AssetPath = string.Empty;
        Model.PendingSource = null;
        Model.IsDirty = true;
        Model.Message = "New atlas";
        Repaint();
    }

    public void LoadAtlas()
    {
        if (string.IsNullOrWhiteSpace(Model.AssetPath)) return;
        try
        {
            Model.Atlas = TextureAtlas.Load(ResolvePath(Model.AssetPath));
            Model.IsDirty = false;
            Model.Message = "Atlas loaded";
        }
        catch (Exception exception)
        {
            Model.Message = exception.Message;
        }
        Repaint();
    }

    public void SaveAtlas()
    {
        try
        {
            EditorAssetWritePolicy.EnsureCanWrite("Saving texture atlases");
            EnsureAssetPath();
            Model.Atlas.Save(ResolvePath(Model.AssetPath));
            AssetDatabase.ImportAsset(Model.AssetPath, ImportAssetOptions.ForceUpdate);
            Model.IsDirty = false;
            Model.Message = "Atlas saved";
        }
        catch (Exception exception)
        {
            Model.Message = exception.Message;
        }
        Repaint();
    }

    public void BuildAtlas()
    {
        try
        {
            EditorAssetWritePolicy.EnsureCanWrite("Building texture atlases");
            EnsureAssetPath();
            var result = TextureAtlasBuilder.Build(Model.Atlas, ResolvePath(Model.AssetPath));
            Model.AssetPath = result.AtlasAssetPath;
            Model.IsDirty = false;
            Model.Message = $"Built {result.SpriteCount} sprites at {result.Width} x {result.Height}";
        }
        catch (Exception exception)
        {
            Model.Message = exception.Message;
        }
        Repaint();
    }

    public static string ResolvePreviewPath(string path)
    {
        if (path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
            BAsset.Load<Texture>(path) is { } texture)
        {
            var importedPath = string.IsNullOrWhiteSpace(texture.artifactPath)
                ? texture.sourcePath
                : texture.artifactPath;
            if (FileSystem.FileExists(importedPath)) return importedPath;
        }
        return Paths.IsPathRooted(path)
            ? path
            : Paths.Combine(EditorApplication.projectPath,
                path.Replace('/', Paths.DirectorySeparatorChar));
    }

    private bool ContainsSource(Sprite sprite, int ignoredIndex = -1)
    {
        var identity = TextureAtlas.SourceIdentity(sprite);
        return Model.Atlas.Sources.Where((_, index) => index != ignoredIndex)
            .Any(source => TextureAtlas.SourceIdentity(source)
                .Equals(identity, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureAssetPath()
    {
        if (!string.IsNullOrWhiteSpace(Model.AssetPath)) return;
        var folder = ProjectWindowUtil.GetActiveFolderPath() ?? "Assets";
        Model.AssetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{folder.TrimEnd('/', '\\')}/New Texture Atlas.atlas.yaml");
    }

    private static string ResolvePath(string path) => Paths.IsPathRooted(path)
        ? Paths.GetFullPath(path)
        : AssetDatabase.ResolveAssetPath(path);

    private static FileUtility FileSystem =>
        EditorServiceAccess.GetRequiredService<FileUtility>();

    private static PathUtility Paths =>
        EditorServiceAccess.GetRequiredService<PathUtility>();
}
