namespace BEngine.DependencyInjection;

internal sealed class EmptyServiceProvider : IServiceProvider
{
    internal static EmptyServiceProvider Instance { get; } = new();

    private EmptyServiceProvider() { }

    public object? GetService(Type serviceType) =>
        serviceType == typeof(IServiceProvider) ? this : null;
}
