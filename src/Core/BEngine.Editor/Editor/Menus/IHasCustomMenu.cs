using System.Reflection;
using System.Linq.Expressions;

namespace BEngine.Editor;

public interface IHasCustomMenu
{
    void AddItemsToMenu(GenericMenu menu);
}
