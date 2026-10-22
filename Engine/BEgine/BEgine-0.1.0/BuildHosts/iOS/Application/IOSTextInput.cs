using System;
using BEngine.Platform;
using CoreGraphics;
using Foundation;
using UIKit;

namespace BEngine.Player.Hosts.iOS;

internal sealed class IOSTextInput
{
    private readonly UITextView _editor;
    private readonly EditorDelegate _delegate;
    private TextInputRequest? _request;
    private bool _syncing, _pending, _returnPressed;
    internal IOSTextInput(UIView parent)
    {
        _editor = new UITextView(new CGRect(1, 1, 1, 1)) { Alpha = .01f, BackgroundColor = UIColor.Clear,
            TextColor = UIColor.Clear, IsAccessibilityElement = false, ScrollEnabled = false };
        _delegate = new EditorDelegate(this); _editor.Delegate = _delegate; parent.AddSubview(_editor);
        if (PlatformRuntime.GetService<IInputService>() is PlatformInputService input)
        { input.ReadClipboard = () => UIPasteboard.General.String; input.WriteClipboard = text => { UIPasteboard.General.String = text; return true; }; }
    }
    private void Submit()
    {
        if (_syncing || _request is null) return;
        if (_editor.MarkedTextRange is { } marked)
        { Input.SetComposition(_editor.TextInRange(marked) ?? string.Empty); return; }
        Input.SetComposition(string.Empty); _pending = true;
        var selection = _editor.SelectedRange;
        Input.SubmitTextEdit(new(_request.Id, _editor.Text ?? string.Empty, (int)selection.Location, (int)(selection.Location + selection.Length)));
    }
    internal void Sync()
    {
        if (_returnPressed) { Input.SetKeyState(KeyCode.Return, false); _returnPressed = false; }
        var next = Input.textInputRequest;
        if (next is null)
        { if (_request is not null) _editor.ResignFirstResponder(); _request = null; _pending = false; return; }
        var changed = next.Id != _request?.Id; _request = next;
        _syncing = true;
        try
        {
            if (changed) { _pending = false; _editor.SecureTextEntry = next.Password; _editor.ReturnKeyType = next.Multiline ? UIReturnKeyType.Default : UIReturnKeyType.Done; }
            if (_pending && _editor.Text == next.Text) _pending = false;
            if (!_pending && _editor.MarkedTextRange is null)
            {
                if (_editor.Text != next.Text) _editor.Text = next.Text;
                var start = Math.Min(next.Anchor, next.Caret); var length = Math.Abs(next.Anchor - next.Caret);
                if ((int)_editor.SelectedRange.Location != start || (int)_editor.SelectedRange.Length != length)
                    _editor.SelectedRange = new NSRange(start, length);
            }
            if (changed) _editor.BecomeFirstResponder();
        }
        finally { _syncing = false; }
    }
    private sealed class EditorDelegate(IOSTextInput owner) : UITextViewDelegate
    {
        public override void Changed(UITextView textView) => owner.Submit();
        public override void SelectionChanged(UITextView textView) => owner.Submit();
        public override bool ShouldChangeText(UITextView textView, NSRange range, string text)
        {
            if (text != "\n" || owner._request?.Multiline != false) return true;
            Input.SetKeyState(KeyCode.Return, true); owner._returnPressed = true; textView.ResignFirstResponder(); return false;
        }
    }
}
