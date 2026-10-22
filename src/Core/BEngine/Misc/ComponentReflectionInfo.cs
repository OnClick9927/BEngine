using System.Reflection;

namespace BEngine;

internal readonly record struct ComponentReflectionInfo(
    Func<Component>? Factory,
    bool DisallowMultiple,
    Type[] RequiredComponents,
    MemberInfo[] SerializableMembers,
    MemberInfo[] InspectorMembers);
