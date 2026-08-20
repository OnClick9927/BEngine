using BEngine.DependencyInjection;
using BEngine.Entities;
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
            VerifyWorldScopeIsolationAndDisposal();
            VerifyPackageModuleDiscovery();
            VerifyEcsSystemConstructorInjection();
            VerifyLegacyRuntimeSystemConstructorInjection();
            HostCompositionRootContract.Verify();
            global::System.Console.WriteLine(
                "DEPENDENCY_INJECTION_ARCHITECTURE_OK|dotnet-lifetimes,core-abstractions,world-scopes,scope-disposal," +
                "module-discovery,ecs-constructor-injection,legacy-constructor-injection,host-composition-roots");
            return 0;
        }
        catch (Exception exception)
        {
            global::System.Console.Error.WriteLine($"DEPENDENCY_INJECTION_ARCHITECTURE_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyCoreDependencyBoundary()
    {
        var references = typeof(World).Assembly.GetReferencedAssemblies();
        Require(references.Any(reference =>
                reference.Name == "Microsoft.Extensions.DependencyInjection.Abstractions"),
            "BEngine Core does not expose the standard Microsoft DI abstractions.");
        Require(references.All(reference =>
                reference.Name != "Microsoft.Extensions.DependencyInjection"),
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
        using var secondScope = provider.CreateScope();
        using var thirdScope = provider.CreateScope();
        Require(!ReferenceEquals(secondScope.ServiceProvider.GetRequiredService<ScopedProbe>(),
                thirdScope.ServiceProvider.GetRequiredService<ScopedProbe>()),
            "Two Microsoft DI scopes shared a scoped service instance.");

        var ownedProvider = new ServiceCollection().AddSingleton<DisposableProbe>()
            .BuildServiceProvider();
        var rootDisposable = ownedProvider.GetRequiredService<DisposableProbe>();
        ownedProvider.Dispose();
        Require(rootDisposable.IsDisposed,
            "Disposing the root ServiceProvider did not dispose its owned singleton.");
    }

    private static void VerifyWorldScopeIsolationAndDisposal()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SingletonProbe>();
        services.AddScoped<ScopedProbe>();
        services.AddScoped<DisposableProbe>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var worldA = new World("IoC World A", provider);
        using var worldB = new World("IoC World B", provider);

        var scopedA = worldA.Services.GetRequiredService<ScopedProbe>();
        var scopedB = worldB.Services.GetRequiredService<ScopedProbe>();
        Require(ReferenceEquals(scopedA, worldA.Services.GetRequiredService<ScopedProbe>()),
            "World.Services did not preserve a scoped instance inside one World.");
        Require(!ReferenceEquals(scopedA, scopedB),
            "Two Worlds created from one root provider shared their scoped service.");
        Require(ReferenceEquals(worldA.Services.GetRequiredService<SingletonProbe>(),
                worldB.Services.GetRequiredService<SingletonProbe>()),
            "World scopes did not share a root singleton.");

        var worldDisposable = worldA.Services.GetRequiredService<DisposableProbe>();
        worldA.Dispose();
        Require(worldDisposable.IsDisposed, "Disposing a World did not dispose its IServiceScope.");
        Require(provider.GetRequiredService<SingletonProbe>() is not null,
            "Disposing a World disposed the externally-owned root provider.");
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
        Require(typeof(World).Assembly.GetReferencedAssemblies().All(reference =>
                !string.Equals(reference.Name, testAssemblyName, StringComparison.Ordinal)),
            "BEngine Core has a compile-time dependency on the package service module.");
    }

    private static void VerifyEcsSystemConstructorInjection()
    {
        using var provider = CreateEngineProvider(CreateContext("ECS injection contract"));
        using var world = new World("Injected ECS World", provider);
        var system = world.SimulationSystemGroup.GetOrCreateSystem<InjectedEcsSystem>();

        Require(ReferenceEquals(system.Dependency, world.Services.GetRequiredService<ScopedProbe>()),
            "SimulationSystemGroup did not construct ISystem from World.Services.");
        Require(ReferenceEquals(system, world.SimulationSystemGroup.GetOrCreateSystem<InjectedEcsSystem>()),
            "GetOrCreateSystem created the same ECS system more than once.");
        Require(system.CreateCount == 1, "The DI-created ECS system did not run OnCreate exactly once.");
    }

    private static void VerifyLegacyRuntimeSystemConstructorInjection()
    {
        using var provider = CreateEngineProvider(CreateContext("Legacy runtime injection contract"));
        var scene = new Scene("Injected legacy runtime", provider);
        var observation = provider.GetRequiredService<LegacyRuntimeObservation>();
        var expectedDependency = scene.world.Services.GetRequiredService<ScopedProbe>();
        var runtime = new SceneRuntime(scene);
        try
        {
            runtime.Start();
            Require(observation.StartCount == 1,
                "SceneRuntime did not resolve and start the DI-registered legacy runtime system exactly once.");
            Require(observation.DependencyId == expectedDependency.Id,
                "The legacy runtime system was not constructed from the Scene World scope.");
        }
        finally
        {
            runtime.Stop();
            scene.world.Dispose();
        }
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
