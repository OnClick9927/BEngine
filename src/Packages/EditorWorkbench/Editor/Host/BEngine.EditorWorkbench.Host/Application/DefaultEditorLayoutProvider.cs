using BEngine.Editor.Documents;

namespace BEngine.Workbench;

/// <summary>The default quick-development workbench, separate from layout storage.</summary>
public sealed class DefaultEditorLayoutProvider : IEditorLayoutDefaults
{
    public IReadOnlyList<string> Names { get; } = Array.AsReadOnly<string>(["Default", "2 by 3", "Tall", "Wide"]);

    public EditorLayoutDocument CreateCurrent() => new()
    {
        Name = EditorLayoutStore.LastSessionName,
        ActiveLayout = EditorLayoutStore.LastSessionName,
        WindowWidth = 1500,
        WindowHeight = 920,
        HierarchyWidth = 250,
        InspectorWidth = 340,
        BottomHeight = 220,
        ProjectFoldersWidth = 240,
        ProjectThumbnailSize = 72,
        ShowHierarchy = true,
        ShowInspector = true,
        ShowSceneView = true,
        ShowGameView = true,
        ShowProject = true,
        ShowConsole = true
    };

    public EditorLayoutDocument CreatePreset(string requestedName)
    {
        [global::BEngine.SkipLoopGuard]
        string Query1036(global::System.Collections.Generic.IEnumerable<string> querySource1036)
        {
            ArgumentNullException.ThrowIfNull(querySource1036, "source");
            bool Matches1036(string candidate) => candidate.Equals(requestedName, StringComparison.OrdinalIgnoreCase);
            global::System.ReadOnlySpan<string> queryValues1036;
            if (querySource1036.GetType() == typeof(string[]))
                queryValues1036 = (string[])querySource1036;
            else if (querySource1036.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues1036 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource1036);
            else
            {
                foreach (var candidate in querySource1036)
                    if (Matches1036(candidate))
                        return candidate;
                return global::System.Linq.Enumerable.First(global::System.Array.Empty<string>(), static _ => true);
            }
        
            foreach (var candidate in queryValues1036)
                if (Matches1036(candidate))
                    return candidate;
            return global::System.Linq.Enumerable.First(global::System.Array.Empty<string>(), static _ => true);
        }
        
        var name = Query1036(Names);
        var document = CreateCurrent();
        document.Name = name;
        document.ActiveLayout = name;
        document.Windows = StandardWindows();
        document.DockRoot = name switch
        {
            "2 by 3" => Split(true, 0.22f,
                Split(false, 0.5f, Group("Hierarchy"), Group("Project")),
                Split(true, 0.70f,
                    Split(false, 0.5f, Group("Scene"), Group("Game")),
                    Split(false, 0.5f, Group("Inspector"), Group("Console")))),
            "Tall" => Split(true, 0.18f,
                Group("Hierarchy"),
                Split(true, 0.78f,
                    Split(false, 0.72f, Group("Scene"), Group("Game", "Project", "Console")),
                    Group("Inspector"))),
            "Wide" => Split(false, 0.72f,
                Split(true, 0.18f, Group("Hierarchy"),
                    Split(true, 0.78f, Group("Scene", "Game"), Group("Inspector"))),
                Group("Project", "Console")),
            _ => Split(true, 0.18f,
                Group("Hierarchy"),
                Split(true, 0.78f,
                    Split(false, 0.70f, Group("Scene", "Game"), Group("Project", "Console")),
                    Group("Inspector")))
        };
        return document;
    }

    private static List<EditorWindowLayoutDocument> StandardWindows() =>
    [
        Window("Hierarchy", DockArea.Left),
        Window("Scene", DockArea.Center),
        Window("Game", DockArea.Center),
        Window("Inspector", DockArea.Right),
        Window("Project", DockArea.Bottom),
        Window("Console", DockArea.Bottom)
    ];

    private static EditorWindowLayoutDocument Window(string id, DockArea area) => new()
    {
        Id = id,
        State = nameof(EditorWindowState.Normal),
        Docked = true,
        PreferredDockArea = area.ToString()
    };

    private static EditorDockNodeDocument Group(params string[] panels) => new()
    {
        Type = "Group",
        Panels = [.. panels],
        SelectedId = panels.FirstOrDefault()
    };

    private static EditorDockNodeDocument Split(bool sideBySide, float ratio,
        EditorDockNodeDocument first, EditorDockNodeDocument second) => new()
    {
        Type = "Split",
        SideBySide = sideBySide,
        Ratio = ratio,
        First = first,
        Second = second
    };

}
