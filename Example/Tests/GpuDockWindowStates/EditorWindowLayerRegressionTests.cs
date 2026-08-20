using BEngine.Editor;

namespace BEngine.ExampleTests.GpuDockWindowStates;

internal static class EditorWindowLayerRegressionTests
{
    private static readonly Rect Canvas = new(0, 0, 900, 640);

    public static void Run()
    {
        VerifyPresentationStateAndOrder();
        VerifyTopmostInputAndModalBlocking();
        VerifyPopupOutsideClick();
        VerifyDockRequestOnlyForNormalWindow();
    }

    private static void VerifyPresentationStateAndOrder()
    {
        var layer = new EditorWindowLayer();
        var normal = CreateWindow("Layer Normal", new Rect(60, 60, 320, 220));
        var popup = CreateWindow("Layer Popup", new Rect(90, 90, 300, 200));
        var modal = CreateWindow("Layer Modal", new Rect(120, 120, 280, 190));
        var auxiliary = CreateWindow("Layer Aux", new Rect(150, 150, 260, 180));
        try
        {
            var normalPresentation = layer.Show(normal, EditorWindowState.Normal);
            var popupPresentation = layer.Show(popup, EditorWindowState.Pop);
            var auxiliaryPresentation = layer.Show(auxiliary, EditorWindowState.Aux);

            Require(normalPresentation.Window == normal &&
                    normalPresentation.State == EditorWindowState.Normal,
                "Normal floating EditorWindow did not enter the in-process presentation layer.");
            Require(popupPresentation.Window == popup && popupPresentation.State == EditorWindowState.Pop,
                "ShowPopup did not enter the in-process popup layer.");
            Require(auxiliaryPresentation.Window == auxiliary &&
                    auxiliaryPresentation.State == EditorWindowState.Aux,
                "ShowUtility/ShowAuxWindow did not enter the in-process auxiliary layer.");
            Require(layer.Count == 3 && !layer.HasModal && layer.Windows[^1].Window == auxiliary,
                "EditorWindow layer did not retain non-modal presentations in back-to-front order.");
            Require(layer.Contains(normal) && layer.TryGet(normal, out var found) &&
                    ReferenceEquals(found, normalPresentation),
                "EditorWindow layer could not query an existing presentation.");

            layer.BringToFront(normal);
            Require(layer.Windows[^1].Window == normal,
                "BringToFront did not update the engine-local EditorWindow z-order.");
            layer.Focus(popup);
            Require(layer.Windows[^1].Window == popup && popup.hasFocus,
                "Focus did not raise and focus the requested in-process EditorWindow.");

            var repeated = layer.Show(popup, EditorWindowState.Pop);
            Require(ReferenceEquals(repeated, popupPresentation) && layer.Count == 3,
                "Showing an existing EditorWindow duplicated its in-process presentation.");

            var modalPresentation = layer.Show(modal, EditorWindowState.Modal);
            Require(modalPresentation.Window == modal && modalPresentation.State == EditorWindowState.Modal,
                "ShowModal did not enter the in-process modal layer.");
            Require(layer.Count == 4 && layer.HasModal && layer.Windows[^1].Window == modal,
                "A modal EditorWindow was not kept at the top of the in-process layer.");
            layer.BringToFront(normal);
            Require(layer.Windows[^1].Window == modal,
                "BringToFront raised a non-modal EditorWindow above the active modal window.");

            layer.Remove(modal);
            Require(!layer.Contains(modal) && !layer.HasModal && layer.Count == 3,
                "Removing the modal EditorWindow left stale modal state in the layer.");
        }
        finally
        {
            Close(normal, popup, modal, auxiliary);
        }
    }

    private static void VerifyTopmostInputAndModalBlocking()
    {
        var layer = new EditorWindowLayer();
        var lower = CreateWindow("Input Lower", new Rect(70, 70, 380, 270));
        var upper = CreateWindow("Input Upper", new Rect(110, 100, 380, 270));
        var modal = CreateWindow("Input Modal", new Rect(520, 100, 260, 210));
        try
        {
            layer.Show(lower, EditorWindowState.Normal);
            layer.Show(upper, EditorWindowState.Aux);
            Render(layer, new Event(EventType.Layout), inputPass: false);
            Render(layer, new Event(EventType.Repaint), inputPass: false);

            Render(layer, MouseDown(220, 210));
            Require(lower.MouseDownCount == 0 && upper.MouseDownCount == 1,
                "Mouse input reached more than the topmost overlapping EditorWindow.");

            layer.BringToFront(lower);
            Render(layer, MouseDown(220, 210));
            Require(lower.MouseDownCount == 1 && upper.MouseDownCount == 1,
                "BringToFront did not change which overlapping EditorWindow receives input.");

            layer.Focus(upper);
            Render(layer, new Event(EventType.KeyDown) { keyCode = KeyCode.A });
            Require(lower.KeyDownCount == 0 && upper.KeyDownCount == 1,
                "Keyboard input was not restricted to the focused top-level EditorWindow.");

            layer.Show(modal, EditorWindowState.Modal);
            layer.Focus(lower);
            var blockedMouse = MouseDown(220, 210);
            Render(layer, blockedMouse);
            Require(lower.MouseDownCount == 1 && upper.MouseDownCount == 1 &&
                    blockedMouse.type == EventType.Used,
                "A modal EditorWindow allowed mouse input to reach a lower presentation.");

            var modalKey = new Event(EventType.KeyDown) { keyCode = KeyCode.B };
            Render(layer, modalKey);
            Require(modal.KeyDownCount == 1 && lower.KeyDownCount == 0 && upper.KeyDownCount == 1,
                "A modal EditorWindow did not exclusively receive keyboard input.");
        }
        finally
        {
            Close(lower, upper, modal);
        }
    }

    private static void VerifyPopupOutsideClick()
    {
        var layer = new EditorWindowLayer();
        var lower = CreateWindow("Popup Lower", new Rect(70, 70, 380, 270));
        var popup = CreateWindow("Popup Top", new Rect(520, 100, 250, 190));
        EditorWindow? closed = null;
        layer.WindowClosed += window => closed = window;
        try
        {
            layer.Show(lower, EditorWindowState.Normal);
            layer.Show(popup, EditorWindowState.Pop);
            Render(layer, new Event(EventType.Layout), inputPass: false);
            Render(layer, new Event(EventType.Repaint), inputPass: false);

            var outsideClick = MouseDown(220, 210);
            Render(layer, outsideClick);
            Require(ReferenceEquals(closed, popup) && !layer.Contains(popup),
                "Clicking outside a popup did not remove it and request its closure.");
            Require(lower.MouseDownCount == 0 && outsideClick.type == EventType.Used,
                "The click that dismissed a popup passed through to the EditorWindow below it.");
        }
        finally
        {
            Close(lower, popup);
        }
    }

    private static void VerifyDockRequestOnlyForNormalWindow()
    {
        var layer = new EditorWindowLayer();
        var normal = CreateWindow("Dock Normal", new Rect(100, 100, 320, 220));
        var auxiliary = CreateWindow("Dock Aux", new Rect(460, 100, 320, 220));
        EditorWindow? docked = null;
        layer.DockRequested += (window, _) => docked = window;
        try
        {
            layer.Show(normal, EditorWindowState.Normal);
            DragTitle(layer, new Vector2(150, 112), new Vector2(190, 145));
            Require(docked is null && normal.position.x > 100 && normal.position.y > 100,
                "Dragging a Normal in-process EditorWindow did not move it independently.");
            Render(layer, new Event(EventType.MouseDown)
            {
                mousePosition = new Vector2(normal.position.x + 50, normal.position.y + 12),
                button = 0,
                clickCount = 2
            });
            Require(ReferenceEquals(docked, normal),
                "Double-clicking a Normal in-process EditorWindow title did not request docking.");

            docked = null;
            layer.Show(auxiliary, EditorWindowState.Aux);
            DragTitle(layer, new Vector2(510, 112), new Vector2(550, 145));
            Require(docked is null,
                "Dragging an auxiliary EditorWindow incorrectly requested docking into the workspace.");
        }
        finally
        {
            Close(normal, auxiliary);
        }
    }

    private static void DragTitle(EditorWindowLayer layer, Vector2 start, Vector2 end)
    {
        Render(layer, new Event(EventType.MouseDown) { mousePosition = start, button = 0 });
        Render(layer, new Event(EventType.MouseDrag) { mousePosition = end, button = 0 });
        Render(layer, new Event(EventType.MouseUp) { mousePosition = end, button = 0 });
    }

    private static LayerProbeWindow CreateWindow(string title, Rect position)
    {
        var window = new LayerProbeWindow(title) { position = position };
        window.OpenInternal();
        return window;
    }

    private static Event MouseDown(Fix64 x, Fix64 y) => new(EventType.MouseDown)
    {
        mousePosition = new Vector2(x, y),
        button = 0
    };

    private static void Render(EditorWindowLayer layer, Event evt, bool inputPass = true)
    {
        GUI.BeginFrame(evt, (int)Canvas.width, (int)Canvas.height, []);
        try { layer.Draw(Canvas, inputPass); }
        finally { GUI.EndFrame(); }
    }

    private static void Close(params EditorWindow[] windows)
    {
        foreach (var window in windows) window.CloseInternal();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class LayerProbeWindow(string title) : EditorWindow
    {
        public int MouseDownCount { get; private set; }
        public int KeyDownCount { get; private set; }

        protected override void OnGUI()
        {
            if (Event.current.type == EventType.MouseDown) MouseDownCount++;
            if (Event.current.type == EventType.KeyDown) KeyDownCount++;
            GUI.Label(new Rect(8, 8, 180, 20), title);
        }
    }
}
