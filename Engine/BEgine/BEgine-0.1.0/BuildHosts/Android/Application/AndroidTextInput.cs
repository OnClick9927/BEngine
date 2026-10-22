using System;
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using BEngine.Platform;

#pragma warning disable CA1422
namespace BEngine.Player.Hosts.Android;

internal sealed class AndroidTextInput
{
    private readonly ActivityTextEditor _editor;
    private readonly View _surface;
    private readonly InputMethodManager? _keyboard;
    private TextInputRequest? _request;
    private bool _syncing, _pending;
    private volatile bool _safeAreaDirty = true;
    private readonly int[] _surfacePosition = new int[2], _windowPosition = new int[2];
    internal bool IsEditing => _request is not null && _editor.HasFocus;
    internal bool NeedsSync => _safeAreaDirty || _request is not null || Input.textInputRequest is not null;
    internal void RefreshSafeArea() { _safeAreaDirty = true; ReportSafeArea(); }
    internal AndroidTextInput(Context context, FrameLayout parent, View surface)
    {
        _surface = surface;
        surface.LayoutChange += (_, _) => RefreshSafeArea();
        parent.LayoutChange += (_, _) => RefreshSafeArea();
        _editor = new ActivityTextEditor(context) { Alpha = .01f, Focusable = true, FocusableInTouchMode = true,
            ImportantForAccessibility = ImportantForAccessibility.No };
        _editor.SetTextColor(global::Android.Graphics.Color.Transparent); _editor.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        parent.AddView(_editor, new FrameLayout.LayoutParams(1, 1));
        _keyboard = context.GetSystemService(Context.InputMethodService) as InputMethodManager;
        _editor.TextChanged += (_, _) => Submit(); _editor.SelectionChanged = Submit;
        if (PlatformRuntime.GetService<IInputService>() is PlatformInputService input)
        {
            var clipboard = context.GetSystemService(Context.ClipboardService) as global::Android.Content.ClipboardManager;
            input.ReadClipboard = () => clipboard?.PrimaryClip?.GetItemAt(0)?.CoerceToText(context)?.ToString();
            input.WriteClipboard = text => { if (clipboard is null) return false; clipboard.PrimaryClip = ClipData.NewPlainText("BEngine", text); return true; };
        }
    }
    private void Submit()
    {
        if (_syncing || _request is null) return;
        var editable = _editor.EditableText;
        var a = editable is null ? -1 : BaseInputConnection.GetComposingSpanStart(editable);
        var b = editable is null ? -1 : BaseInputConnection.GetComposingSpanEnd(editable);
        var text = _editor.Text ?? string.Empty;
        if (a >= 0 && b > a && b <= text.Length) { Input.SetComposition(text[a..b]); return; }
        Input.SetComposition(string.Empty); _pending = true;
        Input.SubmitTextEdit(new(_request.Id, text, _editor.SelectionStart, _editor.SelectionEnd));
    }
    internal void Sync()
    {
        var next = Input.textInputRequest;
        if (next is null)
        {
            if (_request is not null) { _keyboard?.HideSoftInputFromWindow(_editor.WindowToken, HideSoftInputFlags.None); _editor.ClearFocus(); _surface.RequestFocus(); }
            _request = null; _pending = false; ReportSafeArea(); return;
        }
        var changed = _request?.Id != next.Id; _request = next;
        _syncing = true;
        try
        {
            if (changed)
            {
                _pending = false;
                _editor.InputType = next.Password ? InputTypes.ClassText | InputTypes.TextVariationPassword :
                    InputTypes.ClassText | (next.Multiline ? InputTypes.TextFlagMultiLine : InputTypes.TextVariationNormal);
            }
            if (_pending && (_editor.Text ?? string.Empty) == next.Text) _pending = false;
            if (!_pending && Input.compositionString.Length == 0)
            {
                if (_editor.Text != next.Text) _editor.Text = next.Text;
                if (_editor.SelectionStart != next.Anchor || _editor.SelectionEnd != next.Caret)
                    _editor.SetSelection(Math.Clamp(next.Anchor, 0, next.Text.Length), Math.Clamp(next.Caret, 0, next.Text.Length));
            }
            if (changed) { _editor.RequestFocus(); _keyboard?.ShowSoftInput(_editor, ShowFlags.Implicit); }
        }
        finally { _syncing = false; }
        ReportSafeArea();
    }
    private void ReportSafeArea()
    {
        // Insets change with layout/focus, not every game frame. Polling the
        // Java hierarchy at 60 Hz creates avoidable JNI peers and bridge GC.
        if (!_safeAreaDirty) return;
        var insets = _surface.RootWindowInsets;
        if (insets is null) return;
        _safeAreaDirty = false;
        int left, bottom, right, top;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            left = safe.Left; bottom = safe.Bottom; right = safe.Right; top = safe.Top;
        }
        else
        {
            left = insets.SystemWindowInsetLeft; bottom = insets.SystemWindowInsetBottom;
            right = insets.SystemWindowInsetRight; top = insets.SystemWindowInsetTop;
            if (OperatingSystem.IsAndroidVersionAtLeast(28) && insets.DisplayCutout is { } cutout)
            {
                left = Math.Max(left, cutout.SafeInsetLeft); bottom = Math.Max(bottom, cutout.SafeInsetBottom);
                right = Math.Max(right, cutout.SafeInsetRight); top = Math.Max(top, cutout.SafeInsetTop);
            }
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // A rectangle inset by r*(1-1/sqrt(2)) stays inside a rounded corner.
            var radius = 0;
            for (var corner = 0; corner < 4; corner++)
                radius = Math.Max(radius, insets.GetRoundedCorner(corner)?.Radius ?? 0);
            var cornerInset = (int)Math.Ceiling(radius * .293);
            left = Math.Max(left, cornerInset); right = Math.Max(right, cornerInset);
            top = Math.Max(top, cornerInset); bottom = Math.Max(bottom, cornerInset);
        }
        // The window manager may already inset the GL surface. Report only the
        // remaining overlap, rather than subtracting the system bars twice.
        _surface.GetLocationOnScreen(_surfacePosition); var root = _surface.RootView!; root.GetLocationOnScreen(_windowPosition);
        var x = _surfacePosition[0] - _windowPosition[0]; var y = _surfacePosition[1] - _windowPosition[1];
        var scaleX = (float)Screen.width / Math.Max(1, _surface.Width);
        var scaleY = (float)Screen.height / Math.Max(1, _surface.Height);
        DisplayUtility.Current.ReportSafeArea(new((Fix64)(Math.Max(0, left - x) * scaleX),
            (Fix64)(Math.Max(0, y + _surface.Height - (root.Height - bottom)) * scaleY),
            (Fix64)(Math.Max(0, x + _surface.Width - (root.Width - right)) * scaleX),
            (Fix64)(Math.Max(0, top - y) * scaleY)));
    }
    private sealed class ActivityTextEditor(Context context) : EditText(context)
    {
        internal System.Action? SelectionChanged;
        protected override void OnSelectionChanged(int selStart, int selEnd)
        { base.OnSelectionChanged(selStart, selEnd); SelectionChanged?.Invoke(); }
    }
}
