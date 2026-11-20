using System.Reflection;
using YamlDotNet.RepresentationModel;

namespace BEngine.Editor.Build;

/// <summary>Reads build documents while retaining scalar options owned by external content providers.</summary>
public static class PlayerBuildSettingsSerialization
{
    private static readonly HashSet<string> KnownProperties = new(
        typeof(PlayerBuildSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name), StringComparer.OrdinalIgnoreCase);

    public static PlayerBuildSettings DeserializeSettings(string yaml)
    {
        var settings = YamlUtility.Deserialize<PlayerBuildSettings>(yaml);
        PreserveExternalOptions(settings, ReadRoot(yaml));
        return settings;
    }

    public static PlayerBuildProfilesDocument DeserializeProfiles(string yaml)
    {
        var document = YamlUtility.Deserialize<PlayerBuildProfilesDocument>(yaml);
        var root = ReadRoot(yaml);
        if (document.Profiles is not null &&
            root.Children.TryGetValue(new YamlScalarNode("profiles"), out var profiles) &&
            profiles is YamlSequenceNode sequence)
        {
            for (var index = 0; index < Math.Min(document.Profiles.Count, sequence.Children.Count); index++)
                if (document.Profiles[index]?.Settings is { } profileSettings &&
                    sequence.Children[index] is YamlMappingNode profile &&
                    profile.Children.TryGetValue(new YamlScalarNode("settings"), out var value) &&
                    value is YamlMappingNode settings)
                    PreserveExternalOptions(profileSettings, settings);
        }
        return document;
    }

    private static YamlMappingNode ReadRoot(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents.Count == 1 && stream.Documents[0].RootNode is YamlMappingNode root
            ? root : throw new InvalidDataException("A build settings document must contain one mapping.");
    }

    private static void PreserveExternalOptions(PlayerBuildSettings settings, YamlMappingNode document)
    {
        settings.ContentOptions ??= new(StringComparer.Ordinal);
        foreach (var pair in document.Children)
            if (pair.Key is YamlScalarNode { Value: { } name } && !KnownProperties.Contains(name) &&
                pair.Value is YamlScalarNode { Value: { } value })
                settings.ContentOptions.TryAdd("legacy." + name, value);
    }
}
