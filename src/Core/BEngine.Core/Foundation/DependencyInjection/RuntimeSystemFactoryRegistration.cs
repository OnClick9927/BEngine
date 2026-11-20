using System.Reflection;

namespace BEngine;

internal sealed record RuntimeSystemFactoryRegistration(
    Type? SystemType,
    Func<IServiceProvider, ISceneRuntimeSystem> Factory,
    Assembly Owner);
