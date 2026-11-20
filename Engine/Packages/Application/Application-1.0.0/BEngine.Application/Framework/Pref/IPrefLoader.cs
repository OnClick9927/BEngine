using System.Diagnostics.CodeAnalysis;

namespace BEngine.App.Pref;

/// <summary>Storage for already-namespaced keys. Save/Delete stage changes; Flush makes them durable.</summary>
public interface IPrefLoader
{
    bool TryLoad(string key, [NotNullWhen(true)] out string? value);
    void Save(string key, string value);
    bool Delete(string key);
    void Flush();
}
