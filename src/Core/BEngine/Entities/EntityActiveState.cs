namespace BEngine.Entities;

public struct EntityActiveState : IComponentData
{
    public bool ActiveSelf;
    public bool ActiveInHierarchy;

    public EntityActiveState(bool activeSelf, bool activeInHierarchy)
    {
        ActiveSelf = activeSelf;
        ActiveInHierarchy = activeInHierarchy;
    }
}
