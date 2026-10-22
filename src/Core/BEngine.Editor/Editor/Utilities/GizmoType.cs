using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Serialization;

namespace BEngine.Editor;

[Flags]
public enum GizmoType
{
    Pickable = 1,
    NotInSelectionHierarchy = 2,
    NonSelected = 4,
    Selected = 8,
    Active = 16,
    InSelectionHierarchy = 32
}
