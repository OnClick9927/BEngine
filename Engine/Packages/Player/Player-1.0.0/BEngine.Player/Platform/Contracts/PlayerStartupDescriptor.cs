
namespace BEngine.Player;

public sealed record PlayerStartupDescriptor(
    string ProjectPath,
    string NativeLibraryDirectory,
    BEngine.Build.PlayerBootstrapManifest? Manifest);

