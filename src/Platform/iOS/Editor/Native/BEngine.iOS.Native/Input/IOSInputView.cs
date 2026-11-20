using System;
using System.Collections.Generic;
using System.Linq;
using CoreGraphics;
using Foundation;
using UIKit;

namespace BEngine.Platform.IosNative;

internal sealed class IOSInputView(CGRect frame) : UIView(frame)
{
    private readonly Dictionary<nint, BEngine.Touch> _touches = [];
    private bool _inputLogged;

    public override void TouchesBegan(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Began);
    public override void TouchesMoved(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Moved);
    public override void TouchesEnded(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Ended);
    public override void TouchesCancelled(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Canceled);

    private void ApplyTouches(NSSet nativeTouches, TouchPhase forcedPhase)
    {
        var ended = new List<nint>();
        var scale = Math.Max(1, ContentScaleFactor);
        foreach (UITouch touch in nativeTouches)
        {
            var id = touch.Handle;
            var point = touch.LocationInView(this);
            var position = new BEngine.Vector2((Fix64)(point.X * scale), (Fix64)(point.Y * scale));
            var previous = _touches.TryGetValue(id, out var previousTouch) ? previousTouch.position : position;
            var phase = forcedPhase == TouchPhase.Moved && position == previous
                ? TouchPhase.Stationary : forcedPhase;
            _touches[id] = new BEngine.Touch(id.GetHashCode() & int.MaxValue, position, position - previous,
                Time.unscaledDeltaTime, (int)touch.TapCount, phase,
                (Fix64)(double)touch.Force, (Fix64)(double)touch.MaximumPossibleForce,
                (Fix64)(touch.MajorRadius * scale), (Fix64)(touch.MajorRadiusTolerance * scale));
            if (phase is TouchPhase.Ended or TouchPhase.Canceled) ended.Add(id);
        }
        Input.SetTouches(_touches.Values);
        if (!_inputLogged && forcedPhase == TouchPhase.Began && _touches.Count > 0)
        {
            _inputLogged = true;
            Console.WriteLine($"BENGINE_IOS_INPUT_OK|kind=touch|count={_touches.Count}");
        }
        var primary = _touches.Values.FirstOrDefault();
        if (_touches.Count > 0)
        {
            Input.SetMousePosition(primary.position.x, primary.position.y);
            Input.SetKeyState(KeyCode.Mouse0, forcedPhase is not TouchPhase.Ended and not TouchPhase.Canceled);
        }
        foreach (var id in ended) _touches.Remove(id);
        foreach (var pair in _touches.ToArray())
            _touches[pair.Key] = pair.Value with
            {
                phase = TouchPhase.Stationary,
                deltaPosition = BEngine.Vector2.zero,
                deltaTime = Fix64.Zero
            };
    }
}

internal sealed class IOSInputViewController(IOSInputView view) : UIViewController
{
    public override void LoadView() => View = view;

    public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
    {
        ApplyPresses(presses, true);
        base.PressesBegan(presses, evt);
    }

    public override void PressesEnded(NSSet<UIPress> presses, UIPressesEvent evt)
    {
        ApplyPresses(presses, false);
        base.PressesEnded(presses, evt);
    }

    private static void ApplyPresses(NSSet<UIPress> presses, bool pressed)
    {
        foreach (var press in presses)
        {
            if (press.Key is not { } nativeKey || !TryMapKey(nativeKey.KeyCode, out var key)) continue;
            Input.SetKeyState(key, pressed);
            if (pressed)
                foreach (var character in nativeKey.Characters) Input.AppendTextInput(character);
        }
    }

    private static bool TryMapKey(UIKeyboardHidUsage code, out KeyCode key)
    {
        if (code is >= UIKeyboardHidUsage.KeyboardA and <= UIKeyboardHidUsage.KeyboardZ)
        {
            key = (KeyCode)((int)KeyCode.A + (int)(code - UIKeyboardHidUsage.KeyboardA));
            return true;
        }
        if (code is >= UIKeyboardHidUsage.Keyboard1 and <= UIKeyboardHidUsage.Keyboard9)
        {
            key = (KeyCode)((int)KeyCode.Alpha1 + (int)(code - UIKeyboardHidUsage.Keyboard1));
            return true;
        }
        if (code == UIKeyboardHidUsage.Keyboard0)
        {
            key = KeyCode.Alpha0;
            return true;
        }
        key = code switch
        {
            UIKeyboardHidUsage.KeyboardSpacebar => KeyCode.Space,
            UIKeyboardHidUsage.KeyboardReturnOrEnter => KeyCode.Return,
            UIKeyboardHidUsage.KeyboardEscape => KeyCode.Escape,
            UIKeyboardHidUsage.KeyboardTab => KeyCode.Tab,
            UIKeyboardHidUsage.KeyboardDeleteOrBackspace => KeyCode.Backspace,
            UIKeyboardHidUsage.KeyboardDeleteForward => KeyCode.Delete,
            UIKeyboardHidUsage.KeyboardUpArrow => KeyCode.UpArrow,
            UIKeyboardHidUsage.KeyboardDownArrow => KeyCode.DownArrow,
            UIKeyboardHidUsage.KeyboardLeftArrow => KeyCode.LeftArrow,
            UIKeyboardHidUsage.KeyboardRightArrow => KeyCode.RightArrow,
            _ => KeyCode.None
        };
        return key != KeyCode.None;
    }
}
