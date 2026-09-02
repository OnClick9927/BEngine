namespace BEngine.Editor;

internal static class EditorToolbarDropdowns
{
    internal static GenericMenu CreateUndoHistoryMenu()
    {
        var menu = new GenericMenu();
        var history = Undo.GetHistory(out var cursor);
        if (cursor == 0)
            menu.AddDisabledItem(new GUIContent("Undo/No Undo History"));
        else
            for (var index = cursor - 1; index >= 0; index--)
            {
                var targetCursor = index;
                menu.AddItem(new GUIContent($"Undo/{HistoryLabel(history[index].Name)}"), false,
                    () => Undo.MoveToHistoryCursor(targetCursor));
            }

        menu.AddSeparator(string.Empty);
        if (cursor >= history.Count)
            menu.AddDisabledItem(new GUIContent("Redo/No Redo History"));
        else
            for (var index = cursor; index < history.Count; index++)
            {
                var targetCursor = index + 1;
                menu.AddItem(new GUIContent($"Redo/{HistoryLabel(history[index].Name)}"), false,
                    () => Undo.MoveToHistoryCursor(targetCursor));
            }
        return menu;
    }

    internal static GenericMenu CreateLayoutMenu(
        string activeLayout,
        bool hasLastSession,
        IEnumerable<string> layoutNames,
        Action saveCurrent,
        Action saveAs,
        Action loadLastSession,
        Action<string> switchLayout,
        Action<string> renameLayout,
        Action<string> deleteLayout)
    {
        ArgumentNullException.ThrowIfNull(layoutNames);
        ArgumentNullException.ThrowIfNull(saveCurrent);
        ArgumentNullException.ThrowIfNull(saveAs);
        ArgumentNullException.ThrowIfNull(loadLastSession);
        ArgumentNullException.ThrowIfNull(switchLayout);
        ArgumentNullException.ThrowIfNull(renameLayout);
        ArgumentNullException.ThrowIfNull(deleteLayout);

        var names = layoutNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var editableNames = names.Where(static name => !EditorLayoutStore.IsBuiltInName(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Save Current"), false, saveCurrent.Invoke);
        menu.AddItem(new GUIContent("Save As..."), false, saveAs.Invoke);
        menu.AddSeparator(string.Empty);

        var lastSessionContent = new GUIContent($"Switch/{EditorLayoutStore.LastSessionName}");
        if (hasLastSession)
            menu.AddItem(lastSessionContent,
                activeLayout.Equals(EditorLayoutStore.LastSessionName, StringComparison.OrdinalIgnoreCase),
                loadLastSession.Invoke);
        else menu.AddDisabledItem(lastSessionContent);

        foreach (var name in names)
        {
            var captured = name;
            menu.AddItem(new GUIContent($"Switch/{captured}"),
                activeLayout.Equals(captured, StringComparison.OrdinalIgnoreCase),
                () => switchLayout(captured));
        }

        menu.AddSeparator(string.Empty);
        if (editableNames.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("Rename/No Saved Layouts"));
            menu.AddDisabledItem(new GUIContent("Delete/No Saved Layouts"));
        }
        else
            foreach (var name in editableNames)
            {
                var captured = name;
                menu.AddItem(new GUIContent($"Rename/{captured}"), false, () => renameLayout(captured));
                menu.AddItem(new GUIContent($"Delete/{captured}"), false, () => deleteLayout(captured));
            }
        return menu;
    }

    private static string HistoryLabel(string name)
    {
        var label = string.IsNullOrWhiteSpace(name) ? "Unnamed Action" : name.Trim();
        return label.Replace('/', '-').Replace('\\', '-');
    }
}
