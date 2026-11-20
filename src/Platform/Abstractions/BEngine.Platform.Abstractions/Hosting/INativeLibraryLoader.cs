using System.Reflection;
using System.Runtime.InteropServices;

namespace BEngine.Hosting;

public interface INativeLibraryLoader
{
    void Initialize(string nativeLibraryDirectory, bool reportDiagnostics = false);
    nint ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath);
}
