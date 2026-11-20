using System;
using System.Diagnostics.CodeAnalysis;

namespace BEngine.App.Value;

/// <summary>A lookup and explicit member-injection facade over the game's standard DI provider.</summary>
public interface IValueService : IGameService
{
    object? Get(Type type, string? name = null);
    object GetRequired(Type type, string? name = null);
    bool TryGet(Type type, [NotNullWhen(true)] out object? value, string? name = null);

    [RequiresUnreferencedCode("Member injection needs all attributed fields/properties, including private base members, preserved. Prefer constructor injection in trimmed players.")]
    void Inject(object target);
}
