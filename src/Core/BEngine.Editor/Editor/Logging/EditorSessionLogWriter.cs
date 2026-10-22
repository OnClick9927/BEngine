using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using BEngine.Platform;

namespace BEngine.Editor;

internal sealed class EditorSessionLogWriter : IDisposable
{
    private readonly string _path;
    private readonly IEditorTaskScheduler _scheduler;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly Lock _writeGate = new();
    private int _drainScheduled;
    private int _disposed;

    internal EditorSessionLogWriter(string path, IEditorTaskScheduler scheduler)
    {
        _fileSystem = EditorServiceAccess.GetRequiredService<FileUtility>();
        _pathService = EditorServiceAccess.GetRequiredService<PathUtility>();
        _platformService = EditorServiceAccess.GetRequiredService<PlatformUtility>();
        _path = _pathService.GetFullPath(path);
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    internal void Write(string message)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _pending.Enqueue($"[{_platformService.UtcNow:O}] {message}{_platformService.NewLine}");
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
                _fileSystem.CreateDirectory(_pathService.GetDirectoryName(_path)!);
                using var stream = _fileSystem.OpenFile(
                    _path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
                    4096, FileOptions.SequentialScan);
                using var writer = new StreamWriter(
                    stream, new UTF8Encoding(false), 4096, leaveOpen: true);
                writer.Write(buffer.ToString());
                writer.Flush();
                _fileSystem.Flush(stream);
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
