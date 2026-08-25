using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace BEngine.Editor;

internal sealed class EditorSessionLogWriter : IDisposable
{
    private readonly string _path;
    private readonly IEditorTaskScheduler _scheduler;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly Lock _writeGate = new();
    private int _drainScheduled;
    private int _disposed;

    internal EditorSessionLogWriter(string path, IEditorTaskScheduler scheduler)
    {
        _path = Path.GetFullPath(path);
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    internal void Write(string message)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _pending.Enqueue($"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        ScheduleDrain();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Drain();
        GC.SuppressFinalize(this);
    }

    private void ScheduleDrain()
    {
        if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0) return;
        try
        {
            _ = _scheduler.ScheduleAsync("Write editor session log", _ => Drain(),
                EditorTaskPriority.Background);
        }
        catch (ObjectDisposedException)
        {
            Interlocked.Exchange(ref _drainScheduled, 0);
        }
    }

    private void Drain()
    {
        lock (_writeGate)
        {
            try
            {
                var buffer = new StringBuilder();
                while (_pending.TryDequeue(out var line)) buffer.Append(line);
                if (buffer.Length == 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, buffer.ToString(), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"BEngine editor log writer failed: {exception}");
            }
            finally
            {
                Interlocked.Exchange(ref _drainScheduled, 0);
                if (!_pending.IsEmpty && Volatile.Read(ref _disposed) == 0) ScheduleDrain();
            }
        }
    }
}
