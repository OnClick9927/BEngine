using GameController;

namespace BEngine.Platform.IosNative;

public static class IOSGamepadInput
{
    public static void Poll()
    {
        var controllers = GCController.Controllers;
        for (var index = 0; index < controllers.Length; index++)
        {
            var controller = controllers[index];
            Input.SetGamepadConnected(index, true, controller.VendorName ?? "iOS gamepad");
            var pad = controller.ExtendedGamepad;
            if (pad is null) continue;
            Input.SetGamepadAxis(index, GamepadAxis.LeftStickX, (Fix64)pad.LeftThumbstick.XAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.LeftStickY, (Fix64)pad.LeftThumbstick.YAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.RightStickX, (Fix64)pad.RightThumbstick.XAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.RightStickY, (Fix64)pad.RightThumbstick.YAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.LeftTrigger, (Fix64)pad.LeftTrigger.Value);
            Input.SetGamepadAxis(index, GamepadAxis.RightTrigger, (Fix64)pad.RightTrigger.Value);
            SetButton(index, GamepadButton.South, pad.ButtonA);
            SetButton(index, GamepadButton.East, pad.ButtonB);
            SetButton(index, GamepadButton.West, pad.ButtonX);
            SetButton(index, GamepadButton.North, pad.ButtonY);
            SetButton(index, GamepadButton.LeftShoulder, pad.LeftShoulder);
            SetButton(index, GamepadButton.RightShoulder, pad.RightShoulder);
            SetButton(index, GamepadButton.LeftStick, pad.LeftThumbstickButton);
            SetButton(index, GamepadButton.RightStick, pad.RightThumbstickButton);
            SetButton(index, GamepadButton.DPadUp, pad.DPad.Up);
            SetButton(index, GamepadButton.DPadRight, pad.DPad.Right);
            SetButton(index, GamepadButton.DPadDown, pad.DPad.Down);
            SetButton(index, GamepadButton.DPadLeft, pad.DPad.Left);
        }
        for (var index = controllers.Length; index < Input.gamepadCount; index++)
            Input.SetGamepadConnected(index, false);
    }

    private static void SetButton(int index, GamepadButton button, GCControllerButtonInput? input) =>
        Input.SetGamepadButtonState(index, button, input?.IsPressed == true);

}
