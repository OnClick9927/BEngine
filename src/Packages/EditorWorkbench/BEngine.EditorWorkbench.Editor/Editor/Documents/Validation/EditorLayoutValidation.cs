using BEngine.Editor.Documents;
namespace BEngine.Editor;
internal static class EditorLayoutValidation
{
    internal static void ValidateEditorLayout(EditorLayoutDocument document)
    {
        if (document.Format != "BEngine.EditorLayout" || document.Version is < 1 or > 2)
            throw new InvalidDataException(
                $"Unsupported editor layout '{document.Format}' v{document.Version}.");
        if (document.WindowWidth < 640 || document.WindowHeight < 480)
            throw new InvalidDataException("The editor window size is too small.");
        if (!float.IsFinite(document.HierarchyWidth) || !float.IsFinite(document.InspectorWidth) ||
            !float.IsFinite(document.BottomHeight) || !float.IsFinite(document.ProjectFoldersWidth) ||
            !float.IsFinite(document.ProjectPackagesHeight) || !float.IsFinite(document.ProjectThumbnailSize))
            throw new InvalidDataException("Editor layout panel sizes must be finite numbers.");

        float[] values =
        [
            document.SceneCameraPositionX, document.SceneCameraPositionY,
            document.SceneCameraRotation, document.SceneCameraSize,
            document.SceneCameraBackgroundR, document.SceneCameraBackgroundG, document.SceneCameraBackgroundB
        ];
        bool __queryResult1286;
        {
            var __querySource1286 = values;
            ArgumentNullException.ThrowIfNull(__querySource1286, "source");
            __queryResult1286 = false;
            foreach (var __queryItem1286 in __querySource1286)
            {
                if ((!float.IsFinite(__queryItem1286)))
                {
                    __queryResult1286 = true;
                    break;
                }
            }
        }
        
        if (__queryResult1286)
            throw new InvalidDataException("Scene camera settings must be finite numbers.");
        if (document.SceneCameraSize <= 0)
            throw new InvalidDataException("Scene camera settings are outside the supported range.");
        if (document.Version < 2) return;
        if (string.IsNullOrWhiteSpace(document.Name) || string.IsNullOrWhiteSpace(document.ActiveLayout))
            throw new InvalidDataException("Editor layout names cannot be empty.");
        var windows = document.Windows ?? throw new InvalidDataException("Editor layout windows are missing.");
        var closedWindows = document.ClosedWindows ??
                            throw new InvalidDataException("Closed editor layout windows are missing.");
        bool __queryResult2084;
        {
            var __querySource2084 = windows.Concat(closedWindows);
            ArgumentNullException.ThrowIfNull(__querySource2084, "source");
            __queryResult2084 = false;
            var __querySource2084Type = __querySource2084.GetType();
            if (__querySource2084Type == typeof(BEngine.Editor.Documents.EditorWindowLayoutDocument[]) || __querySource2084Type == typeof(System.Collections.Generic.List<BEngine.Editor.Documents.EditorWindowLayoutDocument>))
            {
                System.ReadOnlySpan<BEngine.Editor.Documents.EditorWindowLayoutDocument> __querySource2084Span = __querySource2084Type == typeof(BEngine.Editor.Documents.EditorWindowLayoutDocument[]) ? (BEngine.Editor.Documents.EditorWindowLayoutDocument[])(object)__querySource2084 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.Editor.Documents.EditorWindowLayoutDocument>)(object)__querySource2084);
                foreach (var __queryItem2084 in __querySource2084Span)
                {
                    if ((string.IsNullOrWhiteSpace(__queryItem2084.Id) || string.IsNullOrWhiteSpace(__queryItem2084.TypeName) || !Enum.TryParse<EditorWindowState>(__queryItem2084.State, true, out _) || !Enum.TryParse<DockArea>(__queryItem2084.PreferredDockArea, true, out _) || !float.IsFinite(__queryItem2084.X) || !float.IsFinite(__queryItem2084.Y) || !float.IsFinite(__queryItem2084.Width) || !float.IsFinite(__queryItem2084.Height) || !float.IsFinite(__queryItem2084.DockX) || !float.IsFinite(__queryItem2084.DockY) || __queryItem2084.Width < 1 || __queryItem2084.Height < 1))
                    {
                        __queryResult2084 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem2084 in __querySource2084)
                {
                    if ((string.IsNullOrWhiteSpace(__queryItem2084.Id) || string.IsNullOrWhiteSpace(__queryItem2084.TypeName) || !Enum.TryParse<EditorWindowState>(__queryItem2084.State, true, out _) || !Enum.TryParse<DockArea>(__queryItem2084.PreferredDockArea, true, out _) || !float.IsFinite(__queryItem2084.X) || !float.IsFinite(__queryItem2084.Y) || !float.IsFinite(__queryItem2084.Width) || !float.IsFinite(__queryItem2084.Height) || !float.IsFinite(__queryItem2084.DockX) || !float.IsFinite(__queryItem2084.DockY) || __queryItem2084.Width < 1 || __queryItem2084.Height < 1))
                    {
                        __queryResult2084 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult2084)
            throw new InvalidDataException("Editor layout contains an invalid window record.");
        if (((((global::System.Collections.Generic.IEnumerable<string>)(windows.Select(window => window.Id))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems5349, distinctComparer5349) => distinctItems5349.ToHashSet<string>(distinctComparer5349).Count
        }) != windows.Count)
            throw new InvalidDataException("Editor layout contains duplicate window IDs.");
        if (((((global::System.Collections.Generic.IEnumerable<string>)(closedWindows.Select(window => window.Id))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems5548, distinctComparer5548) => distinctItems5548.ToHashSet<string>(distinctComparer5548).Count
        }) !=
            closedWindows.Count || windows.Select(window => window.Id).Intersect(
                closedWindows.Select(window => window.Id), StringComparer.Ordinal).Any())
            throw new InvalidDataException("Editor layout contains duplicate closed window IDs.");
        if (document.FocusedWindowId is not null && !Query5970())
            throw new InvalidDataException("The focused layout window has no window record.");
        bool Query5970()
        {
            var querySource5970 = windows;
            ArgumentNullException.ThrowIfNull(querySource5970, "source");
            if (querySource5970.GetType() == typeof(global::BEngine.Editor.Documents.EditorWindowLayoutDocument[]) || querySource5970.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.Documents.EditorWindowLayoutDocument>))
            {
                global::System.ReadOnlySpan<global::BEngine.Editor.Documents.EditorWindowLayoutDocument> queryValues5970 = querySource5970.GetType() == typeof(global::BEngine.Editor.Documents.EditorWindowLayoutDocument[]) ? (global::BEngine.Editor.Documents.EditorWindowLayoutDocument[])(object)querySource5970 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.Documents.EditorWindowLayoutDocument>)(object)querySource5970);
                foreach (var window in queryValues5970)
                {
                    if (window.Id == document.FocusedWindowId)
                        return true;
                }
            }
            else
            {
                foreach (var window in querySource5970)
                {
                    if (window.Id == document.FocusedWindowId)
                        return true;
                }
            }
        
            return false;
        }
        ValidateDockNode(document.DockRoot, BuildIndex7527(windows), new HashSet<string>(StringComparer.Ordinal));
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.HashSet<string> BuildIndex7527(global::System.Collections.Generic.List<global::BEngine.Editor.Documents.EditorWindowLayoutDocument> indexSource7527)
        {
            ArgumentNullException.ThrowIfNull(indexSource7527, "source");
            var indexComparer7527 = StringComparer.Ordinal;
            var result = new global::System.Collections.Generic.HashSet<string>(indexComparer7527);
            foreach (var window in indexSource7527)
                result.Add(window.Id);
            return result;
        }
    }

    private static void ValidateDockNode(EditorDockNodeDocument? node, IReadOnlySet<string> windows,
        ISet<string> dockedPanels)
    {
        if (node is null) return;
        if (node.Type.Equals("Group", StringComparison.OrdinalIgnoreCase))
        {
            if (node.First is not null || node.Second is not null)
                throw new InvalidDataException("A dock group cannot contain child nodes.");
            foreach (var panel in node.Panels)
            {
                if (!windows.Contains(panel))
                    throw new InvalidDataException($"Dock panel '{panel}' has no window record.");
                if (!dockedPanels.Add(panel))
                    throw new InvalidDataException($"Dock panel '{panel}' appears more than once.");
            }
            if (node.SelectedId is not null && !node.Panels.Contains(node.SelectedId, StringComparer.Ordinal))
                throw new InvalidDataException("The selected dock panel is not part of its group.");
            return;
        }
        if (!node.Type.Equals("Split", StringComparison.OrdinalIgnoreCase) ||
            !float.IsFinite(node.Ratio) || node.Ratio is <= 0.05f or >= 0.95f ||
            node.First is null || node.Second is null || node.Panels.Count > 0)
            throw new InvalidDataException("Editor layout contains an invalid dock split.");
        ValidateDockNode(node.First, windows, dockedPanels);
        ValidateDockNode(node.Second, windows, dockedPanels);
    }
}
