namespace BEngine;

/// <summary>Host text session; indices use UTF-16 and are normalized by the UI editing buffer.</summary>
public sealed record TextInputRequest(long Id, string Text, int Anchor, int Caret, bool Password, bool Multiline);
public sealed record TextInputEdit(long Id, string Text, int Anchor, int Caret);
