using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace BEngine;

internal sealed class TextureAtlasYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(TextureAtlas);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var data = rootDeserializer(typeof(TextureAtlasYamlData)) as TextureAtlasYamlData ??
                   throw new InvalidDataException("The TextureAtlas YAML document is empty.");
        return data.ToAsset();
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is not TextureAtlas atlas)
            throw new InvalidDataException($"Expected {nameof(TextureAtlas)}, received {value?.GetType().FullName}.");
        serializer(TextureAtlasYamlData.FromAsset(atlas), typeof(TextureAtlasYamlData));
    }
}

internal sealed class TextureAtlasYamlData
{
    public string Format { get; set; } = "BEngine.TextureAtlas";
    public string Texture { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int MaxSize { get; set; } = 2048;
    public int Padding { get; set; } = 2;
    public int Extrude { get; set; } = 1;
    public List<TextureAtlasSourceYamlData> Sources { get; set; } = [];
    public List<TextureAtlasSprite> Sprites { get; set; } = [];
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HideFlags HideFlags { get; set; }

    // Read-only compatibility with the previous versioned/reference-list formats.
    public int? Version { get; set; }
    public List<string>? SpriteReferences { get; set; }

    internal static TextureAtlasYamlData FromAsset(TextureAtlas atlas)
    {
        var sources = atlas.Sources.Select(TextureAtlasSourceYamlData.FromSprite).ToList();
        return new TextureAtlasYamlData
        {
            Format = atlas.Format,
            Texture = atlas.Texture,
            Width = atlas.Width,
            Height = atlas.Height,
            MaxSize = atlas.MaxSize,
            Padding = atlas.Padding,
            Extrude = atlas.Extrude,
            Sources = sources,
            Sprites = atlas.Sprites,
            Id = atlas.Id,
            Name = atlas.name,
            HideFlags = atlas.hideFlags
        };
    }

    internal TextureAtlas ToAsset()
    {
        Sources ??= [];
        Sprites ??= [];
        var sources = Sources.Select(source => source.ToSprite()).ToList();
        if (sources.Count == 0 && SpriteReferences is { Count: > 0 })
            sources.AddRange(SpriteReferences.Select(TextureAtlasSourceYamlData.FromLegacyReference)
                .Select(source => source.ToSprite()));
        var atlas = new TextureAtlas
        {
            Format = Format,
            Texture = Texture,
            Width = Width,
            Height = Height,
            MaxSize = MaxSize,
            Padding = Padding,
            Extrude = Extrude,
            Sources = sources.ToArray(),
            Sprites = Sprites,
            name = Name,
            hideFlags = HideFlags
        };
        if (Id != Guid.Empty) atlas.Id = Id;
        return atlas;
    }
}

internal sealed class TextureAtlasSourceYamlData
{
    public string OwnerGuid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; }

    // Legacy fields are accepted when reading, but FromSprite never populates them.
    public string? Texture { get; set; }
    public string? Path { get; set; }
    public string? AssetPath { get; set; }
    public string? Name { get; set; }
    public float? PivotX { get; set; }
    public float? PivotY { get; set; }

    internal static TextureAtlasSourceYamlData FromSprite(Sprite sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        var ownerGuid = sprite.OwnerGuid?.Trim() ?? string.Empty;
        if (!Guid.TryParse(ownerGuid, out _))
            throw new InvalidDataException(
                $"Texture atlas Sprite '{sprite.name}' does not have a valid owner GUID.");
        if (sprite.LocalIdentifier <= 0)
            throw new InvalidDataException(
                $"Texture atlas Sprite '{sprite.name}' does not have a valid local identifier.");
        return new TextureAtlasSourceYamlData
        {
            OwnerGuid = ownerGuid,
            LocalIdentifier = sprite.LocalIdentifier
        };
    }

    internal static TextureAtlasSourceYamlData FromLegacyReference(string reference)
    {
        var normalized = reference?.Replace('\\', '/').Trim() ?? string.Empty;
        return Guid.TryParse(normalized, out _)
            ? new TextureAtlasSourceYamlData { OwnerGuid = normalized, LocalIdentifier = 21300000 }
            : new TextureAtlasSourceYamlData { Path = normalized, LocalIdentifier = 21300000 };
    }

    internal Sprite ToSprite()
    {
        var ownerGuid = OwnerGuid?.Trim() ?? string.Empty;
        var localIdentifier = LocalIdentifier > 0 ? LocalIdentifier : 21300000;
        if (Guid.TryParse(ownerGuid, out _))
        {
            var reference = $"guid:{ownerGuid}#subasset={localIdentifier}";
            try
            {
                if (BAssetReferenceLoader.LoadObjectReference(reference, typeof(Sprite)) is Sprite resolved)
                    return resolved;
            }
            catch (Exception exception) when (IsRecoverableReferenceException(exception))
            {
                Debug.LogWarning($"Could not resolve TextureAtlas Sprite reference '{reference}': " +
                                 exception.Message);
            }
        }

        var texturePath = FirstNonEmpty(Texture, Path, AssetPath);
        var pivot = new Vector2(
            (Fix64)(double)Math.Clamp(PivotX ?? 0.5f, 0, 1),
            (Fix64)(double)Math.Clamp(PivotY ?? 0.5f, 0, 1));
        Sprite sprite;
        if (texturePath.Length > 0)
        {
            try
            {
                sprite = BAsset.Load<Texture>(texturePath)?.CreateSprite(pivot, localIdentifier) ??
                         Sprite.Create(texturePath, pivot, texturePath, ownerGuid, localIdentifier);
            }
            catch (Exception exception) when (IsRecoverableReferenceException(exception))
            {
                sprite = Sprite.Create(texturePath, pivot, texturePath, ownerGuid, localIdentifier);
            }
        }
        else
        {
            sprite = new Sprite();
            sprite.BindSourceIdentity(ownerGuid, localIdentifier, string.Empty);
        }
        if (!string.IsNullOrWhiteSpace(Name)) sprite.name = Name.Trim();
        return sprite;
    }

    private static string FirstNonEmpty(params string?[] values) => values
        .Select(value => value?.Replace('\\', '/').Trim() ?? string.Empty)
        .FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static bool IsRecoverableReferenceException(Exception exception) => exception is IOException or
        UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException or
        OverflowException or YamlDotNet.Core.YamlException;
}
