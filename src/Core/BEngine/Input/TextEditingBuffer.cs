using System.Globalization;

namespace BEngine;

/// <summary>Grapheme-safe editing with independent selection and bounded undo/redo history.</summary>
public sealed class TextEditingBuffer
{
    private readonly List<State> _undo = [], _redo = [];
    private int? _preferredColumn;
    private readonly record struct State(string Text, int Caret, int Anchor);
    public string Text { get; private set; } = string.Empty;
    public int Caret { get; private set; }
    public int Anchor { get; private set; }
    public int SelectionStart => Math.Min(Caret, Anchor);
    public int SelectionEnd => Math.Max(Caret, Anchor);
    public string SelectedText => Text[SelectionStart..SelectionEnd];
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Synchronize(string? value)
    {
        value ??= string.Empty;
        if (Text == value) return;
        Text = value; Caret = Boundary(Math.Min(Caret, Text.Length)); Anchor = Boundary(Math.Min(Anchor, Text.Length));
        _undo.Clear(); _redo.Clear(); _preferredColumn = null;
    }
    public void Select(int anchor, int caret) { Anchor = Boundary(anchor); Caret = Boundary(caret); _preferredColumn = null; }
    public void SelectAll() => Select(0, Text.Length);
    public void Move(int index, bool extend = false) { Caret = Boundary(index); if (!extend) Anchor = Caret; _preferredColumn = null; }
    public void ReplaceSelection(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var start = SelectionStart; var end = SelectionEnd;
        if (value.Length == 0 && end == start) return;
        Remember(); Text = Text[..start] + value + Text[end..]; Caret = Anchor = Boundary(start + value.Length); _preferredColumn = null;
    }
    /// <summary>Limits inserted UTF-16 units without splitting a grapheme. Existing text is preserved.</summary>
    public void ReplaceSelection(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maximumLength < -1) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        var available = maximumLength < 0 ? value.Length : Math.Max(0, maximumLength - (Text.Length - (SelectionEnd - SelectionStart)));
        if (value.Length > available)
            value = value[..StringInfo.ParseCombiningCharacters(value).LastOrDefault(index => index <= available)];
        ReplaceSelection(value);
    }
    public void Delete(bool backward, bool word = false)
    {
        var previous = new State(Text, Caret, Anchor);
        if (Caret == Anchor)
        {
            var next = word ? WordBoundary(backward) : backward
                ? TextElementUtility.PreviousBoundary(Text, Caret) : TextElementUtility.NextBoundary(Text, Caret);
            Anchor = next;
        }
        ReplaceSelection(string.Empty);
        // Navigation used to choose the deletion range is not part of the user's selection.
        if (Text != previous.Text) _undo[^1] = previous;
    }
    public void MoveHorizontal(bool backward, bool extend, bool word = false)
    {
        if (!extend && Caret != Anchor) { Move(backward ? SelectionStart : SelectionEnd); return; }
        Move(word ? WordBoundary(backward) : backward
            ? TextElementUtility.PreviousBoundary(Text, Caret) : TextElementUtility.NextBoundary(Text, Caret), extend);
    }
    public void MoveLine(bool end, bool extend, bool wholeDocument = false)
    {
        if (wholeDocument) { Move(end ? Text.Length : 0, extend); return; }
        var start = Caret == 0 ? 0 : Text.LastIndexOf('\n', Math.Max(0, Caret - 1)) + 1;
        Move(end ? LineEnd(Caret) : start, extend);
    }
    public void MoveVertical(bool down, bool extend)
    {
        var start = Caret == 0 ? 0 : Text.LastIndexOf('\n', Caret - 1) + 1;
        var column = _preferredColumn ?? new StringInfo(Text[start..Math.Min(Caret, LineEnd(start))]).LengthInTextElements;
        var currentEnd = Text.IndexOf('\n', Caret); if (currentEnd < 0) currentEnd = Text.Length;
        var nextStart = down ? Math.Min(Text.Length, currentEnd + 1) : start == 0 ? 0 :
            start > 1 ? Text.LastIndexOf('\n', start - 2) + 1 : 0;
        if (down && currentEnd == Text.Length || !down && start == 0) return;
        var nextEnd = LineEnd(nextStart);
        var offsets = StringInfo.ParseCombiningCharacters(Text[nextStart..nextEnd]);
        Move(nextStart + (column >= offsets.Length ? nextEnd - nextStart : offsets[column]), extend);
        _preferredColumn = column;
    }
    public bool Undo() => Restore(_undo, _redo);
    public bool Redo() => Restore(_redo, _undo);
    private bool Restore(List<State> source, List<State> destination)
    {
        if (source.Count == 0) return false;
        destination.Add(new(Text, Caret, Anchor)); var state = source[^1]; source.RemoveAt(source.Count - 1);
        Text = state.Text; Caret = state.Caret; Anchor = state.Anchor; _preferredColumn = null; return true;
    }
    private int LineEnd(int index)
    {
        var end = Text.IndexOf('\n', index);
        if (end < 0) return Text.Length;
        return end > 0 && Text[end - 1] == '\r' ? end - 1 : end;
    }
    private void Remember()
    {
        if (_undo.Count == 100) _undo.RemoveAt(0);
        _undo.Add(new(Text, Caret, Anchor)); _redo.Clear();
    }
    private int Boundary(int index)
    {
        index = Math.Clamp(index, 0, Text.Length);
        if (index == Text.Length) return index;
        return StringInfo.ParseCombiningCharacters(Text).LastOrDefault(value => value <= index);
    }
    private int WordBoundary(bool backward)
    {
        var cursor = Caret;
        if (backward)
        {
            while (cursor > 0 && char.IsWhiteSpace(Text[cursor - 1])) cursor = TextElementUtility.PreviousBoundary(Text, cursor);
            while (cursor > 0 && !char.IsWhiteSpace(Text[cursor - 1])) cursor = TextElementUtility.PreviousBoundary(Text, cursor);
        }
        else
        {
            while (cursor < Text.Length && !char.IsWhiteSpace(Text[cursor])) cursor = TextElementUtility.NextBoundary(Text, cursor);
            while (cursor < Text.Length && char.IsWhiteSpace(Text[cursor])) cursor = TextElementUtility.NextBoundary(Text, cursor);
        }
        return cursor;
    }
}
