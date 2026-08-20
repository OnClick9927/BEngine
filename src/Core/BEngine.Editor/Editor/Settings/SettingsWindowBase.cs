using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public abstract class SettingsWindowBase : EditorWindow
{
    private SettingsProvider[] _providers = [];
    private SettingsProvider? _selected;
    private string _requestedPath = string.Empty;
    private string _search = string.Empty;
    private int _registryVersion = -1;
    private Vector2 _navigationScroll;
    private Vector2 _contentScroll;
    private Fix64 _providerContentHeight;
    private bool _activationFailed;

    protected abstract SettingsScope Scope { get; }
    protected abstract string WindowTitle { get; }
    protected abstract string IconPath { get; }

    internal void SelectPath(string path)
    {
        _requestedPath = path ?? string.Empty;
        RefreshProviders(force: true);
    }

    protected override void OnEnable()
    {
        titleContent = new GUIContent(EditorLocalization.Tr(WindowTitle), IconPath, WindowTitle);
        minSize = new Vector2(420, 300);
        if (position.width <= 1 || position.height <= 1)
            position = new Rect(position.x, position.y, 900, 620);
        EditorLocalization.localeChanged += OnLocaleChanged;
        SettingsProviderRegistry.changed += OnRegistryChanged;
        RefreshProviders(force: true);
    }

    protected override void OnDisable()
    {
        EditorLocalization.localeChanged -= OnLocaleChanged;
        SettingsProviderRegistry.changed -= OnRegistryChanged;
        Deactivate(_selected);
        _selected = null;
        _providers = [];
    }

    protected override void OnProjectChange() => RefreshProviders(force: true);

    protected override void OnInspectorUpdate()
    {
        if (_registryVersion != SettingsProviderRegistry.version) RefreshProviders(force: true);
        if (_selected is { } selected)
            EditorFeatureGuard.Invoke($"SettingsProvider {selected.settingsPath}.OnInspectorUpdate",
                selected.OnInspectorUpdate);
    }

    protected override void OnGUI()
    {
        RefreshProviders(force: false);
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight);
        var toolbarHeight = Fix64.Min(
            Fix64.Max(30, EditorGUIUtility.singleLineHeight + 8), height);

        GUI.DrawRect(new Rect(0, 0, width, height), EditorAppearance.palette.Window);
        DrawToolbar(new Rect(0, 0, width, toolbarHeight));
        if (height <= toolbarHeight) return;

        var navigationWidth = CalculateNavigationWidth(width);
        var contentHeight = Fix64.Max(0, height - toolbarHeight);
        var navigation = new Rect(0, toolbarHeight, navigationWidth, contentHeight);
        var dividerX = Fix64.Min(width, navigation.xMax);
        var content = new Rect(Fix64.Min(width, dividerX + 1), toolbarHeight,
            Fix64.Max(0, width - dividerX - 1), contentHeight);

        GUI.DrawRect(navigation, EditorAppearance.palette.Panel);
        if (dividerX < width)
            GUI.DrawRect(new Rect(dividerX, toolbarHeight, 1, contentHeight),
                EditorAppearance.palette.Border);
        GUI.DrawRect(content, EditorAppearance.palette.Window);

        DrawNavigation(navigation);
        DrawProvider(content);
    }

    private void DrawToolbar(Rect rect)
    {
        GUI.DrawRect(rect, EditorAppearance.palette.Toolbar);
        if (rect.height > 0)
            GUI.DrawRect(new Rect(rect.x, Fix64.Max(rect.y, rect.yMax - 1), rect.width, 1),
                EditorAppearance.palette.Border);
        if (rect.width < 24 || rect.height < 12) return;

        var controlHeight = Fix64.Min(EditorGUIUtility.singleLineHeight + 2,
            Fix64.Max(1, rect.height - 6));
        var y = rect.y + Fix64.Max(2, (rect.height - controlHeight) / 2);
        var showIcon = rect.width >= 64;
        var iconWidth = showIcon ? controlHeight : Fix64.Zero;
        if (showIcon)
            GUI.Label(new Rect(7, y, iconWidth, controlHeight),
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Search, "Search"));

        var clearWidth = _search.Length > 0 && rect.width >= 96
            ? controlHeight + 2
            : Fix64.Zero;
        var fieldX = showIcon ? (Fix64)9 + iconWidth : (Fix64)6;
        var fieldWidth = Fix64.Min(320,
            Fix64.Max(0, rect.width - fieldX - clearWidth - 7));
        if (fieldWidth >= 20)
        {
            GUI.SetNextControlName("SettingsSearch");
            var next = GUI.TextField(new Rect(fieldX, y, fieldWidth, controlHeight), _search,
                style: EditorStyles.toolbarSearchField);
            if (!next.Equals(_search, StringComparison.Ordinal))
            {
                _search = next;
                _navigationScroll = Vector2.zero;
            }
        }

        if (clearWidth > 0 && GUI.Button(
                new Rect(rect.width - clearWidth - 4, y, clearWidth, controlHeight),
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Clear, "Clear search"),
                EditorStyles.toolbarIconButton))
        {
            _search = string.Empty;
            _navigationScroll = Vector2.zero;
            GUI.FocusControl("SettingsSearch");
        }
    }

    private void DrawNavigation(Rect rect)
    {
        if (rect.width <= 1 || rect.height <= 1) return;
        var filter = _search.Trim();
        var visible = _providers.Where(provider => Matches(provider, filter)).ToArray();
        var metrics = NavigationMetrics();
        var contentHeight = CalculateNavigationContentHeight(visible, metrics);
        var viewWidth = Fix64.Max(1, rect.width - 12);
        var viewHeight = Fix64.Max(rect.height, contentHeight);

        _navigationScroll = GUI.BeginScrollView(rect, _navigationScroll,
            new Rect(0, 0, viewWidth, viewHeight));
        try
        {
            var y = (Fix64)6;
            string? lastGroup = null;
            foreach (var provider in visible)
            {
                var group = GroupName(provider);
                if (!string.Equals(group, lastGroup, StringComparison.OrdinalIgnoreCase))
                {
                    GUI.Label(new Rect(10, y, Fix64.Max(0, viewWidth - 20), metrics.GroupHeight),
                        group, EditorStyles.miniLabel);
                    y += metrics.GroupHeight + 1;
                    lastGroup = group;
                }

                var row = new Rect(6, y, Fix64.Max(0, viewWidth - 12), metrics.RowHeight);
                var selected = ReferenceEquals(_selected, provider);
                var icon = provider.isPackageProvider
                    ? EditorBuiltinIcons.Toolbar.Settings
                    : IconPath;
                if (GUI.Button(row,
                        new GUIContent(provider.displayName, icon, provider.settingsPath),
                        selected ? EditorStyles.treeViewRowSelected : EditorStyles.treeViewRow))
                    Select(provider);
                y += metrics.RowHeight + 1;
            }

            if (visible.Length == 0)
                GUI.Label(new Rect(12, 12, Fix64.Max(0, viewWidth - 24),
                        Fix64.Max(40, EditorGUIUtility.singleLineHeight * 2)),
                    EditorLocalization.Tr("Search"), EditorStyles.miniLabel);
        }
        finally
        {
            GUI.EndScrollView();
        }
    }

    private void DrawProvider(Rect rect)
    {
        if (rect.width <= 1 || rect.height <= 1) return;
        var selected = _selected;
        if (selected is null)
        {
            GUI.Label(new Rect(rect.x + 12, rect.y + 12,
                    Fix64.Max(0, rect.width - 24), EditorGUIUtility.singleLineHeight),
                EditorLocalization.Tr("Search"), EditorStyles.miniLabel);
            return;
        }

        var titleHeight = Fix64.Max(30, EditorStyles.largeLabel.fixedHeight);
        var packageLineHeight = Fix64.Max(18, EditorGUIUtility.singleLineHeight);
        var headerHeight = titleHeight + 14 +
            (selected.isPackageProvider ? packageLineHeight : Fix64.Zero);
        GUI.Label(new Rect(rect.x + 16, rect.y + 6,
                Fix64.Max(0, rect.width - 32), titleHeight),
            selected.displayName, EditorStyles.largeLabel);
        if (selected.isPackageProvider && rect.height >= titleHeight + packageLineHeight + 8)
            GUI.Label(new Rect(rect.x + 16, rect.y + titleHeight + 5,
                    Fix64.Max(0, rect.width - 32), packageLineHeight),
                $"{EditorLocalization.Tr("Package")}: {selected.sourceAssembly}",
                EditorStyles.miniLabel);

        var footerHeight = selected.footerBarGuiHandler is null || rect.height < headerHeight + 72
            ? Fix64.Zero
            : Fix64.Max(36, EditorGUIUtility.singleLineHeight + 10);
        var bodyBottom = Fix64.Max(rect.y, rect.yMax - footerHeight);
        var body = new Rect(rect.x + 12, Fix64.Min(rect.yMax, rect.y + headerHeight),
            Fix64.Max(0, rect.width - 24),
            Fix64.Max(0, bodyBottom - rect.y - headerHeight - 4));
        DrawProviderBody(selected, body);

        if (footerHeight > 0)
            DrawProviderFooter(selected, new Rect(rect.x + 12, rect.yMax - footerHeight,
                Fix64.Max(0, rect.width - 24), footerHeight));
    }

    private void DrawProviderBody(SettingsProvider selected, Rect body)
    {
        if (body.width <= 1 || body.height <= 1) return;
        var contentWidth = Fix64.Max(1, body.width - 12);
        var viewHeight = Fix64.Max(body.height, _providerContentHeight);
        _contentScroll = GUI.BeginScrollView(body, _contentScroll,
            new Rect(0, 0, contentWidth, viewHeight));

        var measuredHeight = body.height;
        GUILayout.BeginArea(new Rect(0, 0, contentWidth, viewHeight));
        var previousLabelWidth = EditorGUI.labelWidth;
        var previousIndent = EditorGUI.indentLevel;
        var previousWideMode = EditorGUIUtility.wideMode;
        try
        {
            EditorGUI.labelWidth = Fix64.Clamp(
                contentWidth * Fix64.FromDecimal(0.38m), 80, 180);
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.wideMode = contentWidth >= 360;

            var succeeded = !_activationFailed && EditorFeatureGuard.Invoke(
                $"SettingsProvider {selected.settingsPath}.OnGUI",
                () => selected.OnGUI(_search));
            if (!succeeded)
                EditorGUILayout.HelpBox(
                    "This settings page failed. See Console for the full stack trace.",
                    MessageType.Error);
            measuredHeight = Fix64.Max(body.height, GUILayout.CurrentContentHeight + 8);
        }
        finally
        {
            EditorGUI.labelWidth = previousLabelWidth;
            EditorGUI.indentLevel = previousIndent;
            EditorGUIUtility.wideMode = previousWideMode;
            GUILayout.EndArea();
            GUI.EndScrollView();
        }

        _providerContentHeight = measuredHeight;
    }

    private void DrawProviderFooter(SettingsProvider selected, Rect footer)
    {
        if (footer.width <= 1 || footer.height <= 1) return;
        GUI.DrawRect(new Rect(footer.x, footer.y, footer.width, 1),
            EditorAppearance.palette.Border);
        GUILayout.BeginArea(new Rect(footer.x, footer.y + 2, footer.width,
            Fix64.Max(0, footer.height - 2)));
        try
        {
            EditorFeatureGuard.Invoke(
                $"SettingsProvider {selected.settingsPath}.OnFooterBarGUI",
                selected.OnFooterBarGUI);
        }
        finally
        {
            GUILayout.EndArea();
        }
    }

    private void RefreshProviders(bool force)
    {
        if (!force && _registryVersion == SettingsProviderRegistry.version && _providers.Length > 0)
            return;

        var previous = _selected;
        var previousPath = previous?.settingsPath;
        _providers = SettingsProviderRegistry.GetProviders(Scope).ToArray();
        _registryVersion = SettingsProviderRegistry.version;
        var wanted = !string.IsNullOrWhiteSpace(_requestedPath) ? _requestedPath : previousPath;
        var next = _providers.FirstOrDefault(provider => provider.settingsPath.Equals(wanted,
                       StringComparison.OrdinalIgnoreCase)) ?? _providers.FirstOrDefault();
        _requestedPath = string.Empty;
        if (ReferenceEquals(previous, next)) return;

        Deactivate(previous);
        _selected = next;
        ResetProviderView();
        Activate(next);
    }

    private void Select(SettingsProvider provider)
    {
        if (ReferenceEquals(provider, _selected)) return;
        Deactivate(_selected);
        _selected = provider;
        ResetProviderView();
        Activate(provider);
    }

    private void ResetProviderView()
    {
        _contentScroll = Vector2.zero;
        _providerContentHeight = 0;
        _activationFailed = false;
    }

    private static bool Matches(SettingsProvider provider, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return provider.settingsPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               provider.displayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               provider.keywords.Any(keyword => keyword.Contains(search,
                   StringComparison.OrdinalIgnoreCase));
    }

    private static Fix64 CalculateNavigationWidth(Fix64 windowWidth)
    {
        var desired = Fix64.Clamp(windowWidth * Fix64.FromDecimal(0.29m), 150, 250);
        var maximum = Fix64.Max(80, windowWidth - 180);
        return Fix64.Min(windowWidth, Fix64.Min(desired, maximum));
    }

    private static NavigationSize NavigationMetrics()
    {
        var rowHeight = Fix64.Max(EditorGUIUtility.singleLineHeight + 4,
            EditorStyles.toolbarButton.fixedHeight);
        var groupHeight = Fix64.Max(EditorGUIUtility.singleLineHeight,
            EditorStyles.miniLabel.fixedHeight);
        return new NavigationSize(rowHeight, groupHeight);
    }

    private static Fix64 CalculateNavigationContentHeight(
        IReadOnlyList<SettingsProvider> providers,
        NavigationSize metrics)
    {
        var height = (Fix64)12;
        string? lastGroup = null;
        foreach (var provider in providers)
        {
            var group = GroupName(provider);
            if (!string.Equals(group, lastGroup, StringComparison.OrdinalIgnoreCase))
            {
                height += metrics.GroupHeight + 1;
                lastGroup = group;
            }
            height += metrics.RowHeight + 1;
        }
        return height;
    }

    private static string GroupName(SettingsProvider provider)
    {
        var segments = provider.settingsPath.Split('/');
        return segments.Length > 2 ? segments[^2] : provider.isPackageProvider
            ? EditorLocalization.Tr("Package")
            : "BEngine";
    }

    private void OnLocaleChanged()
    {
        titleContent = new GUIContent(EditorLocalization.Tr(WindowTitle), IconPath, WindowTitle);
        SettingsProviderRegistry.Invalidate();
        Repaint();
    }

    private void OnRegistryChanged()
    {
        _registryVersion = -1;
        Repaint();
    }

    private void Activate(SettingsProvider? provider)
    {
        if (provider is null) return;
        _activationFailed = !EditorFeatureGuard.Invoke(
            $"SettingsProvider {provider.settingsPath}.OnActivate",
            () => provider.OnActivate(_search));
    }

    private static void Deactivate(SettingsProvider? provider)
    {
        if (provider is not null)
            EditorFeatureGuard.Invoke($"SettingsProvider {provider.settingsPath}.OnDeactivate",
                provider.OnDeactivate);
    }

    private readonly record struct NavigationSize(Fix64 RowHeight, Fix64 GroupHeight);
}
