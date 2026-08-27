namespace BEngine.Editor;

internal enum NativeDockDragPhase
{
    None,
    Preview,
    Drop
}

internal enum NativeWindowPointerOperation
{
    Unknown,
    CaptionMove,
    BorderResize
}

internal readonly record struct NativeDockDragUpdate(NativeDockDragPhase Phase, Vector2? DockPoint);

/// <summary>
/// Tracks a native title-bar move without depending on a particular windowing backend. Windows can
/// enter a modal OS move loop, so a move first observed after mouse release is still treated as a
/// completed drag when the preceding sample saw the button down.
/// </summary>
internal sealed class NativeWindowDockTracker
{
    private Vector2 _lastPosition;
    private Vector2 _lastSize;
    private bool _hasPosition;
    private bool _pointerWasDown;
    private bool _dragging;
    private bool _resizing;
    private NativeWindowPointerOperation _pointerOperation;

    internal bool NeedsPointerOperation => !_pointerWasDown;

    internal NativeDockDragUpdate Observe(
        Vector2 windowPosition,
        Vector2 windowSize,
        bool pointerDown,
        Vector2? dockPoint,
        NativeWindowPointerOperation pointerOperation = NativeWindowPointerOperation.Unknown)
    {
        var moved = _hasPosition && windowPosition != _lastPosition;
        var resized = _hasPosition && windowSize != _lastSize;
        _lastPosition = windowPosition;
        _lastSize = windowSize;
        _hasPosition = true;

        if (pointerDown)
        {
            if (!_pointerWasDown)
            {
                _pointerOperation = pointerOperation;
                _resizing = pointerOperation == NativeWindowPointerOperation.BorderResize;
            }
            _pointerWasDown = true;
            if (_pointerOperation != NativeWindowPointerOperation.CaptionMove)
                _resizing |= resized;
            _dragging |= moved && !_resizing;
            return _dragging && dockPoint is not null
                ? new NativeDockDragUpdate(NativeDockDragPhase.Preview, dockPoint)
                : default;
        }

        if (_pointerWasDown)
        {
            if (_pointerOperation != NativeWindowPointerOperation.CaptionMove)
                _resizing |= resized;
            _dragging |= moved && !_resizing;
        }
        var update = _dragging && !_resizing && dockPoint is not null
            ? new NativeDockDragUpdate(NativeDockDragPhase.Drop, dockPoint)
            : default;
        _pointerWasDown = false;
        _dragging = false;
        _resizing = false;
        _pointerOperation = NativeWindowPointerOperation.Unknown;
        return update;
    }

    internal void Reset(Vector2 windowPosition, Vector2 windowSize)
    {
        _lastPosition = windowPosition;
        _lastSize = windowSize;
        _hasPosition = true;
        _pointerWasDown = false;
        _dragging = false;
        _resizing = false;
        _pointerOperation = NativeWindowPointerOperation.Unknown;
    }
}
