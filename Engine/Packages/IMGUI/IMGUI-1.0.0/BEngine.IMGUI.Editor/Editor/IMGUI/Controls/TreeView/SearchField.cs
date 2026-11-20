using BEngine;
using BEngine.Editor;

namespace UnityEditor.IMGUI.Controls;

/// <summary>Reusable IMGUI search text field with clear and keyboard navigation behavior.</summary>
public class SearchField
{
    public delegate void SearchFieldCallback();

    private static int _nextId;
    private readonly string _controlName = $"IMGUI.Controls.SearchField.{Interlocked.Increment(ref _nextId)}";
    private bool _requestFocus;

    public bool autoSetFocusOnFindCommand { get; set; }
    public int searchFieldControlID { get; private set; }
    public event SearchFieldCallback? downOrUpArrowKeyPressed;

    public string OnGUI(string text, params GUILayoutOption[] options) => OnGUI(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), text);

    public string OnGUI(Rect rect, string text) => Draw(rect, text, EditorStyles.searchField,
        EditorStyles.searchFieldCancelButton, EditorStyles.searchFieldCancelButtonEmpty);

    public string OnGUI(Rect rect, string text, GUIStyle? searchFieldStyle,
        GUIStyle? cancelButtonStyle, GUIStyle? emptyCancelButtonStyle) => Draw(rect, text,
        searchFieldStyle ?? EditorStyles.searchField,
        cancelButtonStyle ?? EditorStyles.searchFieldCancelButton,
        emptyCancelButtonStyle ?? EditorStyles.searchFieldCancelButtonEmpty);

    public string OnToolbarGUI(string text, params GUILayoutOption[] options) => OnToolbarGUI(
        GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight, options), text);

    public string OnToolbarGUI(Rect rect, string text) => Draw(rect, text, EditorStyles.toolbarSearchField,
        EditorStyles.toolbarSearchFieldCancelButton, EditorStyles.toolbarSearchFieldCancelButtonEmpty);

    public string OnToolbarGUI(Rect rect, string text, GUIStyle? searchFieldStyle,
        GUIStyle? cancelButtonStyle, GUIStyle? emptyCancelButtonStyle) => Draw(rect, text,
        searchFieldStyle ?? EditorStyles.toolbarSearchField,
        cancelButtonStyle ?? EditorStyles.toolbarSearchFieldCancelButton,
        emptyCancelButtonStyle ?? EditorStyles.toolbarSearchFieldCancelButtonEmpty);

    public void SetFocus()
    {
        _requestFocus = true;
        GUI.FocusControl(_controlName);
    }

    public bool HasFocus() => searchFieldControlID != 0 && GUIUtility.keyboardControl == searchFieldControlID &&
        string.Equals(GUI.GetNameOfFocusedControl(), _controlName, StringComparison.Ordinal);

    private string Draw(Rect rect, string text, GUIStyle fieldStyle, GUIStyle cancelStyle,
        GUIStyle emptyCancelStyle)
    {
        text ??= string.Empty;
        HandleFindCommand();
        // The underlying text field consumes key events, so search commands run first.
        var evt = Event.current;
        if (GUI.enabled && HasFocus() && evt.type == EventType.KeyDown)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                if (text.Length > 0) { text = string.Empty; GUI.changed = true; }
                evt.Use();
            }
            else if (evt.keyCode is KeyCode.UpArrow or KeyCode.DownArrow)
            {
                downOrUpArrowKeyPressed?.Invoke();
                evt.Use();
            }
        }
        var buttonWidth = Fix64.Min(rect.width, Fix64.Max(18, rect.height));
        var fieldRect = new Rect(rect.x, rect.y, Fix64.Max(0, rect.width - buttonWidth), rect.height);
        var cancelRect = new Rect(fieldRect.xMax, rect.y, buttonWidth, rect.height);
        GUI.SetNextControlName(_controlName);
        var next = GUI.TextField(fieldRect, text, fieldStyle);
        searchFieldControlID = GUI.lastTextControlId;
        if (_requestFocus)
        {
            GUI.FocusControl(_controlName);
            _requestFocus = false;
        }

        var hasText = next.Length > 0;
        var cancelContent = hasText ? EditorGUIUtility.IconContent("Close") : GUIContent.none;
        if (GUI.Button(cancelRect, cancelContent,
                hasText ? cancelStyle : emptyCancelStyle) && hasText)
        {
            next = string.Empty;
            GUI.changed = true;
            SetFocus();
        }

        return next;
    }

    private void HandleFindCommand()
    {
        if (!autoSetFocusOnFindCommand) return;
        var evt = Event.current;
        var isFind = evt.commandName == "Find" ||
                     (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.F &&
                      (evt.control || evt.command));
        if (!isFind) return;
        SetFocus();
        if (evt.type is not EventType.Layout and not EventType.Repaint) evt.Use();
    }
}
