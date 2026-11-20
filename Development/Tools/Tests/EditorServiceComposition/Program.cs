using System.Reflection;
using System.Text.Json;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;

var checks = new List<string>();
void Check(string label, bool ok) { if (!ok) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS|" + label); }
BEngine.Platform.DotNet.DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
var access = typeof(EditorPlatformServiceCollectionExtensions).Assembly.GetType("BEngine.Editor.EditorServiceAccess", true)!;
IDisposable Attach(IServiceProvider host) => EditorServiceScope.Attach(host);
T? Resolve<T>() where T : class => EditorServices.GetService<T>();
RuntimeServiceContributions.Register(static services => services.AddSingleton<SharedState>());
using var host = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<HostOnly>().AddScoped<HostScoped>().AddSingleton<UnusedHost>()
    .AddSingleton<IProbe, FirstProbe>().AddSingleton<IProbe, SecondProbe>()
    .AddKeyedSingleton<SharedState>("host").BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
var attachment = Attach(host);
var hostOnly = host.GetRequiredService<HostOnly>();
var shared = host.GetRequiredService<SharedState>();
RuntimeServiceContributions.Register(static services => services
    .AddSingleton<ConstructorConsumer>().AddSingleton<FactoryConsumer>(sp => new(sp.GetRequiredService<HostOnly>()))
    .AddSingleton<ProviderConsumer>().AddSingleton<MultiConsumer>().AddSingleton<DefaultConsumer>()
    .AddSingleton<IHostAlias>(sp => sp.GetRequiredService<HostOnly>())
    .AddSingleton<ILateBox<string>, SpecificBox>().AddSingleton(typeof(ILateBox<>), typeof(LateBox<>))
    .AddTransient<TransientService>().AddScoped<ScopedService>().AddSingleton<OwnedService>()
    .AddSingleton<IProbe, ThirdProbe>().AddSingleton<CycleA>().AddSingleton<CycleB>()
    .AddKeyedSingleton<KeyedConsumer>("late").AddKeyedSingleton<KeyedFactoryConsumer>("factory", (sp, _) => new(sp.GetRequiredKeyedService<SharedState>("host"))));
var consumer = Resolve<ConstructorConsumer>()!;
Check("Constructor dependencies reuse host singleton", ReferenceEquals(consumer.State, shared));
Check("Constructor resolves host-only registration", ReferenceEquals(consumer.Host, hostOnly));
Check("Factory dependencies reuse host instances", ReferenceEquals(Resolve<FactoryConsumer>()!.Host, hostOnly));
Check("Direct host resolution preserves identity", ReferenceEquals(Resolve<SharedState>(), shared));
Check("Fallback does not instantiate unused host services", UnusedHost.Created == 0);
Check("Extension singleton remains stable", ReferenceEquals(Resolve<ConstructorConsumer>(), consumer));
Check("Optional constructor defaults preserved", Resolve<DefaultConsumer>()!.Count == 37);
var multi = Resolve<IEnumerable<IProbe>>()!.ToArray();
Check("Host and late enumerable registrations merge without duplicates", multi.Length == 3 && multi[0] is FirstProbe && multi[1] is SecondProbe && multi[2] is ThirdProbe);
Check("Enumerable injection sees late contribution", Resolve<MultiConsumer>()!.Probes.Count() == 3);
Check("Inherited enumerable entries retain host identity", ReferenceEquals(multi[0], host.GetServices<IProbe>().First()));
Func<HostOnly?> direct = EditorServices.GetService<HostOnly>;
for (var i = 0; i < 10000; i++) _ = direct();
var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
var ticksBefore = System.Diagnostics.Stopwatch.GetTimestamp();
for (var i = 0; i < 100000; i++) _ = direct();
var directMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(ticksBefore).TotalMilliseconds;
var directAllocation = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
Check("100000 existing-host lookups allocate no managed memory", directAllocation == 0);
var provider = Resolve<ProviderConsumer>()!.Services;
Check("Injected provider resolves host-only service", ReferenceEquals(provider.GetRequiredService<HostOnly>(), hostOnly));
Check("Keyed constructor dependencies resolve host key", ReferenceEquals(provider.GetRequiredKeyedService<KeyedConsumer>("late").State, host.GetRequiredKeyedService<SharedState>("host")));
Check("Keyed factory dependencies resolve host key", ReferenceEquals(provider.GetRequiredKeyedService<KeyedFactoryConsumer>("factory").State, host.GetRequiredKeyedService<SharedState>("host")));
Check("Open generic service resolves host constructor dependency", ReferenceEquals(provider.GetRequiredService<ILateBox<int>>().Host, hostOnly));
Check("Open generic singleton is cached per closed type", ReferenceEquals(provider.GetRequiredService<ILateBox<int>>(), provider.GetRequiredService<ILateBox<int>>()));
Check("Exact generic registration takes precedence", provider.GetRequiredService<ILateBox<string>>() is SpecificBox);
Check("Generic enumeration preserves registration order", provider.GetServices<ILateBox<string>>().Select(p => p.GetType()).SequenceEqual(new[] { typeof(SpecificBox), typeof(LateBox<string>) }));
var transient1 = provider.GetRequiredService<TransientService>(); var transient2 = provider.GetRequiredService<TransientService>();
Check("Transient services retain transient lifetime", !ReferenceEquals(transient1, transient2));
var owned = provider.GetRequiredService<OwnedService>();
Check("Alias factory borrows host disposable", ReferenceEquals(provider.GetRequiredService<IHostAlias>(), hostOnly));
ScopedService scoped;
HostScoped inheritedScoped;
using (var scope = provider.CreateScope())
{
    scoped = scope.ServiceProvider.GetRequiredService<ScopedService>();
    inheritedScoped = scoped.Host;
    Check("Extension scoped lifetime is stable within scope", ReferenceEquals(scoped, scope.ServiceProvider.GetRequiredService<ScopedService>()));
    Check("Scoped dependency shares parent host scope", ReferenceEquals(inheritedScoped, scope.ServiceProvider.GetRequiredService<HostScoped>()));
    Check("Singleton is shared across extension scopes", ReferenceEquals(owned, scope.ServiceProvider.GetRequiredService<OwnedService>()));
}
Check("Extension scope disposes its own scoped instance once", scoped.Disposals == 1);
Check("Extension scope disposes its owned host scope once", inheritedScoped.Disposals == 1);
Check("Closing child scope preserves host singleton", hostOnly.Disposals == 0);
using (var scope = provider.CreateScope())
    Check("Separate scopes receive distinct scoped instances", !ReferenceEquals(scoped, scope.ServiceProvider.GetRequiredService<ScopedService>()));
try { _ = provider.GetRequiredService<CycleA>(); throw new Exception("Cycle was accepted"); }
catch (InvalidOperationException exception) { Check("Circular dependency is rejected explicitly", exception.Message.Contains("Circular")); }
RuntimeServiceContributions.Register(static services => services.AddSingleton<InvalidationMarker>());
Check("Registration invalidation constructs fresh extensions", Resolve<InvalidationMarker>() is not null);
Check("Invalidation disposes owned extension singleton once", owned.Disposals == 1);
Check("Invalidation disposes owned transients once", transient1.Disposals == 1 && transient2.Disposals == 1);
Check("Invalidation does not dispose host-owned aliases", hostOnly.Disposals == 0);
Check("Rebuilt extension still shares original host", ReferenceEquals(Resolve<ConstructorConsumer>()!.Host, hostOnly));
var lastOwned = Resolve<OwnedService>()!;
attachment.Dispose();
Check("Detaching host releases extension scope immediately", lastOwned.Disposals == 1);
Check("Detaching host leaves host-owned singleton alive", hostOnly.Disposals == 0);
RuntimeServiceContributions.UnregisterAssembly(typeof(HostOnly).Assembly);
host.Dispose();
Check("Host disposes borrowed singleton exactly once", hostOnly.Disposals == 1);

// Direct extension scope checks also cover async ownership, constrained generics,
// externally supplied instances, and disposal failure cleanup without a GUI host.
var scopeType = access.Assembly.GetType("BEngine.Editor.EditorContributionServices", true)!;
object NewScope(params ServiceDescriptor[] registrations) => Activator.CreateInstance(scopeType, BindingFlags.Instance | BindingFlags.NonPublic, null, [null, registrations], null)!;
var external = new OwnedService();
var isolated = NewScope(ServiceDescriptor.Singleton(external), ServiceDescriptor.Singleton<AsyncOwned, AsyncOwned>(),
    ServiceDescriptor.Singleton(typeof(IConstrained<>), typeof(Constrained<>)));
var isolatedProvider = (IServiceProvider)isolated;
Check("External instance registration preserves identity", ReferenceEquals(external, isolatedProvider.GetRequiredService<OwnedService>()));
var asyncOwned = isolatedProvider.GetRequiredService<AsyncOwned>();
Check("Invalid generic constraints are skipped in enumeration", !isolatedProvider.GetServices<IConstrained<int>>().Any());
Check("Valid generic constraints still resolve", isolatedProvider.GetServices<IConstrained<string>>().Count() == 1);
await ((IAsyncDisposable)isolated).DisposeAsync();
Check("Async-only extension disposal is awaited once", asyncOwned.Disposals == 1);
Check("Externally supplied instance is not disposed", external.Disposals == 0);
await ((IAsyncDisposable)isolated).DisposeAsync();
Check("Repeated async disposal is idempotent", asyncOwned.Disposals == 1);

var keyedScope = NewScope(ServiceDescriptor.KeyedSingleton<KeyedValue>("alpha", (_, key) => new(key)),
    ServiceDescriptor.KeyedSingleton<KeyedValue>("beta", (_, key) => new(key)),
    ServiceDescriptor.KeyedSingleton<KeyedValue>(KeyedService.AnyKey, (_, key) => new(key)));
var keyed = (IServiceProvider)keyedScope;
Check("AnyKey fallback receives requested key", Equals(keyed.GetRequiredKeyedService<KeyedValue>("gamma").Key, "gamma"));
Check("AnyKey enumeration contains explicit keyed registrations only", keyed.GetKeyedServices<KeyedValue>(KeyedService.AnyKey).Select(v => v.Key).SequenceEqual(new[] { "alpha", "beta" }));
Check("Keyed enumeration preserves singleton identity", ReferenceEquals(keyed.GetKeyedServices<KeyedValue>("alpha").Single(), keyed.GetRequiredKeyedService<KeyedValue>("alpha")));
try { _ = keyed.GetKeyedService<KeyedValue>(KeyedService.AnyKey); throw new Exception("AnyKey scalar accepted"); }
catch (InvalidOperationException) { Check("AnyKey scalar resolution is rejected", true); }
((IDisposable)keyedScope).Dispose();
try { _ = keyed.GetService<KeyedValue>(); throw new Exception("Disposed scope accepted"); }
catch (ObjectDisposedException) { Check("Disposed extension scope rejects resolution", true); }

var failureScope = NewScope(ServiceDescriptor.Singleton<OwnedService, OwnedService>(), ServiceDescriptor.Singleton<ThrowingDisposable, ThrowingDisposable>());
var failureProvider = (IServiceProvider)failureScope;
var survivor = failureProvider.GetRequiredService<OwnedService>();
_ = failureProvider.GetRequiredService<ThrowingDisposable>();
try { ((IDisposable)failureScope).Dispose(); throw new Exception("Disposal failure was swallowed"); }
catch (AggregateException error) { Check("Disposal reports errors and releases remaining instances", error.InnerExceptions.Count == 1 && survivor.Disposals == 1); }
((IDisposable)failureScope).Dispose();
Check("Repeated sync disposal is idempotent after error", survivor.Disposals == 1);

var result = new { Passed = true, Checks = checks.Count, DirectLookups = 100000, DirectLookupMilliseconds = directMilliseconds, DirectLookupAllocatedBytes = directAllocation, Details = checks };
File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("EDITOR_SERVICE_COMPOSITION_OK|" + checks.Count);

public sealed class SharedState;
public interface IHostAlias;
public sealed class HostOnly : IHostAlias, IDisposable { public int Disposals; public void Dispose() => Disposals++; }
public sealed class UnusedHost { public static int Created; public UnusedHost() => Created++; }
public sealed class HostScoped : IDisposable { public int Disposals; public void Dispose() => Disposals++; }
public sealed class ConstructorConsumer(SharedState state, HostOnly host) { public SharedState State = state; public HostOnly Host = host; }
public sealed class FactoryConsumer(HostOnly host) { public HostOnly Host = host; }
public sealed class ProviderConsumer(IServiceProvider services) { public IServiceProvider Services = services; }
public sealed class DefaultConsumer(int count = 37) { public int Count = count; }
public sealed class MultiConsumer(IEnumerable<IProbe> probes) { public IEnumerable<IProbe> Probes = probes; }
public interface IProbe;
public sealed class FirstProbe : IProbe;
public sealed class SecondProbe : IProbe;
public sealed class ThirdProbe : IProbe;
public interface ILateBox<T> { HostOnly Host { get; } }
public sealed class LateBox<T>(HostOnly host) : ILateBox<T> { public HostOnly Host { get; } = host; }
public sealed class SpecificBox(HostOnly host) : ILateBox<string> { public HostOnly Host { get; } = host; }
public sealed class OwnedService : IDisposable { public int Disposals; public void Dispose() => Disposals++; }
public sealed class TransientService : IDisposable { public int Disposals; public void Dispose() => Disposals++; }
public sealed class ScopedService(HostScoped host) : IDisposable { public HostScoped Host = host; public int Disposals; public void Dispose() => Disposals++; }
public sealed class CycleA(CycleB b) { public CycleB Dependency = b; }
public sealed class CycleB(CycleA a) { public CycleA Dependency = a; }
public sealed class InvalidationMarker;
public sealed class KeyedConsumer([FromKeyedServices("host")] SharedState state) { public SharedState State = state; }
public sealed class KeyedFactoryConsumer(SharedState state) { public SharedState State = state; }
public sealed class AsyncOwned : IAsyncDisposable { public int Disposals; public async ValueTask DisposeAsync() { await Task.Yield(); Disposals++; } }
public sealed class KeyedValue(object? key) { public object? Key = key; }
public sealed class ThrowingDisposable : IDisposable { public void Dispose() => throw new InvalidOperationException("Expected test failure"); }
public interface IConstrained<T>;
public sealed class Constrained<T> : IConstrained<T> where T : class;
