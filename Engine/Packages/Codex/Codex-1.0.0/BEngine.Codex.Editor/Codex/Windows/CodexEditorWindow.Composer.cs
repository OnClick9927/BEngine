namespace BEngine.Editor.Codex;

public sealed partial class CodexEditorWindow
{
    private Vector2 _composerScroll;
    private Vector2 _contextScroll;
    private string _selectedRequestId = string.Empty;

    private void DrawComposer(Rect rect, CodexClientSnapshot snapshot)
    {
        using var controls = new ControlScope(windowId, "composer");
        GUI.DrawRect(rect, Surface);
        var y = rect.y;
        if (_contextPaths.Count > 0)
        {
            var contextWidth = _contextPaths.Sum(path => (float)Fix64.Min(270, CodexMessageLayout.Measure(controller.GetFileName(path), 12) + 54));
            _contextScroll = GUI.BeginScrollView(new Rect(rect.x, y, rect.width, 38), _contextScroll,
                new Rect(0, 0, Fix64.Max(rect.width, (Fix64)contextWidth), 38), ScrollStyle(Surface));
            Fix64 x = 0;
            foreach (var path in _contextPaths.ToArray())
            {
                var width = Fix64.Min(270, CodexMessageLayout.Measure(controller.GetFileName(path), 12) + 54);
                GUI.Box(new Rect(x, 0, width - 6, 28), "");
                GUI.Label(new Rect(x + 8, 0, width - 42, 28), new GUIContent(CodexMessageLayout.Ellipsis(controller.GetFileName(path), width - 42, 12), "", path), Style(12, Text));
                if (Icon(new Rect(x + width - 32, 2, 24, 24), EditorBuiltinIcons.Toolbar.Close, "Remove context asset")) controller.RemoveContextPath(path);
                x += width;
            }
            GUI.EndScrollView();
            y += 42;
        }
        var editor = new Rect(rect.x, y, rect.width, 104);
        GUI.DrawRect(editor, Border);
        GUI.DrawRect(new Rect(editor.x + 1, editor.y + 1, editor.width - 2, editor.height - 2), Surface);
        var lineHeight = LineHeight(14);
        var lines = _prompt.Replace("\r", string.Empty).Split('\n');
        var textWidth = Fix64.Max(editor.width - 20, lines.Aggregate(Fix64.Zero,
            (maximum, line) => Fix64.Max(maximum, CodexMessageLayout.Measure(line, 14) + 20)));
        var textHeight = Fix64.Max(82, lines.Length * lineHeight + 8);
        using (new ControlScope(windowId, "prompt"))
        {
            _composerScroll = GUI.BeginScrollView(new Rect(editor.x + 8, editor.y + 8, editor.width - 16, 88), _composerScroll,
                new Rect(0, 0, textWidth, textHeight), ScrollStyle(Surface));
            GUI.SetNextControlName("CodexPrompt");
            var inputStyle = new GUIStyle(EditorStyles.textArea) { fontSize = 14, alignment = TextAnchor.UpperLeft };
            inputStyle.normal.backgroundColor = inputStyle.focused.backgroundColor = inputStyle.hover.backgroundColor = Surface;
            inputStyle.normal.textColor = inputStyle.focused.textColor = inputStyle.hover.textColor = Text;
            _prompt = GUI.TextArea(new Rect(0, 0, textWidth - 8, textHeight), _prompt, inputStyle);
            if (_prompt.Length == 0 && GUI.GetNameOfFocusedControl() != "CodexPrompt")
                GUI.Label(new Rect(4, 2, editor.width - 28, 26), snapshot.IsTurnRunning ? "Send a follow-up..." : "Ask Codex to work on this project...", Style(14, Subtle));
            GUI.EndScrollView();
        }

        var controlsY = y + 113;
        var selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(selectedPath)))
            if (Icon(new Rect(rect.x, controlsY, 28, 28), EditorBuiltinIcons.Toolbar.Add, "Attach selected project asset")) controller.AddContextPath(selectedPath);
        if (Icon(new Rect(rect.x + 32, controlsY, 28, 28), _showActivity ? EditorBuiltinIcons.Toolbar.Visible : EditorBuiltinIcons.Toolbar.Hidden,
                "Expand or collapse all execution details")) { _showActivity = !_showActivity; _messageLayouts.Clear(); }

        var settings = _client!.Settings;
        var modelWidth = Fix64.Clamp(rect.width - 270, 90, 210);
        using (new EditorGUI.DisabledScope(model.IsOperationRunning || snapshot.IsTurnRunning))
        {
            var choices = new[] { "Default model" }.Concat(snapshot.Models.Select(item => item.DisplayName)).ToList();
            var ids = new[] { string.Empty }.Concat(snapshot.Models.Select(item => item.Id)).ToList();
            if (!ids.Contains(settings.Model)) { ids.Add(settings.Model); choices.Add(settings.Model); }
            var index = Math.Max(0, ids.IndexOf(settings.Model));
            var selected = CompactPopup(new Rect(rect.x + 68, controlsY, modelWidth, 28), index, choices.ToArray());
            if (selected != index)
            {
                settings.Model = ids[selected];
                settings.Effort = snapshot.Models.FirstOrDefault(item => item.Id == settings.Model)?.DefaultEffort ?? "medium";
                _client.SaveSettings();
            }
            var selectedModel = snapshot.Models.FirstOrDefault(item => item.Id == settings.Model) ?? snapshot.Models.FirstOrDefault(item => item.IsDefault);
            var efforts = selectedModel?.Efforts.Count > 0 ? selectedModel.Efforts.ToArray() : EffortOptions;
            var effort = Math.Max(0, Array.IndexOf(efforts, settings.Effort));
            var nextEffort = CompactPopup(new Rect(rect.x + 76 + modelWidth, controlsY, 86, 28), effort, efforts);
            if (nextEffort != effort) { settings.Effort = efforts[nextEffort]; _client.SaveSettings(); }
        }
        using (new EditorGUI.DisabledScope(model.IsOperationRunning || _prompt.Trim().Length == 0 || snapshot.ConnectionStatus != CodexConnectionStatus.Ready))
            if (Icon(new Rect(rect.xMax - 34, controlsY, 32, 30), EditorBuiltinIcons.Toolbar.Send,
                    snapshot.IsTurnRunning ? "Send follow-up (Enter)" : "Send message (Enter)")) SendComposer();
        if (snapshot.IsTurnRunning)
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(snapshot.ActiveTurnId)))
                if (Icon(new Rect(rect.xMax - 72, controlsY, 30, 30), EditorBuiltinIcons.Toolbar.Stop, "Stop generation (Escape)")) controller.Interrupt();
        }
        else if (snapshot.StatusText == "Sign in required" || snapshot.LoginUrl.Length > 0)
        {
            if (GUI.Button(new Rect(rect.x + 10, editor.y + 32, rect.width - 20, 34), "Sign in with ChatGPT"))
            {
                if (snapshot.LoginUrl.Length > 0) Application.OpenURL(snapshot.LoginUrl); else controller.BeginLogin();
            }
        }
        else if (snapshot.ConnectionStatus == CodexConnectionStatus.Faulted)
        {
            if (GUI.Button(new Rect(rect.x + 10, editor.y + 32, rect.width - 20, 34), "Reconnect Codex")) controller.SaveSettingsAndRestart();
        }
        HandleAssetDrop(editor);
    }

    private void DrawRequests(Rect rect, CodexClientSnapshot snapshot)
    {
        using var controls = new ControlScope(windowId, "requests");
        GUI.DrawRect(rect, Sidebar);
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), Accent);
        var requestIds = snapshot.Approvals.Select(approval => approval.RequestIdJson)
            .Concat(snapshot.Questions.Select(request => request.RequestIdJson)).ToArray();
        var selected = Math.Max(0, Array.IndexOf(requestIds, _selectedRequestId));
        var labels = snapshot.Approvals.Select(approval => approval.Kind == CodexApprovalKind.Command
                ? "Waiting for approval: command" : "Waiting for approval: file changes")
            .Concat(snapshot.Questions.Select(_ => "Waiting for your answer")).ToArray();
        if (requestIds.Length > 1)
            selected = CompactPopup(new Rect(rect.x + 10, rect.y + 7, rect.width - 20, 26), selected,
                labels.Select((label, index) => $"{index + 1}/{labels.Length}  {label}").ToArray());
        else
            GUI.Label(new Rect(rect.x + 10, rect.y + 7, rect.width - 20, 26), labels[selected], Style(14, Accent));
        if (_selectedRequestId != requestIds[selected])
        {
            _selectedRequestId = requestIds[selected];
            model.RequestScroll = Vector2.zero;
        }

        var details = new Rect(rect.x, rect.y + 76, rect.width, Fix64.Max(20, rect.height - 82));
        var actionsY = rect.y + 38;
        var width = rect.width - 28;
        if (selected < snapshot.Approvals.Count)
        {
            var approval = snapshot.Approvals[selected];
            var detailText = string.Join("\n\n", new[] { approval.Reason, approval.Command, approval.WorkingDirectory }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            using (new EditorGUI.DisabledScope(model.IsOperationRunning))
            {
                if (GUI.Button(new Rect(rect.x + 10, actionsY, 100, 28), "Allow once")) controller.RespondToApproval(approval, true);
                if (GUI.Button(new Rect(rect.x + 120, actionsY, 100, 28), "Decline")) controller.RespondToApproval(approval, false);
            }
            if (Icon(new Rect(rect.xMax - 38, actionsY, 28, 28), EditorBuiltinIcons.Toolbar.Duplicate, "Copy approval details"))
                GUIUtility.systemCopyBuffer = detailText;
            var wrapped = CodexMessageLayout.Wrap(detailText, width, 13);
            var height = RequestTextHeight(detailText, width) + 12;
            model.RequestScroll = GUI.BeginScrollView(details, model.RequestScroll,
                new Rect(0, 0, details.width, Fix64.Max(details.height, height)), ScrollStyle(Sidebar));
            DrawLines(new Rect(10, 4, width, height), wrapped, Style(13, Text));
            GUI.EndScrollView();
            return;
        }

        var request = snapshot.Questions[selected - snapshot.Approvals.Count];
        using (new EditorGUI.DisabledScope(model.IsOperationRunning || request.Questions.Any(question =>
                   string.IsNullOrWhiteSpace(model.Answers.GetValueOrDefault(request.RequestIdJson + ":" + question.Id)))))
            if (GUI.Button(new Rect(rect.x + 10, actionsY, 150, 28), "Submit answers")) controller.AnswerQuestions(request);
        var questionHeight = request.Questions.Aggregate((Fix64)12, (height, question) =>
            height + RequestTextHeight(question.Text, width) + question.Options.Count * 56 + 76);
        model.RequestScroll = GUI.BeginScrollView(details, model.RequestScroll,
            new Rect(0, 0, details.width, Fix64.Max(details.height, questionHeight)), ScrollStyle(Sidebar));
        Fix64 y = 4;
        foreach (var question in request.Questions)
        {
            GUI.Label(new Rect(10, y, width, 24), question.Header, Style(13, Accent)); y += 26;
            var text = CodexMessageLayout.Wrap(question.Text, width, 13);
            var textHeight = (text.Count(ch => ch == '\n') + 1) * LineHeight(13);
            DrawLines(new Rect(10, y, width, textHeight), text, Style(13, Text)); y += textHeight + 4;
            var key = request.RequestIdJson + ":" + question.Id;
            var answer = model.Answers.GetValueOrDefault(key, string.Empty);
            foreach (var option in question.Options)
            {
                var selectedOption = GUI.Toggle(new Rect(10, y, width, 25), answer == option.Label, option.Label);
                if (selectedOption && answer != option.Label) answer = option.Label;
                GUI.Label(new Rect(30, y + 25, width - 20, 22), new GUIContent(CodexMessageLayout.Ellipsis(option.Description, width - 22, 11), "", option.Description), Style(11, Subtle));
                y += 56;
            }
            answer = question.IsSecret ? GUI.PasswordField(new Rect(10, y, width, 30), answer) : GUI.TextField(new Rect(10, y, width, 30), answer);
            model.Answers[key] = answer; y += 44;
        }
        GUI.EndScrollView();
    }

    private static Fix64 RequestTextHeight(string text, Fix64 width) =>
        (CodexMessageLayout.Wrap(text, width, 13).Count(ch => ch == '\n') + 1) * LineHeight(13);

    private static int CompactPopup(Rect rect, int selected, string[] choices)
    {
        var previousWidth = EditorGUI.labelWidth;
        var previousIndent = EditorGUI.indentLevel;
        try
        {
            EditorGUI.labelWidth = 0; EditorGUI.indentLevel = 0;
            return EditorGUI.Popup(rect, string.Empty, selected, choices);
        }
        finally { EditorGUI.labelWidth = previousWidth; EditorGUI.indentLevel = previousIndent; }
    }

    private void DrawRenameConversation(Rect rect)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width, 28), "Rename conversation", Style(18, Text));
        model.RenameTitle = GUI.TextField(new Rect(rect.x, rect.y + 40, rect.width, 32), model.RenameTitle);
        if (GUI.Button(new Rect(rect.x, rect.y + 84, 120, 30), "Rename"))
        {
            controller.RenameConversation(model.RenameThreadId, model.RenameTitle);
            model.RenameThreadId = string.Empty;
        }
        if (GUI.Button(new Rect(rect.x + 132, rect.y + 84, 120, 30), "Cancel"))
            model.RenameThreadId = string.Empty;
    }

    private void HandleShortcuts(CodexClientSnapshot snapshot)
    {
        var evt = Event.current;
        if (evt.type != EventType.KeyDown) return;
        if ((evt.control || evt.command) && evt.keyCode == KeyCode.N)
        { NewConversation(); evt.Use(); }
        else if ((evt.control || evt.command) && evt.keyCode == KeyCode.L)
        { GUI.FocusControl("CodexPrompt"); evt.Use(); }
        else if ((evt.control || evt.command) && evt.keyCode == KeyCode.F)
        { model.ShowSidebar = true; _compactHistory = true; GUI.FocusControl("CodexHistorySearch"); evt.Use(); }
        else if (evt.keyCode == KeyCode.Escape && snapshot.IsTurnRunning)
        { controller.Interrupt(); evt.Use(); }
        else if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter && !evt.shift && GUI.GetNameOfFocusedControl() == "CodexPrompt")
        { SendComposer(); evt.Use(); }
    }

    private void SendComposer()
    {
        if (controller.SendPrompt()) { _composerScroll = Vector2.zero; GUI.FocusControl("CodexPrompt"); }
    }

    private void NewConversation()
    {
        controller.NewConversation();
        _compactHistory = false;
        _composerScroll = Vector2.zero;
        GUI.FocusControl("CodexPrompt");
    }

    private void HandleAssetDrop(Rect rect)
    {
        if (!rect.Contains(Event.current.mousePosition) || Event.current.type is not (EventType.DragUpdated or EventType.DragPerform)) return;
        var paths = DragAndDrop.objectReferences.Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (paths.Length == 0) return;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (Event.current.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var path in paths) controller.AddContextPath(path);
        }
        Event.current.Use();
    }
}
