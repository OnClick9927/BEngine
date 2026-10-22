namespace BEngine.UIElements;

public sealed class VisualElementScheduler
{
    private readonly VisualElement _owner;

    internal VisualElementScheduler(VisualElement owner) => _owner = owner;

    public IVisualElementScheduledItem Execute(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new ScheduledItem(_owner, action);
    }

    private sealed class ScheduledItem : IVisualElementScheduledItem
    {
        private readonly VisualElement _owner;
        private readonly Action _action;
        private bool _subscribed;
        private TimeSpan _nextTime;
        private long _nextFrame;
        private TimeSpan? _interval;
        private int _frames = 1;
        private bool _repeating;
        private bool _disposed;

        internal ScheduledItem(VisualElement owner, Action action)
        {
            _owner = owner;
            _action = action;
            Resume();
        }

        public bool isActive => !_disposed && _subscribed;

        public IVisualElementScheduledItem Every(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            _interval = interval;
            _repeating = true;
            Restart();
            return this;
        }

        public IVisualElementScheduledItem EveryFrames(int frameCount)
        {
            if (frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
            _interval = null;
            _frames = frameCount;
            _repeating = true;
            Restart();
            return this;
        }

        public void Pause()
        {
            if (!_subscribed) return;
            BTaskScheduler.Main.FrameAdvanced -= OnFrame;
            _subscribed = false;
        }

        public void Resume()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_subscribed) return;
            _nextFrame = BTaskScheduler.Main.FrameCount + (_repeating ? _frames : 1);
            _nextTime = BTaskScheduler.Main.Elapsed + (_interval ?? TimeSpan.Zero);
            _subscribed = true;
            BTaskScheduler.Main.FrameAdvanced += OnFrame;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Pause();
        }

        private void Restart()
        {
            if (_disposed) return;
            Pause();
            Resume();
        }

        private void OnFrame(BFrameTick tick)
        {
            if (!_subscribed || _disposed) return;
            if (_repeating && _interval.HasValue ? tick.Elapsed < _nextTime : tick.Frame < _nextFrame) return;
            _nextFrame = tick.Frame + _frames;
            _nextTime = tick.Elapsed + (_interval ?? TimeSpan.Zero);
            if (!_repeating) Pause();
            if (!_owner.enabledInHierarchy || !_owner.visible) return;
            try { _action(); }
            catch
            {
                Pause();
                throw;
            }
        }
    }
}
