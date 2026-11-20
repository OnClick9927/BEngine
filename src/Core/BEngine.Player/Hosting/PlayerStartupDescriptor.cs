using BEngine.Hosting;

namespace BEngine.Player;

public sealed record PlayerStartupDescriptor(
    string ProjectPath,
    string NativeLibraryDirectory,
    BEngine.Hosting.HostDeploymentManifest? Manifest);

