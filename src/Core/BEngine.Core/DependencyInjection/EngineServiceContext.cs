namespace BEngine.DependencyInjection;

public sealed record EngineServiceContext(
    EngineHostKind HostKind,
    string ProjectPath = "",
    string InstanceName = "BEngine");
