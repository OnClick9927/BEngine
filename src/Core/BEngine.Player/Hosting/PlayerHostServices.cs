using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

/// <summary>Host services used when composing a Player without exposing their implementations.</summary>
public static class PlayerHostServices
{
    /// <summary>Sets the host-selected physical/virtual data directory. Does not create files.
    /// Call during host initialization, before activating resource providers.</summary>
    public static void ConfigureDataDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Application.dataPath = RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>().GetFullPath(directory);
    }

    /// <summary>Applies an absolute persistent-data override to the supplied services, borrowing the owner's platform services.</summary>
    /// <returns>The normalized override, or null if no override is configured. No directory is created.</returns>
    public static string? ConfigurePersistentDataDirectory(IServiceCollection services,
        string? explicitPath = null, IServiceProvider? owningServices = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return PlayerPersistentApplicationPaths.Configure(services, explicitPath, owningServices);
    }

    /// <summary>Creates an ordered log sink that owns the writer after successful construction.</summary>
    /// <remarks>Disposal flushes accepted records and disposes the writer. Hosts without threads can select synchronous writing.</remarks>
    public static IPlayerLogWriter CreateBufferedLogWriter(TextWriter writer, Action<string> fallback,
        bool useBackgroundThread = true, int capacity = 4096, int characterBudget = 2 * 1024 * 1024,
        int flushIntervalMilliseconds = 100) =>
        new BufferedPlayerLogWriter(writer, fallback, useBackgroundThread,
            capacity, characterBudget, flushIntervalMilliseconds);
}
