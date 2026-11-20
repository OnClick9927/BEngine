using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>Preserves legacy renderer-field migration without making content stores understand Sprites.</summary>
internal sealed class SpriteSceneReferenceRestorer : SceneReferenceRestorer
{
    [ModuleInitializer]
    internal static void Register() => SceneReferenceRestorers.Register(new SpriteSceneReferenceRestorer());
    public override Type ComponentType => typeof(SpriteRenderer);

    public override void Restore(Component component, SceneComponentReferences references,
        IResourceObjectProvider resources)
    {
        if (!references.TryGetValue(nameof(SpriteRenderer.sprite), out var reference) ||
            string.IsNullOrWhiteSpace(reference)) return;
        var renderer = (SpriteRenderer)component;
        if (resources.TryLoadObject(reference, "Resources", typeof(Sprite), out var value))
        {
            if (value is not Sprite sprite)
                throw new InvalidDataException($"Scene reference '{reference}' did not resolve to a Sprite.");
            renderer.sprite = sprite;
        }
        else if (renderer.sprite is null)
            throw new InvalidDataException($"Scene Sprite '{reference}' could not be restored from its resources.");
    }
}
