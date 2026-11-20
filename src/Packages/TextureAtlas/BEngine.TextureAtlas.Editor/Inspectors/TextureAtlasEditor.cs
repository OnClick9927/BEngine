using System.Collections;
using UnityEditorInternal;

namespace BEngine.Editor;

[CustomEditor(typeof(TextureAtlas))]
public sealed class TextureAtlasEditor : BAssetEditor
{
    private readonly List<Sprite?> _sources = [];
    private ReorderableList? _list;

    protected override void OnEnable()
    {
        base.OnEnable(); Reload();
        _list = new ReorderableList((IList)_sources, typeof(Sprite), true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Sources"),
            drawElementCallback = DrawSource,
            elementHeight = (float)EditorGUIUtility.singleLineHeight + 4,
            onAddCallback = list => { _sources.Add(null); list.index = _sources.Count - 1; Changed(); },
            onChangedCallback = _ => Changed()
        };
    }

    public override void OnInspectorGUI()
    {
        if (DrawDefaultInspector()) hasUnsavedChanges = true;
        GUILayout.Space(6); _list?.DoLayoutList(); DrawApplyBar();
    }
    public override void SaveChanges() { Synchronize(); base.SaveChanges(); }
    public override void DiscardChanges() { base.DiscardChanges(); Reload(); if (_list is not null) _list.list = _sources; }

    private void DrawSource(Rect rect, int index, bool active, bool focused)
    {
        if ((uint)index >= (uint)_sources.Count) return;
        var value = EditorGUI.ObjectField(new Rect(rect.x, rect.y + 1, rect.width, Fix64.Max(1, rect.height - 2)),
            _sources[index], typeof(Sprite), false) as Sprite;
        if (ReferenceEquals(value, _sources[index])) return;
        [global::BEngine.SkipLoopGuard]
        bool Query1613(global::System.Collections.Generic.IEnumerable<global::BEngine.Sprite?> querySource1613)
        {
            ArgumentNullException.ThrowIfNull(querySource1613, "source");
            bool Matches1613(global::BEngine.Sprite? item) => item is not null && TextureAtlas.SourceIdentity(item).Equals(TextureAtlas.SourceIdentity(value), StringComparison.OrdinalIgnoreCase);
            global::System.ReadOnlySpan<global::BEngine.Sprite?> queryValues1613;
            if (querySource1613.GetType() == typeof(global::BEngine.Sprite? []))
                queryValues1613 = (global::BEngine.Sprite? [])querySource1613;
            else if (querySource1613.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Sprite?>))
                queryValues1613 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Sprite?>)querySource1613);
            else
            {
                foreach (var item in querySource1613)
                    if (Matches1613(item))
                        return true;
                return false;
            }
        
            foreach (var item in queryValues1613)
                if (Matches1613(item))
                    return true;
            return false;
        }
        
        if (value is not null && Query1613(_sources.Where((_, candidate) => candidate != index)))
            return;
        _sources[index] = value; Changed();
    }
    private void Reload()
    {
        _sources.Clear(); if (target is TextureAtlas atlas) _sources.AddRange(atlas.Sources);
    }
    private void Changed()
    {
        Synchronize(); hasUnsavedChanges = true; if (target is BAsset asset) EditorUtility.SetDirty(asset);
    }
    private void Synchronize()
    {
        if (target is TextureAtlas atlas) atlas.Sources = _sources.Where(item => item is not null).Cast<Sprite>().ToArray();
    }
}
