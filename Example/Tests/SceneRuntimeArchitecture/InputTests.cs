namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal static class InputTests
{
    internal static void Run()
    {
        Input.SetKeyState(KeyCode.Space, true);
        using (var firstScene = new Scene("First input scene"))
        using (var secondScene = new Scene("Second input scene"))
        {
            var firstRuntime = new SceneRuntime(firstScene);
            var secondRuntime = new SceneRuntime(secondScene);
            firstRuntime.Start();
            secondRuntime.Start();
            firstRuntime.Tick(Fix64.Parse("0.016"));
            secondRuntime.Tick(Fix64.Parse("0.016"));
            Require(Input.GetKeyDown(KeyCode.Space),
                "A SceneRuntime cleared transient input before every loaded Scene consumed the frame.");
            firstRuntime.Stop();
            secondRuntime.Stop();
        }

        Input.SetMouseDelta(2, -1);
        Input.SetMouseDelta(3, 4);
        Require(Input.mouseX == 5 && Input.mouseY == 3,
            "Mouse motion events were not accumulated within one frame.");

        Input.SetGamepadConnected(0, true, "Test Pad");
        Input.SetGamepadButtonState(0, GamepadButton.South, true);
        Input.SetGamepadAxis(0, GamepadAxis.LeftStickX, Fix64.Parse("0.75"));
        Require(Input.gamepadCount == 1 && Input.GetJoystickNames().SequenceEqual(["Test Pad"]) &&
                Input.GetGamepadButtonDown(0, GamepadButton.South) &&
                Input.GetGamepadAxis(0, GamepadAxis.LeftStickX) == Fix64.Parse("0.75"),
            "Gamepad state did not preserve connection, button, or axis data.");

        Input.SetTouches([
            new Touch(7, new Vector2(10, 20), new Vector2(1, 2), Fix64.Parse("0.016"), 1,
                TouchPhase.Began, Fix64.One, Fix64.One)
        ]);
        Require(Input.touchCount == 1 && Input.GetTouch(0).fingerId == 7 &&
                Input.GetTouch(0).phase == TouchPhase.Began,
            "Touch state was not exposed through the frame snapshot.");

        Input.EndFrame();
        Require(!Input.GetKeyDown(KeyCode.Space) && Input.GetKey(KeyCode.Space) &&
                !Input.GetGamepadButtonDown(0, GamepadButton.South) &&
                Input.GetGamepadButton(0, GamepadButton.South) &&
                Input.GetTouch(0).phase == TouchPhase.Stationary &&
                Input.mouseX == 0 && Input.mouseY == 0,
            "Input.EndFrame did not clear only transient state.");

        Input.SetKeyState(KeyCode.Space, false);
        Input.SetGamepadButtonState(0, GamepadButton.South, false);
        Input.SetGamepadConnected(0, false);
        Input.SetTouches([]);
        Input.EndFrame();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
