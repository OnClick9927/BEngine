using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BEngine.Rendering.Rhi;

/// <summary>Rejects expensive native startup before consuming a constrained Windows job's remaining commit.</summary>
public static class VulkanStartupMemory
{
    public static long minimumRemainingCommitBytes { get; set; } = 512L * 1024 * 1024;
    public static string? GetRejectionReason() => GetRejectionReason("Vulkan startup", minimumRemainingCommitBytes);
    public static string? GetRejectionReason(string stage, long requiredRemainingBytes)
    {
        if (requiredRemainingBytes < 0) throw new ArgumentOutOfRangeException(nameof(requiredRemainingBytes));
        if (!OperatingSystem.IsWindows() || requiredRemainingBytes == 0) return null;
        if (!QueryInformationJobObject(0, 9, out var limits, (uint)Marshal.SizeOf<ExtendedLimit>(), 0) ||
            (limits.Basic.LimitFlags & 0x100) == 0) return null;
        using var process = Process.GetCurrentProcess();
        var remaining = (long)limits.ProcessMemoryLimit - process.PrivateMemorySize64;
        return remaining >= requiredRemainingBytes ? null :
            $"{stage} rejected by process commit budget: {Math.Max(0, remaining) / 1048576} MiB remaining, {requiredRemainingBytes / 1048576} MiB allowance required. Use a cached/precompiled shader or a lower-cost backend, or increase the process budget.";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(nint job, int informationClass, out ExtendedLimit limits, uint length, nint returnedLength);
}
