using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BEngine.Platform;

/// <summary>Limits a desktop frame loop without running unpresented updates.</summary>
public sealed class DesktopFramePacer : IDisposable
{
    private readonly SafeWaitHandle? _timer = CreateTimer();
    private int _rate;
    private double _deadline;
    private double _spinGuardTicks = Stopwatch.Frequency * .0002;
    private bool _disposed;

    public void Wait(int targetFrameRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (targetFrameRate <= 0)
        {
            _rate = 0;
            _spinGuardTicks = Stopwatch.Frequency * .0002;
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var period = (double)Stopwatch.Frequency / targetFrameRate;
        var adaptiveWait = targetFrameRate >= 240 && _timer is not null;
        var maximumGuard = Math.Min(Stopwatch.Frequency * .00075, period * .35);
        // At unusually high limits the cap can be below 200us. Lower the floor
        // with it rather than reversing Clamp's bounds or spinning a full frame.
        var minimumGuard = Math.Min(Stopwatch.Frequency * .0002, maximumGuard);
        if (_rate != targetFrameRate)
        {
            _rate = targetFrameRate;
            _deadline = now;
            _spinGuardTicks = adaptiveWait ? minimumGuard : Stopwatch.Frequency * .0002;
        }
        // A slow frame must not cause a burst of catch-up renders. Retain the
        // fractional deadline during normal operation instead of accumulating
        // the OS wait's overshoot in every subsequent frame.
        if (now - _deadline > period) _deadline = now;
        var spinTicks = adaptiveWait ? _spinGuardTicks : Stopwatch.Frequency * .0002;
        while (true)
        {
            var remaining = _deadline - Stopwatch.GetTimestamp();
            if (remaining <= 0) break;
            var wait = remaining - spinTicks;
            if (wait > 0 && _timer is not null)
            {
                long due = -(long)Math.Max(1, wait * 10_000_000 / Stopwatch.Frequency);
                var waitStarted = adaptiveWait ? Stopwatch.GetTimestamp() : 0;
                if (SetWaitableTimerEx(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
                {
                    var result = WaitForSingleObject(_timer, uint.MaxValue);
                    if (adaptiveWait && result == 0)
                    {
                        var woke = Stopwatch.GetTimestamp();
                        var overshoot = Math.Max(0, woke - waitStarted + due * (Stopwatch.Frequency / 10_000_000d));
                        _spinGuardTicks = UpdateSpinGuard(_spinGuardTicks, overshoot, minimumGuard, maximumGuard);
                    }
                }
                else Thread.Sleep(1);
            }
            else if (remaining > Stopwatch.Frequency * .002)
                Thread.Sleep(Math.Max(1, (int)(remaining * 1000 / Stopwatch.Frequency) - 1));
            else Thread.SpinWait(32);
        }
        _deadline += period;
    }

    private static double UpdateSpinGuard(double previous, double overshoot, double minimum, double maximum) =>
        // Follow a measured late wake promptly, then decay toward the floor.
        // The bound limits extra CPU use and cannot prevent OS descheduling.
        Math.Clamp(Math.Max(previous * .98, overshoot + Stopwatch.Frequency * .00005), minimum, maximum);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }

    private static SafeWaitHandle? CreateTimer()
    {
        if (!OperatingSystem.IsWindows()) return null;
        // High-resolution timers are local to this loop and do not raise the
        // system-wide timer frequency. Older Windows versions use the fallback.
        var timer = CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x1F0003);
        if (!timer.IsInvalid) return timer;
        timer.Dispose();
        return null;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr completion, IntPtr argument, IntPtr wakeContext, uint tolerableDelay);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
