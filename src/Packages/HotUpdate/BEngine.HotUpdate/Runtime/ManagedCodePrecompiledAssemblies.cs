using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace BEngine.HotUpdate;

/// <summary>
/// Identifies release assemblies compiled into an opt-in Mono/WebAssembly
/// Player. Registering an identity does not load the assembly or make it a
/// host assembly. Its ordinary HotUpdate module lifecycle remains owned by
/// the release. Changing its code requires rebuilding the complete Player.
/// </summary>
public static class ManagedCodePrecompiledAssemblies
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ClaimedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Assembly, int> ActiveUses = new();
    private static readonly HashSet<string> RetiredSharedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string name, string expectedSha256, string expectedMvid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMvid);
        if (!Guid.TryParse(expectedMvid, out var mvid) || mvid == Guid.Empty)
            throw new ArgumentException("A non-empty assembly MVID is required.", nameof(expectedMvid));
        Register(name, expectedSha256, mvid);
    }

    public static void Register(string name, string expectedSha256, Guid expectedMvid)
        => RegisterIdentity(name, expectedSha256, expectedMvid, shared: false);

    /// <summary>Registers an exact canonical dependency which overlapping releases may share.</summary>
    public static void RegisterShared(string name, string expectedSha256, string expectedMvid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMvid);
        if (!Guid.TryParse(expectedMvid, out var mvid) || mvid == Guid.Empty)
            throw new ArgumentException("A non-empty assembly MVID is required.", nameof(expectedMvid));
        RegisterShared(name, expectedSha256, mvid);
    }

    public static void RegisterShared(string name, string expectedSha256, Guid expectedMvid)
        => RegisterIdentity(name, expectedSha256, expectedMvid, shared: true);

    private static void RegisterIdentity(string name, string expectedSha256, Guid expectedMvid, bool shared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if (name != name.Trim() || name.Any(character => char.IsControl(character) ||
                character is '/' or '\\' or ',' or ':' or '=' or '"' or '\'' or ';') ||
            name is "." or ".." || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A simple assembly name without an extension or path is required.", nameof(name));
        if (expectedMvid == Guid.Empty)
            throw new ArgumentException("A non-empty assembly MVID is required.", nameof(expectedMvid));
        if (expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("An exact 64-character SHA-256 hexadecimal value is required.", nameof(expectedSha256));
        var registration = new Registration(name, expectedSha256.ToUpperInvariant(), expectedMvid, shared);
        lock (Gate)
        {
            if (Registrations.TryGetValue(name, out var existing))
            {
                if (existing.Sha256 == registration.Sha256 && existing.Mvid == registration.Mvid && existing.Shared == shared) return;
                throw new InvalidOperationException($"Precompiled assembly '{name}' already has a different identity.");
            }
            Registrations.Add(name, registration);
        }
    }

    // Explicit registration selects this path, independently of reflection-
    // based runtime detection which may change when Mono metadata is trimmed.
    internal static ValidatedRelease? ValidateAndClaim(
        ManagedCodeRelease release, BCancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, Registration> registrations;
        lock (Gate)
        {
            if (Registrations.Count == 0) return null;
            registrations = new Dictionary<string, Registration>(Registrations, StringComparer.OrdinalIgnoreCase);
        }
        var validated = new Dictionary<string, ValidatedAssembly>(StringComparer.OrdinalIgnoreCase);
        // Complete every hash/metadata/default-context check before claiming a
        // domain or loading anything, including dependencies of an early root.
        foreach (var module in release.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!registrations.TryGetValue(module.Name, out var registration)) continue;
            var image = module.GetAssemblyImage();
            if (!Convert.ToHexString(SHA256.HashData(image)).Equals(registration.Sha256, StringComparison.Ordinal))
                throw RebuildRequired(module.Name, "its release code hash differs from the Player");
            var identity = ReadIdentity(image, module.GetSymbols(), registration);
            if (ManagedCodeHostAssemblies.Find(module.Name) is not null)
                throw RebuildRequired(module.Name, "it was incorrectly registered as a shared host assembly");
            foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
            {
                if (!module.Name.Equals(loaded.GetName().Name, StringComparison.OrdinalIgnoreCase)) continue;
                VerifyLoaded(loaded, identity);
            }
            validated.Add(module.Name, identity);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (validated.Count == 0) return null;
        lock (Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retired = validated.Keys.FirstOrDefault(RetiredSharedAssemblies.Contains);
            if (retired is not null)
                throw new InvalidOperationException(
                    $"Shared managed-code dependency '{retired}' was already released by its last owner. " +
                    "Restart the Player before loading it again; its module initializer cannot be repeated.");
            var repeated = validated.Values.FirstOrDefault(image => !image.Registration.Shared && ClaimedAssemblies.Contains(image.Registration.Name));
            if (repeated is not null)
                throw new InvalidOperationException(
                    $"Precompiled managed-code module '{repeated.Registration.Name}' was already claimed by an earlier release " +
                    "in this Player process. Restart the Player before loading that module again, including " +
                    "after a failed or canceled load. Disjoint bootstrap and game releases remain supported.");
            foreach (var image in validated.Values)
                if (!image.Registration.Shared) ClaimedAssemblies.Add(image.Registration.Name);
        }
        return new ValidatedRelease(validated);
    }

    private static ValidatedAssembly ReadIdentity(byte[] image, byte[]? symbols, Registration registration)
    {
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) throw RebuildRequired(registration.Name, "its release image has no managed metadata");
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) throw RebuildRequired(registration.Name, "its release image is not an assembly");
            var definition = metadata.GetAssemblyDefinition();
            var name = metadata.GetString(definition.Name);
            var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            if (!registration.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || mvid != registration.Mvid)
                throw RebuildRequired(registration.Name, "its release assembly name or MVID differs from the Player");
            var identity = new AssemblyName { Name = name, Version = definition.Version, CultureName = string.Empty };
            if (!definition.Culture.IsNil) identity.CultureName = metadata.GetString(definition.Culture);
            if (!definition.PublicKey.IsNil) identity.SetPublicKey(metadata.GetBlobBytes(definition.PublicKey));
            else identity.SetPublicKeyToken([]);
            return new ValidatedAssembly(registration, identity, image, symbols);
        }
        catch (BadImageFormatException exception)
        {
            throw RebuildRequired(registration.Name, "its release assembly metadata is invalid", exception);
        }
    }

    private static void VerifyLoaded(Assembly assembly, ValidatedAssembly expected)
    {
        if (!expected.Registration.Name.Equals(assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            !expected.Identity.FullName.Equals(assembly.GetName().FullName, StringComparison.OrdinalIgnoreCase) ||
            assembly.ManifestModule.ModuleVersionId != expected.Registration.Mvid)
            throw RebuildRequired(expected.Registration.Name,
                "the load context contains a different assembly identity or MVID");
    }

    private static InvalidDataException RebuildRequired(string name, string reason, Exception? inner = null)
        => new($"Precompiled managed-code module '{name}' cannot be activated because {reason}. " +
               "Rebuild and redeploy the complete Web Player for code changes; an AssetBundle-only update " +
               "cannot replace code compiled into this Player.", inner);

    internal static bool AcquireUse(Assembly assembly)
    {
        lock (Gate)
        {
            var name = assembly.GetName().Name!;
            if (!Registrations.TryGetValue(name, out var registration)) return false;
            if (assembly.ManifestModule.ModuleVersionId != registration.Mvid)
                throw RebuildRequired(name, "its loaded canonical dependency has a different MVID");
            if (RetiredSharedAssemblies.Contains(name))
                throw new InvalidOperationException($"Shared managed-code dependency '{name}' was already released. Restart the Player.");
            ActiveUses[assembly] = ActiveUses.GetValueOrDefault(assembly) + 1;
            return true;
        }
    }

    internal static void ReleaseUses(IEnumerable<Assembly> assemblies)
    {
        lock (Gate)
        {
            foreach (var assembly in assemblies)
            {
                if (!ActiveUses.TryGetValue(assembly, out var count))
                    throw new InvalidOperationException("The canonical managed-code assembly lease was already released.");
                if (count > 1) { ActiveUses[assembly] = count - 1; continue; }
                ActiveUses.Remove(assembly);
                var name = assembly.GetName().Name!;
                if (Registrations[name].Shared) RetiredSharedAssemblies.Add(name);
            }
        }
    }

    internal static Assembly[] GetUnleasedAssemblies(IEnumerable<Assembly> assemblies)
    {
        lock (Gate) return assemblies.Where(assembly => !ActiveUses.ContainsKey(assembly)).Distinct().ToArray();
    }

    internal sealed record Registration(string Name, string Sha256, Guid Mvid, bool Shared);
    internal sealed record ValidatedAssembly(Registration Registration, AssemblyName Identity, byte[] Image, byte[]? Symbols);

    internal sealed class ValidatedRelease
    {
        private readonly Dictionary<string, ValidatedAssembly> _assemblies;

        internal ValidatedRelease(Dictionary<string, ValidatedAssembly> assemblies) => _assemblies = assemblies;

        internal bool TryResolve(string name, BCancellationToken cancellationToken, out Assembly assembly)
        {
            var loadContext = AssemblyLoadContext.Default;
            assembly = null!;
            if (!_assemblies.TryGetValue(name, out var expected)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            // A dependency may already have been resolved while loading an
            // earlier root. The identity was preflighted for the whole release;
            // reusing that exact assembly within this one claim is legitimate.
            foreach (var loaded in loadContext.Assemblies)
            {
                if (!name.Equals(loaded.GetName().Name, StringComparison.OrdinalIgnoreCase)) continue;
                VerifyLoaded(loaded, expected);
                assembly = loaded;
                return true;
            }
            // Mono binds a streamed IL image to its precompiled code when its
            // exact registered MVID is present. Preserve the ordinary AB image
            // loading pipeline; assembly-name probing is not portable on Wasm.
            using var image = new MemoryStream(expected.Image, writable: false);
            if (expected.Symbols is { Length: > 0 })
            {
                using var symbols = new MemoryStream(expected.Symbols, writable: false);
                assembly = loadContext.LoadFromStream(image, symbols);
            }
            else assembly = loadContext.LoadFromStream(image);
            VerifyLoaded(assembly, expected);
            // The runtime records this owned assembly before its post-load
            // cancellation check, so rollback can unregister its callbacks.
            return true;
        }
    }
}
