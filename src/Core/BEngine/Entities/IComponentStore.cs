namespace BEngine.Entities;

internal interface IComponentStore
{
    Type ComponentType { get; }
    bool Contains(int entityIndex);
    bool Remove(int entityIndex);
    void Clear();
}
