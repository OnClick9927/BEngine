namespace BEngine;

internal sealed record RuntimeSystemFactoryRegistration(
    Type? SystemType,
    Func<IServiceProvider, ISceneRuntimeSystem> Factory);
