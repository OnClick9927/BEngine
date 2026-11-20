using System.Runtime.CompilerServices;
namespace BEngine;
internal static class SceneRenderQueries
{
    private sealed class Cache { internal IReadOnlyList<Renderer2D>? Source; internal Renderer2D[]? Items; }
    private static readonly ConditionalWeakTable<Scene,Cache> Caches = new();
    internal static IReadOnlyList<Renderer2D> QueryNonSpriteRenderers(this Scene scene)
    {
        var cache = Caches.GetOrCreateValue(scene);
        var renderers = scene.QueryComponents<Renderer2D>();
        if (ReferenceEquals(cache.Source, renderers) && cache.Items is not null) return cache.Items;
        var count = 0;
        for (var cursor = 0; cursor < renderers.Count; cursor++)
            if (renderers[cursor] is not SpriteRenderer) count++;
        var snapshot = count == 0 ? [] : new Renderer2D[count];
        var index = 0;
        for (var cursor = 0; cursor < renderers.Count; cursor++)
            if (renderers[cursor] is not SpriteRenderer) snapshot[index++] = renderers[cursor];
        cache.Items = snapshot; cache.Source = renderers;
        return snapshot;
    }

}
