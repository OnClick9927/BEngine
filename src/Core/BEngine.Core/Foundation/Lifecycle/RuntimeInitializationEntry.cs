using System.Reflection;
using System.Reflection.Emit;
using System.Collections;

namespace BEngine;

internal readonly record struct RuntimeInitializationEntry(
    RuntimeInitializeLoadType Phase,
    Action Callback,
    string Description,
    int MetadataToken);
