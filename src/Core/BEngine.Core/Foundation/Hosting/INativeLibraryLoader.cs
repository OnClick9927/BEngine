namespace BEngine.Hosting;

/// <summary>Provides an import-resolver callback without exposing a platform implementation.</summary>
public sealed class NativeLibraryUtility(INativeLibraryLoader implementation)
{
    public static NativeLibraryUtility Current => RuntimeServices.GetRequiredService<NativeLibraryUtility>();

    public nint ResolveImport(string libraryName, System.Reflection.Assembly assembly,
        System.Runtime.InteropServices.DllImportSearchPath? searchPath) =>
        implementation.ResolveImport(libraryName, assembly, searchPath);
}
