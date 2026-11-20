using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;

namespace BEngine.Platform;

internal sealed class DotNetBackgroundWorkExecutor : IBackgroundWorkExecutor
{
    public bool IsSupported => !OperatingSystem.IsBrowser();
    public Task<T> Run<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken) =>
        Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); return work(cancellationToken); }, cancellationToken);

    [ModuleInitializer]
    internal static void Register() => RuntimeServiceContributions.RegisterDefault<IBackgroundWorkExecutor>(new DotNetBackgroundWorkExecutor());
}
