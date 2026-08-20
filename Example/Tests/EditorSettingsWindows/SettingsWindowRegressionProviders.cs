using BEngine.Editor;

namespace BEngine.ExampleTests.EditorSettingsWindows;

internal static class SettingsWindowRegressionState
{
    internal const string UserOverviewPath = "Preferences/Packages/Render Regression/Overview";
    internal const string UserSecondaryPath = "Preferences/Packages/Render Regression/Overview Secondary";
    internal const string UserFaultPath = "Preferences/Packages/Render Regression/Fault";
    internal const string ProjectOverviewPath = "Project/Packages/Render Regression/Overview";
    internal const string ProjectSecondaryPath = "Project/Packages/Render Regression/Secondary";
    internal const string ProjectFaultPath = "Project/Packages/Render Regression/Fault";
    internal const string UserBodyMarker = "USER_SETTINGS_BODY";
    internal const string UserSecondaryMarker = "USER_SETTINGS_SECONDARY_BODY";
    internal const string UserFooterMarker = "USER_SETTINGS_FIXED_FOOTER";
    internal const string ProjectBodyMarker = "PROJECT_SETTINGS_BODY";
    internal const string ProjectSecondaryMarker = "PROJECT_SETTINGS_SECONDARY_BODY";
    internal const string FaultPartialMarker = "FAULTING_PROVIDER_PARTIAL_CONTENT";
    internal const string UserRowPrefix = "USER_SETTINGS_LONG_ROW_";
    internal const string ProjectRowPrefix = "PROJECT_SETTINGS_LONG_ROW_";

    internal static readonly List<string> Activations = [];
    internal static readonly List<string> Deactivations = [];

    internal static void Reset()
    {
        Activations.Clear();
        Deactivations.Clear();
    }

    internal static SettingsProvider CreatePage(
        string path,
        SettingsScope scope,
        string label,
        string marker,
        string? rowPrefix = null,
        string? footerMarker = null,
        IEnumerable<string>? keywords = null)
    {
        return new SettingsProvider(path, scope, keywords)
        {
            label = label,
            activateHandler = _ => Activations.Add(path),
            deactivateHandler = () => Deactivations.Add(path),
            guiHandler = _ =>
            {
                GUILayout.Label(marker);
                if (rowPrefix is null) return;
                for (var index = 0; index < 48; index++)
                    GUILayout.Label($"{rowPrefix}{index:00}");
            },
            footerBarGuiHandler = footerMarker is null ? null : () => GUILayout.Label(footerMarker)
        };
    }
}

internal static class SettingsWindowRegressionProviders
{
    [SettingsProvider]
    private static SettingsProvider UserOverview() => SettingsWindowRegressionState.CreatePage(
        SettingsWindowRegressionState.UserOverviewPath,
        SettingsScope.User,
        "User Overview",
        SettingsWindowRegressionState.UserBodyMarker,
        SettingsWindowRegressionState.UserRowPrefix,
        SettingsWindowRegressionState.UserFooterMarker,
        ["overview", "render-user-overview"]);

    [SettingsProvider]
    private static SettingsProvider UserSecondary() => SettingsWindowRegressionState.CreatePage(
        SettingsWindowRegressionState.UserSecondaryPath,
        SettingsScope.User,
        "User Secondary",
        SettingsWindowRegressionState.UserSecondaryMarker);

    [SettingsProvider]
    private static SettingsProvider UserFault() => new FaultingSettingsProvider(
        SettingsWindowRegressionState.UserFaultPath, SettingsScope.User, "User Fault");

    [SettingsProvider]
    private static SettingsProvider ProjectOverview() => SettingsWindowRegressionState.CreatePage(
        SettingsWindowRegressionState.ProjectOverviewPath,
        SettingsScope.Project,
        "Project Overview",
        SettingsWindowRegressionState.ProjectBodyMarker,
        SettingsWindowRegressionState.ProjectRowPrefix);

    [SettingsProvider]
    private static SettingsProvider ProjectSecondary() => SettingsWindowRegressionState.CreatePage(
        SettingsWindowRegressionState.ProjectSecondaryPath,
        SettingsScope.Project,
        "Project Secondary",
        SettingsWindowRegressionState.ProjectSecondaryMarker);

    [SettingsProvider]
    private static SettingsProvider ProjectFault() => new FaultingSettingsProvider(
        SettingsWindowRegressionState.ProjectFaultPath, SettingsScope.Project, "Project Fault");

    [SettingsProvider] private static SettingsProvider UserScroll01() => Filler(1);
    [SettingsProvider] private static SettingsProvider UserScroll02() => Filler(2);
    [SettingsProvider] private static SettingsProvider UserScroll03() => Filler(3);
    [SettingsProvider] private static SettingsProvider UserScroll04() => Filler(4);
    [SettingsProvider] private static SettingsProvider UserScroll05() => Filler(5);
    [SettingsProvider] private static SettingsProvider UserScroll06() => Filler(6);
    [SettingsProvider] private static SettingsProvider UserScroll07() => Filler(7);
    [SettingsProvider] private static SettingsProvider UserScroll08() => Filler(8);
    [SettingsProvider] private static SettingsProvider UserScroll09() => Filler(9);
    [SettingsProvider] private static SettingsProvider UserScroll10() => Filler(10);
    [SettingsProvider] private static SettingsProvider UserScroll11() => Filler(11);
    [SettingsProvider] private static SettingsProvider UserScroll12() => Filler(12);
    [SettingsProvider] private static SettingsProvider UserScroll13() => Filler(13);
    [SettingsProvider] private static SettingsProvider UserScroll14() => Filler(14);
    [SettingsProvider] private static SettingsProvider UserScroll15() => Filler(15);
    [SettingsProvider] private static SettingsProvider UserScroll16() => Filler(16);
    [SettingsProvider] private static SettingsProvider UserScroll17() => Filler(17);
    [SettingsProvider] private static SettingsProvider UserScroll18() => Filler(18);
    [SettingsProvider] private static SettingsProvider UserScroll19() => Filler(19);
    [SettingsProvider] private static SettingsProvider UserScroll20() => Filler(20);
    [SettingsProvider] private static SettingsProvider UserScroll21() => Filler(21);
    [SettingsProvider] private static SettingsProvider UserScroll22() => Filler(22);
    [SettingsProvider] private static SettingsProvider UserScroll23() => Filler(23);
    [SettingsProvider] private static SettingsProvider UserScroll24() => Filler(24);

    private static SettingsProvider Filler(int index) => SettingsWindowRegressionState.CreatePage(
        $"Preferences/Packages/Render Regression/Scroll {index:00}",
        SettingsScope.User,
        $"User Scroll {index:00}",
        $"USER_SCROLL_BODY_{index:00}",
        keywords: [$"needle{index:00}"]);

    private sealed class FaultingSettingsProvider : SettingsProvider
    {
        internal FaultingSettingsProvider(string path, SettingsScope scope, string displayLabel)
            : base(path, scope) => label = displayLabel;

        public override void OnActivate(string searchContext) =>
            SettingsWindowRegressionState.Activations.Add(settingsPath);

        public override void OnDeactivate() =>
            SettingsWindowRegressionState.Deactivations.Add(settingsPath);

        public override void OnGUI(string searchContext)
        {
            GUILayout.Label(SettingsWindowRegressionState.FaultPartialMarker);
            GUI.BeginGroup(new Rect(2, 2, 40, 40));
            GUILayout.BeginHorizontal();
            throw new InvalidOperationException("Intentional settings provider render failure.");
        }
    }
}
