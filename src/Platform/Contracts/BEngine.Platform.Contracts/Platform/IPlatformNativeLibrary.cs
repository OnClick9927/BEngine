namespace BEngine.Platform;

/// <summary>Loads caller-owned dynamic modules. This is separate from process-lifetime import
/// resolution: each successful call returns an independent lease that the consumer must dispose.</summary>
public interface IPlatformNativeLibraryFactory
{
    /// <summary>Resolves a file path and loads its module, or throws if dynamic loading is unsupported.</summary>
    IPlatformNativeLibrary Load(string path);
}

/// <summary>Owns one module lease. Export addresses are borrowed and may only be called while
/// this lease remains alive. Consumers own the ABI and must stop all calls before disposal.</summary>
public interface IPlatformNativeLibrary : IDisposable
{
    /// <summary>Returns a nonzero export address, or throws when the symbol is unavailable.</summary>
    nint GetExport(string name);
}
