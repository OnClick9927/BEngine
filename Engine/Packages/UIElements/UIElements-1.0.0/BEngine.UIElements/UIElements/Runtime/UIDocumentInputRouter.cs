using System.Buffers;

namespace BEngine.UIElements;

// Routes pointer/keyboard events once per frame; document visibility is controlled by its component/GameObject.
internal sealed class UIDocumentInputRouter(Scene scene) : IDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Scene, UIDocumentInputRouter> Routers = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Scene, DocumentSet> DocumentSets = new();
    internal static UIDocumentInputRouter ForScene(Scene scene) => Routers.GetValue(scene, static value => new(value));
    private readonly Scene _scene = scene;
    private readonly List<UIDocument> _inputDocuments = [];
    private readonly Dictionary<UIDocument, int> _inputOrder = [];
    private long _inputFrame = -1;
    private UIDocument? _captured;
    private readonly Dictionary<int, UIDocument> _touchCaptures = [];
    private readonly HashSet<int> _presentTouches = [];
    private readonly List<int> _missingTouches = [];
    private readonly HashSet<UIDocument> _cancellationDocuments = [];
    private Comparison<UIDocument>? _inputComparison;
    private UIDocument? _focused;

    // Input and rendering share the hierarchy snapshot. Visibility and sorting
    // remain live, including changes made between input and rendering in one frame.
    internal static IReadOnlyList<UIDocument> Collect(Scene scene) =>
        DocumentSets.GetValue(scene, static _ => new DocumentSet()).Collect(scene);
    internal void ProcessInput()
    {
        var scenes = _scene.GetService<SceneManagement.IRuntimeSceneManager>()?.LoadedScenes;
        if (scenes is { Count: > 0 } && !ReferenceEquals(scenes[0], _scene))
        {
            ForScene(scenes[0]).ProcessInput();
            return;
        }
        if (_inputFrame == Time.frameCount) return;
        _inputFrame = Time.frameCount;
        _inputDocuments.Clear();
        _inputOrder.Clear();
        if (scenes is { Count: > 0 })
        {
            if (scenes is List<Scene> list)
                foreach (var scene in list) CollectInput(scene);
            else if (scenes is Scene[] array)
                foreach (var scene in array) CollectInput(scene);
            else
                foreach (var scene in scenes) CollectInput(scene);
        }
        else CollectInput(_scene);
        if (scenes is { Count: > 1 }) _inputDocuments.Sort(_inputComparison ??= CompareInput);
        if (!Application.isFocused)
        {
            CancelAll();
            return;
        }
        foreach (var document in _inputDocuments) document.ValidatePointerState();
        var width = Screen.width;
        var height = Screen.height;
        var point = new Vector2(Input.mousePosition.x, height - Input.mousePosition.y);
        var hasTouches = Input.touchCount > 0;
        // Android's legacy Mouse0 mirrors touch. Do not hit-test and rebuild a
        // second hover stream before processing the actual physical pointers.
        var routeMouse = !Application.isMobilePlatform || !hasTouches &&
            (_captured is not null || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0) || Input.mouseScrollDelta.y != 0);
        UIDocument? picked = null;
        for (var index = routeMouse ? _inputDocuments.Count - 1 : -1; index >= 0; index--)
        {
            var candidate = _inputDocuments[index];
            if (candidate.interactable && candidate.HitTest(point, width, height)) { picked = candidate; break; }
        }
        if (!CanReceiveInput(_captured)) _captured = null;
        if (!CanReceiveInput(_focused)) _focused = null;
        foreach (var document in _inputDocuments)
            if (!ReferenceEquals(document, picked) && !ReferenceEquals(document, _captured)) document.ClearHover();
        if (routeMouse) (_captured ?? picked)?.DispatchPointerMove(point, width, height);
        if (Input.mouseScrollDelta.y != 0)
            picked?.DispatchScroll(point, -(float)Input.mouseScrollDelta.y * 48, width, height);
        // Mobile hosts also emulate Mouse0 for legacy gameplay input. Route the
        // physical touch only once instead of dispatching a second mouse click.
        if (!hasTouches && Input.GetMouseButtonDown(0))
        {
            if (!ReferenceEquals(_focused, picked)) _focused?.BlurKeyboardFocus();
            _captured = _focused = picked;
            picked?.DispatchPointerDown(point, width, height);
        }
        if (!hasTouches && Input.GetMouseButtonUp(0))
        {
            var captured = _captured;
            _captured = null;
            captured?.DispatchPointerUp(point, width, height);
        }
        if (!Input.GetMouseButton(0) && _captured is not null)
        {
            _captured.CancelPointer(-1);
            _captured = null;
        }
        ProcessTouches(width, height);
        if (!CanReceiveInput(_focused)) _focused = null;
        var keyboard = _focused ?? picked ?? _inputDocuments.LastOrDefault();
        foreach (var key in KeyboardKeys)
        {
            if (!Input.GetKeyDown(key) || !CanReceiveInput(keyboard)) continue;
            if (Input.compositionString.Length > 0) continue;
            if (keyboard!.DispatchKeyDown(key, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)))
                _focused = keyboard;
        }
        if (CanReceiveInput(keyboard)) keyboard!.DispatchTextInput(Input.inputString);
    }

    private void ProcessTouches(int width, int height)
    {
        var touches = Input.touches;
        if (touches.Length == 0 && _touchCaptures.Count == 0) return;
        _presentTouches.Clear();
        foreach (var touch in touches) _presentTouches.Add(touch.fingerId);
        _missingTouches.Clear();
        foreach (var id in _touchCaptures.Keys)
            if (!_presentTouches.Contains(id)) _missingTouches.Add(id);
        foreach (var id in _missingTouches)
            if (_touchCaptures.Remove(id, out var missing)) missing.CancelPointer(id);
        foreach (var touch in touches)
        {
            var id = touch.fingerId;
            var point = new Vector2(touch.position.x, height - touch.position.y);
            if (touch.phase == TouchPhase.Began)
            {
                if (_touchCaptures.Remove(id, out var previous)) previous.CancelPointer(id);
                UIDocument? picked = null;
                for (var index = _inputDocuments.Count - 1; index >= 0; index--)
                {
                    var candidate = _inputDocuments[index];
                    if (candidate.interactable && candidate.HitTest(point, width, height)) { picked = candidate; break; }
                }
                if (picked is null) continue;
                if (!ReferenceEquals(_focused, picked)) _focused?.BlurKeyboardFocus();
                _focused = picked;
                if (picked.DispatchPointerDown(point, width, height, id, UIPointerType.Touch, touch.pressure)) _touchCaptures[id] = picked;
            }
            else if (_touchCaptures.TryGetValue(id, out var captured))
            {
                if (!CanReceiveInput(captured) || touch.phase == TouchPhase.Canceled)
                { captured.CancelPointer(id); _touchCaptures.Remove(id); }
                else if (touch.phase == TouchPhase.Ended)
                { _touchCaptures.Remove(id); captured.DispatchPointerUp(point, width, height, id, UIPointerType.Touch); }
                else captured.DispatchPointerMove(point, width, height, id, UIPointerType.Touch, touch.pressure);
            }
        }
    }

    private void CancelAll()
    {
        foreach (var document in _inputDocuments) _cancellationDocuments.Add(document);
        foreach (var document in _touchCaptures.Values) _cancellationDocuments.Add(document);
        if (_captured is not null) _cancellationDocuments.Add(_captured);
        if (_focused is not null) _cancellationDocuments.Add(_focused);
        var count = _cancellationDocuments.Count;
        var documents = count == 0 ? null : ArrayPool<UIDocument>.Shared.Rent(count);
        if (documents is not null) _cancellationDocuments.CopyTo(documents);
        _cancellationDocuments.Clear();
        _touchCaptures.Clear(); _captured = _focused = null;
        // Snapshot before invoking user callbacks, which may stop the scene/router.
        if (documents is null) return;
        try
        {
            for (var index = 0; index < count; index++) documents[index].CancelInteraction();
        }
        finally { ArrayPool<UIDocument>.Shared.Return(documents, clearArray: true); }
    }

    private static readonly KeyCode[] KeyboardKeys =
        [KeyCode.Tab, KeyCode.Backspace, KeyCode.Return, KeyCode.Escape, KeyCode.Space, KeyCode.LeftArrow, KeyCode.RightArrow,
         KeyCode.Home, KeyCode.End, KeyCode.Delete, KeyCode.UpArrow, KeyCode.DownArrow,
         KeyCode.A, KeyCode.C, KeyCode.X, KeyCode.V, KeyCode.Z, KeyCode.Y];

    private void CollectInput(Scene scene)
    {
        var documents = Collect(scene);
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            _inputOrder[document] = _inputDocuments.Count;
            _inputDocuments.Add(document);
        }
    }

    private int CompareInput(UIDocument left, UIDocument right)
    {
        var result = left.sortingLayer.CompareTo(right.sortingLayer);
        if (result != 0) return result;
        result = left.sortingOrder.CompareTo(right.sortingOrder);
        return result != 0 ? result : _inputOrder[left].CompareTo(_inputOrder[right]);
    }

    private bool CanReceiveInput(UIDocument? document)
    {
        if (document is null) return false;
        if (document.isVisible && document.interactable && _inputOrder.ContainsKey(document)) return true;
        document.CancelInteraction();
        return false;
    }

    public void Dispose()
    {
        CancelAll();
        _inputDocuments.Clear();
        _inputOrder.Clear();
        _presentTouches.Clear();
        _missingTouches.Clear();
    }

    private sealed class DocumentSet
    {
        private long _hierarchyVersion = -1;
        private readonly List<UIDocument> _hierarchy = [];
        private readonly List<UIDocument> _scratch = [];
        private readonly List<UIDocument> _visible = [];
        private readonly IReadOnlyList<UIDocument> _view;
        private readonly Dictionary<UIDocument, int> _hierarchyOrder = [];
        private readonly Comparison<UIDocument> _comparison;
        private DocumentState[] _states = [];

        internal DocumentSet()
        {
            _view = _visible.AsReadOnly();
            _comparison = Compare;
        }

        internal IReadOnlyList<UIDocument> Collect(Scene scene)
        {
            var changed = _hierarchyVersion != scene.hierarchyLookupVersion;
            if (changed)
            {
                _hierarchy.Clear(); _hierarchyOrder.Clear();
                if (scene.QueryComponents<UIDocument>().Count > 0)
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        root.GetComponentsInChildren(_scratch, includeInactive: true);
                        _hierarchy.AddRange(_scratch);
                    }
                _scratch.Clear();
                _states = new DocumentState[_hierarchy.Count];
                for (var index = 0; index < _hierarchy.Count; index++)
                    _hierarchyOrder.Add(_hierarchy[index], index);
                _hierarchyVersion = scene.hierarchyLookupVersion;
            }
            for (var index = 0; index < _hierarchy.Count; index++)
            {
                var document = _hierarchy[index];
                var state = new DocumentState(document.isVisible, document.sortingLayer, document.sortingOrder);
                if (_states[index] != state) { _states[index] = state; changed = true; }
            }
            if (!changed) return _view;
            _visible.Clear();
            for (var index = 0; index < _hierarchy.Count; index++)
                if (_states[index].Visible) _visible.Add(_hierarchy[index]);
            _visible.Sort(_comparison);
            return _view;
        }

        private int Compare(UIDocument left, UIDocument right)
        {
            var result = left.sortingLayer.CompareTo(right.sortingLayer);
            if (result == 0) result = left.sortingOrder.CompareTo(right.sortingOrder);
            return result != 0 ? result : _hierarchyOrder[left].CompareTo(_hierarchyOrder[right]);
        }

        private readonly record struct DocumentState(bool Visible, ulong Layer, int Order);
    }
}
