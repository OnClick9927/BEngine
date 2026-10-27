using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

public sealed record TiledImportResult(GameObject Root, IReadOnlyList<TilePalette> Palettes, IReadOnlyList<string> Dependencies);

/// <summary>Imports finite/chunked TMX and Tiled JSON tile layers, external TSX/JSON tilesets,
/// spacing, margins, offsets, animation and flip flags. Unsupported layouts are rejected explicitly.</summary>
public static class TiledFileImporter
{
    [MenuItem("Assets/Import Tiled Map", false, 240)]
    private static void ImportMenu()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Import Tiled Map");
        var path = EditorUtility.OpenFilePanelWithFilters("Import Tiled Map", Application.dataPath, ["Tiled maps", "tmx,tmj,json"]);
        if (string.IsNullOrWhiteSpace(path)) return;
        var scene = EditorSceneManager.GetActiveScene() ?? throw new InvalidOperationException("Open a scene before importing a map.");
        var result = Import(path, scene);
        var destination = Path.Combine(Application.dataPath, "Tiled", Path.GetFileNameWithoutExtension(path)); Directory.CreateDirectory(destination);
        var importedTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in result.Palettes.SelectMany(palette => palette.Tiles))
        {
            if (!Path.IsPathRooted(tile.Texture)) continue;
            if (!importedTextures.TryGetValue(tile.Texture, out var imported))
            {
                using var stream = File.OpenRead(tile.Texture);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))[..16];
                var target = Path.Combine(destination, "Textures", hash + "-" + Path.GetFileName(tile.Texture));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(tile.Texture, target, true);
                imported = AssetPath(target); importedTextures.Add(tile.Texture, imported);
            }
            tile.Texture = imported;
        }
        for (var index = 0; index < result.Palettes.Count; index++) result.Palettes[index].Save(Path.Combine(destination, "Layer-" + index + ".tilepalette.yaml"));
        Undo.RegisterCreatedObjectUndo(result.Root, "Import Tiled Map"); EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = result.Root;
        AssetDatabase.Refresh();
    }
    public static TiledImportResult Import(string path, Scene scene, Fix64 pixelsPerUnit = default) => Import(path, scene, pixelsPerUnit, true);

    public static TiledImportResult Import(string path, Scene scene, Fix64 pixelsPerUnit, bool generateObjectColliders)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Import Tiled Map"); ArgumentNullException.ThrowIfNull(scene);
        path = Path.GetFullPath(path); if (pixelsPerUnit <= 0) pixelsPerUnit = 100;
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
        var map = Read(path); var width = Number(map, "tilewidth"); var height = Number(map, "tileheight");
        if (width <= 0 || height <= 0) throw new InvalidDataException("Tiled tile dimensions must be positive.");
        var orientation = Text(map, "orientation", "orthogonal");
        var layout = orientation switch { "orthogonal" => TilemapLayout.Rectangle, "isometric" => TilemapLayout.Isometric,
            "hexagonal" or "staggered" => Text(map, "staggeraxis") == "x" ? TilemapLayout.HexagonFlatTop : TilemapLayout.HexagonPointTop,
            _ => throw new NotSupportedException($"Tiled orientation '{orientation}' is unsupported.") };
        var hexDimension = layout == TilemapLayout.HexagonFlatTop ? width : height;
        var hexSide = orientation == "staggered" ? 0 : Number(map, "hexsidelength", hexDimension / 2);
        if (hexSide < 0 || hexSide > hexDimension) throw new InvalidDataException("Tiled hexsidelength exceeds the tile dimension.");
        var tiles = new Dictionary<int, TileDefinition>();
        var loadedTilesets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in (map["tilesets"] as JsonArray ?? []))
            LoadTileset(node!.AsObject(), Path.GetDirectoryName(path)!);
        void LoadTileset(JsonObject reference, string directory)
        {
            var first = Number(reference, "firstgid"); if (first <= 0) throw new InvalidDataException("Tileset firstgid must be positive.");
            var tileset = reference;
            if (Text(reference, "source") is { Length: > 0 } source)
            { var external = Path.GetFullPath(Path.Combine(directory, source)); loadedTilesets[external] = first; dependencies.Add(external); tileset = Read(external); directory = Path.GetDirectoryName(external)!; }
            var tw = Number(tileset, "tilewidth", width); var th = Number(tileset, "tileheight", height);
            var columns = Number(tileset, "columns"); var count = Number(tileset, "tilecount"); var imageWidth = Number(tileset, "imagewidth"); var imageHeight = Number(tileset, "imageheight");
            var spacing = Number(tileset, "spacing"); var margin = Number(tileset, "margin");
            var image = Text(tileset, "image");
            var definitions = (tileset["tiles"] as JsonArray ?? []).Select(value => value!.AsObject()).ToDictionary(value => Number(value, "id"));
            if (image.Length == 0 || count == 0) count = definitions.Count == 0 ? 0 : definitions.Keys.Max() + 1;
            if (count > 65536 || count < 0 || tw <= 0 || th <= 0) throw new InvalidDataException("Tiled tileset exceeds its limits.");
            for (var local = 0; local < count; local++)
            {
                var own = definitions.GetValueOrDefault(local); var tileImage = own is null ? image : Text(own, "image", image);
                if (tileImage.Length == 0) continue;
                var texture = Path.GetFullPath(Path.Combine(directory, tileImage)); dependencies.Add(texture);
                var id = checked(first + local);
                var tile = new TileDefinition { Id = id, Name = Text(own, "type", "Tile " + id), Texture = AssetPath(texture), HasCollider = own?["objectgroup"] is not null, PixelWidth = tw, PixelHeight = th };
                if (own?["objectgroup"] is JsonObject collisionGroup)
                {
                    tile.CollisionPaths = ReadCollisionPaths(collisionGroup, tw, th);
                    tile.HasCollider = tile.CollisionPaths.Count > 0;
                }
                if (tileImage == image && columns > 0)
                {
                    if (imageWidth <= 0 || imageHeight <= 0) throw new InvalidDataException("Tiled atlas dimensions are missing.");
                    tile.UvX = (Fix64)checked(margin + local % columns * (tw + spacing)) / imageWidth;
                    tile.UvY = (Fix64)checked(margin + local / columns * (th + spacing)) / imageHeight;
                    tile.UvWidth = (Fix64)tw / imageWidth; tile.UvHeight = (Fix64)th / imageHeight;
                }
                if (own?["animation"] is JsonArray animation)
                    tile.Animation = animation.Select(frame => new TileAnimationFrame { TileId = checked(first + Number(frame!.AsObject(), "tileid")), Duration = (Fix64)Number(frame.AsObject(), "duration") / 1000 }).ToList();
                if (!tiles.TryAdd(id, tile)) throw new InvalidDataException("Tiled tilesets overlap global IDs.");
            }
        }
        var root = scene.CreateGameObject(Path.GetFileNameWithoutExtension(path)); var palettes = new List<TilePalette>();
        var objectIds = new HashSet<int>();
        try
        {
            ImportLayers(map["layers"] as JsonArray ?? [], root.transform, 0, 0, true, 1);
            return new(root, palettes, dependencies.ToArray());
        }
        catch { BObject.DestroyImmediate(root); foreach (var palette in palettes) BObject.DestroyImmediate(palette); throw; }
        void ImportLayers(JsonArray layers, Transform parent, Fix64 inheritedX, Fix64 inheritedY, bool visible, Fix64 opacity, int depth = 0)
        {
            if (depth > 32) throw new InvalidDataException("Tiled groups exceed 32 levels.");
            foreach (var item in layers)
            {
                var layer = item!.AsObject(); var type = Text(layer, "type"); var x = inheritedX + Real(layer, "offsetx"); var y = inheritedY + Real(layer, "offsety");
                var shown = visible && (layer["visible"]?.GetValue<bool>() ?? true); var alpha = opacity * Real(layer, "opacity", Fix64.One);
                if (type == "group") { ImportLayers(layer["layers"]!.AsArray(), parent, x, y, shown, alpha, depth + 1); continue; }
                if (type == "objectgroup") { ImportObjects(layer, parent, x, y, shown, alpha); continue; }
                if (type == "imagelayer")
                {
                    if (layer["repeatx"]?.GetValue<bool>() == true || layer["repeaty"]?.GetValue<bool>() == true) throw new NotSupportedException("Repeating Tiled image layers require a viewport background renderer.");
                    var image = Text(layer, "image"); var imagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, image)); dependencies.Add(imagePath);
                    var imageWidth = Number(layer, "imagewidth"); var imageHeight = Number(layer, "imageheight");
                    if (imageWidth <= 0 || imageHeight <= 0)
                    { if (!BEngine.Rendering.ImageCodec.TryGetInfo(File.ReadAllBytes(imagePath), out imageWidth, out imageHeight)) throw new InvalidDataException("Tiled image layer has no valid image dimensions."); }
                    var imageOwner = scene.CreateGameObject(Text(layer, "name", "Image")); imageOwner.transform.SetParent(parent, false);
                    imageOwner.transform.localPosition = new((x + Real(layer, "x")) / pixelsPerUnit, -(y + Real(layer, "y")) / pixelsPerUnit); imageOwner.SetActive(shown);
                    var imageMap = imageOwner.AddComponent<Tilemap>(); imageMap.cellSize = new((Fix64)imageWidth / pixelsPerUnit, (Fix64)imageHeight / pixelsPerUnit);
                    var imagePalette = new TilePalette { Name = imageOwner.name, Tiles = [new() { Id = 1, Texture = AssetPath(imagePath), Alpha = AlphaByte(alpha) }] };
                    palettes.Add(imagePalette); imageMap.palette = imagePalette; imageMap.SetTile(new(0, -1), 1); imageOwner.AddComponent<TilemapRenderer>(); continue;
                }
                if (type != "tilelayer") throw new NotSupportedException($"Tiled layer type '{type}' is unsupported.");
                var owner = scene.CreateGameObject(Text(layer, "name", "Tile Layer")); owner.transform.SetParent(parent, false);
                owner.transform.localPosition = new(x / pixelsPerUnit, -y / pixelsPerUnit); owner.SetActive(shown);
                var tilemap = owner.AddComponent<Tilemap>(); tilemap.cellLayout = layout; tilemap.cellSize = new((Fix64)width / pixelsPerUnit, (Fix64)height / pixelsPerUnit);
                tilemap.hexSideRatio = (Fix64)hexSide / hexDimension;
                owner.AddComponent<TilemapRenderer>();
                var palette = new TilePalette { Name = owner.name, Tiles = tiles.Values.Select(Clone).ToList() }; palettes.Add(palette);
                foreach (var definition in palette.Tiles) definition.Alpha = AlphaByte(alpha);
                palette.Validate(); tilemap.palette = palette;
                var data = new List<TileCell>();
                if (layer["chunks"] is JsonArray chunks)
                    foreach (var chunk in chunks) AddData(chunk!.AsObject(), Number(chunk.AsObject(), "x"), Number(chunk.AsObject(), "y"));
                else AddData(layer, Number(layer, "x"), Number(layer, "y"));
                tilemap.SetTiles(data);
                void AddData(JsonObject source, int startX, int startY)
                {
                    var w = Number(source, "width"); var h = Number(source, "height");
                    var total = checked((long)w * h); if (w <= 0 || h <= 0 || total > 1000000) throw new InvalidDataException("Tiled layer exceeds 1000000 cells.");
                    var ids = Decode(source["data"], Text(layer, "encoding"), Text(layer, "compression"), (int)total);
                    for (var index = 0; index < ids.Length; index++)
                    {
                        var gid = ids[index]; var id = (int)(gid & 0x0fffffff); if (id == 0) continue;
                        if (!tiles.ContainsKey(id)) throw new InvalidDataException($"Tiled layer refers to unknown tile {id}.");
                        var flags = (gid & 0x80000000) != 0 ? TileTransformFlags.FlipX : TileTransformFlags.None;
                        if ((gid & 0x40000000) != 0) flags |= TileTransformFlags.FlipY;
                        if ((gid & 0x20000000) != 0) flags ^= TileTransformFlags.Rotate90 | TileTransformFlags.FlipX;
                        if (layout is TilemapLayout.HexagonFlatTop or TilemapLayout.HexagonPointTop && (gid & 0x30000000) != 0)
                            throw new NotSupportedException("Tiled hexagonal 60/120 degree tile rotations require arbitrary tile rotation support.");
                        var cx = checked(startX + index % w); var cy = checked(startY + index / w);
                        var odd = Text(map, "staggerindex", "odd") == "odd";
                        var coordinate = layout switch
                        {
                            TilemapLayout.Isometric => new TileCoordinate(-cy, -cx),
                            TilemapLayout.HexagonPointTop => new(cx + (cy + (odd ? cy & 1 : -(cy & 1))) / 2, -cy),
                            TilemapLayout.HexagonFlatTop => new(cx, -cy - (cx - (odd ? cx & 1 : -(cx & 1))) / 2),
                            _ => new TileCoordinate(cx, -cy - 1)
                        };
                        data.Add(new(coordinate, id, flags));
                    }
                }
            }
        }
        void ImportObjects(JsonObject layer, Transform parent, Fix64 x, Fix64 y, bool visible, Fix64 alpha)
        {
            var owner = scene.CreateGameObject(Text(layer, "name", "Objects")); owner.transform.SetParent(parent, false);
            owner.transform.localPosition = new(x / pixelsPerUnit, -y / pixelsPerUnit); owner.SetActive(visible);
            foreach (var node in layer["objects"] as JsonArray ?? [])
            {
                var source = ResolveTemplate(node!.AsObject(), path, new HashSet<string>(StringComparer.OrdinalIgnoreCase)); var id = Number(source, "id");
                if (id <= 0 || !objectIds.Add(id) || objectIds.Count > 65536) throw new InvalidDataException("Tiled objects require unique positive IDs and at most 65536 objects.");
                if (source["text"] is not null || source["capsule"] is not null)
                    throw new NotSupportedException("Tiled text and capsule objects require dedicated import support.");
                var item = scene.CreateGameObject(Text(source, "name", "Object " + id)); item.transform.SetParent(owner.transform, false);
                item.transform.localPosition = new(Coordinate("x"), -Coordinate("y"));
                item.transform.localRotation = -Real(source, "rotation");
                item.SetActive(source["visible"]?.GetValue<bool>() ?? true);
                var data = item.AddComponent<TiledObject>(); data.objectId = id; data.className = Text(source, "class", Text(source, "type"));
                data.isometricProjection = layout == TilemapLayout.Isometric && source["gid"] is null;
                data.projectionAspect = (Fix64)width / height;
                if (data.isometricProjection)
                    item.transform.localPosition = data.ProjectPoint(item.transform.localPosition);
                data.size = new(Coordinate("width"), Coordinate("height"));
                if (source["gid"] is { } gidNode)
                {
                    var gid = uint.Parse(gidNode.ToJsonString(), CultureInfo.InvariantCulture); var tileId = (int)(gid & 0x0fffffff);
                    if (!tiles.TryGetValue(tileId, out var definition)) throw new InvalidDataException("Tiled tile object refers to an unknown tile.");
                    data.tileGid = gid;
                    if (data.size.x == 0) data.size = new((Fix64)definition.PixelWidth / pixelsPerUnit, data.size.y);
                    if (data.size.y == 0) data.size = new(data.size.x, (Fix64)definition.PixelHeight / pixelsPerUnit);
                    var objectMap = item.AddComponent<Tilemap>(); objectMap.cellSize = data.size;
                    var objectPalette = new TilePalette { Name = item.name, Tiles = tiles.Values.Select(Clone).ToList() }; palettes.Add(objectPalette); objectMap.palette = objectPalette;
                    var flags = (gid & 0x80000000) != 0 ? TileTransformFlags.FlipX : TileTransformFlags.None;
                    if ((gid & 0x40000000) != 0) flags |= TileTransformFlags.FlipY;
                    if ((gid & 0x20000000) != 0) flags ^= TileTransformFlags.Rotate90 | TileTransformFlags.FlipX;
                    objectMap.SetTile(new(0, 0), tileId, flags); item.AddComponent<TilemapRenderer>().opacity = alpha;
                    data.isometricProjection = false;
                }
                if (data.size.x < 0 || data.size.y < 0) throw new InvalidDataException("Tiled object dimensions must not be negative.");
                var polygon = source["polygon"] as JsonArray; var polyline = source["polyline"] as JsonArray;
                data.shape = polygon is not null ? TiledObjectShape.Polygon : polyline is not null ? TiledObjectShape.Polyline
                    : source["point"]?.GetValue<bool>() == true ? TiledObjectShape.Point : source["ellipse"]?.GetValue<bool>() == true ? TiledObjectShape.Ellipse : TiledObjectShape.Rectangle;
                if ((polygon ?? polyline) is { } vertices)
                {
                    if (vertices.Count < (polygon is null ? 2 : 3) || vertices.Count > 65536) throw new InvalidDataException("Invalid Tiled object vertex count.");
                    data.points = vertices.Select(vertex => new Vector2(Real(vertex!.AsObject(), "x") / pixelsPerUnit, -Real(vertex.AsObject(), "y") / pixelsPerUnit)).ToList();
                }
                var properties = source["properties"] as JsonArray ?? [];
                if (properties.Count > 1024) throw new InvalidDataException("Tiled object exceeds 1024 properties.");
                foreach (var property in properties)
                {
                    var value = property!.AsObject(); var raw = value["value"];
                    data.properties.Add(new() { name = Text(value, "name"), type = Text(value, "type", "string"),
                        value = raw is JsonValue primitive && primitive.TryGetValue<string>(out var text) ? text : raw?.ToJsonString() ?? "" });
                }
                if (generateObjectColliders && !string.Equals(data.GetProperty("collider"), "false", StringComparison.OrdinalIgnoreCase))
                    TiledObjectColliderUtility.Create(data);
                Fix64 Coordinate(string field)
                {
                    var value = Real(source, field);
                    if (value < -1000000000 || value > 1000000000) throw new InvalidDataException("Tiled object coordinate exceeds its limits.");
                    return value / pixelsPerUnit;
                }
            }
        }
        JsonObject ResolveTemplate(JsonObject instance, string ownerPath, HashSet<string> active)
        {
            if (Text(instance, "template") is not { Length: > 0 } name) return instance;
            var templatePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ownerPath)!, name));
            if (active.Count >= 16 || !active.Add(templatePath)) throw new InvalidDataException("Cyclic or excessive Tiled object templates.");
            try
            {
                dependencies.Add(templatePath); var template = Read(templatePath);
                var inherited = ResolveTemplate(template["object"]?.AsObject() ?? throw new InvalidDataException("Template has no object."), templatePath, active).DeepClone().AsObject();
                var tileset = template["tileset"] as JsonObject ?? (template["tilesets"] as JsonArray)?.FirstOrDefault()?.AsObject();
                if (inherited["gid"] is { } gidNode && template["object"]?["gid"] is not null && tileset is not null)
                {
                    var external = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(templatePath)!, Text(tileset, "source")));
                    if (!loadedTilesets.TryGetValue(external, out var first))
                    { first = tiles.Count == 0 ? 1 : checked(tiles.Keys.Max() + 1); LoadTileset(new JsonObject { ["firstgid"] = first, ["source"] = external }, Path.GetDirectoryName(templatePath)!); }
                    var gid = uint.Parse(gidNode.ToJsonString(), CultureInfo.InvariantCulture);
                    inherited["gid"] = (gid & 0xf0000000) | checked((uint)(first + (gid & 0x0fffffff) - Number(tileset, "firstgid", 1)));
                }
                var properties = (inherited["properties"] as JsonArray ?? []).Select(value => value!.DeepClone()).ToList();
                foreach (var property in instance["properties"] as JsonArray ?? [])
                { properties.RemoveAll(value => Text(value.AsObject(), "name") == Text(property!.AsObject(), "name")); properties.Add(property!.DeepClone()); }
                foreach (var pair in instance) if (pair.Key is not ("template" or "properties")) inherited[pair.Key] = pair.Value?.DeepClone();
                inherited.Remove("template"); inherited["properties"] = new JsonArray(properties.ToArray()); return inherited;
            }
            finally { active.Remove(templatePath); }
        }
    }
    private static TileDefinition Clone(TileDefinition value) => new() { Id = value.Id, Name = value.Name, Texture = value.Texture, UvX = value.UvX, UvY = value.UvY,
        UvWidth = value.UvWidth, UvHeight = value.UvHeight, HasCollider = value.HasCollider, PixelWidth = value.PixelWidth, PixelHeight = value.PixelHeight,
        CollisionPaths = value.CollisionPaths?.Select(path => new TileCollisionPath2D { Points = path.Points.ToList(), Closed = path.Closed, IsHole = path.IsHole }).ToList(),
        Animation = value.Animation.Select(frame => new TileAnimationFrame { TileId = frame.TileId, Duration = frame.Duration }).ToList() };

    private static List<TileCollisionPath2D> ReadCollisionPaths(JsonObject group, int width, int height)
    {
        var result = new List<TileCollisionPath2D>();
        var objects = group["objects"] as JsonArray ?? [];
        if (objects.Count > 1024) throw new InvalidDataException("A tile exceeds 1024 collision shapes.");
        foreach (var node in objects)
        {
            var shape = node!.AsObject();
            if (shape["point"]?.GetValue<bool>() == true || shape["visible"]?.GetValue<bool>() == false) continue;
            if (shape["gid"] is not null || shape["template"] is not null || shape["text"] is not null)
                throw new NotSupportedException("Tile collision objects must use geometric shapes.");
            var polygon = shape["polygon"] as JsonArray; var polyline = shape["polyline"] as JsonArray;
            var points = new List<(Fix64 X, Fix64 Y)>();
            var w = Real(shape, "width"); var h = Real(shape, "height");
            if ((polygon ?? polyline) is { } vertices)
            {
                if (vertices.Count < (polygon is null ? 2 : 3) || vertices.Count > 65536) throw new InvalidDataException("Invalid tile collision vertices.");
                points.AddRange(vertices.Select(vertex => (Real(vertex!.AsObject(), "x"), Real(vertex.AsObject(), "y"))));
            }
            else if (shape["ellipse"]?.GetValue<bool>() == true)
            {
                if (w <= 0 || h <= 0) continue;
                for (var index = 0; index < 48; index++)
                { var angle = index * Fix64.TwoPi / 48; points.Add((w * (1 + Fix64.Cos(angle)) / 2, h * (1 + Fix64.Sin(angle)) / 2)); }
            }
            else
            {
                if (w <= 0 || h <= 0) continue;
                points.AddRange([(0, 0), (w, 0), (w, h), (0, h)]);
            }
            var angleRadians = Real(shape, "rotation") * Fix64.Deg2Rad;
            var cosine = Fix64.Cos(angleRadians); var sine = Fix64.Sin(angleRadians);
            var x = Real(shape, "x"); var y = Real(shape, "y");
            var path = new TileCollisionPath2D { Closed = polyline is null };
            foreach (var point in points)
            {
                var px = (x + point.X * cosine - point.Y * sine) / width - Fix64.Half;
                var py = Fix64.Half - (y + point.X * sine + point.Y * cosine) / height;
                if (px < -10000 || px > 10000 || py < -10000 || py > 10000)
                    throw new InvalidDataException("Tile collision coordinate exceeds its limits.");
                path.Points.Add(new(px, py));
            }
            result.Add(path);
        }
        return result;
    }
    private static uint[] Decode(JsonNode? node, string encoding, string compression, int count)
    {
        uint[] values;
        if (node is JsonArray array) values = array.Select(value => value!.GetValue<uint>()).ToArray();
        else if (encoding == "csv") values = (node?.GetValue<string>() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        else if (encoding == "base64")
        {
            var bytes = Convert.FromBase64String(node?.GetValue<string>() ?? ""); using var input = new MemoryStream(bytes);
            using Stream stream = compression switch { "" => input, "gzip" => new GZipStream(input, CompressionMode.Decompress), "zlib" => new ZLibStream(input, CompressionMode.Decompress), _ => throw new NotSupportedException($"Tiled compression '{compression}' is unsupported.") };
            var decoded = new byte[checked(count * 4)]; stream.ReadExactly(decoded); if (stream.ReadByte() != -1) throw new InvalidDataException("Tiled compressed layer has excess data.");
            values = new uint[count]; for (var index = 0; index < count; index++) values[index] = BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(index * 4, 4));
        }
        else throw new NotSupportedException("Unsupported Tiled layer encoding.");
        if (values.Length != count) throw new InvalidDataException("Tiled layer dimensions and data count differ."); return values;
    }
    private static JsonObject Read(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Tiled source exceeds 64 MiB.");
        if (Path.GetExtension(path) is not (".tmx" or ".tsx" or ".tx")) return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        using var reader = System.Xml.XmlReader.Create(path, new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, MaxCharactersInDocument = 64 * 1024 * 1024 });
        return Xml(XDocument.Load(reader).Root!);
    }
    private static JsonObject Xml(XElement element)
    {
        var name = element.Name.LocalName;
        var result = new JsonObject();
        foreach (var attribute in element.Attributes())
        {
            var key = attribute.Name.LocalName; var text = attribute.Value;
            result[key] = key is "visible" or "repeatx" or "repeaty" ? JsonValue.Create(text is "1" or "true") : key == "gid" ? JsonValue.Create(uint.Parse(text, CultureInfo.InvariantCulture)) : key is "opacity" or "offsetx" or "offsety" || name == "object" && key is "x" or "y" or "width" or "height" or "rotation" ? JsonValue.Create(text)
                : key is "name" or "source" or "type" or "class" or "orientation" or "staggeraxis" or "staggerindex" or "encoding" or "compression" ? JsonValue.Create(text)
                : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number) : JsonValue.Create(text);
        }
        if (name == "layer") { result["type"] = "tilelayer"; var data = element.Element("data")!; result["encoding"] = (string?)data.Attribute("encoding") ?? ""; result["compression"] = (string?)data.Attribute("compression") ?? "";
            if (data.Elements("chunk").Any()) result["chunks"] = new JsonArray(data.Elements("chunk").Select(chunk => { var value = Xml(chunk); value["data"] = chunk.Value; return (JsonNode)value; }).ToArray());
            else if (result["encoding"]!.GetValue<string>().Length == 0) result["data"] = new JsonArray(data.Elements("tile").Select(tile => (JsonNode)JsonValue.Create(uint.Parse(tile.Attribute("gid")!.Value))!).ToArray());
            else result["data"] = data.Value;
        }
        if (name is "group" or "imagelayer") result["type"] = name;
        if (name == "template" && element.Element("object") is { } templateObject) result["object"] = Xml(templateObject);
        if (name == "objectgroup") { result["type"] = "objectgroup"; result["objects"] = new JsonArray(element.Elements("object").Select(value => (JsonNode)Xml(value)).ToArray()); }
        var layers = element.Elements().Where(value => value.Name.LocalName is "layer" or "group" or "objectgroup" or "imagelayer").ToArray(); if (layers.Length > 0) result["layers"] = new JsonArray(layers.Select(value => (JsonNode)Xml(value)).ToArray());
        if (name == "object")
        {
            if (element.Element("point") is not null) result["point"] = true;
            if (element.Element("ellipse") is not null) result["ellipse"] = true;
            if (element.Element("text") is not null) result["text"] = new JsonObject();
            foreach (var kind in new[] { "polygon", "polyline" })
                if (element.Element(kind) is { } shape)
                    result[kind] = new JsonArray(((string?)shape.Attribute("points") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(point =>
                    { var pair = point.Split(','); return (JsonNode)new JsonObject { ["x"] = pair[0], ["y"] = pair[1] }; }).ToArray());
        }
        if (element.Element("properties") is { } properties)
            result["properties"] = new JsonArray(properties.Elements("property").Select(property =>
            { var value = Xml(property); if (value["value"] is null) value["value"] = property.Value; return (JsonNode)value; }).ToArray());
        var sets = element.Elements("tileset").ToArray(); if (sets.Length > 0) result["tilesets"] = new JsonArray(sets.Select(value => (JsonNode)Xml(value)).ToArray());
        if (element.Element("image") is { } image) { result["image"] = (string?)image.Attribute("source"); result["imagewidth"] = (int?)image.Attribute("width") ?? 0; result["imageheight"] = (int?)image.Attribute("height") ?? 0; }
        var tiles = element.Elements("tile").ToArray(); if (tiles.Length > 0) result["tiles"] = new JsonArray(tiles.Select(value => (JsonNode)Xml(value)).ToArray());
        if (element.Element("objectgroup") is { } collisionGroup) result["objectgroup"] = Xml(collisionGroup);
        if (element.Element("animation") is { } animation) result["animation"] = new JsonArray(animation.Elements("frame").Select(value => (JsonNode)Xml(value)).ToArray());
        return result;
    }
    private static int Number(JsonObject? node, string name, int fallback = 0) => node?[name]?.GetValue<int>() ?? fallback;
    private static Fix64 Real(JsonObject node, string name, Fix64 fallback = default) => node[name] is { } value
        ? Fix64.Parse(value is JsonValue primitive && primitive.TryGetValue<string>(out var text)
            ? text : value.ToJsonString(), CultureInfo.InvariantCulture)
        : fallback;
    private static byte AlphaByte(Fix64 alpha) => (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(alpha));
    private static string Text(JsonObject? node, string name, string fallback = "") => node?[name]?.GetValue<string>() ?? fallback;
    private static string AssetPath(string path)
    { var assets = Path.GetFullPath(Application.dataPath); return path.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "Assets/" + Path.GetRelativePath(assets, path).Replace('\\', '/') : path; }
}
