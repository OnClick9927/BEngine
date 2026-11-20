using System;
using System.Diagnostics.CodeAnalysis;

namespace BEngine.App.Pref;

public interface IPrefService : IGameService
{
    string NamespaceKey { get; }
    T Load<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = PrefService.DefaultKey) where T : notnull, new();
    T Load<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key, Func<T> createDefault) where T : notnull;
    PrefContext<T> GetContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = PrefService.DefaultKey) where T : notnull, new();
    PrefContext<T> GetContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key, Func<T> createDefault) where T : notnull;
    void Save<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key, T value) where T : notnull;
    void Save<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = PrefService.DefaultKey) where T : notnull;
    void SaveAll();
    bool Delete<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key = PrefService.DefaultKey) where T : notnull;
}
