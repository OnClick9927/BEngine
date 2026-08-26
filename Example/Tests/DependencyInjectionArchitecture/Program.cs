using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyStandardDotNetLifetimes();
            VerifyCoreDependencyBoundary();
            VerifySceneScopeIsolationAndRuntimeInjection();
            VerifyPackageModuleDiscovery();
            HostCompositionRootContract.Verify();
            PlayerAssetEnvironmentTests.Verify();
            Console.WriteLine(
                "DEPENDENCY_INJECTION_ARCHITECTURE_OK|dotnet-lifetimes,core-abstractions,scene-scopes," +
                "scope-disposal,module-discovery,runtime-system-injection,host-composition-roots," +
                "player-asset-environment");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DEPENDENCY_INJECTION_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyCoreDependencyBoundary()
    {
        var references = typeof(Scene).Assembly.GetReferencedAssemblies();
        Require(references.Any(reference =>
                reference.Name == "Microsoft.Extensions.DependencyInjection.Abstractions"),
            "BEngine Core does not expose the standard Microsoft DI abstractions.");
        Require(references.All(reference => reference.Name != "Microsoft.Extensions.DependencyInjection"),
            "BEngine Core depends on the concrete Microsoft DI provider instead of abstractions only.");
    }

    private static void VerifyStandardDotNetLifetimes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SingletonProbe>();
        services.AddScoped<ScopedProbe>();
        services.AddTransient<TransientProbe>();
        services.AddScoped<DisposableProbe>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Require(ReferenceEquals(provider.GetRequiredService<SingletonProbe>(),
                provider.GetRequiredService<SingletonProbe>()),
            "Microsoft DI singleton lifetime is not preserved by the engine toolchain.");

        DisposableProbe disposable;
        using (var firstScope = provider.CreateScope())
        {
            var first = firstScope.ServiceProvider.GetRequiredService<ScopedProbe>();
            Require(ReferenceEquals(first, firstScope.ServiceProvider.GetRequiredService<ScopedProbe>()),
                "A scoped service changed inside one Microsoft DI scope.");
            Require(!ReferenceEquals(firstScope.ServiceProvider.GetRequiredService<TransientProbe>(),
                    firstScope.ServiceProvider.GetRequiredService<TransientProbe>()),
                "A transient service was reused inside one Microsoft DI scope.");
            disposable = firstScope.ServiceProvider.GetRequiredService<DisposableProbe>();
            Require(!disposable.IsDisposed, "A scoped disposable was disposed before its scope ended.");
        }
        Require(disposable.IsDisposed, "Disposing IServiceScope did not dispose its scoped service.");
    }

    private static void VerifySceneScopeIsolationAndRuntimeInjection()
    {
        using var provider = CreateEngineProvider(CreateContext("Scene runtime injection contract"));
        var observation = provider.GetRequiredService<RuntimeSystemObservation>();
        var firstScene = new Scene("First scoped Scene", provider);
        var secondScene = new Scene("Second scoped Scene", provider);
        var firstRuntime = new SceneRuntime(firstScene);
        var secondRuntime = new SceneRuntime(secondScene);
        try
        {
            firstRuntime.Start();
            firstRuntime.Stop();
            secondRuntime.Start();
            secondRuntime.Stop();

            Require(observation.StartCount == 2 && observation.DependencyIds.Count == 2,
                "SceneRuntime did not resolve one runtime system per Scene.");
            Require(observation.DependencyIds[0] != observation.DependencyIds[1],
                "Two Scenes shared a scoped runtime-system dependency.");
            Require(observation.Disposables.All(item => !item.IsDisposed),
                "A Scene-scoped dependency was disposed before its Scene.");

            firstScene.Dispose();
            Require(observation.Disposables[0].IsDisposed && !observation.Disposables[1].IsDisposed,
                "Disposing the first Scene did not isolate its DI scope.");
            secondScene.Dispose();
            Require(observation.Disposables[1].IsDisposed,
                "Disposing the second Scene did not release its DI scope.");
        }
        finally
        {
            firstRuntime.Stop();
            secondRuntime.Stop();
            firstScene.Dispose();
            secondScene.Dispose();
        }
    }

    private static void VerifyPackageModuleDiscovery()
    {
        var context = CreateContext("Package module contract");
        using var provider = CreateEngineProvider(context);
        var moduleProbe = provider.GetRequiredService<PackageModuleProbe>();

        Require(ReferenceEquals(provider.GetRequiredService<EngineServiceContext>(), context),
            "AddBEngine did not register the active EngineServiceContext as a singleton.");
        Require(ReferenceEquals(moduleProbe.Context, context),
            "IEngineServiceModule did not receive the active EngineServiceContext.");
        var testAssemblyName = typeof(ProbeEngineServiceModule).Assembly.GetName().Name;
        Require(typeof(Scene).Assembly.GetReferencedAssemblies().All(reference =>
                !string.Equals(reference.Name, testAssemblyName, StringComparison.Ordinal)),
            "BEngine Core has a compile-time dependency on the package service module.");
    }

    private static ServiceProvider CreateEngineProvider(EngineServiceContext context)
    {
        var services = new ServiceCollection();
        services.AddBEngine(context, [typeof(ProbeEngineServiceModule).Assembly]);
        services.AddScoped<DisposableProbe>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static EngineServiceContext CreateContext(string instanceName) =>
        new(EngineHostKind.Runtime, AppContext.BaseDirectory, instanceName);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
