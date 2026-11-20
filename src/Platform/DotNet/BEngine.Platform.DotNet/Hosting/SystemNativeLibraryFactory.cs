using System.Runtime.InteropServices;

namespace BEngine.Platform;

/// <summary>System dynamic-module leases; no consumer ABI or process import cache is stored here.</summary>
public sealed class SystemNativeLibraryFactory : IPlatformNativeLibraryFactory
{
    public static SystemNativeLibraryFactory Shared { get; } = new();

    public IPlatformNativeLibrary Load(string path)
    {
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsIOS())
            throw new PlatformNotSupportedException("Dynamic native libraries are unavailable on this target.");
        return new Library(Path.GetFullPath(path));
    }

    private sealed class Library(string path) : IPlatformNativeLibrary
    {
        private readonly object _gate = new();
        private nint _handle = NativeLibrary.Load(path);

        public nint GetExport(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_handle == 0, this);
                return NativeLibrary.GetExport(_handle, name);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                var previous = _handle;
                _handle = 0;
                if (previous != 0) NativeLibrary.Free(previous);
            }
        }
    }
}
