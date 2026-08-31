using System.Collections;
using UnityEditorInternal;

namespace BEngine.Editor;

[CustomEditor(typeof(TextureAtlas))]
public sealed class TextureAtlasEditor : BAssetEditor
{
    private readonly List<AtlasSpriteEntry> _sprites = [];
    private ReorderableList? _spriteList;

    protected override void OnEnable()
    {
        base.OnEnable();
        ReloadReferences();
        _spriteList = new ReorderableList((IList)_sprites, typeof(AtlasSpriteEntry),
            draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Sprites"),
            drawElementCallback = DrawSprite,
            elementHeight = (float)EditorGUIUtility.singleLineHeight + 4,
            onAddCallback = list =>
            {
                _sprites.Add(new AtlasSpriteEntry());
                list.index = _sprites.Count - 1;
                MarkChanged();
            },
            onChangedCallback = _ => MarkChanged()
        };
    }

    public override void OnInspectorGUI()
    {
        if (DrawDefaultInspector()) hasUnsavedChanges = true;
        GUILayout.Space(6);
        _spriteList?.DoLayoutList();
        DrawApplyBar();
    }

    public override void SaveChanges()
    {
        SynchronizeReferences();
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        base.DiscardChanges();
        ReloadReferences();
        _spriteList!.list = _sprites;
    }

    private void DrawSprite(Rect rect, int index, bool isActive, bool isFocused)
    {
        if ((uint)index >= (uint)_sprites.Count) return;
        var entry = _sprites[index];
        var current = EditorGUI.ObjectField(
            new Rect(rect.x, rect.y + 1, rect.width, Fix64.Max(1, rect.height - 2)),
            entry.Sprite, typeof(Sprite), allowSceneObjects: false) as Sprite;
        if (ReferenceEquals(current, entry.Sprite)) return;
        var reference = current is not null &&
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(current, out var guid, out _)
            ? guid
            : string.Empty;
        if (reference.Length > 0 && _sprites.Where((_, candidate) => candidate != index)
                .Any(candidate => candidate.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase)))
            return;
        entry.Sprite = current;
        entry.Reference = reference;
        MarkChanged();
    }

    private void ReloadReferences()
    {
        _sprites.Clear();
        if (target is not TextureAtlas atlas) return;
        foreach (var reference in atlas.SpriteReferences)
        {
            var path = Guid.TryParse(reference, out _)
                ? AssetDatabase.GUIDToAssetPath(reference)
                : reference;
            var sprite = path.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var migratedReference = sprite is not null &&
                                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var guid, out _)
                ? guid
                : reference;
            _sprites.Add(new AtlasSpriteEntry
            {
                Reference = migratedReference,
                Sprite = sprite
            });
        }
    }

    private void MarkChanged()
    {
        SynchronizeReferences();
        hasUnsavedChanges = true;
        if (target is BAsset asset) EditorUtility.SetDirty(asset);
    }

    private void SynchronizeReferences()
    {
        if (target is not TextureAtlas atlas) return;
        atlas.SpriteReferences = _sprites.Where(entry => entry.Reference.Length > 0)
            .Select(entry => entry.Reference).ToList();
        atlas.Version = atlas.SpriteReferences.All(reference => Guid.TryParse(reference, out _)) ? 3 : 2;
    }

    private sealed class AtlasSpriteEntry
    {
        internal string Reference { get; set; } = string.Empty;
        internal Sprite? Sprite { get; set; }
    }
}
