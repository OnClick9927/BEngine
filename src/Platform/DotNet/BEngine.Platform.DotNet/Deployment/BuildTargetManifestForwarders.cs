using System.Runtime.CompilerServices;

// Keep already compiled consumers resolvable while the data and its codec have
// one owner in the engine's platform-neutral Player deployment layer.
[assembly: TypeForwardedTo(typeof(BEngine.Build.BuildTargetManifest))]
[assembly: TypeForwardedTo(typeof(BEngine.Build.BuildTargetManifestSerializer))]
[assembly: TypeForwardedTo(typeof(BEngine.Platform.LegacyProjectWorkspace))]
[assembly: TypeForwardedTo(typeof(BEngine.Platform.HostPresentationConfiguration))]
[assembly: TypeForwardedTo(typeof(BEngine.Player.AotInterpreterRuntimeHost))]
[assembly: TypeForwardedTo(typeof(BEngine.Player.AotInterpreterManagedCodeRuntimeProvider))]
[assembly: TypeForwardedTo(typeof(BEngine.Player.CoreClrManagedCodeRuntimeProvider))]
[assembly: TypeForwardedTo(typeof(BEngine.Player.PlayerManagedCodeRuntimeFactory))]
