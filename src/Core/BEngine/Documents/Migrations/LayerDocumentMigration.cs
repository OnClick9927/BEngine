using System.Globalization;

namespace BEngine.Documents;

internal static class LayerDocumentMigration
{
    internal static bool Normalize(SceneDocument document)
    {
        if (document.Version >= 2) return false;
        Migrate(document.GameObjects);
        document.Version = 2;
        return true;
    }

    internal static bool Normalize(PrefabDocument document)
    {
        if (document.Version >= 2) return false;
        Migrate(document.GameObjects);
        document.Version = 2;
        return true;
    }

    private static void Migrate(IEnumerable<GameObjectDocument> gameObjects)
    {
        foreach (var gameObject in gameObjects)
        {
            gameObject.Layer = LegacyLayer(gameObject.Layer);
            MigrateFields(gameObject.Transform.Fields);
            foreach (var component in gameObject.Components) MigrateFields(component.Fields);
        }
    }

    private static void MigrateFields(IDictionary<string, string> fields)
    {
        foreach (var key in fields.Keys.ToArray())
        {
            if (!ulong.TryParse(fields[key], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var value)) continue;
            // GameObject.layer is stored on GameObjectDocument and migrated above. A component
            // field merely named "layer" can be unrelated user data, so do not rewrite it.
            if (key.Equals("sortingLayer", StringComparison.OrdinalIgnoreCase))
                fields[key] = LegacyLayer(value).ToString(CultureInfo.InvariantCulture);
            else if (key.EndsWith("LayerMask", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("cullingMask", StringComparison.OrdinalIgnoreCase))
                fields[key] = SortingLayer.FromLegacyMask(value).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static ulong LegacyLayer(ulong value)
    {
        if (value >= 2 && (value & (value - 1)) == 0)
            return SortingLayer.FromLegacyPowerOfTwo(value);
        return SortingLayer.IsValid(value) ? value : SortingLayer.Default;
    }
}
