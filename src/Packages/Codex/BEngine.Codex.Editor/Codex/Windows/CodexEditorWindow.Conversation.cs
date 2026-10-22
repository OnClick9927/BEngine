namespace BEngine.Editor.Codex;

public sealed partial class CodexEditorWindow
{
    private readonly Dictionary<string, MessageLayout> _messageLayouts = [];
    private string _layoutThread = string.Empty;
    private bool _compactHistory;
    private static Color Surface => EditorGUIUtility.isProSkin ? C(0.12f, 0.12f, 0.12f) : C(0.98f, 0.98f, 0.98f);
    private static Color Sidebar => EditorGUIUtility.isProSkin ? C(0.09f, 0.09f, 0.09f) : C(0.93f, 0.93f, 0.93f);
    private static Color Subtle => EditorGUIUtility.isProSkin ? C(0.67f, 0.68f, 0.70f) : C(0.38f, 0.39f, 0.41f);
    private static Color Text => EditorGUIUtility.isProSkin ? C(0.92f, 0.93f, 0.94f) : C(0.12f, 0.13f, 0.14f);
    private static Color Border => EditorGUIUtility.isProSkin ? C(0.26f, 0.27f, 0.29f) : C(0.78f, 0.79f, 0.80f);
    private static Color Accent => C(0.32f, 0.76f, 0.64f);
    private static Color C(float r, float g, float b) => new((Fix64)r, (Fix64)g, (Fix64)b, 1);

    private void DrawWorkspace()
    {
        var width = GUIUtility.currentViewWidth;
        var height = GUIUtility.currentViewHeight;
        GUI.DrawRect(new Rect(0, 0, width, height), Surface);
        if (_client is null) { GUI.Label(new Rect(20, 20, width - 40, 30), "Codex unavailable"); return; }
        var snapshot = _client.GetSnapshot();
        HandleShortcuts(snapshot);
        var sidebarWidth = model.ShowSidebar && width >= 720 ? (Fix64)224 : Fix64.Zero;
        if (sidebarWidth > 0) DrawHistory(new Rect(0, 0, sidebarWidth, height), snapshot);
        var area = new Rect(sidebarWidth, 0, width - sidebarWidth, height);
        DrawHeader(area, snapshot);
        if (model.RenameThreadId.Length > 0) { DrawRenameConversation(new Rect(area.x + 18, 62, area.width - 36, height - 80)); return; }

        var inset = Fix64.Clamp((area.width - 800) / 2, 18, 100);
        var content = new Rect(area.x + inset, 58, area.width - inset * 2, height - 66);
        var composerHeight = _contextPaths.Count == 0 ? (Fix64)152 : 194;
        var composer = new Rect(content.x, height - composerHeight - 12, content.width, composerHeight);
        var requestHeight = snapshot.Approvals.Count + snapshot.Questions.Count > 0
            ? Fix64.Min(230, Fix64.Max(110, height / 3)) : Fix64.Zero;
        var conversation = new Rect(content.x, content.y, content.width,
            Fix64.Max(32, composer.y - content.y - requestHeight - 8));
        DrawConversation(conversation, snapshot);
        if (requestHeight > 0)
            DrawRequests(new Rect(content.x, composer.y - requestHeight - 4, content.width, requestHeight), snapshot);
        DrawComposer(composer, snapshot);
        if (sidebarWidth == 0 && _compactHistory && width < 720)
        {
            GUI.DrawRect(new Rect(0, 50, width, height - 50), Surface);
            DrawHistory(new Rect(0, 50, width, height - 50), snapshot);
        }
    }

    private void DrawHeader(Rect area, CodexClientSnapshot snapshot)
    {
        var x = area.x + 12;
        if (Icon(new Rect(x, 10, 30, 30), EditorBuiltinIcons.Toolbar.Layout, "Toggle conversation list"))
        {
            if (GUIUtility.currentViewWidth < 720) _compactHistory = !_compactHistory;
            else model.ShowSidebar = !model.ShowSidebar;
        }
        var hasConversation = !string.IsNullOrWhiteSpace(snapshot.ThreadId);
        if (Icon(new Rect(x + 38, 10, 30, 30), "Icons/Windows/Codex.png",
                hasConversation ? "Open this conversation in Codex" : "Open current project in Codex"))
            Application.OpenURL(hasConversation
                ? "codex://threads/" + Uri.EscapeDataString(snapshot.ThreadId)
                : "codex://threads/new?path=" + Uri.EscapeDataString(_client!.ProjectRoot));
        var newWidth = GUIUtility.currentViewWidth < 720 ? (Fix64)158 : 30;
        var titleWidth = area.width - 146 - (newWidth - 30);
        var title = string.IsNullOrWhiteSpace(snapshot.ThreadTitle) ? "New conversation" : snapshot.ThreadTitle;
        GUI.Label(new Rect(x + 74, 8, titleWidth, 24), CodexMessageLayout.Ellipsis(title, titleWidth - 4, 16), Style(16, Text));
        GUI.Label(new Rect(x + 74, 31, titleWidth, 18),
            new GUIContent(CodexMessageLayout.Ellipsis(snapshot.StatusText + "  /  " + _client!.ProjectRoot, titleWidth - 4, 11), "", _client.ProjectRoot), Style(11, Subtle));
        using (new EditorGUI.DisabledScope(model.IsOperationRunning))
            if (GUI.Button(new Rect(area.xMax - 12 - newWidth, 10, newWidth, 30),
                    new GUIContent(newWidth > 30 ? "New conversation" : string.Empty, EditorBuiltinIcons.Toolbar.New,
                        NewConversationTooltip(snapshot)))) NewConversation();
        GUI.DrawRect(new Rect(area.x, 51, area.width, 1), Border);
    }

    private void DrawHistory(Rect rect, CodexClientSnapshot snapshot)
    {
        using var controls = new ControlScope(windowId, "history");
        GUI.DrawRect(rect, Sidebar);
        GUI.Label(new Rect(rect.x + 16, rect.y + 12, rect.width - 32, 28), "Codex", Style(20, Text));
        using (new EditorGUI.DisabledScope(model.IsOperationRunning))
            if (GUI.Button(new Rect(rect.x + 12, rect.y + 80, rect.width - 24, 30),
                    new GUIContent("New conversation", EditorBuiltinIcons.Toolbar.New, NewConversationTooltip(snapshot)))) NewConversation();
        GUI.Label(new Rect(rect.x + 16, rect.y + 47, rect.width - 32, 22),
            new GUIContent(CodexMessageLayout.Ellipsis(controller.GetFileName(_client!.ProjectRoot), rect.width - 36), "", _client.ProjectRoot), Style(13, Subtle));
        GUI.SetNextControlName("CodexHistorySearch");
        model.HistorySearch = GUI.TextField(new Rect(rect.x + 12, rect.y + 120, rect.width - 24, 30), model.HistorySearch);
        if (model.HistorySearch.Length == 0 && GUI.GetNameOfFocusedControl() != "CodexHistorySearch")
            GUI.Label(new Rect(rect.x + 18, rect.y + 121, rect.width - 40, 28), "Search conversations", Style(12, Subtle));
        using (new EditorGUI.DisabledScope(model.IsOperationRunning))
        {
            if (Icon(new Rect(rect.xMax - 42, rect.y + 158, 28, 28), EditorBuiltinIcons.Toolbar.Refresh, "Refresh conversations"))
                controller.RefreshConversations(false);
        }
        var threads = snapshot.Threads.Where(item => item.Title.Contains(model.HistorySearch, StringComparison.OrdinalIgnoreCase)).ToArray();
        var viewport = new Rect(rect.x + 8, rect.y + 198, rect.width - 16, Fix64.Max(30, rect.height - 243));
        var rowHeight = (Fix64)68;
        model.HistoryScroll = GUI.BeginScrollView(viewport, model.HistoryScroll,
            new Rect(0, 0, viewport.width, Fix64.Max(viewport.height, threads.Length * rowHeight + 44)), ScrollStyle(Sidebar));
        for (var i = 0; i < threads.Length; i++)
        {
            var thread = threads[i];
            var row = new Rect(2, i * rowHeight, viewport.width - 14, 62);
            using (new EditorGUI.DisabledScope(model.IsOperationRunning))
            {
                GUI.DrawRect(row, thread.Id == snapshot.ThreadId ? Border : Sidebar);
                var tooltip = snapshot.IsTurnRunning && thread.Id != snapshot.ThreadId
                    ? thread.Title + "\nStop current generation and open this conversation" : thread.Title;
                if (GUI.Button(new Rect(row.x, row.y, row.width, 32), new GUIContent("", "", tooltip), RowStyle(thread.Id == snapshot.ThreadId)))
                {
                    controller.ResumeConversation(thread.Id);
                    _compactHistory = false;
                }
                GUI.Label(new Rect(row.x + 10, row.y + 5, row.width - 20, 25), CodexMessageLayout.Ellipsis(thread.Title, row.width - 22, 13), Style(13, Text));
                var date = thread.UpdatedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(thread.UpdatedAt).ToLocalTime().ToString("MM-dd HH:mm") : "";
                GUI.Label(new Rect(row.x + 10, row.y + 32, row.width - 76, 22), date, Style(11, Subtle));
            }
            if (row.Contains(Event.current.mousePosition) || thread.Id == snapshot.ThreadId)
            {
                using (new EditorGUI.DisabledScope(model.IsOperationRunning || snapshot.IsTurnRunning))
                {
                    if (Icon(new Rect(row.xMax - 58, row.y + 32, 23, 23), EditorBuiltinIcons.Toolbar.Field, "Rename conversation"))
                    { model.RenameThreadId = thread.Id; model.RenameTitle = thread.Title; _compactHistory = false; }

                }
            }
        }
        if (threads.Length == 0) GUI.Label(new Rect(10, 16, viewport.width - 24, 28), "No conversations", Style(13, Subtle));
        if (snapshot.NextThreadCursor.Length > 0 && GUI.Button(new Rect(8, threads.Length * rowHeight + 4, viewport.width - 26, 30), "Load more"))
            controller.RefreshConversations(false, true);
        GUI.EndScrollView();
        GUI.Label(new Rect(rect.x + 14, rect.yMax - 36, rect.width - 28, 24),
            snapshot.CatalogError.Length > 0 ? CodexMessageLayout.Ellipsis(snapshot.CatalogError, rect.width - 32, 11) :
            snapshot.AuthMode.Length > 0 ? snapshot.AuthMode + "  " + snapshot.PlanType : "Local workspace", Style(11, Subtle));
    }

    private static string NewConversationTooltip(CodexClientSnapshot snapshot) => snapshot.IsTurnRunning
        ? "Stop current generation and start a new conversation (Ctrl+N)"
        : "New conversation (Ctrl+N)";

    private void DrawConversation(Rect viewport, CodexClientSnapshot snapshot)
    {
        if (_layoutThread != snapshot.ThreadId)
        {
            _layoutThread = snapshot.ThreadId; _messageLayouts.Clear();
            model.FollowOutput = true;
        }
        var entries = snapshot.Transcript;
        var messageWidth = Fix64.Max(120, viewport.width - 30);
        Fix64 height = 16;
        foreach (var entry in entries) height += Layout(entry, messageWidth).Height + 24;
        height += 36;
        var maximum = Fix64.Max(0, height - viewport.height);
        if (model.FollowOutput) model.ConversationScroll = new Vector2(0, maximum);
        var previous = model.ConversationScroll;
        model.ConversationScroll = GUI.BeginScrollView(viewport, previous,
            new Rect(0, 0, viewport.width, Fix64.Max(height, viewport.height)), ScrollStyle(Surface));
        if (model.ConversationScroll.y != previous.y)
            model.FollowOutput = model.ConversationScroll.y >= maximum - 16;
        Fix64 y = 16;
        if (entries.Count == 0)
        {
            GUI.DrawTexture(new Rect(12, 28, 40, 40), "Icons/Windows/Codex.png");
            GUI.Label(new Rect(12, 86, messageWidth, 36), "What are we building?", Style(24, Text));
            GUI.Label(new Rect(12, 130, messageWidth, 28), controller.GetFileName(_client!.ProjectRoot), Style(14, Subtle));
        }
        foreach (var entry in entries)
        {
            var layout = Layout(entry, messageWidth);
            if (y + layout.Height >= model.ConversationScroll.y && y <= model.ConversationScroll.y + viewport.height)
                DrawMessage(new Rect(8, y, messageWidth, layout.Height), entry, layout);
            y += layout.Height + 24;
        }
        if (snapshot.IsTurnRunning)
            GUI.Label(new Rect(12, y, messageWidth, 24), snapshot.Approvals.Count > 0 ? "Waiting for approval" :
                snapshot.Questions.Count > 0 ? "Waiting for your answer" : "Working...", Style(13, Accent));
        GUI.EndScrollView();
        if (!model.FollowOutput && maximum > 0 && Icon(new Rect(viewport.xMax - 40, viewport.yMax - 38, 30, 30),
                EditorBuiltinIcons.Toolbar.Dropdown, "Jump to latest message")) { model.FollowOutput = true; Repaint(); }
    }

    private MessageLayout Layout(CodexTranscriptEntry entry, Fix64 width)
    {
        var details = entry.Kind is CodexTranscriptKind.Activity or CodexTranscriptKind.Diff;
        var expanded = !details || _showActivity || model.ExpandedEntries.Contains(entry.Id);
        if (_messageLayouts.TryGetValue(entry.Id, out var cached) && cached.Source == entry.Text && cached.Width == width && cached.Expanded == expanded)
            return cached;
        IReadOnlyList<CodexMessageBlock> blocks = !expanded ? [] : entry.Kind switch
        {
            CodexTranscriptKind.Assistant => CodexMessageLayout.Parse(entry.Text),
            CodexTranscriptKind.Diff => [new(CodexBlockKind.Code, entry.Text, "diff")],
            CodexTranscriptKind.Activity => [new(CodexBlockKind.Code, entry.Text, "output")],
            _ => [new(CodexBlockKind.Paragraph, entry.Text)]
        };
        var bodyWidth = width - (entry.Kind == CodexTranscriptKind.User ? 58 : 20);
        Fix64 height = 32;
        var measured = new List<MeasuredBlock>();
        foreach (var block in blocks)
        {
            var previousFont = GUIUtility.fontFamily;
            if (block.Kind == CodexBlockKind.Code) GUIUtility.fontFamily = "Consolas";
            var size = block.Kind == CodexBlockKind.Heading ? (Fix64)18 : 14;
            try
            {
                var wrapped = CodexMessageLayout.Wrap(block.Text, bodyWidth - 18, size);
                var blockHeight = block.Kind is CodexBlockKind.Link ? (Fix64)26 : block.Kind == CodexBlockKind.Rule ? 12 :
                    (wrapped.Count(ch => ch == '\n') + 1) * LineHeight(size) + (block.Kind == CodexBlockKind.Code ? 40 : 8);
                measured.Add(new(block, wrapped, blockHeight, size)); height += blockHeight;
            }
            finally { GUIUtility.fontFamily = previousFont; }
        }
        var layout = new MessageLayout(entry.Text, width, expanded, measured, height + 8);
        _messageLayouts[entry.Id] = layout;
        return layout;
    }

    private void DrawMessage(Rect rect, CodexTranscriptEntry entry, MessageLayout layout)
    {
        using var controls = new ControlScope(windowId, entry.Id);
        var user = entry.Kind == CodexTranscriptKind.User;
        if (user)
        {
            rect = new Rect(rect.x + 38, rect.y, rect.width - 38, rect.height);
            GUI.DrawRect(rect, EditorGUIUtility.isProSkin ? C(0.18f, 0.19f, 0.20f) : C(0.90f, 0.92f, 0.93f));
        }
        var kind = entry.Kind switch
        {
            CodexTranscriptKind.User => "You",
            CodexTranscriptKind.Assistant => "Codex",
            CodexTranscriptKind.Plan => "Plan",
            CodexTranscriptKind.Diff => "File changes",
            CodexTranscriptKind.Error => "Request failed",
            _ => entry.Text.Split('\n')[0]
        };
        if (entry.Kind is CodexTranscriptKind.Activity or CodexTranscriptKind.Diff)
        {
            if (Icon(new Rect(rect.x, rect.y, 24, 24), layout.Expanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen : EditorBuiltinIcons.Toolbar.FoldoutClosed, "Expand execution details"))
            {
                if (!model.ExpandedEntries.Add(entry.Id)) model.ExpandedEntries.Remove(entry.Id);
                _messageLayouts.Remove(entry.Id); Repaint();
            }
            kind = CodexMessageLayout.Ellipsis(kind, rect.width - 140, 12);
        }
        GUI.Label(new Rect(rect.x + (entry.Kind is CodexTranscriptKind.Activity or CodexTranscriptKind.Diff ? 28 : 10), rect.y, rect.width - 106, 26),
            kind, Style(12, entry.Kind == CodexTranscriptKind.Error ? C(0.92f, 0.47f, 0.44f) : Subtle));
        GUI.Label(new Rect(rect.xMax - 106, rect.y, 70, 26), entry.Status == "inProgress" ? "Running" : entry.Status == "failed" ? "Failed" : "", Style(11, Accent));
        if (Icon(new Rect(rect.xMax - 30, rect.y, 24, 24), EditorBuiltinIcons.Toolbar.Duplicate, "Copy message")) GUIUtility.systemCopyBuffer = entry.Text;
        var y = rect.y + 32;
        foreach (var block in layout.Blocks)
        {
            var body = new Rect(rect.x + 10, y, rect.width - 20, block.Height);
            if (block.Block.Kind == CodexBlockKind.Code)
            {
                GUI.DrawRect(body, Sidebar);
                GUI.Label(new Rect(body.x + 10, body.y + 2, body.width - 48, 24), block.Block.Detail, Style(11, Subtle));
                if (Icon(new Rect(body.xMax - 30, body.y + 2, 24, 24), EditorBuiltinIcons.Toolbar.Duplicate, "Copy code")) GUIUtility.systemCopyBuffer = block.Block.Text;
                var previousFont = GUIUtility.fontFamily;
                try
                {
                    GUIUtility.fontFamily = "Consolas";
                    DrawLines(new Rect(body.x + 10, body.y + 32, body.width - 20, body.height - 34), block.Text, Style(14, Text), block.Block.Detail == "diff");
                }
                finally { GUIUtility.fontFamily = previousFont; }
            }
            else if (block.Block.Kind == CodexBlockKind.Link)
            {
                if (GUI.Button(body, new GUIContent(CodexMessageLayout.Ellipsis(block.Block.Text, body.width - 10, 13), "", block.Block.Detail), LinkStyle()))
                    OpenMessageLink(block.Block.Detail);
            }
            else if (block.Block.Kind == CodexBlockKind.Rule) GUI.DrawRect(new Rect(body.x, body.y + 4, body.width, 1), Border);
            else
            {
                if (block.Block.Kind == CodexBlockKind.Quote) GUI.DrawRect(new Rect(body.x, body.y, 2, body.height - 6), Accent);
                DrawLines(body, block.Text, Style(block.FontSize, block.Block.Kind == CodexBlockKind.Quote ? Subtle : Text));
            }
            y += block.Height;
        }
    }

    private static void DrawLines(Rect rect, string text, GUIStyle style, bool diff = false)
    {
        var y = rect.y;
        foreach (var line in text.Split('\n'))
        {
            var color = diff && line.StartsWith('+') ? Accent : diff && line.StartsWith('-') ? C(0.93f, 0.51f, 0.48f) : style.normal.textColor;
            EditorGUI.SelectableLabel(new Rect(rect.x, y, rect.width, LineHeight(style.fontSize)), line, Style(style.fontSize, color));
            y += LineHeight(style.fontSize);
        }
    }

    private void OpenMessageLink(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") { Application.OpenURL(link); return; }
        var path = link;
        var line = 1;
        var colon = path.LastIndexOf(':');
        if (colon > 1 && int.TryParse(path[(colon + 1)..], out var number)) { path = path[..colon]; line = number; }
        try
        {
            path = new CodexProjectStore(_client!.ProjectRoot).NormalizeContextPath(path);
            var asset = AssetDatabase.LoadAssetAtPath<BObject>(path);
            if (asset is not null) AssetDatabase.OpenAsset(asset, line);
        }
        catch (Exception exception) { EditorFeatureGuard.Report("Open Codex file link", exception); }
    }

    private static Fix64 LineHeight(Fix64 size) => GUITextMetrics.MeasureLineHeight(size, GUIUtility.fontFamily) + 2;
    private static bool Icon(Rect rect, string icon, string tooltip) => GUI.Button(rect, new GUIContent("", icon, tooltip), EditorStyles.toolbarButton);
    private static GUIStyle Style(Fix64 size, Color color)
    {
        var style = new GUIStyle(EditorStyles.label) { fontSize = size, alignment = TextAnchor.UpperLeft };
        style.normal.textColor = style.hover.textColor = style.focused.textColor = style.active.textColor = color;
        return style;
    }
    private static GUIStyle ScrollStyle(Color color)
    {
        var style = new GUIStyle(GUI.skin.scrollView); style.normal.backgroundColor = style.hover.backgroundColor = color; return style;
    }
    private static GUIStyle RowStyle(bool selected)
    {
        var style = new GUIStyle(EditorStyles.toolbarButton);
        style.normal.backgroundColor = selected ? Border : Sidebar;
        style.normal.textColor = Text; return style;
    }
    private static GUIStyle LinkStyle()
    {
        var style = new GUIStyle(EditorStyles.toolbarButton) { fontSize = 13 };
        style.normal.backgroundColor = Surface; style.normal.textColor = Accent; return style;
    }
    private sealed record MeasuredBlock(CodexMessageBlock Block, string Text, Fix64 Height, Fix64 FontSize);
    private sealed record MessageLayout(string Source, Fix64 Width, bool Expanded, IReadOnlyList<MeasuredBlock> Blocks, Fix64 Height);

    private readonly struct ControlScope : IDisposable
    {
        private readonly int[] _scopes;
        private readonly int _count;

        internal ControlScope(string window, string section)
        {
            _scopes = GUIUtility.CaptureContainerScopes();
            _count = GUIUtility.CaptureControlCount();
            GUIUtility.BeginContainer(HashCode.Combine(window, section));
        }

        public void Dispose() => GUIUtility.RestoreContainerScopes(_scopes, _count);
    }
}
