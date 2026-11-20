using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Shared, undoable operations used by both animation authoring windows.</summary>
public static partial class AnimationEditing
{
    public static void Edit(BAsset asset, string label, Action change)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        AssetEditing.Edit(asset, label, change);
    }

    public static void SetKey(AnimationClip clip, AnimationBinding binding, Fix64 time, Fix64 value,
        Fix64 inTangent = default, Fix64 outTangent = default)
    {
        if (!clip.bindings.Contains(binding)) throw new ArgumentException("The binding belongs to another clip.");
        if (binding.kind == AnimationBindingKind.ObjectReference) throw new ArgumentException("Use SetObjectReferenceKey for object tracks.");
        time = Fix64.Max(0, time);
        var key = new Keyframe(time, value, inTangent, outTangent);
        Edit(clip, "Set Animation Key", () =>
        {
            var keys = binding.curve.keys;
            var existing = Array.FindIndex(keys, item => item.time == time);
            if (existing >= 0) binding.curve.MoveKey(existing, key);
            else binding.curve.AddKey(key);
        });
    }

    public static void RenameState(AnimatorController asset, AnimatorState state, string name)
    {
        for (var layer = 0; layer < asset.layerCount; layer++)
            if (asset.GetStatePath(layer, state) is { } path)
            { RenameNode(asset, layer, path, name, value => state.name = value); return; }
        throw new ArgumentException("State does not belong to this controller.");
    }
    public static void RemoveState(AnimatorController asset, AnimatorState state)
    {
        for (var layer = 0; layer < asset.layerCount; layer++)
            if (asset.GetStatePath(layer, state) is { } path)
            { var index = layer; RemoveNode(asset, layer, path, () => asset.GetStates(index, AnimatorController.ParentPath(path)).Remove(state)); return; }
    }

    public static void RenameParameter(AnimatorController asset, AnimatorControllerParameter parameter, string name)
    {
        name = name.Trim();
        [global::BEngine.SkipLoopGuard]
        bool Query2228(global::System.Collections.Generic.IEnumerable<global::BEngine.AnimatorControllerParameter> querySource2228)
        {
            ArgumentNullException.ThrowIfNull(querySource2228, "source");
            bool Matches2228(global::BEngine.AnimatorControllerParameter item) => item != parameter && item.name == name;
            global::System.ReadOnlySpan<global::BEngine.AnimatorControllerParameter> queryValues2228;
            if (querySource2228.GetType() == typeof(global::BEngine.AnimatorControllerParameter[]))
                queryValues2228 = (global::BEngine.AnimatorControllerParameter[])querySource2228;
            else if (querySource2228.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.AnimatorControllerParameter>))
                queryValues2228 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.AnimatorControllerParameter>)querySource2228);
            else
            {
                foreach (var item in querySource2228)
                    if (Matches2228(item))
                        return true;
                return false;
            }
        
            foreach (var item in queryValues2228)
                if (Matches2228(item))
                    return true;
            return false;
        }
        
        if (name.Length == 0 || Query2228(asset.parameters))
            throw new ArgumentException("Parameter names must be nonempty and unique.");
        Edit(asset, "Rename Animator Parameter", () =>
        {
            foreach (var condition in asset.GetAllTransitions().SelectMany(item => item.conditions))
                if (condition.parameter == parameter.name) condition.parameter = name;
            foreach (var state in Enumerable.Range(0, asset.layerCount).SelectMany(asset.GetAllStates))
                foreach (var tree in state.blendTree?.EnumerateTrees() ?? [])
                {
                    if (tree.parameter == parameter.name) tree.parameter = name;
                    if (tree.parameterY == parameter.name) tree.parameterY = name;
                }
            parameter.name = name;
        });
    }

    public static string Save(BAsset asset)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Save Animation Asset");
        var extension = asset is AnimationClip ? ".anim.yaml" : ".controller.yaml";
        var path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(path))
            path = AssetDatabase.GenerateUniqueAssetPath($"{ProjectWindowUtil.GetActiveFolderPath() ?? "Assets"}/{asset.name}{extension}");
        if (!path.Replace('\\', '/').StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Animation assets must be saved inside Assets. Create a project copy to edit a package asset.");
        var paths = EditorServices.GetRequiredService<PathUtility>();
        var fullPath = paths.Combine(EditorApplication.projectPath, path);
        if (asset is AnimationClip clip) clip.Save(fullPath);
        else if (asset is AnimatorController controller) controller.Save(fullPath);
        else throw new ArgumentException("Expected an animation clip or controller.");
        AssetDatabase.ImportAsset(path);
        EditorUtility.ClearDirty(asset);
        return path;
    }
}

internal static class AnimationAuthoringGUI
{
    internal static void BeginScroll(ref Vector2 scroll, Fix64 contentHeight)
    {
        var area = GUILayoutUtility.GetControlRect(Fix64.Max(80, GUIUtility.currentViewHeight - 180));
        scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, Fix64.Max(100, area.width - 16), contentHeight));
        GUILayout.BeginArea(new Rect(4, 4, Fix64.Max(90, area.width - 24), contentHeight));
    }
    internal static void EndScroll() { GUILayout.EndArea(); GUI.EndScrollView(); }

    internal static void Commands(BAsset? asset, Action create, Action save)
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("New", GUILayout.Width(70))) create();
        using (new EditorGUI.DisabledScope(asset is null))
            if (GUILayout.Button("Save", GUILayout.Width(70))) save();
        using (new EditorGUI.DisabledScope(!Undo.canUndo))
            if (GUILayout.Button("Undo", GUILayout.Width(70))) Undo.PerformUndo();
        using (new EditorGUI.DisabledScope(!Undo.canRedo))
            if (GUILayout.Button("Redo", GUILayout.Width(70))) Undo.PerformRedo();
        GUILayout.EndHorizontal();
    }
}
