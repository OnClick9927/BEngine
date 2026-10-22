using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BEngine.Player;

// Only the consumer touches the writer. Accepted records are never overwritten:
// saturation applies backpressure, including the large end-of-probe reports.
internal sealed class BufferedPlayerLogWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly TextWriter _writer;
    private readonly Action<string> _fallback;
    private readonly PendingLine[] _queue;
    private readonly PendingLine[] _batch = new PendingLine[64];
    private readonly int _characterBudget, _flushIntervalMilliseconds;
    private readonly Thread? _consumer;
    private int _head, _count, _characters, _consumerId, _batchCount;
    private long _batchCharacters;
    private long _assigned, _written, _flushed, _flushRequested;
    private bool _stopping, _failed, _disposed;
    [ThreadStatic] private static bool _reportingFallback;
    [ThreadStatic] private static bool _writingSynchronously;

    internal BufferedPlayerLogWriter(TextWriter writer, Action<string> fallback,
        bool useBackgroundThread = true, int capacity = 4096, int characterBudget = 2 * 1024 * 1024,
        int flushIntervalMilliseconds = 100)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fallback);
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (characterBudget <= 0) throw new ArgumentOutOfRangeException(nameof(characterBudget));
        if (flushIntervalMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(flushIntervalMilliseconds));
        _writer = writer; _fallback = fallback; _queue = new PendingLine[capacity];
        _characterBudget = characterBudget; _flushIntervalMilliseconds = flushIntervalMilliseconds;
        if (!useBackgroundThread) return;
        try
        {
            var consumer = new Thread(Consume) { IsBackground = true, Name = "BEngine.Player.Log" };
            consumer.Start();
            _consumer = consumer;
        }
        catch (NotSupportedException)
        {
            // Browser/WASM and hosts without threading retain synchronous,
            // serialized diagnostics rather than failing Player startup.
        }
    }

    internal void Write(string line, bool flush = false)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (_consumer is null) { WriteSynchronously(line); return; }
        long sequence = 0;
        bool fallback = false;
        lock (_gate)
        {
            while (!_stopping && !_failed &&
                   (_count == _queue.Length || _count > 0 && (long)_characters + line.Length > _characterBudget))
            {
                // A writer/failure callback can itself log. It must never wait
                // for the consumer on that same consumer thread.
                if (Environment.CurrentManagedThreadId == _consumerId) { fallback = true; break; }
                Monitor.Wait(_gate);
            }
            if (_stopping || _disposed && !_failed) return;
            fallback |= _failed;
            if (!fallback)
            {
                sequence = ++_assigned;
                _queue[(_head + _count) % _queue.Length] = new(line, sequence);
                _count++; _characters += line.Length;
                if (flush) _flushRequested = sequence;
                Monitor.PulseAll(_gate);
            }
        }
        if (fallback) ReportFallback(line);
        else if (flush) WaitForFlush(sequence);
    }

    internal void Flush()
    {
        if (_consumer is null)
        {
            lock (_gate)
            {
                if (_disposed || _writingSynchronously) return;
                _writingSynchronously = true;
                try { _writer.Flush(); }
                catch (Exception error) { FailSynchronously(error); }
                finally { _writingSynchronously = false; }
            }
            return;
        }
        long sequence;
        lock (_gate)
        {
            sequence = _assigned;
            _flushRequested = sequence;
            Monitor.PulseAll(_gate);
        }
        WaitForFlush(sequence);
    }

    private void WaitForFlush(long sequence)
    {
        if (Environment.CurrentManagedThreadId == Volatile.Read(ref _consumerId)) return;
        lock (_gate)
            while (!_failed && !_disposed && _flushed < sequence) Monitor.Wait(_gate);
    }

    private void Consume()
    {
        _consumerId = Environment.CurrentManagedThreadId;
        long lastFlush = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                PendingLine next = default;
                bool finish;
                lock (_gate)
                {
                    while (_count == 0 && !_stopping && _flushRequested <= _flushed)
                    {
                        int wait = _batchCount == 0 ? Timeout.Infinite : Math.Max(0,
                            _flushIntervalMilliseconds - (int)Stopwatch.GetElapsedTime(lastFlush).TotalMilliseconds);
                        if (wait == 0) break;
                        Monitor.Wait(_gate, wait);
                    }
                    if (_count > 0)
                    {
                        next = _queue[_head]; _queue[_head] = default;
                        _head = (_head + 1) % _queue.Length; _count--; _characters -= next.Line!.Length;
                        Monitor.PulseAll(_gate);
                    }
                    finish = _stopping && _count == 0 && next.Line is null;
                }
                if (next.Line is not null)
                {
                    // Keep the unflushed batch bounded and reachable until a
                    // successful Flush, so an I/O failure can report it too.
                    _batch[_batchCount++] = next;
                    _batchCharacters += next.Line.Length;
                    _writer.WriteLine(next.Line);
                    _written = next.Sequence;
                }
                bool flush;
                lock (_gate)
                    flush = finish || _batchCount == _batch.Length || _batchCharacters >= _characterBudget ||
                        _flushRequested > _flushed && _written >= _flushRequested ||
                        _batchCount > 0 && Stopwatch.GetElapsedTime(lastFlush).TotalMilliseconds >= _flushIntervalMilliseconds;
                if (flush)
                {
                    _writer.Flush();
                    Array.Clear(_batch, 0, _batchCount); _batchCount = 0; _batchCharacters = 0;
                    lastFlush = Stopwatch.GetTimestamp();
                    lock (_gate) { _flushed = _written; Monitor.PulseAll(_gate); }
                }
                if (finish) break;
            }
        }
        catch (Exception error)
        {
            // Publish accepted records before releasing waiting producers into
            // the fallback path. A fallback callback may reenter on this thread;
            // ReportFallback's guard makes that failure recursion harmless.
            lock (_gate)
            {
                _failed = true;
                ReportFallback("BENGINE_LOG_WRITE_FAILED|" + error);
                for (int i = 0; i < _batchCount; i++) ReportFallback(_batch[i].Line!);
                Array.Clear(_batch); _batchCount = 0; _batchCharacters = 0;
                while (_count > 0)
                {
                    var pending = _queue[_head]; _queue[_head] = default;
                    _head = (_head + 1) % _queue.Length; _count--; _characters -= pending.Line!.Length;
                    ReportFallback(pending.Line!);
                }
                Monitor.PulseAll(_gate);
            }
        }
        finally
        {
            try { _writer.Dispose(); }
            catch (Exception error) { ReportFallback("BENGINE_LOG_CLOSE_FAILED|" + error); }
            lock (_gate) { _disposed = true; Monitor.PulseAll(_gate); }
        }
    }

    private void WriteSynchronously(string line)
    {
        lock (_gate)
        {
            if (_disposed || _stopping) return;
            if (_failed || _writingSynchronously) { ReportFallback(line); return; }
            _writingSynchronously = true;
            try { _writer.WriteLine(line); _writer.Flush(); }
            catch (Exception error) { FailSynchronously(error); ReportFallback(line); }
            finally { _writingSynchronously = false; }
        }
    }

    private void FailSynchronously(Exception error)
    {
        _failed = true;
        ReportFallback("BENGINE_LOG_WRITE_FAILED|" + error);
    }

    private void ReportFallback(string line)
    {
        if (_reportingFallback) return;
        _reportingFallback = true;
        try { _fallback(line); }
        catch { /* A diagnostic failure must not recursively log or kill the consumer. */ }
        finally { _reportingFallback = false; }
    }

    public void Dispose()
    {
        lock (_gate) { _stopping = true; Monitor.PulseAll(_gate); }
        if (_consumer is not null)
        {
            if (Environment.CurrentManagedThreadId != Volatile.Read(ref _consumerId)) _consumer.Join();
            return;
        }
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer.Dispose(); }
            catch (Exception error) { ReportFallback("BENGINE_LOG_CLOSE_FAILED|" + error); }
        }
    }

    private readonly record struct PendingLine(string? Line, long Sequence);
}
