using System;
using System.Diagnostics.CodeAnalysis;

namespace BEngine.App.Pref;

/// <summary>Reuses Core PlayerPrefs and its platform storage, including persistent browser storage.</summary>
public sealed class PlayerPrefsPrefLoader : IPrefLoader
{
    public bool TryLoad(string key, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (PlayerPrefs.HasKey(key)) { value = PlayerPrefs.GetString(key); return true; }
        value = null;
        return false;
    }
    public void Save(string key, string value) => PlayerPrefs.SetString(key, value);
    public bool Delete(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return false;
        PlayerPrefs.DeleteKey(key);
        return true;
    }
    public void Flush() => PlayerPrefs.Save();
}
