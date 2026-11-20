using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace BEngine.Editor.Host;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            return RunApplication(AppContext.BaseDirectory, arguments);
        }
        catch (Exception exception)
        {
            var error = exception.GetBaseException();
            Console.Error.WriteLine($"BENGINE_EDITOR_FAILED|{error}");
            if (arguments.Length == 0 || !arguments[0].StartsWith("--", StringComparison.Ordinal))
                MessageBox.Show($"BEngine editor could not start.\n\n{error.Message}", "BEngine",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static int RunApplication(string installationDirectory, string[] arguments)
    {
        var descriptorPath = Path.Combine(installationDirectory, "editor.application.json");
        if (!File.Exists(descriptorPath))
            throw new FileNotFoundException(
                "This installation has no editor application descriptor. Repair or re-export the engine installation.",
                descriptorPath);
        using var descriptor = JsonDocument.Parse(File.ReadAllBytes(descriptorPath));
        var root = descriptor.RootElement;
        if (root.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported editor application descriptor version.");
        var assemblyName = root.GetProperty("assembly").GetString();
        var typeName = root.GetProperty("type").GetString();
        if (string.IsNullOrWhiteSpace(assemblyName) ||
            !assemblyName.Equals(Path.GetFileName(assemblyName), StringComparison.Ordinal) ||
            assemblyName.AsSpan(0, assemblyName.Length).IndexOfAny('/', '\\', ':') >= 0 ||
            !assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(typeName))
            throw new InvalidDataException("The editor application descriptor must identify a local assembly and entry type.");
        var applicationPath = Path.GetFullPath(Path.Combine(installationDirectory, assemblyName));
        var resolvers = new List<AssemblyDependencyResolver> { new(applicationPath) };
        if (root.TryGetProperty("bootstrapAssemblies", out var bootstraps))
        {
            if (bootstraps.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Editor bootstrap assemblies must be an array.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assemblyName };
            foreach (var item in bootstraps.EnumerateArray())
            {
                var name = item.GetString();
                if (string.IsNullOrWhiteSpace(name) ||
                    !name.Equals(Path.GetFileName(name), StringComparison.Ordinal) ||
                    name.AsSpan().IndexOfAny('/', '\\', ':') >= 0 ||
                    !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !seen.Add(name))
                    throw new InvalidDataException("Editor bootstrap assemblies must be distinct local DLLs.");
                var path = Path.Combine(installationDirectory, name);
                if (!File.Exists(path) || !File.Exists(Path.ChangeExtension(path, ".deps.json")))
                    throw new FileNotFoundException("Editor bootstrap dependency metadata is missing.", path);
                resolvers.Add(new AssemblyDependencyResolver(path));
            }
        }
        Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name)
        {
            foreach (var resolver in resolvers)
                if (resolver.ResolveAssemblyToPath(name) is { } path)
                    return context.LoadFromAssemblyPath(path);
            return null;
        }
        nint ResolveNativeLibrary(Assembly assembly, string name)
        {
            foreach (var resolver in resolvers)
                if (resolver.ResolveUnmanagedDllToPath(name) is { } path)
                    return NativeLibrary.Load(path);
            return 0;
        }
        AssemblyLoadContext.Default.Resolving += ResolveAssembly;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNativeLibrary;
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(applicationPath);
            var type = assembly.GetType(typeName, throwOnError: true)!;
            if (!type.IsClass || type.IsAbstract || !typeof(IEditorApplicationEntry).IsAssignableFrom(type))
                throw new InvalidDataException("The editor entry type does not implement the editor application contract.");
            var application = (IEditorApplicationEntry)(Activator.CreateInstance(type) ??
                throw new InvalidDataException("The editor application entry could not be constructed."));
            return application.Run(arguments);
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll -= ResolveNativeLibrary;
        }
    }
}
